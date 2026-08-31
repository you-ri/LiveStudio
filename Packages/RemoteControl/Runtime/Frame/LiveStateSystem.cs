// Copyright (c) You-Ri, 2026
using System;
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

        /// <summary>
        /// How far the walk follows nested live objects.
        ///
        /// Bounded rather than followed to the end: an object graph is free to contain a cycle, and
        /// nothing about the state lane says it may not. Four is past anything exposed today (a
        /// camera holds a controller, a prop holds an attachment) and short enough that a cycle
        /// costs a handful of getter calls rather than a hung frame.
        /// </summary>
        public const int kMaxNestingDepth = 4;

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

            var captured = 0;
            _unaddressableObjectCount = 0;

            foreach (var handle in LiveObjectRegistry.instances)
            {
                if (!handle.isValid) continue;

                var target = handle.target;

                // A static live class has no instance to read members off. What such a class holds
                // is settings rather than the state of something in the world, so it stays on the
                // event lane, where a change to it is recorded when it happens.
                if (target == null) continue;

                if (!handle.hasId)
                {
                    _NoteUnaddressable(target);
                    continue;
                }

                captured += _Capture(target, handle.id, state, time, depth: 0);
            }

            return captured;
        }

        /// <summary>
        /// Writes a set back onto the live objects it came from. Used by replay, after the structure
        /// has been reconciled so the objects it names exist.
        /// </summary>
        public static int ApplyFrom(StateBlockSet state)
        {
            if (state == null) return 0;

            var applied = 0;

            foreach (var handle in LiveObjectRegistry.instances)
            {
                if (!handle.isValid) continue;

                var target = handle.target;
                if (target == null || !handle.hasId) continue;

                applied += _Apply(target, handle.id, state, depth: 0);
            }

            return applied;
        }

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

        /// <summary>
        /// The address a nested live object's state is carried under: the owner's id and the name of
        /// the member holding it, which is how the event lane addresses a write to it as well.
        ///
        /// Composed rather than an id of its own. A nested object is not registered and has nothing
        /// to give it a stable identity, so an id would have to be invented on the spot -- and a
        /// polymorphic member swapped from one controller to another would then come back as a
        /// different object, losing the thread through a change that is really "the same slot,
        /// holding something else".
        /// </summary>
        public static string ComposeNestedId(string ownerId, string memberName)
        {
            if (string.IsNullOrEmpty(ownerId)) return memberName;
            if (string.IsNullOrEmpty(memberName)) return ownerId;

            var key = (ownerId, memberName);
            if (_composedIds.TryGetValue(key, out var composed)) return composed;

            composed = ownerId + "/" + memberName;
            _composedIds[key] = composed;
            return composed;
        }

        /// <summary>Forgets the cached class layouts, composed ids and reports. For tests.</summary>
        internal static void ClearCaches()
        {
            _classInfo.Clear();
            _composedIds.Clear();
            _reported.Clear();
        }

        private const string kSourceName = "live";

        // Composed nested ids, so the walk does not build the same string sixty times a second.
        // Keyed by the pair rather than by the result: building the result is the cost being avoided.
        private static readonly Dictionary<(string, string), string> _composedIds =
            new Dictionary<(string, string), string>();

        // What each exposed class contributes to the walk, worked out once. The alternative is
        // deciding it per object per frame, which is the same answer reached sixty times a second.
        private static readonly Dictionary<LiveClass, ClassInfo> _classInfo =
            new Dictionary<LiveClass, ClassInfo>();

        // Types already reported as unable to take part, so the console says it once rather than
        // every frame for as long as the recording runs.
        private static readonly HashSet<Type> _reported = new HashSet<Type>();

        private static readonly int[] _noNested = Array.Empty<int>();

        /// <summary>What the walk needs to know about one exposed class.</summary>
        private sealed class ClassInfo
        {
            /// <summary>Indices of the members holding a nested live object.</summary>
            public int[] nested;

            /// <summary>True when the class asks for the state lane at all.</summary>
            public bool declaresState;
        }

        private static void _OnFrameHead(ref Frame frame)
        {
            // A supplied frame already carries the world. Capturing into it here would replace the
            // recording with the present, on the very frame meant to reproduce it -- and the replay
            // would then compare the present against itself and find no difference at all.
            if (frame.isSupplied)
            {
                _appliedObjectCount = ApplyFrom(frame.state);
                return;
            }

            _capturedObjectCount = CaptureInto(frame.state, frame.frameNumber);
        }

        /// <summary>
        /// Writes one object's state, then the state of whatever live objects it holds.
        ///
        /// Owner before nested, which is the rule keyframes are applied in as well (structure, then
        /// state): what holds the slot is settled before what is in it.
        /// </summary>
        private static int _Capture(object target, string id, StateBlockSet state, long time, int depth)
        {
            var captured = 0;
            var type = target.GetType();
            var bridge = StateBridgeRegistry.Find(type);

            if (bridge != null)
            {
                bridge.Capture(target, FrameGate.symbols.Intern(id), state, _source, time);
                captured++;
            }

            var info = _InfoFor(type, out var liveClass);
            if (info == null) return captured;

            if (bridge == null && info.declaresState) _ReportNoBridge(type);

            if (depth >= kMaxNestingDepth) return captured;

            var nested = info.nested;
            for (int i = 0; i < nested.Length; i++)
            {
                var member = liveClass.propertyTypes[nested[i]];
                var value = LivePropertyUtility.GetValueRaw(target, in member);
                if (value == null) continue;

                captured += _Capture(value, ComposeNestedId(id, member.name), state, time, depth + 1);
            }

            return captured;
        }

        /// <summary>Writes a set back onto one object and the live objects it holds.</summary>
        private static int _Apply(object target, string id, StateBlockSet state, int depth)
        {
            var applied = 0;
            var type = target.GetType();
            var bridge = StateBridgeRegistry.Find(type);

            if (bridge != null && bridge.Apply(target, FrameGate.symbols.Intern(id), state)) applied++;

            var info = _InfoFor(type, out var liveClass);
            if (info == null || depth >= kMaxNestingDepth) return applied;

            var nested = info.nested;
            for (int i = 0; i < nested.Length; i++)
            {
                var member = liveClass.propertyTypes[nested[i]];
                var value = LivePropertyUtility.GetValueRaw(target, in member);
                if (value == null) continue;

                applied += _Apply(value, ComposeNestedId(id, member.name), state, depth + 1);
            }

            return applied;
        }

        /// <summary>What the walk needs to know about a type, worked out once per class.</summary>
        private static ClassInfo _InfoFor(Type type, out LiveClass liveClass)
        {
            // Find rather than TryGet: a class is registered on demand, and a type first met here
            // (a nested object whose owner was registered before it) would otherwise look like a
            // type with nothing exposed at all.
            liveClass = LiveClass.Find(type);
            if (liveClass == null) return null;
            if (_classInfo.TryGetValue(liveClass, out var info)) return info;

            var members = liveClass.propertyTypes;
            List<int> nested = null;
            var declaresState = false;

            for (int i = 0; i < members.Length; i++)
            {
                var member = members[i];
                if (member == null) continue;

                if (member.lane == FrameLane.State) declaresState = true;
                if (!_HoldsNestedLiveObject(member)) continue;

                if (nested == null) nested = new List<int>();
                nested.Add(i);
            }

            info = new ClassInfo
            {
                nested = nested != null ? nested.ToArray() : _noNested,
                declaresState = declaresState,
            };

            _classInfo[liveClass] = info;
            return info;
        }

        /// <summary>
        /// Whether a member holds a live object whose own state the frame should carry.
        ///
        /// What is wanted is ownership: a member that *is* the nested object, the way a camera holds
        /// its controller. A member pointing at something registered elsewhere is not followed --
        /// that object is carried under its own id, and following it here would put the same state
        /// in the frame twice, under two different addresses.
        /// </summary>
        private static bool _HoldsNestedLiveObject(LivePropertyType member)
        {
            if (member.isArrayElement || member.isStatic) return false;
            if (member.isLivePropertyReference) return false;

            var valueType = member.valueType;
            if (valueType == null || !valueType.IsClass) return false;
            if (valueType == typeof(string) || valueType.IsArray) return false;

            // A Unity object is registered in its own right and carried under its own id. Following
            // it here would put the same state in the frame twice, under two addresses.
            if (typeof(UnityEngine.Object).IsAssignableFrom(valueType)) return false;

            // A collection is shape as much as value, and shape belongs to the structure lane: the
            // length has to be in the frame before the elements mean anything. Until that is in, a
            // collection of live objects stays on the event lane rather than being carried half-way.
            if (typeof(System.Collections.IEnumerable).IsAssignableFrom(valueType)) return false;

            // Exposed in its own right, asked in a way that does not depend on which of the two
            // types happened to be registered first: LiveClass.Find registers on demand, where the
            // member's own liveValueClass is only filled in if the value type was already known
            // when the owner was registered.
            //
            // The declared type is what is asked about, so a polymorphic member follows only if its
            // base is exposed. That is how such members are declared here -- a type selector needs
            // an exposed base to offer the choices from.
            return LiveClass.Find(valueType) != null;
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
        /// Says once that a registered object cannot be carried because it has no id.
        ///
        /// Scene components resolved on demand are what reaches this: they are exposed and writable,
        /// but nothing has given them an identity a replay could address.
        /// </summary>
        private static void _NoteUnaddressable(object target)
        {
            _unaddressableObjectCount++;

            var type = target.GetType();
            var info = _InfoFor(type, out _);
            if (info == null || !info.declaresState) return;
            if (!_reported.Add(type)) return;

            Debug.LogWarning(
                $"[RemoteControl] '{type.Name}' declares members in the state lane but the object has " +
                "no id, so there is no address to carry its state under. Register it with an id.");
        }
    }
}
