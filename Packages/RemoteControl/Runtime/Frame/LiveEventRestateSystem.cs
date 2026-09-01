// Copyright (c) You-Ri, 2026
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

using UnityEngine;

namespace Lilium.RemoteControl.Frames
{
    /// <summary>
    /// Writes the value of every event-lane member into a frame as events, so a recording carries a
    /// point it can be read from rather than only the changes made after it started.
    ///
    /// The event lane is sparse by design: a record says a value changed, and between changes the
    /// lane says nothing. That is the right shape for a live take and the wrong one for reading it
    /// back, because a value settled before the recording started -- which avatar is out, what layer
    /// it is on, how a camera is framed -- appears nowhere in the file. Playing such a recording
    /// leaves those members at whatever the machine happened to hold, and no amount of work on the
    /// replay side can recover them: the value was never written down.
    ///
    /// So a frame is asked, now and then, to carry the values as well as the changes. What this
    /// produces is not something that happened -- it is the world as it stands, written in the
    /// vocabulary of things that happen, which is what lets the ordinary replay path put it back
    /// with no second mechanism (<see cref="EventFlags.Reemitted"/> is how the apply side tells the
    /// two apart, so it can skip a value that already matches).
    ///
    /// The state lane needs none of this: it carries every declared member on every frame, so any
    /// frame is a complete statement of it. This is the same guarantee bought for the other lane at
    /// the other price -- occasionally and in bulk, rather than continuously.
    ///
    /// <para>
    /// Addressed the way a live write is addressed: <c>/live/object/{id}/{slashPath}</c>. Sharing the
    /// address space with real records is what lets a reader fold the two together and keep the last
    /// value for a target, whichever kind of record it came from.
    /// </para>
    ///
    /// <para>
    /// ⚠ What this cannot carry: a member whose value has no layout and is not text -- an asset
    /// reference, a nested value written as a request body. Those are counted
    /// (<see cref="unrepresentableCount"/>) rather than written half-way, and the string that
    /// usually stands behind such a member (an asset id, a selection by name) is carried in its
    /// place. Collection <em>shape</em> is not carried either: the elements that exist are walked,
    /// and standing missing ones back up belongs to the structure lane.
    /// </para>
    /// </summary>
    public static class LiveEventRestateSystem
    {
        /// <summary>
        /// The source restated records are attributed to.
        ///
        /// Its own source rather than the one that wrote the value originally, for two reasons: a
        /// reader has to be able to tell a restatement from the write it copies, and
        /// <see cref="EventRecord.sequence"/> is per source -- mixing restatements into a real
        /// source's numbering would leave gaps that read as dropped events.
        /// </summary>
        public const string kSourceName = "restate";

        /// <summary>
        /// The verb every restated record carries. A restatement says "this is the value", which is
        /// the same thing a PUT says, and going in through the same verb means it reaches the same
        /// operation a live write reached.
        /// </summary>
        public const string kVerb = "PUT";

        /// <summary>How deep the walk follows nested live objects. Same bound, same reason.</summary>
        public const int kMaxNestingDepth = LiveStateSystem.kMaxNestingDepth;

        private const string kObjectPathPrefix = "/live/object/";

        private static long _sequence;
        private static long _unrepresentableCount;
        private static long _restatedMemberCount;

        // The address being built, kept across the walk: a restatement touches every exposed member
        // of every object, and composing each path from its parts would allocate one string per
        // segment per member. Segments are pushed and popped by length.
        private static readonly StringBuilder _path = new StringBuilder(128);

        // Objects already written during this restatement. An exposed component is reachable two
        // ways -- through the GameObject that owns it, and by its own type name from the roster --
        // and stating it under both would put the same value at two addresses, where a fold that
        // sees one has no idea it has already seen the other.
        private static readonly HashSet<object> _visited = new HashSet<object>();

        /// <summary>Members written by the most recent restatement.</summary>
        public static long restatedMemberCount => _restatedMemberCount;

        /// <summary>
        /// Members passed over by the most recent restatement because their value is neither laid
        /// out nor text.
        ///
        /// Counted rather than logged per member: this is a property of what is exposed, so it is
        /// the same number every time and a warning would repeat once a keyframe for the length of
        /// a take. A recording whose count is high is one whose event lane cannot be fully restored,
        /// which is worth showing next to the recorder rather than in a log.
        /// </summary>
        public static long unrepresentableCount => _unrepresentableCount;

