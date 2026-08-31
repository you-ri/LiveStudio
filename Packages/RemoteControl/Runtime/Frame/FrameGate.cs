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
    /// </summary>
    public static class FrameGate
    {
        /// <summary>Frames retained for read-back. A few frames is enough to absorb jitter.</summary>
        private const int kDefaultBufferFrames = 16;

        private static readonly EventSequencer _sequencer = new EventSequencer();
        private static readonly FrameSymbolTable _symbols = new FrameSymbolTable();

        private static EventFrameBuffer _buffer = new EventFrameBuffer(kDefaultBufferFrames);
        private static IFrameClock _clock = _NewDefaultClock();

        // Frame number the tick was last worked out for, and the width it came to. long.MinValue
        // rather than -1: a frame number is a position on a time axis and negative ones are legal.
        private static long _tickFrameNumber = long.MinValue;
        private static double _deltaTime;

        private static bool _driveEngineTime;
        private static bool _engineTimeDriven;

        private static int _supplyHolds;
        private static long _heldFrameCount;
        private static volatile string _supplyHoldReason;

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
        private static long _truncatedPayloadCount;
        private static long _omittedRecordCount;
        private static long _repeatedWriteCount;
        private static int _lastRepeatedTargetId = FrameSymbolTable.kNone;

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

        private static readonly List<IFrameObserver> _observers = new List<IFrameObserver>();
        private static IFrameObserver[] _observerSnapshot = Array.Empty<IFrameObserver>();
        private static bool _observerSnapshotStale;
        private static int _detachedObserverCount;

        // The frame currently being built. Kept as a field rather than a local so it can be handed
        // out by reference without the callee's writes landing in a copy.
        private static Frame _frame;

        // Carried from frame to frame: together these are the current state of the world, so they
        // are created once and reset only when a run restarts.
        private static readonly StructureBlock _structure = new StructureBlock();
        private static readonly StateBlockSet _state = new StateBlockSet();

        private static IFrameSink _sink;
        private static IFrameSource _source;

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
        /// Where frames come from when they are not being produced here: a recording being played,
        /// or another machine being followed. Null for an ordinary live run.
        ///
        /// One at a time, for the same reason as <see cref="sink"/>. Retired automatically when it
        /// runs out, which raises <see cref="onSourceEnded"/>.
        /// </summary>
        public static IFrameSource source
        {
            get => _source;
            set => _source = value;
        }

        /// <summary>
        /// Raised on the main thread when a source runs out and is detached, so whoever attached it
        /// can put the run back the way it was. Not raised when a source is cleared by hand -- the
        /// caller doing that already knows.
        /// </summary>
        public static event Action onSourceEnded;

        /// <summary>Observers watching frames go by. For diagnostics.</summary>
        public static int observerCount => _observers.Count;

        /// <summary>
        /// Observers dropped for throwing, since the run started. Not zero means something that was
        /// watching has stopped, which a viewer has to say out loud rather than just going quiet.
        /// </summary>
        public static int detachedObserverCount => _detachedObserverCount;

        /// <summary>
        /// Starts watching frames. Idempotent, and safe to call from inside a notification.
        ///
        /// Unlike <see cref="sink"/> there can be any number, and unlike <see cref="source"/> they
        /// survive <see cref="ResetState"/>: watching is not owning, and a watcher that quietly
        /// stopped at the start of a run is exactly the kind of silence this is here to find.
        /// </summary>
        public static void AddFrameObserver(IFrameObserver observer)
        {
            if (observer == null) throw new ArgumentNullException(nameof(observer));
            if (_observers.Contains(observer)) return;

            _observers.Add(observer);
            _observerSnapshotStale = true;
        }

        /// <summary>
        /// The event currently being applied at a frame head, or null outside one. Main thread only,
        /// because that is the only thread a frame head runs on.
        /// </summary>
        private static PendingEvent _applyingEvent;

        /// <summary>
        /// Says the event being applied does not need keeping, because the state lane carries what
        /// it wrote.
        ///
        /// Called from inside the apply, by the code that resolved the target and can see which
        /// lane it belongs to. The write still took its place in the order -- that is the half of
        /// the gate this does not touch -- it simply leaves no record, because a record saying what
        /// the state lane already says is the same value written twice at 548 bytes a frame.
        /// </summary>
        public static void OmitAppliedRecord(string target)
        {
            var evt = _applyingEvent;
            if (evt == null || string.IsNullOrEmpty(target)) return;

            var targetId = _symbols.Intern(target);

            for (int i = 0; i < evt.recordCount; i++)
            {
                if (evt.records[i].targetId != targetId) continue;

                evt.records[i].flags |= EventFlags.NotRecorded;
                return;
            }
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
        /// A string is written length first, inline. A property that declares a maximum is a
        /// FixedString, which is unmanaged and packs at its own width like any other value.
        /// Anything else with no layout is left alone -- the request text already stands in for it,
        /// and half a value would be worse than the text that produced it.
        /// </summary>
        public static void StampAppliedPayload(string target, Type type, object value)
        {
            var evt = _applyingEvent;
            if (evt == null || type == null || value == null || string.IsNullOrEmpty(target)) return;

            Span<byte> packed = stackalloc byte[EventRecord.kPayloadCapacity];
            int written;
            string typeName;

            var fitted = true;

            if (type == typeof(string))
            {
                fitted = EventPayload.TryWriteString((string)value, packed, out written);
                typeName = EventPayload.kStringTypeName;
            }
            else
            {
                if (!EventPayload.TryPack(type, value, packed, out written)) return;
                typeName = EventPayload.NameOf(type);
            }

            var targetId = _symbols.Intern(target);
            var typeId = _symbols.Intern(typeName);

            for (int i = 0; i < evt.recordCount; i++)
            {
                if (evt.records[i].targetId != targetId) continue;

                evt.records[i].SetPayload(packed.Slice(0, written), typeId);

                // The mark belongs to what is in the record now, not to the request text this
                // replaced. A laid-out value is written at its own width and always fits; only a
                // string long enough to overrun the record can still be short.
                if (fitted)
                {
                    evt.records[i].flags &= ~EventFlags.PayloadTruncated;
                }
                else
                {
                    evt.records[i].flags |= EventFlags.PayloadTruncated;
                    Interlocked.Increment(ref _truncatedPayloadCount);
                }

                return;
            }
        }

        /// <summary>Stops watching. Safe to call from inside a notification.</summary>
        public static void RemoveFrameObserver(IFrameObserver observer)
        {
            if (observer == null) return;
            if (!_observers.Remove(observer)) return;

            _observerSnapshotStale = true;
        }

        /// <summary>
        /// The strings behind the ids in the records. A recording writes this into its
        /// header, and nothing can be read back out of a frame without it.
        /// </summary>
        public static FrameSymbolTable symbols => _symbols;

        /// <summary>
        /// Events whose payload did not fit in a record and was cut short. They applied correctly,
        /// but what was kept of them cannot be replayed faithfully.
        /// </summary>
        public static long truncatedPayloadCount => Interlocked.Read(ref _truncatedPayloadCount);

        /// <summary>
        /// Writes applied but left out of the frame because the state lane carries them. Counted so
        /// "the recording has no event for this" can be told from "the event went missing".
        /// </summary>
        public static long omittedRecordCount => Interlocked.Read(ref _omittedRecordCount);

        /// <summary>Supplies the frame number stamped on each committed frame.</summary>
        public static IFrameClock clock => _clock;

        /// <summary>
        /// Holds a replay where it is until something it needs has finished.
        ///
        /// A recording carries the write that asked for an avatar, not the avatar: replaying it
        /// starts a load that takes as long as this machine takes, and the frames behind it address
        /// an object that is not there yet. Waiting is what keeps the take honest -- the alternative
        /// is a replay whose fidelity depends on disk speed.
        ///
        /// Only supplied frames are held. A live run has nothing to wait for, so a holder may take
        /// one out whenever it starts loading without asking whether a replay is running.
        ///
        /// Balanced by <see cref="ReleaseSupply"/>, and counted: two loads at once are two holds,
        /// and the replay goes on when the last of them is done.
        /// </summary>
        public static void HoldSupply(string reason)
        {
            Interlocked.Increment(ref _supplyHolds);
            _supplyHoldReason = reason;
        }

        /// <summary>Gives back a hold taken by <see cref="HoldSupply"/>.</summary>
        public static void ReleaseSupply(string reason)
        {
            if (Interlocked.Decrement(ref _supplyHolds) >= 0) return;

            // Never below zero: an unbalanced release would let the next hold be cancelled by it,
            // and a replay would then run past the load it was told to wait for.
            Interlocked.Exchange(ref _supplyHolds, 0);
            Debug.LogWarning($"[RemoteControl] Frame supply released more times than held ('{reason}').");
        }

        /// <summary>How many things a replay is currently waiting on.</summary>
        public static int supplyHoldCount => Interlocked.CompareExchange(ref _supplyHolds, 0, 0);

        /// <summary>What was most recently waited on, for a viewer to show while a replay stalls.</summary>
        public static string supplyHoldReason => _supplyHoldReason;

        /// <summary>
        /// Frames a replay stood still for, waiting. Not an error -- but a replay that spends most
        /// of its frames here is one whose timing no longer resembles the take.
        /// </summary>
        public static long heldFrameCount => Interlocked.Read(ref _heldFrameCount);

        /// <summary>
        /// Seconds the frame just committed covers: the distance from the previous one, worked out
        /// from the frame numbers and the rate.
        ///
        /// **Anything that advances with time reads this rather than <c>Time.deltaTime</c>.** The
        /// engine's tick is whatever this machine managed to render, so two machines fed the same
        /// events integrate different amounts and drift apart -- which is the single largest source
        /// of that drift. This is the recorded width of the step, identical on every machine
        /// replaying the same take.
        ///
        /// A skipped frame number widens it rather than being hidden: the pump missed an interval,
        /// and time really did pass. A step to somewhere else in a take (a scrub) is one interval,
        /// because a seek is not a duration anything should integrate over.
        /// </summary>
        public static float deltaTime => (float)_deltaTime;

        /// <summary>
        /// Whether a supplied frame also drives the engine's clock (<c>Time.captureDeltaTime</c>).
        ///
        /// Off by default, and turned on by whoever is actually replaying. With it on, code that
        /// still reads <c>Time.deltaTime</c> follows the recording without being touched -- which is
        /// what makes a replay of existing code reproducible, and what a faster-than-real or a
        /// slower-than-real redraw runs on. A viewer merely watching frames go by leaves it off:
        /// stepping the engine's clock would stop the application it is being watched in from
        /// running at its own speed.
        /// </summary>
        public static bool driveEngineTimeOnSuppliedFrames
        {
            get => _driveEngineTime;
            set
            {
                _driveEngineTime = value;
                if (!value) _ReleaseEngineTime();
            }
        }

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
            _lastPumpedFrameNumber = -1;
            _buffer.Reset();
        }

        /// <summary>
        /// Puts the clock back to the one a live run uses.
        ///
        /// The clock is process-wide, so anything that installs one for its own purposes -- a test
        /// driving the pump by hand, a tool stepping through a recording -- hands the rest of the
        /// editor session whatever it left behind. A counter clock left in place makes the timecode
        /// read as fast as the editor happens to tick, which is how it once ran ten times fast.
        ///
        /// Deliberately not done by <see cref="ResetState"/>: a restart must not throw away an
        /// external sync source someone installed on purpose.
        /// </summary>
        public static void RestoreDefaultClock() => SetClock(_NewDefaultClock());

        /// <summary>The clock a live run is driven by, unless something replaced it.</summary>
        private static IFrameClock _NewDefaultClock() => new RealtimeFrameClock(FrameRate.FPS60);

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
            _ReleaseEngineTime();

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
            _writeKeysThisFrame.Clear();

            // Cleared so a new run reports its undeclared sources again rather than staying quiet
            // about them because a previous run already mentioned them.
            lock (_sourceLock) _warnedUndeclaredSources.Clear();

            Interlocked.Exchange(ref _bypassedCount, 0);
            Interlocked.Exchange(ref _omittedRecordCount, 0);
            Interlocked.Exchange(ref _truncatedPayloadCount, 0);
            Interlocked.Exchange(ref _repeatedWriteCount, 0);

            // Reset with the other diagnostics even though the observers themselves stay attached:
            // the count says how much went quiet during this run, and carrying it over would report
            // a previous run's losses against a run that has not lost anything.
            _detachedObserverCount = 0;
            Volatile.Write(ref _lastRepeatedTargetId, FrameSymbolTable.kNone);
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
            var drained = _sequencer.Drain();

            for (int i = 0; i < drained.Count; i++)
            {
                var evt = drained[i];
                evt.fault?.Invoke(new OperationCanceledException(reason));
                evt.Clear();
            }

            drained.Clear();
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
            Pump();
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

            _writeKeysThisFrame.Clear();

            // Before the queued events, so what the recording asked for lands first and an operator
            // acting right now lands on top of it rather than under it.
            _FillFromSource();

            // After the source, because a supplied frame brings its own number and rate: the tick a
            // replay hands out is the recorded one, not this machine's.
            _UpdateTick();

            var drained = _sequencer.Drain();
            for (int i = 0; i < drained.Count; i++)
            {
                var evt = drained[i];

                try
                {
                    // Published while the event runs so the code that applies it can say what value
                    // it wrote. Records are added to the lane below, after this, so a stamp made
                    // here is part of what gets recorded.
                    _applyingEvent = evt;
                    evt.apply?.Invoke();
                }
                catch (Exception e)
                {
                    // The caller was handed this through its own completion; log it here as well so
                    // a failure nobody awaited is still visible. A group applies as one unit, so
                    // every record it carries is marked.
                    evt.SetFlags(EventFlags.Faulted);
                    Debug.LogError($"[RemoteControl] Frame evt #{evt.firstSequence} ({_DescribeFirst(evt)}) failed: {e}");
                }
                finally
                {
                    _applyingEvent = null;
                }

                for (int r = 0; r < evt.recordCount; r++)
                {
                    // Applied above, but the state lane is already carrying what it did. See
                    // EventFlags.NotRecorded.
                    if ((evt.records[r].flags & EventFlags.NotRecorded) != 0)
                    {
                        Interlocked.Increment(ref _omittedRecordCount);
                        continue;
                    }

                    _CountIfRepeatedWrite(in evt.records[r]);
                    events.Add(evt.records[r]);
                }

                evt.Clear();
            }

            drained.Clear();

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

        /// <summary>
        /// Notes a write that follows an earlier write to the same target in this frame. The record
        /// is kept either way; the count is a signal that the target belongs in the state lane.
        /// </summary>
        private static void _CountIfRepeatedWrite(in EventRecord record)
        {
            if (record.kind != EventKind.PropertyWrite) return;
            if (record.targetId == FrameSymbolTable.kNone) return;

            var key = ((long)record.sourceId << 32) | (uint)record.targetId;
            if (_writeKeysThisFrame.Add(key)) return;

            Interlocked.Increment(ref _repeatedWriteCount);
            Volatile.Write(ref _lastRepeatedTargetId, record.targetId);
        }

        private static void _FillFromSource()
        {
            var source = _source;
            if (source == null) return;

            // Something the take needs is still loading. The source is not asked for a frame, so it
            // stays where it is and the replay resumes from the same place -- rather than playing
            // frames into a world that has not finished being built.
            if (supplyHoldCount > 0)
            {
                Interlocked.Increment(ref _heldFrameCount);
                return;
            }

            try
            {
                if (source.FillFrame(ref _frame))
                {
                    _frame.isSupplied = true;
                    return;
                }
            }
            catch (Exception e)
            {
                // Detached rather than left to throw every frame, for the same reason as the sink:
                // one failure must not become one per frame, and the run is still fine live.
                _source = null;
                Debug.LogError($"[RemoteControl] Frame source failed and was detached: {e}");
                _RaiseSourceEnded();
                return;
            }

            // Ran out. The frame falls back to the live lanes it was already pointing at.
            _source = null;
            _RaiseSourceEnded();
        }

        /// <summary>
        /// Works out how much time the frame just filled covers, and lets a replay drive the engine
        /// with it.
        /// </summary>
        private static void _UpdateTick()
        {
            var advanced = _tickFrameNumber == long.MinValue ? 1 : _frame.frameNumber - _tickFrameNumber;

            // A scrub, or the first frame of a run: one interval. A seek covers no time -- nothing
            // integrating over it moved through those frames -- and a backwards one would otherwise
            // hand out a negative tick.
            if (advanced <= 0) advanced = 1;

            _deltaTime = _frame.frameRate.AsSecounds(advanced);
            _tickFrameNumber = _frame.frameNumber;

            if (_frame.isSupplied && _driveEngineTime)
            {
                _engineTimeDriven = true;
                Time.captureDeltaTime = (float)_deltaTime;
            }
            else if (_engineTimeDriven)
            {
                _ReleaseEngineTime();
            }
        }

        /// <summary>
        /// Gives the engine's clock back to real time.
        ///
        /// Only when this is what took it: <c>Time.captureDeltaTime</c> is process-wide and a screen
        /// recorder is entitled to be holding it for its own reasons.
        /// </summary>
        private static void _ReleaseEngineTime()
        {
            if (!_engineTimeDriven) return;

            _engineTimeDriven = false;
            Time.captureDeltaTime = 0f;
        }

        private static void _RaiseSourceEnded()
        {
            try
            {
                onSourceEnded?.Invoke();
            }
            catch (Exception e)
            {
                Debug.LogError($"[RemoteControl] Frame source teardown failed: {e}");
            }
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

        private static void _NotifyObservers()
        {
            if (_observerSnapshotStale)
            {
                _observerSnapshot = _observers.ToArray();
                _observerSnapshotStale = false;
            }

            var observers = _observerSnapshot;
            for (int i = 0; i < observers.Length; i++)
            {
                var observer = observers[i];

                try
                {
                    observer.OnFrameCompleted(in _frame, _symbols);
                }
                catch (Exception e)
                {
                    // Detached rather than left to throw every frame, like the sink. Counted as well
                    // as logged, so a viewer can say it stopped watching instead of just freezing.
                    RemoveFrameObserver(observer);
                    _detachedObserverCount++;
                    Debug.LogError($"[RemoteControl] Frame observer failed and was detached: {e}");
                }
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
        /// Puts an event in the queue and completes once it has been applied at a frame head.
        /// </summary>
        public static Task<T> SubmitAsync<T>(EventKind kind, string sourceId, string verb,
            string target, string requestText, Func<T> action)
            => SubmitGroupAsync(new[] { new EventDescriptor(kind, verb, target, requestText) }, sourceId, action);

        /// <summary>
        /// Puts an event in the queue on behalf of a source resolved once with
        /// <see cref="ResolveSource"/>. Preferred over the string form: the name has already been
        /// checked against a declaration and interned, so nothing is hashed per call.
        /// </summary>
        public static Task<T> SubmitAsync<T>(EventKind kind, FrameSource source, string verb,
            string target, string requestText, Func<T> action)
            => SubmitGroupAsync(new[] { new EventDescriptor(kind, verb, target, requestText) }, source, action);

        /// <summary>Group form of <see cref="SubmitAsync{T}(EventKind, FrameSource, string, string, Func{T})"/>.</summary>
        public static Task<T> SubmitGroupAsync<T>(IReadOnlyList<EventDescriptor> operations,
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
        public static Task<T> SubmitGroupAsync<T>(IReadOnlyList<EventDescriptor> operations,
            string sourceId, Func<T> action)
        {
            // Resolved before the bypass checks so that an undeclared name is reported even when the
            // event never reaches the queue.
            return _SubmitGroup(operations, _ResolveSourceId(sourceId), action);
        }

        private static Task<T> _SubmitGroup<T>(IReadOnlyList<EventDescriptor> operations,
            int sourceId, Func<T> action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (operations == null) throw new ArgumentNullException(nameof(operations));
            if (operations.Count == 0) throw new ArgumentException("No operations.", nameof(operations));

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

            return _Enqueue(operations, sourceId, action);
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
            // Continuations run asynchronously so that whatever the caller does after its await --
            // building a response, writing it out -- does not run inside the pump on the main
            // thread. Every event funnels through one point, so inline continuations would pile
            // the whole cost of a frame's callers onto the frame head.
            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

            // Targets are interned here rather than at the frame head: this runs on the worker
            // thread that is going to wait anyway, and the main thread should not pay for it.
            var records = new EventRecord[operations.Count];

            // One buffer for the whole group. Payloads are copied into the records, so nothing
            // outlives this frame's stack.
            Span<byte> scratch = stackalloc byte[EventRecord.kPayloadCapacity];

            for (int i = 0; i < operations.Count; i++)
            {
                var operation = operations[i];

                // The sequence is stamped by the sequencer, which is where order is decided.
                var record = new EventRecord(0, operation.kind, source,
                    _symbols.Intern(operation.target), EventFlags.None,
                    _symbols.Intern(operation.verb));

                // The request text, until whoever applies it says what value it really wrote.
                // The target has not been resolved yet here, so its type is not knowable -- see
                // StampAppliedPayload for where the typed form arrives.
                if (!string.IsNullOrEmpty(operation.requestText))
                {
                    // Held inline, length first: this is one request body, not a value that
                    // recurs, and a table entry per distinct body would grow without bound.
                    var fits = EventPayload.TryWriteString(operation.requestText, scratch, out var kept);

                    record.SetPayload(scratch.Slice(0, kept), _symbols.Intern(EventPayload.kRequestTypeName));

                    if (!fits)
                    {
                        record.flags |= EventFlags.PayloadTruncated;
                        Interlocked.Increment(ref _truncatedPayloadCount);
                    }
                }

                records[i] = record;
            }

            var evt = new PendingEvent
            {
                records = records,
                recordCount = records.Length,
            };

            evt.apply = () =>
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

            evt.fault = reason => completion.TrySetException(reason);

            _sequencer.Submit(evt);
            return completion.Task;
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
        {
            var record = new EventRecord(0, kind, source.id, _symbols.Intern(target),
                EventFlags.None, _symbols.Intern(verb));

            if (!string.IsNullOrEmpty(requestText))
            {
                Span<byte> scratch = stackalloc byte[EventRecord.kPayloadCapacity];
                var fits = EventPayload.TryWriteString(requestText, scratch, out var kept);

                record.SetPayload(scratch.Slice(0, kept), _symbols.Intern(EventPayload.kRequestTypeName));

                if (!fits)
                {
                    record.flags |= EventFlags.PayloadTruncated;
                    Interlocked.Increment(ref _truncatedPayloadCount);
                }
            }

            var evt = new PendingEvent
            {
                records = new[] { record },
                recordCount = 1,
                apply = apply,

                // Nobody is waiting, so a reset has nothing to hand the failure to. Logged instead
                // of dropped: an operation that silently stopped landing is the kind of quiet this
                // whole layer exists to prevent.
                fault = reason => Debug.LogWarning(
                    $"[RemoteControl] Posted evt ({verb} {target}) never reached a frame: {reason.Message}"),
            };

            _sequencer.Submit(evt);
        }

        private static string _DescribeFirst(PendingEvent evt)
        {
            if (evt.recordCount == 0) return "no records";

            var first = evt.records[0];
            var suffix = evt.recordCount > 1 ? $" (+{evt.recordCount - 1} more)" : string.Empty;
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
