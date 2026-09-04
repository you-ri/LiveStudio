// Copyright (c) You-Ri, 2026
using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;

using Lilium.RemoteControl.Reflection;

namespace Lilium.RemoteControl.Frames
{
    /// <summary>One object the walk reached, at the address it is carried under.</summary>
    public delegate void LiveObjectVisitor(object target, string address, int depth);

    /// <summary>
    /// One collection, before its elements are read.
    ///
    /// Ahead of the descent because a visitor is allowed to change the shape of the collection --
    /// the reconcile does exactly that -- and what it leaves behind is what the elements are read
    /// from. Returns whether to walk into them at all.
    /// </summary>
    public delegate bool LiveCollectionVisitor(object owner, string ownerAddress, LiveClass liveClass,
        in LivePropertyType member);

    /// <summary>
    /// Reaches every live object a frame can carry, and says where each one lives.
    ///
    /// Roots are the registered objects that have an id, plus the exposed scene components the
    /// roster addresses by type name. From each it follows what that object holds: the live objects
    /// nested in it, the elements of its collections, and -- for an exposed GameObject -- its
    /// exposed components.
    ///
    /// One walk rather than one per lane. There were four, alike apart from what they did at each
    /// object, and the faults they produced were all of one kind: a rule written into one copy and
    /// not the others. The state lane stopped at a component instead of carrying what it held while
    /// the structure lane went on, so a replay stood elements up with their defaults; and carrying
    /// what a component holds made the component loop re-entrant in one copy only. Neither was a
    /// mistake in the rule -- both were the cost of having somewhere to write it twice.
    ///
    /// Visitors are static methods over static context rather than closures: this runs per object
    /// per frame for the length of a take, and a lambda here would allocate on every frame.
    /// </summary>
    public static class LiveObjectWalk
    {
        /// <summary>
        /// How far the walk follows what an object holds.
        ///
        /// Bounded rather than followed to the end: an object graph is free to contain a cycle, and
        /// nothing about the frame says it may not. Four is past anything exposed today (a camera
        /// holds a controller, a prop holds an attachment) and short enough that a cycle costs a
        /// handful of getter calls rather than a hung frame.
        /// </summary>
        public const int kMaxNestingDepth = 4;

        // What this pass reached, so a root addressed two ways is not carried twice. A component of
        // an exposed GameObject is addressed through its owner; the type name is the address of
        // last resort, and using both puts one object in the frame at two addresses.
        private static readonly HashSet<object> _visited = new HashSet<object>();

        // Registered roots with no id. Held rather than reported during the walk: being registered
        // without an id only means the registry cannot address the object, and the walks below may
        // still reach it through whatever owns it.
        private static readonly List<object> _idless = new List<object>();

        // Set when a roster entry's object has gone, which means a scene changed under it. Acted on
        // after the walk rather than during it, because refreshing rebuilds the list being walked.
        private static bool _staleRoster;

        /// <summary>Registered roots this pass could not address by id.</summary>
        public static IReadOnlyList<object> unaddressedRoots => _idless;

        /// <summary>Whether the most recent walk reached an object by some other address.</summary>
        public static bool WasVisited(object target) => target != null && _visited.Contains(target);

