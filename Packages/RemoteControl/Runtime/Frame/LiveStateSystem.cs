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

            var captured = 0;
            _unaddressableObjectCount = 0;
            _visited.Clear();
            _idless.Clear();

            foreach (var handle in LiveObjectRegistry.instances)
            {
                if (!handle.isValid) continue;

                var target = handle.target;

                // A static live class has no instance to read members off. What such a class holds
                // is settings rather than the state of something in the world, so it stays on the
                // event lane, where a change to it is recorded when it happens.
                if (target == null) continue;

                // Held rather than reported here. Being registered without an id only means this
                // walk cannot address the object; the walks below still can, through the owner
                // that holds it or by its type name. Whether anything carried it is not known
                // until they have run.
                if (!handle.hasId)
                {
                    _idless.Add(target);
                    continue;
                }

                captured += _Capture(target, handle.id, state, time, depth: 0);
            }

            // Then the exposed scene components, which are not registered but are addressable by
            // their type name. Without them a recording says nothing changed about the screen
            // rather than saying nothing about it.
            var scene = LiveObjectRoster.sceneComponents;
            for (int i = 0; i < scene.Count; i++)
            {
                var entry = scene[i];
                if (!entry.isAlive)
                {
                    _staleRoster = true;
                    continue;
                }

                // Given an id since the roster was resolved: the walk above carried it already,
                // and carrying it here too would write the same state at a second address.
                //
                // Asked of the id rather than of the registration. A handle with no id is not an
                // address: the walk above passes such an object over, so standing aside for one
                // here would leave the object carried by nothing at all.
                if (LiveObjectRegistry.HasAddress(entry.target)) continue;
                if (_visited.Contains(entry.target)) continue;

                captured += _Capture(entry.target, entry.id, state, time, depth: 0);
            }

            _ReportUnaddressable();
            _RefreshRosterIfStale();

            return captured;
        }

        /// <summary>
        /// Writes a set back onto the live objects it came from. Used by replay, after the structure
        /// has been reconciled so the objects it names exist.
        /// </summary>
        public static int ApplyFrom(StateBlockSet state) => ApplyFrom(state, null);

        /// <inheritdoc cref="ApplyFrom(StateBlockSet)"/>
        /// <param name="idOf">
        /// Turns an address into the id the rows are filed under, or null to use this run's table.
        ///
        /// ⚠ A supplied frame's rows are filed under the ids the *recording* gave them, and the same
        /// address gets a different number in this run's table. Interning locally and looking that
        /// number up found either nothing or somebody else's row, which is why replaying a take put
        /// nothing back.
        /// </param>
        public static int ApplyFrom(StateBlockSet state, Func<string, int> idOf)
        {
            if (state == null) return 0;

            _idOf = idOf;

            var applied = 0;
            _visited.Clear();

            foreach (var handle in LiveObjectRegistry.instances)
            {
                if (!handle.isValid) continue;

                var target = handle.target;
                if (target == null || !handle.hasId) continue;

                applied += _Apply(target, handle.id, state, depth: 0);
            }

            var scene = LiveObjectRoster.sceneComponents;
            for (int i = 0; i < scene.Count; i++)
            {
                var entry = scene[i];
                if (!entry.isAlive)
                {
                    _staleRoster = true;
                    continue;
                }

                if (LiveObjectRegistry.HasAddress(entry.target)) continue;
                if (_visited.Contains(entry.target)) continue;

                applied += _Apply(entry.target, entry.id, state, depth: 0);
            }

            _RefreshRosterIfStale();

            _idOf = null;
            return applied;
        }

        // How an address becomes the id its row is filed under, for the apply now running. Null
        // means this run's table, which is right for everything but a supplied frame.
        private static Func<string, int> _idOf;

        private static int _OwnerId(string id)
            => _idOf != null ? _idOf(id) : FrameGate.symbols.Intern(id);

        // Set when the walk finds an entry whose object has gone, which means a scene changed under
        // the roster. Acted on after the walk rather than during it, because refreshing rebuilds the
        // list being walked.
        private static bool _staleRoster;

        private static void _RefreshRosterIfStale()
        {
            if (!_staleRoster) return;

            _staleRoster = false;
            LiveObjectRoster.Refresh();
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
            var key = _KeyOf(element) ?? _IndexKey(index);
            var slot = (memberName, key);

            if (!_elementSlots.TryGetValue(slot, out var composed))
            {
                composed = memberName + "[" + key + "]";
                _elementSlots[slot] = composed;
            }

            return ComposeNestedId(ownerId, composed);
        }

        /// <summary>The element's own key, when its type declares one. Null otherwise.</summary>
        private static string _KeyOf(object element)
        {
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

        /// <summary>Forgets the cached class layouts, composed ids and reports. For tests.</summary>
        internal static void ClearCaches()
        {
            _classInfo.Clear();
            _composedIds.Clear();
            _elementSlots.Clear();
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

        // What the registry walk reached on this pass, so the roster below it does not carry the
        // same object at a second address. A component of an exposed GameObject is addressed
        // through its owner; the type name is the address of last resort, and using both puts one
        // object's state in the frame twice.
        private static readonly HashSet<object> _visited = new HashSet<object>();

        // Registered objects this pass could not address by id, pending the question of whether one
        // of the other walks reached them anyway.
        private static readonly List<object> _idless = new List<object>();

        // "member[key]" by the pair it was built from, for the same reason the composed ids are
        // cached: the walk asks for the same handful of them sixty times a second.
        private static readonly Dictionary<(string, string), string> _elementSlots =
            new Dictionary<(string, string), string>();

        private static string[] _indexKeys = new string[16];

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
            if (string.IsNullOrEmpty(address)) return FrameSymbolTable.kNone;

            if (frame.isSupplied && FrameGate.source is Recording.FrameReplayer replayer)
            {
                return replayer.player.IdOf(address);
            }

            return FrameGate.symbols.Intern(address);
        }

        /// <summary>
        /// How an address becomes the id a supplied frame files its rows under.
        ///
        /// Null when the frame did not come from a recording, which leaves the walk using this run's
        /// table -- right for everything else.
        /// </summary>
        private static Func<string, int> _SuppliedIdOf()
        {
            if (!(FrameGate.source is Recording.FrameReplayer replayer)) return null;

            var player = replayer.player;
            return address => player.IdOf(address);
        }

        private static void _OnFrameHead(ref Frame frame)
        {
            // A supplied frame already carries the world. Capturing into it here would replace the
            // recording with the present, on the very frame meant to reproduce it -- and the replay
            // would then compare the present against itself and find no difference at all.
            if (frame.isSupplied)
            {
                // Through the recording's own table: the rows are filed under the ids it gave
                // them, and this run would hand the same address a different number.
                _appliedObjectCount = ApplyFrom(frame.state, _SuppliedIdOf());
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

            _visited.Add(target);

            // Counted only when it actually wrote. A bridge that refuses (nothing to read the
            // object through, a layout that moved) still returns, and counting the attempt is what
            // makes a frame carrying nothing report the same number as one carrying the world.
            if (bridge != null && bridge.Capture(target, FrameGate.symbols.Intern(id), state, _source, time))
            {
                captured++;
            }

            if (bridge == null && _InfoFor(type, out _)?.declaresState == true) _ReportNoBridge(type);

            return captured + _CaptureMembers(target, id, state, time, depth);
        }

        /// <summary>
        /// Carries what an object holds: the live objects nested in it, the elements of its
        /// collections, and -- for an exposed GameObject -- its exposed components.
        ///
        /// Apart from <see cref="_Capture"/> because a component is reached differently from a
        /// nested object (through its owner's key rather than a member name) but holds the same
        /// kinds of thing. Sharing this is what stops one of the two ways in from going only one
        /// level deep, which is what happened: a collection on an exposed component was in the
        /// inventory and in no state block at all.
        /// </summary>
        private static int _CaptureMembers(object target, string id, StateBlockSet state, long time,
            int depth)
        {
            var captured = 0;

            var info = _InfoFor(target.GetType(), out var liveClass);
            if (info == null) return captured;

            if (depth >= kMaxNestingDepth) return captured;

            var nested = info.nested;
            for (int i = 0; i < nested.Length; i++)
            {
                var member = liveClass.propertyTypes[nested[i]];
                var value = LivePropertyUtility.GetValueRaw(target, in member);
                if (value == null) continue;

                captured += _Capture(value, ComposeNestedId(id, member.name), state, time, depth + 1);
            }

            var collections = info.collections;
            for (int i = 0; i < collections.Length; i++)
            {
                var member = liveClass.propertyTypes[collections[i]];
                if (!(LivePropertyUtility.GetValueRaw(target, in member) is IList list)) continue;

                for (int e = 0; e < list.Count; e++)
                {
                    var element = list[e];
                    if (element == null) continue;

                    captured += _Capture(element, ComposeElementId(id, member.name, element, e),
                        state, time, depth + 1);
                }
            }

            // The components of an exposed GameObject. Special-cased rather than reached through
            // the collection walk above, which refuses a member whose elements are UnityEngine
            // objects -- rightly, for a member pointing at something registered elsewhere, and
            // wrongly for these: an exposed component is deliberately not registered, so refusing
            // to follow it here means nothing carries it at all.
            //
            // The save path special-cases the same member for the same reason (see
            // FileScopedResolver), and this is the other half of that pair: what the live scene
            // writes, the frame has to carry.
            if (target is LiveGameObject gameObject)
            {
                captured += _CaptureComponents(gameObject, id, state, time, depth);
            }

            return captured;
        }

        // Reused across the walk. GetComponents hands back a fresh array otherwise, once per
        // exposed GameObject per frame, for the whole of a take.
        private static readonly List<Component> _components = new List<Component>();

        /// <summary>
        /// Carries the exposed components of one GameObject, each under the address its owner
        /// gives it.
        ///
        /// Composed from the owner rather than given an id of its own, for the reason
        /// <see cref="ComposeNestedId"/> gives: a component has nothing to make a stable identity
        /// out of, so an id would have to be invented at run time and a recording made yesterday
        /// would name something that no longer exists.
        /// </summary>
        private static int _CaptureComponents(LiveGameObject owner, string ownerId,
            StateBlockSet state, long time, int depth)
        {
            if (!(owner.reference is GameObject go) || go == null) return 0;

            var captured = 0;
            go.GetComponents(_components);
            for (int i = 0; i < _components.Count; i++)
            {
                var component = _components[i];
                if (component == null) continue;

                // Registered under an id of its own -- an instance binding gave it one, or a
                // request reached it first. The registry walk carried it already, and carrying it
                // here as well would put the same state in the frame twice under two addresses.
                // An id-less registration is not such an address, and does not count: the registry
                // walk cannot carry that object, so this walk is the only one that can.
                if (LiveObjectRegistry.HasAddress(component)) continue;

                var type = component.GetType();
                var bridge = StateBridgeRegistry.Find(type);
                if (bridge == null) continue;

                var key = ComponentElementKey.Of(component);
                if (key == null) continue;

                var componentId = _ComposeComponentId(ownerId, key);
                var ownerSymbol = FrameGate.symbols.Intern(componentId);

                // Addressable through its owner, so the roster's type name is not needed for it.
                // Marked here rather than at the top of the loop: a component the owner cannot
                // address (no element key, no bridge) has only the type name to be carried under,
                // and claiming it was reached would drop it from the frame altogether.
                _visited.Add(component);

                // A declared bridge reads through a handle, and this component has no registered
                // one to be found by; a generated bridge reads the object directly and needs none.
                bool wrote;
                if (bridge is DeclaredStateBridge declared)
                {
                    var liveClass = LiveClass.Find(type);
                    if (liveClass == null) continue;

                    var handle = LiveObjectHandle.CreateUnregistered(liveClass, component);
                    wrote = declared.Capture(in handle, ownerSymbol, state, _source, time);
                }
                else
                {
                    wrote = bridge.Capture(component, ownerSymbol, state, _source, time);
                }

                if (wrote) captured++;

                // ⚠ And what the component holds. Only its own block was taken before, which left
                // the members of anything inside it -- the elements of a collection on an exposed
                // MonoBehaviour, mesh overrides being the case that found this -- carried by
                // nothing. The shape of those collections was in the inventory (that walk does go
                // down) and their values were nowhere, so a replay stood the elements back up with
                // their defaults and nothing about the take showed.
                captured += _CaptureMembers(component, componentId, state, time, depth + 1);
            }

            // Held only for the length of the walk: the list is shared, and a reference left in it
            // keeps a destroyed component's managed side alive until the next GameObject is walked.
            _components.Clear();
            return captured;
        }

        /// <summary>
        /// The address of one exposed component, cached the way the other composed ids are: this
        /// runs per component per frame, and building the same string each time is the cost being
        /// avoided.
        /// </summary>
        /// <summary>
        /// The address an exposed component is carried under. Shared with the structure lane for
        /// the reason <see cref="TryDescribe"/> is.
        /// </summary>
        internal static string ComposeComponentId(string ownerId, string key)
            => _ComposeComponentId(ownerId, key);

        private static string _ComposeComponentId(string ownerId, string key)
        {
            var slot = (ComponentElementKey.kMemberName, key);
            if (!_elementSlots.TryGetValue(slot, out var composed))
            {
                composed = ComponentElementKey.kMemberName + "[" + key + "]";
                _elementSlots[slot] = composed;
            }
            return ComposeNestedId(ownerId, composed);
        }

        /// <summary>Writes a set back onto one object and the live objects it holds.</summary>
        private static int _Apply(object target, string id, StateBlockSet state, int depth)
        {
            var applied = 0;
            var type = target.GetType();
            var bridge = StateBridgeRegistry.Find(type);

            _visited.Add(target);

            if (bridge != null && bridge.Apply(target, _OwnerId(id), state)) applied++;

            return applied + _ApplyMembers(target, id, state, depth);
        }

        /// <inheritdoc cref="_CaptureMembers"/>
        private static int _ApplyMembers(object target, string id, StateBlockSet state, int depth)
        {
            var applied = 0;

            var info = _InfoFor(target.GetType(), out var liveClass);
            if (info == null || depth >= kMaxNestingDepth) return applied;

            var nested = info.nested;
            for (int i = 0; i < nested.Length; i++)
            {
                var member = liveClass.propertyTypes[nested[i]];
                var value = LivePropertyUtility.GetValueRaw(target, in member);
                if (value == null) continue;

                applied += _Apply(value, ComposeNestedId(id, member.name), state, depth + 1);
            }

            var collections = info.collections;
            for (int i = 0; i < collections.Length; i++)
            {
                var member = liveClass.propertyTypes[collections[i]];
                if (!(LivePropertyUtility.GetValueRaw(target, in member) is IList list)) continue;

                for (int e = 0; e < list.Count; e++)
                {
                    var element = list[e];
                    if (element == null) continue;

                    applied += _Apply(element, ComposeElementId(id, member.name, element, e),
                        state, depth + 1);
                }
            }

            if (target is LiveGameObject gameObject)
            {
                applied += _ApplyComponents(gameObject, id, state, depth);
            }

            return applied;
        }

        /// <inheritdoc cref="_CaptureComponents"/>
        private static int _ApplyComponents(LiveGameObject owner, string ownerId, StateBlockSet state,
            int depth)
        {
            if (!(owner.reference is GameObject go) || go == null) return 0;

            var applied = 0;
            go.GetComponents(_components);
            for (int i = 0; i < _components.Count; i++)
            {
                var component = _components[i];
                if (component == null) continue;
                if (LiveObjectRegistry.HasAddress(component)) continue;

                var type = component.GetType();
                var bridge = StateBridgeRegistry.Find(type);
                if (bridge == null) continue;

                var key = ComponentElementKey.Of(component);
                if (key == null) continue;

                var componentId = _ComposeComponentId(ownerId, key);
                var ownerSymbol = _OwnerId(componentId);

                _visited.Add(component);

                bool wrote;
                if (bridge is DeclaredStateBridge declared)
                {
                    var liveClass = LiveClass.Find(type);
                    if (liveClass == null) continue;

                    var handle = LiveObjectHandle.CreateUnregistered(liveClass, component);
                    wrote = declared.Apply(in handle, ownerSymbol, state);
                }
                else
                {
                    wrote = bridge.Apply(component, ownerSymbol, state);
                }

                if (wrote) applied++;

                // The other half of the capture above: what the component holds is written back too.
                applied += _ApplyMembers(component, componentId, state, depth + 1);
            }

            _components.Clear();
            return applied;
        }

        /// <summary>
        /// What the walk finds in a type: the members holding a nested live object, and the members
        /// holding a collection of them.
        ///
        /// Shared with the structure lane so the two walks meet the same objects in the same order
        /// and give them the same addresses. Two walks that classify members differently would put
        /// an element's value at an address its inventory entry never mentions.
        /// </summary>
        internal static bool TryDescribe(Type type, out LiveClass liveClass,
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

        /// <summary>
        /// The element's key, or null when it has none. Shared for the same reason as
        /// <see cref="TryDescribe"/>.
        /// </summary>
        internal static string KeyOf(object element) => _KeyOf(element);

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
        /// <summary>
        /// Whether a member holds a collection of live objects the frame should carry each of.
        ///
        /// Elements are addressed individually rather than being packed as a run, which is what lets
        /// them be keyed: an expression keeps its address when the list is reordered. The cost is
        /// the per-element address, which is why this is for collections of objects (an operation, a
        /// deck tile, an expression) and not for a curve of floats.
        /// </summary>
        internal static bool HoldsLiveObjectCollection(LivePropertyType member)
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

        internal static bool HoldsNestedLiveObject(LivePropertyType member)
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
        /// Says once, after the walk, that an object was carried by nothing.
        ///
        /// Deferred to here because an id is not the only address there is: an object registered
        /// without one is still carried if it is a component of an exposed GameObject, or the only
        /// one of its type in the scene. Reporting at the point the id was found missing named
        /// objects the frame goes on to carry perfectly well.
        /// </summary>
        private static void _ReportUnaddressable()
        {
            for (int i = 0; i < _idless.Count; i++)
            {
                var target = _idless[i];
                if (_visited.Contains(target)) continue;

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
            var info = _InfoFor(type, out _);
            if (info == null || !info.declaresState) return;
            if (!_reported.Add(type)) return;

            Debug.LogWarning(
                $"[RemoteControl] '{type.Name}' declares members in the state lane but the object has " +
                "no id, so there is no address to carry its state under. Register it with an id.");
        }
    }
}