        /// <summary>
        /// Fills <paramref name="frame"/> with the current value of every event-lane member of every
        /// addressable live object, and returns how many were written.
        ///
        /// The frame is the caller's: a recorder keeps one of its own and writes it alongside the
        /// frame's real events rather than into them, so nothing watching the live gate sees events
        /// that never happened.
        /// </summary>
        public static int RestateInto(EventFrame frame, FrameSymbolTable symbols)
        {
            if (frame == null || symbols == null) return 0;

            _restatedMemberCount = 0;
            _unrepresentableCount = 0;
            _visited.Clear();

            var sourceId = symbols.Intern(kSourceName);
            var verbId = symbols.Intern(kVerb);

            foreach (var handle in LiveObjectRegistry.instances)
            {
                if (!handle.isValid) continue;

                var target = handle.target;

                // A static live class has no instance to read members off, and the state lane
                // passes over it for the same reason: what it holds is settings rather than the
                // state of something in the world.
                if (target == null || !handle.hasId) continue;

                _RestateObject(target, handle.id, frame, symbols, sourceId, verbId);
            }

            // The exposed scene components, which are addressable by type name without being
            // registered. Same roster the state lane walks, so the two lanes cover the same world.
            //
            // After the registry, and skipping whatever that walk already reached: a component of an
            // exposed GameObject is addressed through its owner by every client there is, and the
            // type name is the address of last resort. Stating it here as well would be the same
            // value at a second address.
            var scene = LiveObjectRoster.sceneComponents;
            for (int i = 0; i < scene.Count; i++)
            {
                var entry = scene[i];
                if (!entry.isAlive) continue;
                if (LiveObjectRegistry.FindByTarget(entry.target) != null) continue;
                if (_visited.Contains(entry.target)) continue;

                _RestateObject(entry.target, entry.id, frame, symbols, sourceId, verbId);
            }

            return (int)_restatedMemberCount;
        }

        /// <summary>Forgets the running sequence. For tests.</summary>
        internal static void ResetSequence() => _sequence = 0;

        private static void _RestateObject(object target, string id, EventFrame frame,
            FrameSymbolTable symbols, int sourceId, int verbId)
        {
            _path.Clear();
            _path.Append(kObjectPathPrefix).Append(id).Append('/');

            _Restate(target, frame, symbols, sourceId, verbId, depth: 0);
        }

        /// <summary>
        /// Walks one object, writing its own members and then the objects it holds.
        ///
        /// The same walk the state lane makes, and deliberately so -- a member that moved from one
        /// lane to the other should not also change which walk can find it. What differs is the
        /// address: the state lane composes an id per nested object, while a write is addressed as
        /// a path into the object that owns it, because that is the address REST gave it.
        /// </summary>
        private static void _Restate(object target, EventFrame frame, FrameSymbolTable symbols,
            int sourceId, int verbId, int depth)
        {
            var type = target.GetType();
            var liveClass = LiveClass.Find(type);
            if (liveClass == null) return;

            _visited.Add(target);

            // Whether the state lane is actually carrying this type's state members, as opposed to
            // being asked to. A type that declares the state lane but produced no bridge (not
            // partial, no reference to the simulation) has its state members carried by neither
            // lane -- the block leaves them out and the write path omits their records on the
            // strength of the declaration. Asking the bridge rather than the declaration puts them
            // back here, so a build that went past the generator's warnings still records a world
            // that changes.
            var carriedByState = StateBridgeRegistry.Find(type) != null;

            var members = liveClass.propertyTypes;
            for (int i = 0; i < members.Length; i++)
            {
                var member = members[i];
                if (member == null || member.isStatic || member.isArrayElement) continue;

                // Objects held by this one are walked whether or not the member itself can be
                // written: a read-only getter returning a live object is how most of them are
                // exposed, and what is wanted is the members inside it.
                if (depth < kMaxNestingDepth && LiveStateSystem.HoldsNestedLiveObject(member))
                {
                    var nested = LivePropertyUtility.GetValueRaw(target, in member);
                    if (nested == null) continue;

                    var mark = _Push(member.name);
                    _Restate(nested, frame, symbols, sourceId, verbId, depth + 1);
                    _path.Length = mark;
                    continue;
                }

                // The exposed components of an exposed GameObject. Their elements are
                // UnityEngine.Objects, which the collection walk above refuses -- rightly for a
                // member pointing at something registered elsewhere, and wrongly for these: an
                // exposed component is deliberately not registered, and REST addresses it through
                // its owner (`components/2/selectedAvatar`). Miss this and a member's restated value
                // lands at an address no live write ever used, so the two never fold together and a
                // seek applies both in whatever order they were written.
                if (depth < kMaxNestingDepth && _HoldsExposedComponents(member))
                {
                    if (!(LivePropertyUtility.GetValueRaw(target, in member) is IList components)) continue;

                    var mark = _Push(member.name);
                    for (int e = 0; e < components.Count; e++)
                    {
                        // By position in the list as returned, which is how REST resolves one. An
                        // element that is skipped still counts, or every component after it would be
                        // addressed one place early.
                        if (!(components[e] is UnityEngine.Object component) || component == null) continue;
                        if (LiveClass.Find(component.GetType()) == null) continue;

                        var componentMark = _Push(e);
                        _Restate(component, frame, symbols, sourceId, verbId, depth + 1);
                        _path.Length = componentMark;
                    }
                    _path.Length = mark;
                    continue;
                }

                if (depth < kMaxNestingDepth && LiveStateSystem.HoldsLiveObjectCollection(member))
                {
                    if (!(LivePropertyUtility.GetValueRaw(target, in member) is IList list)) continue;

                    var mark = _Push(member.name);
                    for (int e = 0; e < list.Count; e++)
                    {
                        var element = list[e];
                        if (element == null) continue;

                        // By position, which is how REST addresses an element and therefore how a
                        // real write to one is recorded. The state lane addresses the same element
                        // by key; the two are different address spaces and stay that way.
                        var elementMark = _Push(e);
                        _Restate(element, frame, symbols, sourceId, verbId, depth + 1);
                        _path.Length = elementMark;
                    }
                    _path.Length = mark;
                    continue;
                }

                if (!_ShouldRestate(member, carriedByState)) continue;

                var value = LivePropertyUtility.GetValueRaw(target, in member);
                if (value == null) continue;

                var leafMark = _path.Length;
                _path.Append(member.name);
                _Emit(member, value, frame, symbols, sourceId, verbId);
                _path.Length = leafMark;
            }
        }

