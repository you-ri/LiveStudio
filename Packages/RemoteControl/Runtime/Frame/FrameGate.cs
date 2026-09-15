// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;

namespace Lilium.RemoteControl.Frames
{
    /// <summary>
    /// The gate every state-changing event passes through before it reaches the application.
    ///
    /// Two things are added on top of what already happened (hand the work to the main thread and
    /// wait for it):
    ///
    /// - a sequence number, assigned once, so order is settled here rather than by whichever worker
    ///   thread arrived first. Order used to be well-defined on one machine but different on the
    ///   next, which is why two machines fed the same events could not stay together.
    /// - a single application point at the head of a frame, so "the state at frame N" means
    ///   something. Callbacks posted to the synchronisation context run wherever the engine happens
    ///   to pump it, which is not a position anything can be pinned to.
    ///
    /// Both are worth having on their own, before anything is recorded or mirrored.
    ///
    /// A caller still gets its result back, and still gets it only once the event has actually been
    /// applied -- success responses have to stay byte for byte what they were.
    ///
    /// <para>
    /// One class kept in four files by concern: this one numbers, applies and commits;
    /// <c>FrameGate.Time.cs</c> works out the tick and drives the engine clock on a replay;
    /// <c>FrameGate.Supply.cs</c> holds what fills a frame and what reads it back (source, sink,
    /// observers, holds); <c>FrameGate.Submit.cs</c> is how an event gets into the queue.
    /// </para>
    /// </summary>
    public static partial class FrameGate
    {
        /// <summary>Frames retained for read-back. A few frames is enough to absorb jitter.</summary>
        private const int kDefaultBufferFrames = 16;

        private static readonly EventSequencer _sequencer = new EventSequencer();
        private static readonly FrameSymbolTable _symbols = new FrameSymbolTable();

        private static EventFrameBuffer _buffer = new EventFrameBuffer(kDefaultBufferFrames);

        /// <summary>
        /// Number of the frame last committed, so a clock that reports the same position twice does
        /// not get a second frame there. Negative until the first pump.
        /// </summary>
        private static long _lastPumpedFrameNumber = -1;
        private static SynchronizationContext _mainThreadContext;
        private static int _mainThreadId;
        private static bool _pumpInstalled;
        private static volatile bool _gateClosed;
        private static long _bypassedCount;
        private static long _omittedRecordCount;

        // Declared source names, sorted, so interning them assigns the same ids on every reset.
        private static string[] _declaredSources;
        private static volatile bool _declaredInterned;
        private static readonly object _sourceLock = new object();
        private static readonly HashSet<string> _warnedUndeclaredSources = new HashSet<string>();

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

        /// <summary>Frames retained for read-back, indexed by frame number.</summary>
        public static EventFrameBuffer buffer => _buffer;

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
        /// The batch holding the event currently being applied at a frame head, and where in it the
        /// event sits; null outside one. Main thread only, because that is the only thread a frame
        /// head runs on.
        /// </summary>
        private static EventBatch _applyingBatch;

        private static int _applyingIndex;

        /// <summary>
        /// True while an event is being applied at a frame head, which is the only time
        /// <c>StampAppliedPayload</c> has a record to write onto.
        ///
        /// For a caller whose value costs something to work out: outside a frame head the stamp is a
        /// no-op, so the work would be for nothing. The same write reaches this code with the gate
        /// off (an editor write, a direct call), and that path should stay as cheap as it was.
        /// </summary>
        public static bool isApplyingEvent => _applyingBatch != null;

        /// <summary>
        /// Says the event being applied does not need keeping.
        ///
        /// Two callers, for two reasons. A <see cref="FrameLane.State"/> member is already copied
        /// every frame, so a record for it is the same value written twice at 548 bytes a frame. A
        /// <see cref="FrameLane.None"/> member is not part of the live data at all -- a setting of
        /// this machine rather than of the world -- and recording it means a replay reaches over and
        /// changes the operator's language, resolution or quality level.
        ///
        /// Called from inside the apply, by the code that resolved the target and can see which lane
        /// it belongs to. The write still took its place in the order -- that is the half of the gate
        /// this does not touch -- it simply leaves no record.
        /// </summary>
        public static void OmitAppliedRecord(string target)
        {
            if (!_TryFindApplyingRecord(target, out var batch, out var index)) return;

            batch.RecordAt(index).flags |= EventFlags.NotRecorded;
        }

