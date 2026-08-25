// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Unity.Collections;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;

namespace Lilium.RemoteControl.Frames
{
    /// <summary>
    /// The gate every state-changing input passes through before it reaches the application.
    ///
    /// Two things are added on top of what already happened (hand the work to the main thread and
    /// wait for it):
    ///
    /// - a sequence number, assigned once, so order is settled here rather than by whichever worker
    ///   thread arrived first. Order used to be well-defined on one machine but different on the
    ///   next, which is why two machines fed the same inputs could not stay together.
    /// - a single application point at the head of a frame, so "the state at frame N" means
    ///   something. Callbacks posted to the synchronisation context run wherever the engine happens
    ///   to pump it, which is not a position anything can be pinned to.
    ///
    /// Both are worth having on their own, before anything is recorded or mirrored.
    ///
    /// A caller still gets its result back, and still gets it only once the input has actually been
    /// applied -- success responses have to stay byte for byte what they were.
    /// </summary>
    public static class FrameGate
    {
        /// <summary>Frames retained for read-back. A few frames is enough to absorb jitter.</summary>
        private const int kDefaultBufferFrames = 16;

        private static readonly InputSequencer _sequencer = new InputSequencer();
        private static readonly InputSymbolTable _symbols = new InputSymbolTable();

        private static InputFrameBuffer _buffer = new InputFrameBuffer(kDefaultBufferFrames);
        private static IFrameClock _clock = new FrameCounterClock(FrameRate.FPS60);
        private static SynchronizationContext _mainThreadContext;
        private static int _mainThreadId;
        private static bool _pumpInstalled;
        private static volatile bool _gateClosed;
        private static long _bypassedCount;
        private static long _truncatedPayloadCount;

        /// <summary>Frames retained for read-back, indexed by frame number.</summary>
        public static InputFrameBuffer buffer => _buffer;

        /// <summary>
        /// The strings behind the ids in the records. A recording writes this into its
        /// header, and nothing can be read back out of a frame without it.
        /// </summary>
        public static InputSymbolTable symbols => _symbols;

        /// <summary>
        /// Inputs whose payload did not fit in a record and was cut short. They applied correctly,
        /// but what was kept of them cannot be replayed faithfully.
        /// </summary>
        public static long truncatedPayloadCount => Interlocked.Read(ref _truncatedPayloadCount);

        /// <summary>Supplies the frame number stamped on each committed frame.</summary>
        public static IFrameClock clock => _clock;

        /// <summary>True once a frame-head pump is running and inputs are being ordered.</summary>
        public static bool isGateRunning => _pumpInstalled;

        /// <summary>
        /// Inputs that had to be applied without passing through a frame head, because no pump was
        /// running or the caller was already on the main thread. Exposed rather than silent: each
        /// one is a hole in the ordering, and a run with holes cannot be replayed faithfully.
        /// </summary>
        public static long bypassedCount => Interlocked.Read(ref _bypassedCount);

        /// <summary>Sequence number the next accepted input will get.</summary>
        public static long nextSequence => _sequencer.nextSequence;

        /// <summary>
        /// Replaces the clock, for an external sync source or for replay. Main thread only, and not
        /// while a frame is being filled -- <see cref="Pump"/> reads the clock and the buffer as a
        /// pair.
        /// </summary>
        public static void SetClock(IFrameClock value)
        {
            _clock = value ?? throw new ArgumentNullException(nameof(value));
            _clock.Reset();
            _buffer.Reset();
        }