        /// <summary>
        /// Whether a member holds the exposed components of an exposed Unity object.
        ///
        /// Asked of the member rather than of the owner's type, so the path is built from what the
        /// declaration is actually called -- which is what REST resolves it by.
        /// </summary>
        private static bool _HoldsExposedComponents(LivePropertyType member)
        {
            if (member.isArrayElement || member.isStatic) return false;
            if (member.name != ComponentElementKey.kMemberName) return false;

            var valueType = member.valueType;
            if (valueType == null || !typeof(IList).IsAssignableFrom(valueType)) return false;

            var elementType = valueType.IsArray
                ? valueType.GetElementType()
                : (valueType.IsGenericType ? valueType.GetGenericArguments()[0] : null);

            return elementType != null && typeof(Component).IsAssignableFrom(elementType);
        }

        /// <summary>Whether one leaf member's value belongs in a restatement.</summary>
        private static bool _ShouldRestate(LivePropertyType member, bool carriedByState)
        {
            // Nothing to write it back through. A read-only member is also the one thing the design
            // forbids putting in a frame as a value: replaying an application's own result and then
            // comparing against it would agree with itself.
            if (member.isReadOnly) return false;

            switch (member.lane)
            {
                // Off the live data entirely -- window position, screen size. Things a spare machine
                // is expected to differ on.
                case FrameLane.None:
                    return false;

                // Already in every frame at its own address, so restating it would be the same value
                // twice. Unless nothing is actually carrying it (see the caller), in which case this
                // is the only lane left.
                case FrameLane.State:
                    return !carriedByState;
            }

            // A reference to another member's value. What it points at is restated in its own right,
            // and writing the reference itself would rebind rather than restore.
            if (member.isLivePropertyReference) return false;

            return true;
        }

        /// <summary>
        /// Writes one member's value into the frame, or counts it as one that could not be written.
        /// </summary>
        private static void _Emit(LivePropertyType member, object value, EventFrame frame,
            FrameSymbolTable symbols, int sourceId, int verbId)
        {
            Span<byte> packed = stackalloc byte[EventRecord.kPayloadCapacity];
            int written;
            string typeName;

            if (value is string text)
            {
                // A string that does not fit is not written at all. A real event keeps what fits and
                // marks it truncated, because a truncated record still says something true about
                // what happened; a truncated restatement would be a value nobody ever set, and the
                // replay would write it.
                if (!EventPayload.TryWriteString(text, packed, out written))
                {
                    _unrepresentableCount++;
                    return;
                }

                typeName = EventPayload.kStringTypeName;
            }
            else
            {
                var valueType = member.valueType ?? value.GetType();

                if (!EventPayload.TryPack(valueType, value, packed, out written))
                {
                    // No layout: an asset reference, a collection of values, anything whose live
                    // write travelled as a request body. The body is not reproducible from the value
                    // here -- it is what a client sent -- so this is left out and counted.
                    _unrepresentableCount++;
                    return;
                }

                typeName = EventPayload.NameOf(valueType);
            }

            var record = new EventRecord(
                _sequence++,
                EventKind.PropertyWrite,
                sourceId,
                symbols.Intern(_path.ToString()),
                EventFlags.Reemitted,
                verbId);

            record.SetPayload(packed.Slice(0, written), symbols.Intern(typeName));

            frame.Add(in record);
            _restatedMemberCount++;
        }

        /// <summary>Appends a path segment and returns the length to restore to.</summary>
        private static int _Push(string segment)
        {
            var mark = _path.Length;
            _path.Append(segment).Append('/');
            return mark;
        }

        /// <inheritdoc cref="_Push(string)"/>
        private static int _Push(int index)
        {
            var mark = _path.Length;
            _path.Append(index).Append('/');
            return mark;
        }
    }
}