        /// <summary>
        /// Says what value an event actually wrote, so the record keeps the value rather than the
        /// request that asked for it.
        ///
        /// Called from inside the apply of an event, by the code that resolved the target and knows
        /// its type. That is the earliest the type is knowable: at submit time the target is still
        /// a path. Outside a frame head this does nothing, which is what makes it safe to call from
        /// a write path that is also reachable without the gate.
        ///
        /// A string is written as its UTF-8, whole: the value sits in the frame's arena, which has no
        /// ceiling. Anything else with no layout is left alone -- the request text already stands in
        /// for it, and half a value would be worse than the text that produced it.
        ///
        /// Takes the value boxed, for a caller that only has it as an object (a REST body parsed
        /// against a type found at run time). A caller that knows the type uses
        /// <see cref="StampAppliedPayload{T}"/> and boxes nothing.
        /// </summary>
        public static void StampAppliedPayload(string target, Type type, object value)
        {
            if (type == null || value == null) return;
            if (!_TryFindApplyingRecord(target, out var batch, out var index)) return;

            if (type == typeof(string))
            {
                var text = (string)value;
                var bytes = batch.ReservePayload(ref batch.RecordAt(index), EventPayload.ByteCountOf(text),
                    _symbols.Intern(EventPayload.kStringTypeName));

                EventPayload.WriteString(text, bytes);
                return;
            }

            var size = EventPayload.SizeOf(type);
            if (size < 0) return;

            var destination = batch.ReservePayload(ref batch.RecordAt(index), size,
                _symbols.Intern(EventPayload.NameOf(type)));

            EventPayload.TryPack(type, value, destination, out _);
        }

        /// <summary>
        /// Says what value an event actually wrote, for a caller that knows its type. The same as
        /// <see cref="StampAppliedPayload(string, Type, object)"/> without the box.
        /// </summary>
        public static void StampAppliedPayload<T>(string target, in T value) where T : unmanaged
        {
            if (!_TryFindApplyingRecord(target, out var batch, out var index)) return;

            var destination = batch.ReservePayload(ref batch.RecordAt(index), UnsafeUtility.SizeOf<T>(),
                _symbols.Intern(_PayloadName<T>.value));

            EventPayload.Write(in value, destination);
        }

        // The recorded name of a payload type, worked out once per type.
        private static class _PayloadName<T>
        {
            public static readonly string value = EventPayload.NameOf(typeof(T));
        }

        /// <summary>
        /// The record of the event being applied that addresses <paramref name="target"/>. False
        /// outside a frame head, or when the event has no record for it.
        /// </summary>
        private static bool _TryFindApplyingRecord(string target, out EventBatch batch, out int recordIndex)
        {
            batch = _applyingBatch;
            recordIndex = -1;
            if (batch == null || string.IsNullOrEmpty(target)) return false;

            var targetId = _symbols.Intern(target);
            ref var evt = ref batch[_applyingIndex];

            for (int i = 0; i < evt.recordCount; i++)
            {
                if (batch.RecordAt(evt.firstRecord + i).targetId != targetId) continue;

                recordIndex = evt.firstRecord + i;
                return true;
            }

            return false;
        }

        /// <summary>
        /// The strings behind the ids in the records. A recording writes this into its
        /// header, and nothing can be read back out of a frame without it.
        /// </summary>
        public static FrameSymbolTable symbols => _symbols;

        /// <summary>
        /// Writes applied but left out of the frame because the state lane carries them. Counted so
        /// "the recording has no event for this" can be told from "the event went missing".
        /// </summary>
        public static long omittedRecordCount => Interlocked.Read(ref _omittedRecordCount);

        /// <summary>True once a frame-head pump is running and events are being ordered.</summary>
        public static bool isGateRunning => _pumpInstalled;

        /// <summary>
        /// Events that had to be applied without passing through a frame head, because no pump was
        /// running or the caller was already on the main thread. Exposed rather than silent: each
        /// one is a hole in the ordering, and a run with holes cannot be replayed faithfully.
        /// </summary>
        public static long bypassedCount => Interlocked.Read(ref _bypassedCount);

