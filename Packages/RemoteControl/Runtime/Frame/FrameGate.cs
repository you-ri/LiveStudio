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
        private static long _repeatedWriteCount;
        private static int _lastRepeatedTargetId = InputSymbolTable.kNone;

        // Declared source names, sorted, so interning them assigns the same ids on every reset.
        private static string[] _declaredSources;
        private static volatile bool _declaredInterned;
        private static readonly object _sourceLock = new object();
        private static readonly HashSet<string> _warnedUndeclaredSources = new HashSet<string>();

        // Writes already seen this frame, keyed by (source, target). Reused across frames so the
        // duplicate check costs nothing after the first frame.
        private static readonly HashSet<long> _writeKeysThisFrame = new HashSet<long>();

        private static readonly List<FrameHeadDelegate> _frameHeadHandlers = new List<FrameHeadDelegate>();
        private static FrameHeadDelegate[] _frameHeadSnapshot = Array.Empty<FrameHeadDelegate>();
        private static bool _frameHeadSnapshotStale;

        // The frame currently being built. Kept as a field rather than a local so it can be handed
        // out by reference without the callee's writes landing in a copy.
        private static Frame _frame;

        // Carried from frame to frame: together these are the current state of the world, so they
        // are created once and reset only when a run restarts.
        private static readonly StructureBlock _structure = new StructureBlock();
        private static readonly StateBlockSet _state = new StateBlockSet();

        private static IFrameSink _sink;

        /// <summary>Frames retained for read-back, indexed by frame number.</summary>
        public static InputFrameBuffer buffer => _buffer;

        /// <summary>
        /// Shape of the world: what exists and how many. Carried across frames, and handed to
        /// producers as part of <see cref="Frame"/> at each head.
        /// </summary>
        public static StructureBlock structure => _structure;

        /// <summary>
        /// Values of the world, one dense array per element type. Carried across frames -- an
        /// element that stops being written keeps its last value.
        /// </summary>
        public static StateBlockSet state => _state;

        /// <summary>
        /// Where completed frames go: a recorder, a mirror sender, or nothing.
        ///
        /// One at a time. Two consumers at once is a fan-out sink's job rather than a list here --
        /// the gate should not be the place that decides what order they run in.
        /// </summary>
        public static IFrameSink sink
        {
            get => _sink;
            set => _sink = value;
        }

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
        /// Writes that landed in the same frame as an earlier write to the same target from the same
        /// source. Nothing is dropped -- the record stays exact -- but a target that keeps showing up
        /// here is one that should be declared <see cref="FrameLane.State"/> instead.
        ///
        /// Coalescing these away was considered and rejected: it would be the only lossy step in the
        /// design (a live run fires N callbacks where a replay would fire one), and the sending side
        /// already coalesces, so at sixty writes a second on a sixty-hertz frame there is usually
        /// nothing to fold.
        /// </summary>
        public static long repeatedWriteCount => Interlocked.Read(ref _repeatedWriteCount);

        /// <summary>
        /// The target most recently counted by <see cref="repeatedWriteCount"/>, so the number can be
        /// acted on. Empty until one is seen.
        /// </summary>
        public static string lastRepeatedTarget => _symbols.Resolve(Volatile.Read(ref _lastRepeatedTargetId));

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
            var replaced = _buffer;
            _buffer = new InputFrameBuffer(frameCapacity);

            // Released after the swap, so nothing is reading the old one through the property while
            // its storage goes away.
            replaced?.Dispose();
        }

        /// <summary>
        /// Every source name declared with <see cref="FrameSourceAttribute"/>, sorted, with
        /// <see cref="FrameSource.kUnknown"/> first. This is what a recording writes into its header
        /// as the set of sources that took part.
        /// </summary>
        public static IReadOnlyList<string> declaredSources => _DeclaredSources();

        /// <summary>
        /// Looks up a declared source. Throws if the name was never declared, which is the point:
        /// a misspelling fails here rather than becoming a second source nobody notices.
        /// </summary>
        public static FrameSource ResolveSource(string name)
        {
            if (TryResolveSource(name, out var source)) return source;

            throw new ArgumentException(
                $"[RemoteControl] Input source '{name}' is not declared. " +
                $"Add [assembly: FrameSource(\"{name}\")] to the assembly that submits it.",
                nameof(name));
        }

        /// <summary>Looks up a declared source without throwing.</summary>
        public static bool TryResolveSource(string name, out FrameSource source)
        {
            source = default;
            if (string.IsNullOrEmpty(name)) return false;

            // Declared names are interned as a block before any of them is handed out, so their ids
            // are the same on every run. Resolving one first would give it whichever id happened to
            // be free, and a FrameSource cached in a static field would then point at another name
            // after the next reset.
            _EnsureDeclaredInterned();

            var declared = _DeclaredSources();
            for (int i = 0; i < declared.Length; i++)
            {
                if (!string.Equals(declared[i], name, StringComparison.Ordinal)) continue;

                source = new FrameSource(_symbols.Intern(name));
                return true;
            }

            return false;
        }

        /// <summary>
        /// Runs at the head of every frame, after that frame's inputs have been applied and before
        /// the frame is committed. This is where state-lane producers write their block: the order
        /// is input then state, because an input can change the structure and the container has to
        /// exist before values go into it.
        ///
        /// Main thread only. A handler that throws is logged and the rest still run -- one
        /// misbehaving producer must not stop inputs from being applied.
        /// </summary>
        public static void AddFrameHeadHandler(FrameHeadDelegate handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (_frameHeadHandlers.Contains(handler)) return;

            _frameHeadHandlers.Add(handler);
            _frameHeadSnapshotStale = true;
        }

        /// <summary>Removes a handler added by <see cref="AddFrameHeadHandler"/>.</summary>
        public static void RemoveFrameHeadHandler(FrameHeadDelegate handler)
        {
            if (handler == null) return;
            if (!_frameHeadHandlers.Remove(handler)) return;

            _frameHeadSnapshotStale = true;
        }

        private static string[] _DeclaredSources()
        {
            var cached = Volatile.Read(ref _declaredSources);
            if (cached != null) return cached;

            lock (_sourceLock)
            {
                if (_declaredSources != null) return _declaredSources;

                var names = new SortedSet<string>(StringComparer.Ordinal);

                foreach (var assembly in Reflection.AssemblyUtility.GetLoadedAssemblies())
                {
                    foreach (FrameSourceAttribute attribute in
                        assembly.GetCustomAttributes(typeof(FrameSourceAttribute), false))
                    {
                        if (string.IsNullOrEmpty(attribute.name)) continue;
                        if (string.Equals(attribute.name, FrameSource.kUnknown, StringComparison.Ordinal)) continue;

                        names.Add(attribute.name);
                    }
                }

                // Unknown first, then the declared names in a fixed order, so interning them assigns
                // the same ids after every reset. Without that, a FrameSource resolved into a static
                // field would point at a different name once the gate restarted.
                var ordered = new string[names.Count + 1];
                ordered[0] = FrameSource.kUnknown;
                names.CopyTo(ordered, 1);

                Volatile.Write(ref _declaredSources, ordered);
                return ordered;
            }
        }

        private static void _InternDeclaredSources()
        {
            var declared = _DeclaredSources();
            for (int i = 0; i < declared.Length; i++) _symbols.Intern(declared[i]);
            _declaredInterned = true;
        }

        private static void _EnsureDeclaredInterned()
        {
            if (_declaredInterned) return;

            lock (_sourceLock)
            {
                if (_declaredInterned) return;
                _InternDeclaredSources();
            }
        }

        /// <summary>
        /// Resolves the source of an input submitted by name. An undeclared name is filed under
        /// <see cref="FrameSource.kUnknown"/> and reported once, so a caller not yet migrated keeps
        /// working instead of failing, but does not disappear quietly either.
        /// </summary>
        private static int _ResolveSourceId(string sourceId)
        {
            _EnsureDeclaredInterned();

            if (TryResolveSource(sourceId, out var source)) return source.id;

            bool first;
            lock (_sourceLock)
            {
                first = _warnedUndeclaredSources.Add(sourceId ?? string.Empty);
            }

            if (first)
            {
                Debug.LogWarning(
                    $"[RemoteControl] Input source '{sourceId}' is not declared and is recorded as " +
                    $"'{FrameSource.kUnknown}'. Add [assembly: FrameSource(\"{sourceId}\")] to declare it.");
            }

            return _symbols.Intern(FrameSource.kUnknown);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void _InitializeOnPlay()
        {
            // Domain-reload-disabled safe: every static carrying run state is reset here.
            ResetState("[RemoteControl] Frame gate restarted before this input reached a frame.");

            _CaptureMainThread();
            _InstallPlayerLoopHook();

            Application.quitting -= _ReleaseNativeStorage;
            Application.quitting += _ReleaseNativeStorage;

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
            _declaredInterned = false;

            // Re-interned straight after the wipe and in a fixed order, so a FrameSource held in a
            // static field still points at the name it was resolved from.
            _InternDeclaredSources();

            _buffer.Reset();
            _clock.Reset();
            _structure.Reset();
            _state.Reset();

            // Left attached across a reset would mean a recording quietly spanning two runs.
            _sink = null;
            _writeKeysThisFrame.Clear();

            // Cleared so a new run reports its undeclared sources again rather than staying quiet
            // about them because a previous run already mentioned them.
            lock (_sourceLock) _warnedUndeclaredSources.Clear();

            Interlocked.Exchange(ref _bypassedCount, 0);
            Interlocked.Exchange(ref _truncatedPayloadCount, 0);
            Interlocked.Exchange(ref _repeatedWriteCount, 0);
            Volatile.Write(ref _lastRepeatedTargetId, InputSymbolTable.kNone);
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

            // Statics survive a domain reload only as far as their managed side; the native storage
            // behind them would be reported as a leak. Released here and rebuilt on next use.
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= _ReleaseNativeStorage;
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += _ReleaseNativeStorage;
        }

        private static void _EditorTick()
        {
            // During play the player loop hook drives the pump; ticking here too would double it.
            if (Application.isPlaying) return;

            _pumpInstalled = true;
            Pump();
        }
#endif

        /// <summary>
        /// Frees the structure and state storage. They allocate again on next use, so this is a
        /// release rather than a teardown -- what it prevents is native memory outliving the domain
        /// that was holding the only reference to it.
        /// </summary>
        internal static void _ReleaseNativeStorage()
        {
            _structure.Dispose();
            _state.Dispose();
            _buffer.Dispose();
        }

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
            var inputs = _buffer.BeginFrame(frameNumber, _clock.frameRate);

            _frame.frameNumber = frameNumber;
            _frame.frameRate = _clock.frameRate;
            _frame.structure = _structure;
            _frame.state = _state;
            _frame.inputs = inputs;

            _writeKeysThisFrame.Clear();

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
                    _CountIfRepeatedWrite(in input.records[r]);
                    inputs.Add(input.records[r]);
                }

                input.Clear();
            }

            drained.Clear();

            // State after input: an input can change the structure, and a state block is only
            // meaningful against the structure it belongs to.
            _RunFrameHeadHandlers();

            // After the producers, so what the sink sees is the finished frame rather than half of
            // one, and before the commit, so a sink that throws cannot leave the frame unpublished.
            _DeliverToSink();

            // Dropped so a handler that stashed the frame cannot reach a slot that is about to be
            // handed to a later frame.
            _frame.inputs = null;

            _buffer.Commit(frameNumber);
        }

        /// <summary>
        /// Notes a write that follows an earlier write to the same target in this frame. The record
        /// is kept either way; the count is a signal that the target belongs in the state lane.
        /// </summary>
        private static void _CountIfRepeatedWrite(in InputRecord record)
        {
            if (record.kind != InputKind.PropertyWrite) return;
            if (record.targetId == InputSymbolTable.kNone) return;

            var key = ((long)record.sourceId << 32) | (uint)record.targetId;
            if (_writeKeysThisFrame.Add(key)) return;

            Interlocked.Increment(ref _repeatedWriteCount);
            Volatile.Write(ref _lastRepeatedTargetId, record.targetId);
        }

        private static void _DeliverToSink()
        {
            var sink = _sink;
            if (sink == null) return;

            try
            {
                sink.OnFrameCompleted(in _frame, _symbols);
            }
            catch (Exception e)
            {
                // Detached rather than left to throw every frame: a recorder that has lost its disk
                // would otherwise turn one failure into one per frame, and the run itself is still
                // fine without it.
                _sink = null;
                Debug.LogError($"[RemoteControl] Frame sink failed and was detached: {e}");
            }
        }

        private static void _RunFrameHeadHandlers()
        {
            if (_frameHeadSnapshotStale)
            {
                _frameHeadSnapshot = _frameHeadHandlers.ToArray();
                _frameHeadSnapshotStale = false;
            }

            var handlers = _frameHeadSnapshot;
            for (int i = 0; i < handlers.Length; i++)
            {
                try
                {
                    handlers[i](ref _frame);
                }
                catch (Exception e)
                {
                    // Taken over a rethrow: one producer failing must not stop the others or leave
                    // the frame uncommitted, which would strand every caller waiting on it.
                    Debug.LogError($"[RemoteControl] Frame head handler failed: {e}");
                }
            }
        }

        /// <summary>
        /// Puts an input in the queue and completes once it has been applied at a frame head.
        /// </summary>
        public static Task<T> SubmitAsync<T>(InputKind kind, string sourceId, string method,
            string target, string payload, Func<T> action)
            => SubmitGroupAsync(new[] { new InputDescriptor(kind, method, target, payload) }, sourceId, action);

        /// <summary>
        /// Puts an input in the queue on behalf of a source resolved once with
        /// <see cref="ResolveSource"/>. Preferred over the string form: the name has already been
        /// checked against a declaration and interned, so nothing is hashed per call.
        /// </summary>
        public static Task<T> SubmitAsync<T>(InputKind kind, FrameSource source, string method,
            string target, string payload, Func<T> action)
            => SubmitGroupAsync(new[] { new InputDescriptor(kind, method, target, payload) }, source, action);

        /// <summary>Group form of <see cref="SubmitAsync{T}(InputKind, FrameSource, string, string, Func{T})"/>.</summary>
        public static Task<T> SubmitGroupAsync<T>(IReadOnlyList<InputDescriptor> operations,
            FrameSource source, Func<T> action)
        {
            if (!source.isValid)
            {
                throw new ArgumentException(
                    "[RemoteControl] Input source was never resolved. Use FrameGate.ResolveSource.",
                    nameof(source));
            }

            return _SubmitGroup(operations, source.id, action);
        }

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
            // Resolved before the bypass checks so that an undeclared name is reported even when the
            // input never reaches the queue.
            return _SubmitGroup(operations, _ResolveSourceId(sourceId), action);
        }

        private static Task<T> _SubmitGroup<T>(IReadOnlyList<InputDescriptor> operations,
            int sourceId, Func<T> action)
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
            => _Enqueue(new[] { new InputDescriptor(kind, "PUT", target, payload) },
                _ResolveSourceId(sourceId), action);

        /// <summary>Group convenience for tests, resolving the source by name.</summary>
        internal static Task<T> _Enqueue<T>(IReadOnlyList<InputDescriptor> operations,
            string sourceId, Func<T> action)
            => _Enqueue(operations, _ResolveSourceId(sourceId), action);

        /// <summary>
        /// Queues an input without the bypass checks. Split out so tests can drive the real queue
        /// and <see cref="Pump"/> from the main thread, where <see cref="SubmitGroupAsync"/> would
        /// deliberately refuse to wait.
        /// </summary>
        internal static Task<T> _Enqueue<T>(IReadOnlyList<InputDescriptor> operations,
            int source, Func<T> action)
        {
            // Continuations run asynchronously so that whatever the caller does after its await --
            // building a response, writing it out -- does not run inside the pump on the main
            // thread. Every input funnels through one point, so inline continuations would pile
            // the whole cost of a frame's callers onto the frame head.
            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

            // Targets are interned here rather than at the frame head: this runs on the worker
            // thread that is going to wait anyway, and the main thread should not pay for it.
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
                    _symbols.Intern(operation.target), payload, flags,
                    _symbols.Intern(operation.method));
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