        /// <summary>
        /// Runs <paramref name="visit"/> over every live object the frame can carry.
        ///
        /// Owner before what it holds, which is the order a keyframe is applied in as well
        /// (structure, then state): what holds the slot is settled before what is in it.
        /// </summary>
        /// <param name="visitCollection">
        /// Runs for each collection before its elements are read, and says whether to read them.
        /// Null walks every collection, which is what a lane that only reads values wants.
        /// </param>
        public static void Walk(LiveObjectVisitor visit, LiveCollectionVisitor visitCollection = null)
        {
            if (visit == null) return;

            _visited.Clear();
            _idless.Clear();

            foreach (var handle in LiveObjectRegistry.instances)
            {
                if (!handle.isValid) continue;

                // A static live class has no instance to read members off. What such a class holds
                // is settings rather than the state of something in the world, so it stays on the
                // event lane, where a change to it is recorded when it happens.
                var target = handle.target;
                if (target == null) continue;

                if (!handle.hasId)
                {
                    _idless.Add(target);
                    continue;
                }

                _Walk(target, handle.id, 0, visit, visitCollection);
            }

            // Then the exposed scene components, which are not registered but are addressable by
            // their type name. Without them a recording says nothing changed about the screen rather
            // than saying nothing about it.
            var scene = LiveObjectRoster.sceneComponents;
            for (int i = 0; i < scene.Count; i++)
            {
                var entry = scene[i];
                if (!entry.isAlive)
                {
                    _staleRoster = true;
                    continue;
                }

                // Given an id since the roster was resolved: the walk above reached it already, and
                // reaching it here too would put it in the frame at a second address.
                //
                // Asked of the id rather than of the registration. A handle with no id is not an
                // address: the walk above passes such an object over, so standing aside for one here
                // would leave the object reached by nothing at all.
                if (LiveObjectRegistry.HasAddress(entry.target)) continue;
                if (_visited.Contains(entry.target)) continue;

                _Walk(entry.target, entry.id, 0, visit, visitCollection);
            }

            _RefreshRosterIfStale();
        }

        private static void _Walk(object target, string address, int depth,
            LiveObjectVisitor visit, LiveCollectionVisitor visitCollection)
        {
            if (target == null) return;

            _visited.Add(target);
            visit(target, address, depth);

            // Visited before the depth is checked: an object at the limit is still carried, it is
            // only what it holds that is left out. Refusing it outright would drop the object from
            // the frame for the crime of being deep.
            if (depth >= kMaxNestingDepth) return;
            if (!TryDescribe(target.GetType(), out var liveClass, out var nested, out var collections)) return;

            var members = liveClass.propertyTypes;

            for (int i = 0; i < nested.Length; i++)
            {
                var member = members[nested[i]];
                var value = LivePropertyUtility.GetValueRaw(target, in member);
                if (value == null) continue;

                _Walk(value, ComposeNestedId(address, member.name), depth + 1, visit, visitCollection);
            }

            for (int i = 0; i < collections.Length; i++)
            {
                var member = members[collections[i]];

                if (visitCollection != null && !visitCollection(target, address, liveClass, in member))
                {
                    continue;
                }

                // Read after the visitor, not before: standing an element up replaces the array
                // behind the member, and the elements walked have to be the ones it left.
                if (!(LivePropertyUtility.GetValueRaw(target, in member) is IList list)) continue;

                for (int e = 0; e < list.Count; e++)
                {
                    var element = list[e];
                    if (element == null) continue;

                    _Walk(element, ComposeElementId(address, member.name, element, e), depth + 1,
                        visit, visitCollection);
                }
            }

            // The components of an exposed GameObject. Special-cased rather than reached through the
            // collection walk above, which refuses a member whose elements are UnityEngine objects
            // -- rightly, for a member pointing at something registered elsewhere, and wrongly for
            // these: an exposed component is deliberately not registered, so refusing to follow it
            // here means nothing reaches it at all.
            //
            // The save path special-cases the same member for the same reason (see
            // FileScopedResolver), and this is the other half of that pair: what the live scene
            // writes, the frame has to carry.
            if (target is LiveGameObject gameObject)
            {
                _WalkComponents(gameObject, address, depth, visit, visitCollection);
            }
        }

        /// <summary>
        /// Runs over the exposed components of a GameObject, each under the address its owner gives
        /// it.
        ///
        /// Composed from the owner rather than given an id of its own, for the reason
        /// <see cref="ComposeNestedId"/> gives: a component has nothing to make a stable identity
        /// out of, so an id would have to be invented at run time and a recording made yesterday
        /// would name something that no longer exists.
        /// </summary>
        private static void _WalkComponents(LiveGameObject owner, string ownerAddress, int depth,
            LiveObjectVisitor visit, LiveCollectionVisitor visitCollection)
        {
            if (!(owner.reference is GameObject go) || go == null) return;

            var components = ComponentListPool.Rent();
            go.GetComponents(components);

            for (int i = 0; i < components.Count; i++)
            {
                var component = components[i];
                if (component == null) continue;

                // Registered under an id of its own -- an instance binding gave it one, or a request
                // reached it first. The registry walk carried it already, and carrying it here as
                // well would put it in the frame twice under two addresses.
                if (LiveObjectRegistry.HasAddress(component)) continue;
                if (_visited.Contains(component)) continue;

                // Nothing its owner can address it by. The type name is then the only address it
                // has, which is the roster's business rather than this walk's.
                var key = ComponentElementKey.Of(component);
                if (key == null) continue;

                _Walk(component, ComposeComponentId(ownerAddress, key), depth + 1, visit, visitCollection);
            }

            ComponentListPool.Return(components);
        }

