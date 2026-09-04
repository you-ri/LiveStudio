// Copyright (c) You-Ri, 2026
using System;
using System.Collections;
using System.Collections.Generic;

using Lilium.RemoteControl.Reflection;

namespace Lilium.RemoteControl.Frames
{
    /// <summary>
    /// Writes the inventory -- what exists, of what type, under whom -- into each live frame.
    ///
    /// The state lane says what the values are; this says what they belong to. Without it a
    /// recording holds values addressed to objects it never mentions, and a replay has nothing to
    /// tell it whether the world it is writing into is the world that was recorded.
    ///
    /// This is the capture half only. Applying an inventory is not assignment but a reconcile
    /// against reality -- create what is missing, destroy what is not in it -- and that half is
    /// what makes scrubbing past a spawn work. It is not here yet; see the design note.
    /// </summary>
    public static class LiveStructureSystem
    {
        private static int _users;
        private static int _objectCount;

        // Ids seen this frame, so what is no longer registered can be taken out. A set rather than
        // a walk of the block per object: the inventory is small but the walk is quadratic, and
        // this runs at every frame head.
        private static readonly HashSet<int> _present = new HashSet<int>();

        // What this system put in the inventory. Only these are ever taken out again: the block is
        // shared, and another producer may have entries of its own in it -- a spawned prop, a test
        // standing something up. Removing those because the registry does not know them would be
        // this system quietly deciding it is the only one allowed to say what exists.
        private static readonly HashSet<int> _owned = new HashSet<int>();

        // Objects a replay stood up, and what made each. Held so that only these are ever taken
        // away again: the scene a replay runs in usually has things of its own in it, and a
        // recording is a record of what it watched rather than a claim about everything that exists.
        private static readonly Dictionary<string, ILiveRecipe> _made =
            new Dictionary<string, ILiveRecipe>();

        // Reused so the reconcile does not allocate per frame. Collected separately from the walk
        // because removing from the registry while enumerating it is not allowed.
        private static readonly List<string> _going = new List<string>();

        /// <summary>Objects in the inventory as of the most recent live frame.</summary>
        public static int objectCount => _objectCount;

        /// <summary>True while the per-frame capture is running.</summary>
        public static bool isRunning => _users > 0;

        /// <summary>
        /// Asks for the inventory to be written at each frame head. Counted, and balanced by
        /// <see cref="Release"/> -- see <see cref="LiveStateSystem.Retain"/> for why.
        /// </summary>
        public static void Retain()
        {
            if (_users++ > 0) return;

            // Ahead of every other producer: on a supplied frame this creates and destroys the
            // objects the state lane is about to be written onto, so it cannot run after them.
            FrameGate.AddFrameHeadHandler(_OnFrameHead, first: true);
        }

        /// <summary>Gives it up. Stops once nobody wants it.</summary>
        public static void Release()
        {
            if (_users == 0 || --_users > 0) return;

            FrameGate.RemoveFrameHeadHandler(_OnFrameHead);
        }

        /// <summary>
        /// How to read the ids of a supplied frame.
        ///
        /// A recording carries its own mapping table and its ids index that one. Falls back to the
        /// live table when the frame came from something with no table of its own: there is nothing
        /// better to try, and the reconcile then fails to find things rather than finding wrong ones.
        /// </summary>
        private static Func<int, string> _SuppliedResolver()
        {
            if (FrameGate.source is Recording.FrameReplayer replayer)
            {
                var player = replayer.player;
                return id => player.Resolve(id);
            }

            var symbols = FrameGate.symbols;
            return id => symbols.Resolve(id);
        }