        /// <summary>
        /// Resizes the retained window. Drops what is currently held. Main thread only, and not
        /// between the start and the commit of a frame: the replacement would be committed as
        /// holding a frame it never received.
        /// </summary>
        public static void SetBufferFrames(int frameCapacity)
        {
            _buffer = new InputFrameBuffer(frameCapacity);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void _InitializeOnPlay()
        {
            // Domain-reload-disabled safe: every static carrying run state is reset here.
            ResetState("[RemoteControl] Frame gate restarted before this input reached a frame.");

            _CaptureMainThread();
            _InstallPlayerLoopHook();

            Application.quitting -= _CloseGate;
            Application.quitting += _CloseGate;
        }

        /// <summary>
        /// Clears everything the gate carries between runs. Main thread only.
        ///
        /// Queued inputs are handed their failure rather than dropped. A caller is blocked on the
        /// frame head its input was going to reach, so discarding one silently leaves that caller
        /// waiting forever -- for an HTTP request that means hanging until the client gives up, and
        /// with write coalescing upstream, everything queued behind it stalls too.
        /// </summary>
        internal static void ResetState(string reason)
        {
            _gateClosed = false;
            _FaultPending(reason);
            _sequencer.Reset();
            _symbols.Reset();
            _buffer.Reset();
            _clock.Reset();
            Interlocked.Exchange(ref _bypassedCount, 0);
            Interlocked.Exchange(ref _truncatedPayloadCount, 0);
        }

        /// <summary>
        /// Stops accepting inputs and fails the ones already queued.
        ///
        /// Once the application is going down no frame head is coming, so anything still waiting
        /// would wait forever and hold the shutdown open with it.
        /// </summary>
        private static void _CloseGate()
        {
            _gateClosed = true;
            _FaultPending("[RemoteControl] Frame gate closed: the application is shutting down.");
        }

        private static void _FaultPending(string reason)
        {
            var drained = _sequencer.Drain();

            for (int i = 0; i < drained.Count; i++)
            {
                var input = drained[i];
                input.fault?.Invoke(new OperationCanceledException(reason));
                input.Clear();
            }

            drained.Clear();
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void _InitializeInEditor()
        {
            _CaptureMainThread();

            // The player loop hook does not tick while the editor is not playing, but RemoteControl
            // is expected to work there. Without this heartbeat a submitted input would never reach
            // a frame head and its caller would wait forever.
            UnityEditor.EditorApplication.update -= _EditorTick;
            UnityEditor.EditorApplication.update += _EditorTick;
        }

        private static void _EditorTick()
        {
            // During play the player loop hook drives the pump; ticking here too would double it.
            if (Application.isPlaying) return;

            _pumpInstalled = true;
            Pump();
        }
#endif

        private static void _CaptureMainThread()
        {
            _mainThreadContext = SynchronizationContext.Current;
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        // Dedicated PlayerLoop subsystem type so the hook can be identified unambiguously.
        private struct FrameGateUpdate { }

        private static void _InstallPlayerLoopHook()
        {
            var playerLoop = PlayerLoop.GetCurrentPlayerLoop();

            for (int i = 0; i < playerLoop.subSystemList.Length; i++)
            {
                if (playerLoop.subSystemList[i].type != typeof(EarlyUpdate)) continue;

                var early = playerLoop.subSystemList[i];
                var children = early.subSystemList;

                // Guard against a double install (PlayerLoop edits survive a disabled domain reload).
                for (int c = 0; c < children.Length; c++)
                {
                    if (children[c].type == typeof(FrameGateUpdate))
                    {
                        _pumpInstalled = true;
                        return;
                    }
                }

                // Prepended, not appended: the point is that inputs land before anything else in
                // the frame has looked at the state.
                var inserted = new PlayerLoopSystem[children.Length + 1];
                inserted[0] = new PlayerLoopSystem
                {
                    type = typeof(FrameGateUpdate),
                    updateDelegate = _PlayerLoopTick,
                };
                Array.Copy(children, 0, inserted, 1, children.Length);
                early.subSystemList = inserted;
                playerLoop.subSystemList[i] = early;

                PlayerLoop.SetPlayerLoop(playerLoop);
                _pumpInstalled = true;
                return;
            }
        }

        private static void _PlayerLoopTick()
        {
#if UNITY_EDITOR
            // Player loop edits can outlive Play, and a custom subsystem keeps ticking in edit mode
            // once they do. The editor heartbeat owns edit mode, so stand down here rather than
            // pumping the same frame twice and advancing the clock at double rate.
            if (!Application.isPlaying) return;
#endif
            Pump();
        }

        /// <summary>
        /// Applies everything accepted since the last frame, in sequence order, then commits the
        /// frame. Main thread only.
        /// </summary>
        public static void Pump()
        {
            var frameNumber = _clock.Advance();
            var frame = _buffer.BeginFrame(frameNumber, _clock.frameRate);

            var drained = _sequencer.Drain();
            for (int i = 0; i < drained.Count; i++)
            {
                var input = drained[i];

                try
                {
                    input.apply?.Invoke();
                }
                catch (Exception e)
                {
                    // The caller was handed this through its own completion; log it here as well so
                    // a failure nobody awaited is still visible. A group applies as one unit, so
                    // every record it carries is marked.
                    input.SetFlags(InputFlags.Faulted);
                    Debug.LogError($"[RemoteControl] Frame input #{input.firstSequence} ({_DescribeFirst(input)}) failed: {e}");
                }

                for (int r = 0; r < input.recordCount; r++)
                {
                    frame.Add(input.records[r]);
                }

                input.Clear();
            }

            drained.Clear();
            _buffer.Commit(frameNumber);
        }

        /// <summary>
        /// Puts an input in the queue and completes once it has been applied at a frame head.
        /// </summary>
        public static Task<T> SubmitAsync<T>(InputKind kind, string sourceId, string target,
            string payload, Func<T> action)
            => SubmitGroupAsync(new[] { new InputDescriptor(kind, target, payload) }, sourceId, action);

        /// <summary>
        /// Puts several operations in the queue as one unit and completes once they have been
        /// applied together at a frame head.
        ///
        /// For a bundled request, whose parts have to take effect in the same frame. They are
        /// numbered as one run so they cannot be split, but recorded separately so each stays small
        /// enough to be kept faithfully.
        /// </summary>
        public static Task<T> SubmitGroupAsync<T>(IReadOnlyList<InputDescriptor> operations,
            string sourceId, Func<T> action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (operations == null) throw new ArgumentNullException(nameof(operations));
            if (operations.Count == 0) throw new ArgumentException("No operations.", nameof(operations));

            // Refused rather than queued: after shutdown begins no frame head is coming, so a
            // queued input would keep its caller -- and the shutdown -- waiting indefinitely.
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

            return _Enqueue(operations, sourceId, action);
        }

        /// <summary>Single-operation convenience for tests. See <see cref="_Enqueue{T}"/>.</summary>
        internal static Task<T> _Enqueue<T>(InputKind kind, string sourceId, string target,
            string payload, Func<T> action)
            => _Enqueue(new[] { new InputDescriptor(kind, target, payload) }, sourceId, action);

        /// <summary>
        /// Queues an input without the bypass checks. Split out so tests can drive the real queue
        /// and <see cref="Pump"/> from the main thread, where <see cref="SubmitGroupAsync"/> would
        /// deliberately refuse to wait.
        /// </summary>
        internal static Task<T> _Enqueue<T>(IReadOnlyList<InputDescriptor> operations,
            string sourceId, Func<T> action)
        {
            // Continuations run asynchronously so that whatever the caller does after its await --
            // building a response, writing it out -- does not run inside the pump on the main
            // thread. Every input funnels through one point, so inline continuations would pile
            // the whole cost of a frame's callers onto the frame head.
            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

            // Interned here rather than at the frame head: this runs on the worker thread that is
            // going to wait anyway, and the main thread should not pay for it.
            var source = _symbols.Intern(sourceId);
            var records = new InputRecord[operations.Count];

            for (int i = 0; i < operations.Count; i++)
            {
                var operation = operations[i];
                var payload = default(FixedString512Bytes);
                var flags = InputFlags.None;

                if (!string.IsNullOrEmpty(operation.payload) &&
                    payload.CopyFromTruncated(operation.payload) == CopyError.Truncation)
                {
                    flags |= InputFlags.PayloadTruncated;
                    Interlocked.Increment(ref _truncatedPayloadCount);
                }

                // The sequence is stamped by the sequencer, which is where order is decided.
                records[i] = new InputRecord(0, operation.kind, source,
                    _symbols.Intern(operation.target), payload, flags);
            }

            var input = new PendingInput
            {
                records = records,
                recordCount = records.Length,
            };

            input.apply = () =>
            {
                try
                {
                    completion.SetResult(action());
                }
                catch (Exception e)
                {
                    completion.SetException(e);
                    throw;
                }
            };

            input.fault = reason => completion.TrySetException(reason);

            _sequencer.Submit(input);
            return completion.Task;
        }

        private static string _DescribeFirst(PendingInput input)
        {
            if (input.recordCount == 0) return "no records";

            var first = input.records[0];
            var suffix = input.recordCount > 1 ? $" (+{input.recordCount - 1} more)" : string.Empty;
            return $"{first.kind} {_symbols.Resolve(first.targetId)}{suffix}";
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