        private static void _RefreshRosterIfStale()
        {
            if (!_staleRoster) return;

            _staleRoster = false;
            LiveObjectRoster.Refresh();
        }

        // ---------------------------------------------------------------- addresses

        // Composed nested ids, so the walk does not build the same string sixty times a second.
        // Keyed by the pair rather than by the result: building the result is the cost being avoided.
        private static readonly Dictionary<(string, string), string> _composedIds =
            new Dictionary<(string, string), string>();

        // "member[key]" by the pair it was built from, for the same reason: the walk asks for the
        // same handful of them sixty times a second.
        private static readonly Dictionary<(string, string), string> _elementSlots =
            new Dictionary<(string, string), string>();

        private static string[] _indexKeys = new string[16];

        /// <summary>
        /// The address a nested live object is carried under: the owner's id and the name of the
        /// member holding it, which is how the event lane addresses a write to it as well.
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

        /// <summary>
        /// The address one element of a collection is carried under: the owner, the member holding
        /// the collection, and which element -- written the way this codebase already addresses one
        /// (<c>expressions[Joy]</c>).
        ///
        /// By key when the element type declares one, because that is the address that survives the
        /// collection being reordered: a recording keyed by position would, after an insert, put
        /// every value on the element next door. By position only when there is nothing else to go
        /// on.
        /// </summary>
        public static string ComposeElementId(string ownerId, string memberName, object element, int index)
        {
            var key = KeyOf(element) ?? _IndexKey(index);
            var slot = (memberName, key);

            if (!_elementSlots.TryGetValue(slot, out var composed))
            {
                composed = memberName + "[" + key + "]";
                _elementSlots[slot] = composed;
            }

            return ComposeNestedId(ownerId, composed);
        }

        /// <summary>The address an exposed component is carried under.</summary>
        public static string ComposeComponentId(string ownerId, string key)
        {
            var slot = (ComponentElementKey.kMemberName, key);
            if (!_elementSlots.TryGetValue(slot, out var composed))
            {
                composed = ComponentElementKey.kMemberName + "[" + key + "]";
                _elementSlots[slot] = composed;
            }

            return ComposeNestedId(ownerId, composed);
        }

        /// <summary>The element's own key, when its type declares one. Null otherwise.</summary>
        public static string KeyOf(object element)
        {
            if (element == null) return null;

            var liveClass = LiveClass.Find(element.GetType());
            var key = liveClass?.keyProperty;
            if (key == null) return null;

            var value = LivePropertyUtility.GetValueRaw(element, in key);
            var text = value as string ?? value?.ToString();
            return string.IsNullOrEmpty(text) ? null : text;
        }

        /// <summary>
        /// The position as a string, from a table rather than built each time: this runs per element
        /// per frame, and a fresh string for every index would allocate through the whole take.
        /// </summary>
        private static string _IndexKey(int index)
        {
            if (index < 0) return "0";

            if (index >= _indexKeys.Length)
            {
                var grown = new string[Math.Max(index + 1, _indexKeys.Length * 2)];
                Array.Copy(_indexKeys, grown, _indexKeys.Length);
                _indexKeys = grown;
            }

            return _indexKeys[index] ?? (_indexKeys[index] = index.ToString());
        }

        // ---------------------------------------------------------------- what a class holds

        // What each exposed class contributes to the walk, worked out once. The alternative is
        // deciding it per object per frame, which is the same answer reached sixty times a second.
        private static readonly Dictionary<LiveClass, ClassInfo> _classInfo =
            new Dictionary<LiveClass, ClassInfo>();