        private static void _OnFrameHead(ref Frame frame)
        {
            // A supplied frame brought its own inventory. Writing ours over it would replace the
            // world being replayed with the world that happens to be loaded.
            if (frame.isSupplied)
            {
                _objectCount = frame.structure?.count ?? 0;

                // ⚠ Resolved through the recording's own table, not this run's. The ids in a
                // supplied frame index the table the recording carries, and the live table holds
                // different strings at those numbers -- so reading them here named whatever
                // happened to be interned in that slot, the reconcile matched nothing, and
                // replaying a take changed nothing at all. The viewer resolves the same way
                // (LiveDataTap._ResolverFor), which is why a take could look right on screen and
                // still do nothing on replay.
                if (applyOnSuppliedFrames) ApplyFrom(frame.structure, _SuppliedResolver());

                return;
            }

            _objectCount = CaptureInto(frame.structure, FrameGate.symbols);
        }

        /// <summary>
        /// Whether a supplied frame's inventory is acted on, not just read.
        ///
        /// Off by default. Applying an inventory creates and destroys real objects, and a viewer
        /// watching a replay wants to see what the recording holds without it rearranging the scene
        /// being watched in. Whoever is actually replaying turns it on.
        /// </summary>
        public static bool applyOnSuppliedFrames { get; set; }

        /// <summary>
        /// Forgets what this system stood up, without taking any of it away.
        ///
        /// For ending a replay: the objects it made are now just objects, and the next recording
        /// must not be able to destroy them by not listing them. Also what keeps one test's world
        /// out of the next one's, since the table outlives any single run.
        /// </summary>
        public static void ForgetMade() => _made.Clear();

        /// <summary>Objects the most recent apply created.</summary>
        public static int createdCount { get; private set; }

        /// <summary>Objects the most recent apply destroyed.</summary>
        public static int destroyedCount { get; private set; }

        /// <summary>
        /// Objects the most recent apply could not create, because nothing here knows the recipe the
        /// recording named. Counted rather than thrown: one object that cannot be rebuilt should not
        /// stop the rest of the world from being, but a silent gap reads as a recording that simply
        /// had less in it.
        /// </summary>
        public static int unresolvedCount { get; private set; }

        /// <summary>
        /// Makes the world match an inventory: create what is listed and missing, destroy what is
        /// here and not listed, leave alone what is in both.
        ///
        /// The third case is what makes replay usable -- applying the same keyframe twice must not
        /// reload an avatar -- and the second is what makes scrubbing back past a spawn take it
        /// away again.
        ///
        /// Only what this system stood up is ever destroyed. Something standing in the scene that
        /// the recording never mentioned is left alone, because a recording is a record of what it
        /// watched and not a claim about everything that may exist.
        /// </summary>
        public static void ApplyFrom(StructureBlock structure, FrameSymbolTable symbols)
        {
            if (symbols == null)
            {
                createdCount = 0;
                destroyedCount = 0;
                unresolvedCount = 0;
                return;
            }

            ApplyFrom(structure, symbols.Resolve);
        }

        /// <inheritdoc cref="ApplyFrom(StructureBlock, FrameSymbolTable)"/>
        /// <param name="resolve">
        /// Reads an id back into the string it stood for. Taken as a function rather than a table
        /// because a recording's table is not one: it is the list the file carried, and the ids of a
        /// supplied frame index that rather than anything this run interned.
        /// </param>
        public static void ApplyFrom(StructureBlock structure, Func<int, string> resolve)
        {
            createdCount = 0;
            destroyedCount = 0;
            unresolvedCount = 0;

            if (structure == null || resolve == null) return;

            _present.Clear();
            _presentIds.Clear();

            for (int i = 0; i < structure.count; i++)
            {
                var entry = structure[i];
                _present.Add(entry.id);

                // An element is not addressed by the registry -- it is reached through the member
                // holding it -- so it is reconciled by the walk below rather than by an id lookup
                // that would never find it and a recipe that does not know where to put what it
                // makes.
                if (entry.isElement || entry.isCollection) continue;

                var id = resolve(entry.id);
                if (string.IsNullOrEmpty(id)) continue;

                _presentIds.Add(id);

                if (LiveObjectRegistry.TryFindById(id, out _)) continue;

                _Create(id, resolve(entry.recipeId), resolve(entry.typeId));
            }

            _DestroyUnlisted();

            // After the registry pass: an element belongs to an object, and the object may be one
            // the pass above just stood up.
            _ReconcileElements(structure, resolve);
        }