        /// <summary>Sequence number the next accepted event will get.</summary>
        public static long nextSequence => _sequencer.nextSequence;

        /// <summary>
        /// Resizes the retained window. Drops what is currently held. Main thread only, and not
        /// between the start and the commit of a frame: the replacement would be committed as
        /// holding a frame it never received.
        /// </summary>
        public static void SetBufferFrames(int frameCapacity)
        {
            var replaced = _buffer;
            _buffer = new EventFrameBuffer(frameCapacity);

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
        /// Runs at the head of every frame, after that frame's events have been applied and before
        /// the frame is committed. This is where state-lane producers write their block: the order
        /// is event then state, because an event can change the structure and the container has to
        /// exist before values go into it.
        ///
        /// Main thread only. A handler that throws is logged and the rest still run -- one
        /// misbehaving producer must not stop events from being applied.
        ///
        /// <para>
        /// Handlers run in the order they were added, and <paramref name="first"/> is how the one
        /// that has to precede the others says so rather than relying on whoever installs it doing
        /// so early enough. The inventory needs it: applying a supplied frame's structure creates
        /// and destroys the objects the state is addressed to, so it has to happen before any
        /// producer writes a value -- and that ordering was a property of two Retain calls in a
        /// method nobody would think to read.
        /// </para>
        /// </summary>
        public static void AddFrameHeadHandler(FrameHeadDelegate handler, bool first = false)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (_frameHeadHandlers.Contains(handler)) return;

            if (first) _frameHeadHandlers.Insert(0, handler);
            else _frameHeadHandlers.Add(handler);

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
        /// Resolves the source of an event submitted by name. An undeclared name is filed under
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
            ResetState("[RemoteControl] Frame gate restarted before this evt reached a frame.");

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
        /// Queued events are handed their failure rather than dropped. A caller is blocked on the
        /// frame head its event was going to reach, so discarding one silently leaves that caller
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
            _lastPumpedFrameNumber = -1;
            _tickFrameNumber = long.MinValue;
            _tickSupplied = false;
            _tickSource = null;
            _tickFrameRate = default;
            _ReleaseEngineTime();
            _engineTimeRefused = false;

            // Holds belong to the run that took them. Carried across a reset they would stall the
            // next replay against a load that finished long ago.
            Interlocked.Exchange(ref _supplyHolds, 0);
            Interlocked.Exchange(ref _heldFrameCount, 0);
            _supplyHoldReason = null;
            _structure.Reset();
            _state.Reset();

            // Left attached across a reset would mean a recording quietly spanning two runs.
            _sink = null;
            _source = null;
            onSourceEnded = null;

            // Cleared so a new run reports its undeclared sources again rather than staying quiet
            // about them because a previous run already mentioned them.
            lock (_sourceLock) _warnedUndeclaredSources.Clear();

            Interlocked.Exchange(ref _bypassedCount, 0);
            Interlocked.Exchange(ref _omittedRecordCount, 0);

            // Reset with the other diagnostics even though the observers themselves stay attached:
            // the count says how much went quiet during this run, and carrying it over would report
            // a previous run's losses against a run that has not lost anything.
            _detachedObserverCount = 0;
        }

        /// <summary>
        /// Stops accepting events and fails the ones already queued.
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
            var batch = _sequencer.Drain();

            for (int i = 0; i < batch.Count; i++)
            {
                ref var evt = ref batch[i];

                if (evt.completion != null)
                {
                    evt.completion.Fault(new OperationCanceledException(reason));
                    continue;
                }

                // Nobody is waiting on a posted event, so there is nobody to hand the failure to.
                // Logged instead of dropped: an operation that silently stopped landing is the kind
                // of quiet this whole layer exists to prevent.
                Debug.LogWarning(
                    $"[RemoteControl] Posted event ({_DescribeFirst(batch, in evt)}) never reached a frame: {reason}");
            }

            batch.Clear();
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void _InitializeInEditor()
        {
            _CaptureMainThread();

            // The player loop hook does not tick while the editor is not playing, but RemoteControl
            // is expected to work there. Without this heartbeat a submitted event would never reach
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

            // No throttle here. The editor updates at its own pace -- measured at nearly 600 a
            // second with a window repainting -- and the clock is what decides which of those are
            // frames: it reports the same position for every update inside one interval, and Pump
            // skips a position it has already committed.
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

                // Prepended, not appended: the point is that events land before anything else in
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
            _PumpAtFrameHead();
        }