        private static readonly int[] _noNested = Array.Empty<int>();

        /// <summary>What the walk needs to know about one exposed class.</summary>
        private sealed class ClassInfo
        {
            /// <summary>Indices of the members holding a nested live object.</summary>
            public int[] nested;

            /// <summary>Indices of the members holding a collection of live objects.</summary>
            public int[] collections;

            /// <summary>True when the class asks for the state lane at all.</summary>
            public bool declaresState;
        }

        /// <summary>
        /// What the walk finds in a type: the members holding a nested live object, and the members
        /// holding a collection of them.
        /// </summary>
        public static bool TryDescribe(Type type, out LiveClass liveClass,
            out int[] nested, out int[] collections)
        {
            var info = _InfoFor(type, out liveClass);
            if (info == null)
            {
                nested = _noNested;
                collections = _noNested;
                return false;
            }

            nested = info.nested;
            collections = info.collections;
            return true;
        }

        /// <summary>Whether a type asks for the state lane at all, whatever carries it.</summary>
        public static bool DeclaresState(Type type) => _InfoFor(type, out _)?.declaresState == true;

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
            List<int> collections = null;
            var declaresState = false;

            for (int i = 0; i < members.Length; i++)
            {
                var member = members[i];
                if (member == null) continue;

                if (member.lane == FrameLane.State) declaresState = true;

                if (HoldsNestedLiveObject(member))
                {
                    if (nested == null) nested = new List<int>();
                    nested.Add(i);
                    continue;
                }

                if (HoldsLiveObjectCollection(member))
                {
                    if (collections == null) collections = new List<int>();
                    collections.Add(i);
                }
            }

            info = new ClassInfo
            {
                nested = nested != null ? nested.ToArray() : _noNested,
                collections = collections != null ? collections.ToArray() : _noNested,
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
        public static bool HoldsNestedLiveObject(LivePropertyType member)
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
            // length has to be in the frame before the elements mean anything. A collection of live
            // objects is followed by HoldsLiveObjectCollection instead, element by element, once the
            // inventory has said what is in it.
            if (typeof(IEnumerable).IsAssignableFrom(valueType)) return false;

            // Exposed in its own right, asked in a way that does not depend on which of the two
            // types happened to be registered first: LiveClass.Find registers on demand, where the
            // member's own liveValueClass is only filled in if the value type was already known when
            // the owner was registered.
            //
            // The declared type is what is asked about, so a polymorphic member follows only if its
            // base is exposed. That is how such members are declared here -- a type selector needs
            // an exposed base to offer the choices from.
            return LiveClass.Find(valueType) != null;
        }

        /// <summary>
        /// Whether a member holds a collection of live objects the frame should carry each of.
        ///
        /// Elements are addressed individually rather than being packed as a run, which is what lets
        /// them be keyed: an expression keeps its address when the list is reordered. The cost is
        /// the per-element address, which is why this is for collections of objects (an operation, a
        /// deck tile, an expression) and not for a curve of floats.
        /// </summary>
        public static bool HoldsLiveObjectCollection(LivePropertyType member)
        {
            if (member.isArrayElement || member.isStatic) return false;

            var valueType = member.valueType;
            if (valueType == null) return false;
            if (!typeof(IList).IsAssignableFrom(valueType)) return false;

            var elementType = valueType.IsArray
                ? valueType.GetElementType()
                : (valueType.IsGenericType ? valueType.GetGenericArguments()[0] : null);

            if (elementType == null || !elementType.IsClass) return false;
            if (elementType == typeof(string)) return false;
            if (typeof(UnityEngine.Object).IsAssignableFrom(elementType)) return false;

            // Exposed in its own right, asked the way the nested case asks it -- so a type first met
            // here is registered rather than looking like a type with nothing exposed.
            return LiveClass.Find(elementType) != null;
        }

        /// <summary>Forgets the cached class layouts and composed ids. For tests.</summary>
        internal static void ClearCaches()
        {
            _classInfo.Clear();
            _composedIds.Clear();
            _elementSlots.Clear();
        }
    }
}