        // Ids the inventory listed, as strings. The set of numbers beside it cannot be used for
        // this: those index the recording's table and the objects here are known by name.
        private static readonly HashSet<string> _presentIds = new HashSet<string>();

        // What the inventory says each collection should hold, gathered once per apply and keyed by
        // the pair that identifies a collection: the object holding it and the member it is. Reused
        // across frames -- a reconcile runs at every supplied frame head.
        private static readonly Dictionary<(string owner, string member), List<ObjectEntry>> _wanted =
            new Dictionary<(string, string), List<ObjectEntry>>();

        private static readonly Stack<List<ObjectEntry>> _spare = new Stack<List<ObjectEntry>>();

        /// <summary>Elements created by the most recent apply.</summary>
        public static int elementsCreated { get; private set; }

        /// <summary>Elements removed by the most recent apply.</summary>
        public static int elementsRemoved { get; private set; }

        /// <summary>Elements moved into the recorded order by the most recent apply.</summary>
        public static int elementsMoved { get; private set; }

        /// <summary>
        /// Stands collection elements back up, takes away the ones the inventory does not list, and
        /// puts the rest in the recorded order.
        ///
        /// Walks the world rather than the inventory, for the same reason the state lane does: an
        /// element is addressed through the member holding it, and only the walk knows which live
        /// object that member is on. The inventory is turned into a lookup first so the walk can ask
        /// "what should this collection hold" without scanning it per member.
        ///
        /// <para>
        /// ⚠ Unlike the registry pass above, this takes away elements it did not put there. A
        /// collection the owner opted into (see <see cref="IsRecordedCollection"/>) has its whole
        /// shape recorded, including what was in it before the take started -- otherwise "the
        /// operator deleted a row while recording" would not come back on a scrub.
        /// </para>
        /// </summary>
        private static void _ReconcileElements(StructureBlock structure, Func<int, string> resolve)
        {
            elementsCreated = 0;
            elementsRemoved = 0;
            elementsMoved = 0;

            _ReleaseWanted();

            // Collections first, so one that was recorded empty still gets a list -- an empty list
            // is what tells the walk below to empty the real thing, and no list at all is what tells
            // it to leave the collection alone.
            for (int i = 0; i < structure.count; i++)
            {
                var entry = structure[i];
                if (!entry.isCollection) continue;

                _ListFor(resolve(entry.parentId), resolve(entry.memberId));
            }

            for (int i = 0; i < structure.count; i++)
            {
                var entry = structure[i];
                if (!entry.isElement) continue;

                _ListFor(resolve(entry.parentId), resolve(entry.memberId)).Add(entry);
            }

            if (_wanted.Count == 0) return;

            foreach (var pair in _wanted) pair.Value.Sort(_ByOrdinal);

            _visited.Clear();

            foreach (var handle in LiveObjectRegistry.instances)
            {
                if (!handle.hasId || !handle.isValid) continue;

                var target = handle.target;
                if (target == null) continue;

                _ReconcileElementsOf(target, handle.id, resolve, depth: 0);
            }

            var scene = LiveObjectRoster.sceneComponents;
            for (int i = 0; i < scene.Count; i++)
            {
                var entry = scene[i];
                if (!entry.isAlive) continue;
                if (LiveObjectRegistry.HasAddress(entry.target)) continue;
                if (_visited.Contains(entry.target)) continue;

                _ReconcileElementsOf(entry.target, entry.id, resolve, depth: 0);
            }
        }

        private static readonly Comparison<ObjectEntry> _ByOrdinal =
            (a, b) => a.ordinal.CompareTo(b.ordinal);