        /// <summary>
        /// Applies everything accepted since the last frame, in sequence order, then commits the
        /// frame. Main thread only.
        /// </summary>
        public static void Pump()
        {
            // Whoever calls this is the pump. The flag decides whether a posted event is queued for
            // a frame head or applied on the spot as a hole in the ordering, and the question it
            // answers is "will anything come along and apply this" -- a caller of Pump demonstrably
            // will. Set here as well as at install time so a host driving the gate from its own loop
            // does not have to say so twice, and so a reset does not leave the gate looking dead
            // while something is still pumping it.
            _pumpInstalled = true;

            var frameNumber = _clock.Advance();

            // The same number again means no new frame is due: the rate is the resolution of the
            // time axis, and two pumps inside one interval are two moments at one position.
            // Committing a second frame there would overwrite the first -- the events it carried
            // included -- so the pump is skipped and whatever arrived waits for the next interval.
            if (frameNumber == _lastPumpedFrameNumber) return;
            _lastPumpedFrameNumber = frameNumber;

            var events = _buffer.BeginFrame(frameNumber, _clock.frameRate);

            _frame.frameNumber = frameNumber;
            _frame.frameRate = _clock.frameRate;
            _frame.structure = _structure;
            _frame.state = _state;
            _frame.events = events;
            _frame.isSupplied = false;

            // This run's table, unless a source replaces it along with the lanes below.
            _frame.symbols = _symbols;

            // Before the queued events, so what the recording asked for lands first and an operator
            // acting right now lands on top of it rather than under it.
            _FillFromSource();

            // After the source, because a supplied frame brings its own number and rate: the tick a
            // replay hands out is the recorded one, not this machine's.
            _UpdateTick();

            var batch = _sequencer.Drain();
            for (int i = 0; i < batch.Count; i++)
            {
                ref var evt = ref batch[i];

                try
                {
                    // Published while the event runs so the code that applies it can say what value
                    // it wrote. Records are added to the lane below, after this, so a stamp made
                    // here is part of what gets recorded.
                    _applyingBatch = batch;
                    _applyingIndex = i;

                    if (evt.completion != null) evt.completion.Run();
                    else evt.apply?.Invoke();
                }
                catch (Exception e)
                {
                    // The caller was handed this through its own completion; log it here as well so
                    // a failure nobody awaited is still visible. A group applies as one unit, so
                    // every record it carries is marked.
                    for (int r = 0; r < evt.recordCount; r++)
                    {
                        batch.RecordAt(evt.firstRecord + r).flags |= EventFlags.Faulted;
                    }

                    Debug.LogError(
                        $"[RemoteControl] Frame event #{batch.RecordAt(evt.firstRecord).sequence} ({_DescribeFirst(batch, in evt)}) failed: {e}");
                }
                finally
                {
                    _applyingBatch = null;
                }

                for (int r = 0; r < evt.recordCount; r++)
                {
                    ref var record = ref batch.RecordAt(evt.firstRecord + r);

                    // Applied above, but the state lane is already carrying what it did. See
                    // EventFlags.NotRecorded.
                    if ((record.flags & EventFlags.NotRecorded) != 0)
                    {
                        Interlocked.Increment(ref _omittedRecordCount);
                        continue;
                    }

                    events.Add(in record, batch.PayloadOf(in record));
                }
            }

            batch.Clear();

            // State after event: an event can change the structure, and a state block is only
            // meaningful against the structure it belongs to.
            _RunFrameHeadHandlers();

            // After the producers, so what the sink sees is the finished frame rather than half of
            // one, and before the commit, so a sink that throws cannot leave the frame unpublished.
            _DeliverToSink();

            // The same point, for the same reason: a watcher wants the frame that was, not half of
            // it. After the sink, so the one that owns the frame gets it first.
            _NotifyObservers();

            // Dropped so a handler that stashed the frame cannot reach a slot that is about to be
            // handed to a later frame.
            _frame.events = null;

            _buffer.Commit(frameNumber);
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
    }
}
