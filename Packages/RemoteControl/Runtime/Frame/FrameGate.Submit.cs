// Copyright (c) You-Ri, 2026
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Lilium.RemoteControl.Frames
{
    // How an event gets into the queue: from a request that waits for its result, or from a producer
    // inside the frame that does not.
    public static partial class FrameGate
    {
        // Records and request text for a group up to these sizes are built on the stack; past
        // them, on a pooled array. Either way nothing is left behind once the queue has copied them.
        private const int kStackRecords = 16;
        private const int kStackPayloadBytes = 1024;

        /// <summary>
        /// Puts an event in the queue and completes once it has been applied at a frame head.
        /// </summary>
        public static Task<T> SubmitAsync<T>(EventKind kind, string sourceId, string verb,
            string target, string requestText, Func<T> action)
            => _Submit(null, new EventDescriptor(kind, verb, target, requestText), _ResolveSourceId(sourceId), action);

        /// <summary>
        /// Puts an event in the queue on behalf of a source resolved once with
        /// <see cref="ResolveSource"/>. Preferred over the string form: the name has already been
        /// checked against a declaration and interned, so nothing is hashed per call.
        /// </summary>
        public static Task<T> SubmitAsync<T>(EventKind kind, FrameSource source, string verb,
            string target, string requestText, Func<T> action)
            => _Submit(null, new EventDescriptor(kind, verb, target, requestText), _CheckedSourceId(source), action);

        /// <summary>Group form of <see cref="SubmitAsync{T}(EventKind, FrameSource, string, string, string, Func{T})"/>.</summary>
        public static Task<T> SubmitGroupAsync<T>(IReadOnlyList<EventDescriptor> operations,
            FrameSource source, Func<T> action)
        {
            if (operations == null) throw new ArgumentNullException(nameof(operations));

            return _Submit(operations, default, _CheckedSourceId(source), action);
        }

        private static int _CheckedSourceId(FrameSource source)
        {
            if (!source.isValid)
            {
                throw new ArgumentException(
                    "[RemoteControl] Input source was never resolved. Use FrameGate.ResolveSource.",
                    nameof(source));
            }

            return source.id;
        }

        /// <summary>
        /// Puts several operations in the queue as one unit and completes once they have been
        /// applied together at a frame head.
        ///
        /// For a bundled request, whose parts have to take effect in the same frame. They are
        /// numbered as one run so they cannot be split, but recorded separately so each stays small
        /// enough to be kept faithfully.
        /// </summary>
        public static Task<T> SubmitGroupAsync<T>(IReadOnlyList<EventDescriptor> operations,
            string sourceId, Func<T> action)
        {
            if (operations == null) throw new ArgumentNullException(nameof(operations));

            // Resolved before the bypass checks so that an undeclared name is reported even when the
            // event never reaches the queue.
            return _Submit(operations, default, _ResolveSourceId(sourceId), action);
        }

        /// <summary>
        /// Queues one operation, or a group when <paramref name="group"/> is not null, unless the
        /// gate cannot take it.
        /// </summary>
        private static Task<T> _Submit<T>(IReadOnlyList<EventDescriptor> group, in EventDescriptor single,
            int sourceId, Func<T> action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (group != null && group.Count == 0) throw new ArgumentException("No operations.", nameof(group));

            // Refused rather than queued: after shutdown begins no frame head is coming, so a
            // queued event would keep its caller -- and the shutdown -- waiting indefinitely.
            if (_gateClosed)
            {
                return Task.FromException<T>(new OperationCanceledException(
                    "[RemoteControl] Frame gate is closed: the application is shutting down."));
            }

            // No pump, or already on the main thread. Waiting for a frame head from the main thread
            // would deadlock, so apply straight away and count it as a hole in the ordering.
            if (!_pumpInstalled || Thread.CurrentThread.ManagedThreadId == _mainThreadId)
            {
                Interlocked.Increment(ref _bypassedCount);
                return _ApplyOutsideGate(action);
            }

            var completion = new Completion<T>(action);
            _Queue(group, in single, sourceId, null, completion);
            return completion.Task;
        }

        /// <summary>Single-operation convenience for tests. See <see cref="_Enqueue{T}"/>.</summary>
        internal static Task<T> _Enqueue<T>(EventKind kind, string sourceId, string target,
            string requestText, Func<T> action, string verb = null)
            => _Enqueue(new[] { new EventDescriptor(kind, verb, target, requestText) },
                _ResolveSourceId(sourceId), action);

        /// <summary>Group convenience for tests, resolving the source by name.</summary>
        internal static Task<T> _Enqueue<T>(IReadOnlyList<EventDescriptor> operations,
            string sourceId, Func<T> action)
            => _Enqueue(operations, _ResolveSourceId(sourceId), action);

        /// <summary>
        /// Queues an event without the bypass checks. Split out so tests can drive the real queue
        /// and <see cref="Pump"/> from the main thread, where <see cref="SubmitGroupAsync"/> would
        /// deliberately refuse to wait.
        /// </summary>
        internal static Task<T> _Enqueue<T>(IReadOnlyList<EventDescriptor> operations,
            int source, Func<T> action)
        {
            var completion = new Completion<T>(action);
            _Queue(operations, default, source, null, completion);
            return completion.Task;
        }

        /// <summary>
        /// Builds the records for one event -- a single operation, or a group -- and hands them to
        /// the sequencer, which copies them into its batch. The records and the request text are
        /// built on the stack (or a pooled array past a size), so queueing leaves nothing behind.
        ///
        /// Targets are interned here rather than at the frame head: this runs on the thread that
        /// submitted the event, and the main thread should not pay for it.
        /// </summary>
        private static void _Queue(IReadOnlyList<EventDescriptor> group, in EventDescriptor single,
            int source, Action apply, IPendingCompletion completion)
        {
            var count = group?.Count ?? 1;

            var payloadBytes = 0;
            for (int i = 0; i < count; i++)
            {
                payloadBytes += EventPayload.ByteCountOf(group != null ? group[i].requestText : single.requestText);
            }

            Span<EventRecord> records = count <= kStackRecords
                ? stackalloc EventRecord[count]
                : new EventRecord[count];

            byte[] rented = null;
            Span<byte> payloads = payloadBytes <= kStackPayloadBytes
                ? stackalloc byte[payloadBytes]
                : (rented = ArrayPool<byte>.Shared.Rent(payloadBytes)).AsSpan(0, payloadBytes);

            var requestTypeId = payloadBytes > 0
                ? _symbols.Intern(EventPayload.kRequestTypeName)
                : FrameSymbolTable.kNone;

            var at = 0;
            for (int i = 0; i < count; i++)
            {
                var operation = group != null ? group[i] : single;

                // The sequence is stamped by the sequencer, which is where order is decided.
                records[i] = new EventRecord(0, operation.kind, source,
                    _symbols.Intern(operation.target), EventFlags.None,
                    _symbols.Intern(operation.verb));

                // The request text, until whoever applies it says what value it really wrote. The
                // target has not been resolved yet here, so its type is not knowable -- see
                // StampAppliedPayload for where the typed form arrives. Held inline: this is one
                // request body, not a value that recurs, and a table entry per distinct body would
                // grow without bound.
                if (string.IsNullOrEmpty(operation.requestText)) continue;

                var written = EventPayload.WriteString(operation.requestText, payloads.Slice(at));
                records[i].payloadTypeId = requestTypeId;
                records[i].payloadOffset = at;
                records[i].payloadLength = written;
                at += written;
            }

            _sequencer.Submit(records, payloads, apply, completion);

            if (rented != null) ArrayPool<byte>.Shared.Return(rented);
        }

        /// <summary>
        /// A submitted event and the caller waiting on it, in one object: what to run, and the
        /// completion the caller awaits. One allocation per request, where a closure pair, a record
        /// array and a queue entry used to be four.
        /// </summary>
        private sealed class Completion<T> : TaskCompletionSource<T>, IPendingCompletion
        {
            private readonly Func<T> _action;

            // Continuations run asynchronously so that whatever the caller does after its await --
            // building a response, writing it out -- does not run inside the pump on the main
            // thread. Every event funnels through one point, so inline continuations would pile the
            // whole cost of a frame's callers onto the frame head.
            public Completion(Func<T> action)
                : base(TaskCreationOptions.RunContinuationsAsynchronously)
            {
                _action = action;
            }

            public void Run()
            {
                try
                {
                    SetResult(_action());
                }
                catch (Exception e)
                {
                    SetException(e);
                    throw;
                }
            }

            public void Fault(Exception reason) => TrySetException(reason);
        }

        /// <summary>
        /// Queues an event to be applied at the next frame head and returns immediately.
        ///
        /// For a producer that is already inside the frame -- a deck button, a gamepad axis, a
        /// script -- rather than a request waiting on an answer. Those cannot use
        /// <see cref="SubmitAsync{T}(EventKind, FrameSource, string, string, string, Func{T})"/>:
        /// it is called from a worker thread that then blocks on the frame head, and blocking the
        /// main thread on a frame head the main thread is supposed to run would deadlock. Nothing
        /// waits here, so there is nothing to deadlock.
        ///
        /// The cost is one frame of latency: what fires during this frame lands at the head of the
        /// next one. That is what buys the ordering -- an operation and a remote write racing
        /// within a frame would otherwise land in whichever order the two happened to run in.
        ///
        /// <paramref name="apply"/> does the work and is expected to say what it wrote, with
        /// <see cref="StampAppliedPayload"/>, so the record keeps the value rather than nothing.
        /// </summary>
        public static void Post(EventKind kind, FrameSource source, string verb, string target,
            Action apply, string requestText = null)
        {
            if (apply == null) throw new ArgumentNullException(nameof(apply));

            if (!source.isValid)
            {
                throw new ArgumentException(
                    "[RemoteControl] Input source was never resolved. Use FrameGate.ResolveSource.",
                    nameof(source));
            }

            // Nothing is coming to apply it, and nobody is waiting to be told. Dropped silently
            // would be a write that vanished, so it is applied here and counted as a hole.
            if (_gateClosed || !_pumpInstalled)
            {
                Interlocked.Increment(ref _bypassedCount);
                apply();
                return;
            }

            _Post(kind, source, verb, target, apply, requestText);
        }

        /// <summary>
        /// Queues a posted event without the bypass check, so tests can drive the real queue whether
        /// or not a pump happens to be installed. See <see cref="Post"/>.
        /// </summary>
        internal static void _Post(EventKind kind, FrameSource source, string verb, string target,
            Action apply, string requestText = null)
            => _Queue(null, new EventDescriptor(kind, verb, target, requestText), source.id, apply, null);

        private static string _DescribeFirst(EventBatch batch, in PendingEvent evt)
        {
            if (evt.recordCount == 0) return "no records";

            ref var first = ref batch.RecordAt(evt.firstRecord);
            var suffix = evt.recordCount > 1 ? $" (+{evt.recordCount - 1} more)" : string.Empty;
            return $"{first.kind} {_symbols.Resolve(first.verbId)} {_symbols.Resolve(first.targetId)}{suffix}";
        }

        private static Task<T> _ApplyOutsideGate<T>(Func<T> action)
        {
            if (Thread.CurrentThread.ManagedThreadId == _mainThreadId)
            {
                try
                {
                    return Task.FromResult(action());
                }
                catch (Exception e)
                {
                    return Task.FromException<T>(e);
                }
            }

            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

            if (_mainThreadContext == null)
            {
                completion.SetException(new InvalidOperationException(
                    "[RemoteControl] Frame gate has no main thread context."));
                return completion.Task;
            }

            _mainThreadContext.Post(_ =>
            {
                try
                {
                    completion.SetResult(action());
                }
                catch (Exception e)
                {
                    completion.SetException(e);
                }
            }, null);

            return completion.Task;
        }
    }
}