        private static List<ObjectEntry> _ListFor(string owner, string member)
        {
            var slot = (owner ?? string.Empty, member ?? string.Empty);
            if (_wanted.TryGetValue(slot, out var list)) return list;

            list = _spare.Count > 0 ? _spare.Pop() : new List<ObjectEntry>();
            _wanted[slot] = list;
            return list;
        }

        private static void _ReleaseWanted()
        {
            foreach (var pair in _wanted)
            {
                pair.Value.Clear();
                _spare.Push(pair.Value);
            }

            _wanted.Clear();
        }

        private static void _ReconcileElementsOf(object target, string id, Func<int, string> resolve,
            int depth)
        {
            if (target == null || depth >= LiveStateSystem.kMaxNestingDepth) return;
            if (!LiveStateSystem.TryDescribe(target.GetType(), out var liveClass,
                    out var nested, out var collections)) return;

            _visited.Add(target);

            var members = liveClass.propertyTypes;

            for (int i = 0; i < nested.Length; i++)
            {
                var member = members[nested[i]];
                var value = LivePropertyUtility.GetValueRaw(target, in member);
                if (value == null) continue;

                _ReconcileElementsOf(value, LiveStateSystem.ComposeNestedId(id, member.name),
                    resolve, depth + 1);
            }

            for (int i = 0; i < collections.Length; i++)
            {
                var member = members[collections[i]];
                if (!IsRecordedCollection(member)) continue;

                if (!_wanted.TryGetValue((id, member.name), out var wanted)) continue;

                _ReconcileCollection(target, liveClass, in member, wanted, resolve);

                // Re-read: standing an element up replaces the array behind the member.
                if (!(LivePropertyUtility.GetValueRaw(target, in member) is IList list)) continue;

                for (int e = 0; e < list.Count; e++)
                {
                    var element = list[e];
                    if (element == null) continue;

                    _ReconcileElementsOf(element,
                        LiveStateSystem.ComposeElementId(id, member.name, element, e),
                        resolve, depth + 1);
                }
            }

            if (target is LiveGameObject gameObject)
            {
                _WalkComponents(gameObject, id, depth,
                    (component, componentId, d) =>
                        _ReconcileElementsOf(component, componentId, resolve, d));
            }
        }

        /// <summary>
        /// Brings one collection to the shape the inventory records: remove, create, then order.
        ///
        /// Matched by key where the element type declares one and by position where it does not.
        /// A keyed collection survives being reordered between the recording and the replay; a
        /// keyless one has nothing to survive on, so its elements are whatever sits at that spot.
        /// </summary>
        private static void _ReconcileCollection(object owner, LiveClass ownerClass,
            in LivePropertyType member, List<ObjectEntry> wanted, Func<int, string> resolve)
        {
            if (!(LivePropertyUtility.GetValueRaw(owner, in member) is IList list)) return;

            var handle = LiveObjectHandle.CreateUnregistered(ownerClass, owner);
            var property = new LiveProperty(member, handle, owner, member.name);

            // Out first, so what is left lines up with what is wanted and the walk that follows does
            // not have to step over rows on their way out. Backwards, because removing shifts the
            // rest down.
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var element = list[i];
                var key = element == null ? null : LiveStateSystem.KeyOf(element);

                var keep = key != null
                    ? _IndexOfKey(wanted, key, resolve) >= 0
                    : i < wanted.Count;

                if (keep) continue;

                if (property.RemoveAt(i)) elementsRemoved++;
            }

            if (!(LivePropertyUtility.GetValueRaw(owner, in member) is IList after)) return;

            // In: whatever the inventory names and reality does not have. Appended in recorded
            // order, which puts a keyless collection straight into shape.
            for (int w = 0; w < wanted.Count; w++)
            {
                var entry = wanted[w];
                var key = _KeyText(entry, resolve);

                if (key != null)
                {
                    if (_IndexOfElement(after, key) >= 0) continue;
                }
                else if (after.Count > w)
                {
                    continue;
                }

                var element = _MakeElement(resolve(entry.typeId), in member);
                if (element == null)
                {
                    unresolvedCount++;
                    continue;
                }

                if (key != null) _WriteKey(element, key);
                if (!property.Add(element)) continue;

                elementsCreated++;

                if (!(LivePropertyUtility.GetValueRaw(owner, in member) is IList grown)) return;
                after = grown;
            }

