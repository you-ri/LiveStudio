// Copyright (c) You-Ri, 2026
using System.Collections.Generic;

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

            FrameGate.AddFrameHeadHandler(_OnFrameHead);
        }

        /// <summary>Gives it up. Stops once nobody wants it.</summary>
        public static void Release()
        {
            if (_users == 0 || --_users > 0) return;

            FrameGate.RemoveFrameHeadHandler(_OnFrameHead);
        }

        private static void _OnFrameHead(ref Frame frame)
        {
            // A supplied frame brought its own inventory. Writing ours over it would replace the
            // world being replayed with the world that happens to be loaded.
            if (frame.isSupplied)
            {
                _objectCount = frame.structure?.count ?? 0;
                if (applyOnSuppliedFrames) ApplyFrom(frame.structure, FrameGate.symbols);
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
            createdCount = 0;
            destroyedCount = 0;
            unresolvedCount = 0;

            if (structure == null || symbols == null) return;

            _present.Clear();

            for (int i = 0; i < structure.count; i++)
            {
                var entry = structure[i];
                _present.Add(entry.id);

                var id = symbols.Resolve(entry.id);
                if (string.IsNullOrEmpty(id)) continue;
                if (LiveObjectRegistry.TryFindById(id, out _)) continue;

                _Create(id, symbols.Resolve(entry.recipeId), symbols);
            }

            _DestroyUnlisted(symbols);
        }

        private static void _Create(string id, string recipeKey, FrameSymbolTable symbols)
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

            var instance = recipe.Create(id);
            if (instance == null)
            {
                unresolvedCount++;
                return;
            }

            // Registered under the recorded id rather than one of its own choosing: the state lane
            // addresses it by that id, and an object under any other name is one no recorded value
            // can reach.
            instance.name = id;

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

        private static void _DestroyUnlisted(FrameSymbolTable symbols)
        {
            if (_made.Count == 0) return;

            _going.Clear();
            foreach (var pair in _made)
            {
                if (_present.Contains(symbols.Intern(pair.Key))) continue;

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
