// Copyright (c) You-Ri, 2026
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Lilium.RemoteControl.Frames
{
    /// <summary>
    /// Puts the exposed state-lane members of every live object into the frame, once per frame.
    ///
    /// This is what makes a keyframe a superset of a scene snapshot: a snapshot holds the members
    /// that are persisted, and the frame holds every member declared <see cref="FrameLane.State"/>
    /// whether it is persisted or not. Leaving the unpersisted ones out is what would make a
    /// resynchronised machine drift straight back out of step.
    ///
    /// The roster is every registered live object that has an id, plus the live objects nested
    /// inside them. An id is the requirement rather than a nicety: a state element is addressed by
    /// it, so an object without one has nowhere for a replay to put the value back.
    ///
    /// Only types something produced a bridge for take part. A member declared
    /// <see cref="FrameLane.State"/> on a type with no bridge, or on an object with no id, is
    /// reported once, because silently carrying nothing looks exactly like carrying a world where
    /// nothing changed.
    /// </summary>
    public static class LiveStateSystem
    {
        private static FrameSource _source;
        private static int _users;
        private static long _capturedObjectCount;
        private static long _appliedObjectCount;
        private static long _unaddressableObjectCount;

        /// <summary>Objects whose state was captured on the most recent frame.</summary>
        public static long capturedObjectCount => _capturedObjectCount;

        /// <summary>Objects whose state was written back on the most recent supplied frame.</summary>
        public static long appliedObjectCount => _appliedObjectCount;

        /// <summary>
        /// Registered objects passed over on the most recent frame because they have no id.
        ///
        /// Counted as well as reported: the report names the types once, this says whether it is
        /// still happening.
        /// </summary>
        public static long unaddressableObjectCount => _unaddressableObjectCount;

        /// <summary>True while the per-frame capture is running.</summary>
        public static bool isRunning => _users > 0;

        /// <summary>
        /// Asks for exposed state to be carried at each frame head: written into the frame on a
        /// live one, read back out of it on a supplied one.
        ///
        /// Counted, and balanced by <see cref="Release"/>. A recording and an open viewer both want
        /// this running, and a plain on/off would let whichever stopped first take it away from the
        /// other -- which reads as a recording that quietly stopped carrying state.
        /// </summary>
        public static void Retain()
        {
            if (_users++ > 0) return;

            // The scene as it is now is the scene this is about to start carrying. Resolving it here
            // rather than per frame keeps a scene walk out of the frame head.
            LiveObjectRoster.Refresh();

            _source = FrameGate.ResolveSource(kSourceName);
            FrameGate.AddFrameHeadHandler(_OnFrameHead);
        }

        /// <summary>Gives it up. Stops once nobody wants it. What is in the frame stays there.</summary>
        public static void Release()
        {
            if (_users == 0 || --_users > 0) return;

            FrameGate.RemoveFrameHeadHandler(_OnFrameHead);
        }

        /// <summary>
        /// Writes every bridged live object's state into a set. Exposed separately from the frame
        /// head so a caller can take a snapshot of the world without waiting for one.
        /// </summary>
        public static int CaptureInto(StateBlockSet state, long time)
        {
            if (state == null) return 0;

            _unaddressableObjectCount = 0;
            _walkState = state;
            _walkTime = time;
            _walkCount = 0;

            LiveObjectWalk.Walk(_CaptureOne);

            _walkState = null;
            _ReportUnaddressable();

            return _walkCount;
        }

        // What the walk now running is writing into, and what it has done so far. Static rather
        // than captured in a lambda: the visitor runs per object per frame for the length of a
        // take, and a closure here would allocate on every frame.
        private static StateBlockSet _walkState;
        private static long _walkTime;
        private static int _walkCount;

        /// <summary>
        /// Writes one object's state into the frame.
        ///
        /// Counted only when it actually wrote. A bridge that refuses (nothing to read the object
        /// through, a layout that moved) still returns, and counting the attempt is what makes a
        /// frame carrying nothing report the same number as one carrying the world.
        /// </summary>
        private static void _CaptureOne(object target, string address, int depth)
        {
            var type = target.GetType();
            var bridge = StateBridgeRegistry.Find(type);

            if (bridge == null)
            {
                if (LiveObjectWalk.DeclaresState(type)) _ReportNoBridge(type);
                return;
            }

            if (bridge.Capture(target, FrameGate.symbols.Intern(address), _walkState, _source, _walkTime))
            {
                _walkCount++;
            }
        }

        /// <summary>
        /// Writes a set back onto the live objects it came from. Used by replay, after the structure
        /// has been reconciled so the objects it names exist.
        /// </summary>
        public static int ApplyFrom(StateBlockSet state) => ApplyFrom(state, FrameGate.symbols);

        /// <inheritdoc cref="ApplyFrom(StateBlockSet)"/>
        /// <param name="symbols">
        /// The table the rows are filed under -- <see cref="Frame.symbols"/> for a frame, this run's
        /// for anything else.
        ///
        /// ⚠ A supplied frame's rows carry the ids the *recording* gave them, and the same address
        /// is a different number in this run's table. Reading them against the wrong one found
        /// either nothing or somebody else's row, which is why replaying a take put nothing back.
        /// </param>
        public static int ApplyFrom(StateBlockSet state, FrameSymbolTable symbols)
        {
            if (state == null) return 0;

            _applySymbols = symbols;
            _walkState = state;
            _walkCount = 0;

            LiveObjectWalk.Walk(_ApplyOne);

            _walkState = null;
            _applySymbols = null;
            return _walkCount;
        }

        /// <summary>Writes one object's recorded state back onto it.</summary>
        private static void _ApplyOne(object target, string address, int depth)
        {
            var bridge = StateBridgeRegistry.Find(target.GetType());
            if (bridge == null) return;

            if (bridge.Apply(target, _OwnerId(address), _walkState)) _walkCount++;
        }

        // The table the apply now running reads its ids out of.
        private static FrameSymbolTable _applySymbols;

        // Looked up rather than interned: an address nothing filed a row under has no row, and
        // handing out a fresh id for it would say the same thing while growing the table.
        private static int _OwnerId(string id)
            => _applySymbols != null && _applySymbols.TryGetId(id, out var found)
                ? found
                : FrameSymbolTable.kNone;

        /// <summary>
        /// Creates a block for every registered type, so a recording can be played into a set that
        /// has somewhere to put each one. Without this a replay reports every type as unknown until
        /// something happens to have written it live first.
        /// </summary>
        public static void PrepareBlocks(StateBlockSet state)
        {
            if (state == null) return;

            var bridges = StateBridgeRegistry.all;
            for (int i = 0; i < bridges.Count; i++) bridges[i].EnsureBlock(state);
        }

        /// <summary>Forgets the cached class layouts, composed ids and reports. For tests.</summary>
        internal static void ClearCaches()
        {
            LiveObjectWalk.ClearCaches();
            _reported.Clear();
        }

        private const string kSourceName = "live";

        // Types already reported as unable to take part, so the console says it once rather than
        // every frame for as long as the recording runs.
        private static readonly HashSet<Type> _reported = new HashSet<Type>();

        /// <summary>
        /// The id a frame's rows are filed under for an address.
        ///
        /// ⚠ Not simply interning the address. A supplied frame's rows carry the ids the *recording*
        /// gave them, and this run's table hands the same address a different number -- so a
        /// producer reading its own row back out of a replayed frame has to ask the recording, or it
        /// looks under a number nobody filed anything under and finds nothing. Every place that
        /// reads a row by address needs this; three of them were written without it.
        /// </summary>
        public static int OwnerIdOf(in Frame frame, string address)
        {
            var symbols = frame.symbols ?? FrameGate.symbols;

            return symbols.TryGetId(address, out var id) ? id : FrameSymbolTable.kNone;
        }

        private static void _OnFrameHead(ref Frame frame)
        {
            // A supplied frame already carries the world. Capturing into it here would replace the
            // recording with the present, on the very frame meant to reproduce it -- and the replay
            // would then compare the present against itself and find no difference at all.
            if (frame.isSupplied)
            {
                // Through whatever table the frame carries. For a supplied frame that is the
                // recording's, which is the whole of what used to be got wrong here.
                _appliedObjectCount = ApplyFrom(frame.state, frame.symbols);
                return;
            }

            _capturedObjectCount = CaptureInto(frame.state, frame.frameNumber);
        }

        /// <summary>
        /// Says once that a type asks for the state lane but has nothing to move its state.
        ///
        /// The usual cause is a type that is not <c>partial</c>, or one in an assembly with no
        /// reference to the simulation: the generator reports both at compile time, but a build that
        /// went past those warnings would otherwise record a world in which that type never changes.
        /// </summary>
        private static void _ReportNoBridge(Type type)
        {
            if (!_reported.Add(type)) return;

            Debug.LogWarning(
                $"[RemoteControl] '{type.Name}' declares members in the state lane but has no state " +
                "bridge, so its state is not carried. Make the type partial and give its assembly a " +
                "reference to 'Lilium.RemoteControl.Simulation'.");
        }

        /// <summary>
        /// Says once, after the walk, that an object was carried by nothing.
        ///
        /// Deferred to here because an id is not the only address there is: an object registered
        /// without one is still carried if it is a component of an exposed GameObject, or the only
        /// one of its type in the scene. Reporting at the point the id was found missing named
        /// objects the frame goes on to carry perfectly well.
        /// </summary>
        private static void _ReportUnaddressable()
        {
            var idless = LiveObjectWalk.unaddressedRoots;

            for (int i = 0; i < idless.Count; i++)
            {
                var target = idless[i];

                // Reached by some other address after all -- through whatever owns it, or by its
                // type name. Having no id of its own is then not a hole in the recording.
                if (LiveObjectWalk.WasVisited(target)) continue;

                _NoteUnaddressable(target);
            }
        }

        /// <summary>
        /// Says once that a registered object cannot be carried because it has no id.
        ///
        /// Scene components resolved on demand are what reaches this: they are exposed and writable,
        /// but nothing has given them an identity a replay could address.
        /// </summary>
        private static void _NoteUnaddressable(object target)
        {
            _unaddressableObjectCount++;

            var type = target.GetType();
            if (!LiveObjectWalk.DeclaresState(type)) return;
            if (!_reported.Add(type)) return;

            Debug.LogWarning(
                $"[RemoteControl] '{type.Name}' declares members in the state lane but the object has " +
                "no id, so there is no address to carry its state under. Register it with an id.");
        }
    }
}