            // Order last. Only a keyed collection can be out of order at this point.
            for (int w = 0; w < wanted.Count && w < after.Count; w++)
            {
                var key = _KeyText(wanted[w], resolve);
                if (key == null) continue;

                var at = _IndexOfElement(after, key);
                if (at < 0 || at == w) continue;

                if (!property.Reorder(at, w)) continue;

                elementsMoved++;

                if (!(LivePropertyUtility.GetValueRaw(owner, in member) is IList moved)) return;
                after = moved;
            }
        }

        private static int _IndexOfKey(List<ObjectEntry> wanted, string key, Func<int, string> resolve)
        {
            for (int i = 0; i < wanted.Count; i++)
            {
                if (string.Equals(_KeyText(wanted[i], resolve), key, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// The entry's key, or null when it has none.
        ///
        /// Null and empty are not the same here: no key means "matched by position", and a resolver
        /// that cannot read an id hands back an empty string, which would otherwise read as a key
        /// every keyless element shares.
        /// </summary>
        private static string _KeyText(ObjectEntry entry, Func<int, string> resolve)
        {
            if (entry.keyId == FrameSymbolTable.kNone) return null;

            var key = resolve(entry.keyId);
            return string.IsNullOrEmpty(key) ? null : key;
        }

        private static int _IndexOfElement(IList list, string key)
        {
            for (int i = 0; i < list.Count; i++)
            {
                var element = list[i];
                if (element == null) continue;

                if (string.Equals(LiveStateSystem.KeyOf(element), key, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Makes the element the entry names, falling back to what the member declares.
        ///
        /// The recorded type is asked for first because a collection can be polymorphic: an
        /// operation list holds several concrete types, and making the declared base would stand up
        /// the wrong thing (or nothing, where the base is abstract).
        /// </summary>
        private static object _MakeElement(string typeName, in LivePropertyType member)
        {
            if (!string.IsNullOrEmpty(typeName))
            {
                var recorded = LiveClass.Find(typeName);
                if (recorded?.type != null)
                {
                    var made = LivePropertyUtility.CreateDefaultElement(recorded.type);
                    if (made != null) return made;
                }
            }

            var elementType = LivePropertyUtility.GetCollectionElementType(member.valueType);
            return elementType == null ? null : LivePropertyUtility.CreateDefaultElement(elementType);
        }

        private static void _WriteKey(object element, string key)
        {
            var liveClass = LiveClass.Find(element.GetType());
            var keyMember = liveClass?.keyProperty;
            if (keyMember == null) return;

            LivePropertyUtility.SetValueRaw(element, in keyMember, key);
        }

        private static void _Create(string id, string recipeKey, string typeName)
        {
            if (string.IsNullOrEmpty(recipeKey))
            {
                // Nothing said how to make it. Common and not an error: an object that was in the
                // scene from the start is recorded so its values have an owner, not so it can be
                // stood up somewhere else.
                return;
            }

            if (!LiveRecipes.TryGet(recipeKey, out var recipe))
            {
                unresolvedCount++;
                return;
            }

            var instance = recipe.Create(id, typeName);
            if (instance == null)
            {
                unresolvedCount++;
                return;
            }

            // Registered under the recorded id rather than one of its own choosing: the state lane
            // addresses it by that id, and an object under any other name is one no recorded value
            // can reach. The maker was handed the id for this reason, so nothing is renamed here --
            // a wrapper's name is the name of the object in the scene, and stamping an id over it
            // would rename what other things resolve by name (a transform reference, a bone path).
            var handle = LiveObjectRegistry.Create(instance.GetType(), instance, id);
            if (handle == null)
            {
                recipe.Destroy(instance);
                unresolvedCount++;
                return;
            }

            _made[id] = recipe;
            createdCount++;
        }

        private static void _DestroyUnlisted()
        {
            if (_made.Count == 0) return;

            _going.Clear();
            foreach (var pair in _made)
            {
                if (_presentIds.Contains(pair.Key)) continue;

                _going.Add(pair.Key);
            }

            for (int i = 0; i < _going.Count; i++)
            {
                var id = _going[i];
                var recipe = _made[id];
                _made.Remove(id);

                if (!LiveObjectRegistry.TryFindById(id, out var handle)) continue;

                var target = handle.target as ILiveObject;
                handle.Unregister();

                if (target != null) recipe.Destroy(target);
                destroyedCount++;
            }
        }

        /// <summary>
        /// The maker every collection element is attributed to.
        ///
        /// One key for all of them rather than one per element type: what stands an element back up
        /// is the same three steps whatever it is -- make the element type, write its key, put it in
        /// the member it belongs to -- and the type to make is already in the entry. A recipe per
        /// type would be the same code registered many times, each able to drift from the others.
        /// </summary>
        public const string kElementRecipe = "element";

        // Objects the walk has reached, so one addressable two ways is not walked twice.
        private static readonly HashSet<object> _visited = new HashSet<object>();

        /// <summary>
        /// Walks the exposed world for collection elements and puts each in the inventory.
        ///
        /// The same walk the state lane makes (<see cref="LiveStateSystem.TryDescribe"/> classifies
        /// the members, <see cref="LiveStateSystem.ComposeElementId"/> gives the address), so an
        /// element's entry and its values agree about what it is called. Two walks that disagreed
        /// would put a value at an address the inventory never mentions.
        ///
        /// Only elements are written down. A nested object and an exposed component are walked
        /// *through* -- a collection can sit inside either -- but neither goes in: both exist
        /// because their owner exists, which the owner's own entry already says.
        /// </summary>
        private static void _CaptureElements(StructureBlock structure, FrameSymbolTable symbols)
        {
            _visited.Clear();

            foreach (var handle in LiveObjectRegistry.instances)
            {
                if (!handle.hasId || !handle.isValid) continue;

                var target = handle.target;
                if (target == null) continue;

                _CaptureElementsOf(target, handle.id, structure, symbols, depth: 0);
            }

            var scene = LiveObjectRoster.sceneComponents;
            for (int i = 0; i < scene.Count; i++)
            {
                var entry = scene[i];
                if (!entry.isAlive) continue;
                if (LiveObjectRegistry.HasAddress(entry.target)) continue;
                if (_visited.Contains(entry.target)) continue;

                _CaptureElementsOf(entry.target, entry.id, structure, symbols, depth: 0);
            }
        }

        private static void _CaptureElementsOf(object target, string id, StructureBlock structure,
            FrameSymbolTable symbols, int depth)
        {
            if (target == null || depth >= LiveStateSystem.kMaxNestingDepth) return;
            if (!LiveStateSystem.TryDescribe(target.GetType(), out var liveClass,
                    out var nested, out var collections)) return;

            _visited.Add(target);

            var members = liveClass.propertyTypes;

            for (int i = 0; i < nested.Length; i++)
            {
                var member = members[nested[i]];
                var value = LivePropertyUtility.GetValueRaw(target, in member);
                if (value == null) continue;

                _CaptureElementsOf(value, LiveStateSystem.ComposeNestedId(id, member.name),
                    structure, symbols, depth + 1);
            }

            for (int i = 0; i < collections.Length; i++)
            {
                var member = members[collections[i]];
                if (!IsRecordedCollection(member)) continue;
                if (!(LivePropertyUtility.GetValueRaw(target, in member) is IList list)) continue;

                var parentId = symbols.Intern(id);
                var memberId = symbols.Intern(member.name);
                var recipeId = symbols.Intern(kElementRecipe);

                // The collection itself, so "recorded and empty" is a thing the file can say. Its
                // own address (no key, no position), and no recipe: a member is not something a
                // replay stands up -- it exists because its owner does.
                var collectionId = symbols.Intern(LiveStateSystem.ComposeNestedId(id, member.name));
                if (collectionId != FrameSymbolTable.kNone)
                {
                    _present.Add(collectionId);
                    structure.AddOrUpdate(collectionId, _ElementTypeId(in member, symbols),
                        parentId, FrameSymbolTable.kNone, memberId, FrameSymbolTable.kNone, -1);
                }

                for (int e = 0; e < list.Count; e++)
                {
                    var element = list[e];
                    if (element == null) continue;

                    var address = LiveStateSystem.ComposeElementId(id, member.name, element, e);
                    var elementId = symbols.Intern(address);
                    if (elementId == FrameSymbolTable.kNone) continue;

                    var key = LiveStateSystem.KeyOf(element);

                    _present.Add(elementId);
                    structure.AddOrUpdate(elementId, _TypeId(element, symbols),
                        parentId, recipeId, memberId,
                        key == null ? FrameSymbolTable.kNone : symbols.Intern(key), e);

                    _CaptureElementsOf(element, address, structure, symbols, depth + 1);
                }
            }

            // The exposed components of an exposed GameObject. Reached the way the state lane
            // reaches them -- through the owner rather than the roster -- because a collection can
            // sit on a component that has no address of its own, and the walk above would never
            // arrive at it.
            if (target is LiveGameObject gameObject)
            {
                _WalkComponents(gameObject, id, depth,
                    (component, componentId, d) =>
                        _CaptureElementsOf(component, componentId, structure, symbols, d));
            }
        }

        /// <summary>
        /// Runs <paramref name="visit"/> over the exposed components of a GameObject, each under the
        /// address its owner gives it.
        ///
        /// Skips a component the registry can address on its own: that object is walked from the
        /// registry, and walking it here as well would put one element in the inventory twice under
        /// two different addresses.
        /// </summary>
        private static void _WalkComponents(LiveGameObject owner, string ownerId,
            int depth, Action<object, string, int> visit)
        {
            if (!(owner.reference is UnityEngine.GameObject go) || go == null) return;

            var components = new List<UnityEngine.Component>();
            go.GetComponents(components);

            for (int i = 0; i < components.Count; i++)
            {
                var component = components[i];
                if (component == null) continue;
                if (LiveObjectRegistry.HasAddress(component)) continue;
                if (_visited.Contains(component)) continue;

                var key = ComponentElementKey.Of(component);
                if (key == null) continue;

                visit(component, LiveStateSystem.ComposeComponentId(ownerId, key), depth + 1);
            }
        }

        /// <summary>
        /// The name the collection's declared element type is exposed under.
        ///
        /// On the collection's own entry rather than a concrete type, because an empty collection
        /// has no element to ask. It is what the member says it holds, which is also what a reader
        /// wants to see next to an empty one.
        /// </summary>
        private static int _ElementTypeId(in LivePropertyType member, FrameSymbolTable symbols)
        {
            var elementType = LivePropertyUtility.GetCollectionElementType(member.valueType);
            if (elementType == null) return FrameSymbolTable.kNone;

            var liveClass = LiveClass.Find(elementType);
            var name = liveClass?.typeName ?? elementType.FullName;

            return string.IsNullOrEmpty(name) ? FrameSymbolTable.kNone : symbols.Intern(name);
        }

        /// <summary>
        /// The name the element type is exposed under, which is what a replay resolves it by.
        ///
        /// The exposed name rather than the CLR one, because that is the name a type answers to
        /// everywhere else the inventory is read (the registry pass records the same) -- and
        /// resolving is the whole point of carrying it: a polymorphic collection declares an
        /// abstract element type, so making what the member declares would stand up whichever
        /// concrete subtype happened to be found first.
        /// </summary>
        private static int _TypeId(object element, FrameSymbolTable symbols)
        {
            var liveClass = LiveClass.Find(element.GetType());
            var name = liveClass?.typeName ?? element.GetType().FullName;

            return string.IsNullOrEmpty(name) ? FrameSymbolTable.kNone : symbols.Intern(name);
        }

        /// <summary>
        /// Whether a collection's shape belongs in the inventory.
        ///
        /// The rule is the one the live scene already uses to decide what it saves: a member the
        /// scene writes has to be one a recording can put back, or opening a take would show a world
        /// the save file disagrees with. So a collection joins when it is persisted and can be
        /// written -- and stays out when it is a view of something else (a getter with no setter,
        /// rebuilt from the avatar every time it is asked for) or when it is declared off the frame.
        /// </summary>
        internal static bool IsRecordedCollection(LivePropertyType member)
        {
            if (member == null || member.isReadOnly || member.isStatic) return false;
            if (member.lane == FrameLane.None) return false;

            return member.isPersistable;
        }

        /// <summary>
        /// Reconciles the block against the registry. Exposed separately from the frame head so a
        /// caller can take an inventory without waiting for one.
        ///
        /// Returns how many objects are in it afterwards.
        /// </summary>
        public static int CaptureInto(StructureBlock structure, FrameSymbolTable symbols)
        {
            if (structure == null || symbols == null) return 0;

            _present.Clear();

            foreach (var handle in LiveObjectRegistry.instances)
            {
                // No id means nothing can address it, so it cannot be in an inventory that exists to
                // be addressed. Invalid means the object behind it is gone.
                if (!handle.hasId || !handle.isValid) continue;

                var id = symbols.Intern(handle.id);
                if (id == FrameSymbolTable.kNone) continue;

                _present.Add(id);

                structure.AddOrUpdate(id, symbols.Intern(handle.targetTypeName),
                    _ParentId(handle, symbols), _RecipeId(handle, symbols));
            }

            // Exposed scene components (see LiveObjectRoster) are deliberately not here. The
            // inventory is what a replay stands up or takes away, and one of those comes with the
            // scene: it is neither made nor destroyed by a recording, so listing it would put an
            // entry in every keyframe that nothing ever acts on. The state lane still addresses it,
            // by the type name it answers to -- an address that says what it is on its own.
            //
            // Their *elements* are another matter, and the walk below reaches them: a component
            // comes with the scene, but what sits in a collection on it is put there and taken away
            // while the take runs.

            _CaptureElements(structure, symbols);

            _RemoveMissing(structure);

            _owned.Clear();
            foreach (var id in _present) _owned.Add(id);

            return structure.count;
        }

        /// <summary>
        /// Takes out the entries this system put there that the registry no longer has. Walked back
        /// to front because removing shifts everything after it down, and a forward walk would step
        /// over the entry that moved up.
        /// </summary>
        private static void _RemoveMissing(StructureBlock structure)
        {
            if (_owned.Count == 0) return;

            for (int i = structure.count - 1; i >= 0; i--)
            {
                var id = structure[i].id;
                if (_present.Contains(id) || !_owned.Contains(id)) continue;

                structure.Remove(id);
            }
        }

        /// <summary>
        /// The key of whatever made this object, so a replay can make it again.
        ///
        /// Asked of the object rather than derived from its type: two objects of one type can come
        /// from different prefabs, and the type name would send a replay to whichever maker happened
        /// to be registered for it.
        /// </summary>
        private static int _RecipeId(LiveObjectHandle handle, FrameSymbolTable symbols)
        {
            var key = handle.target is ILiveMadeFromRecipe made ? made.recipeKey : null;
            return string.IsNullOrEmpty(key) ? FrameSymbolTable.kNone : symbols.Intern(key);
        }

        private static int _ParentId(LiveObjectHandle handle, FrameSymbolTable symbols)
        {
            var parent = handle.target is LiveUnityObjectBase proxy ? proxy.parentId : null;
            return string.IsNullOrEmpty(parent) ? FrameSymbolTable.kNone : symbols.Intern(parent);
        }
    }
}
