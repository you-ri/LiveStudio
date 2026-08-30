// Copyright (c) You-Ri, 2026
using System.Collections.Generic;
using UnityEngine;

namespace Lilium.RemoteControl.Frames
{
    /// <summary>
    /// How to make one kind of object again, and how to take it away.
    ///
    /// A replay that scrubs past a spawn has to stand the thing back up, and the inventory alone
    /// cannot: it records what exists, not how it came to. This is the other half -- registered by
    /// whoever owns the object kind, and named by a key the inventory carries.
    ///
    /// Deliberately not the same thing as <see cref="ILiveObjectFactory"/>. A factory is a button on
    /// a page: it is authored per scene, picks its own id, and reports to the UI. A recipe is
    /// addressed by a key that was written into a file possibly on another machine, and has to make
    /// the object under an id it is handed rather than one it chooses. Something can be both, and
    /// the object kinds that are usually are.
    /// </summary>
    public interface ILiveRecipe
    {
        /// <summary>
        /// Makes one, to be registered under <paramref name="id"/> by the caller.
        ///
        /// The id is handed in rather than chosen because a replay is reproducing a specific object,
        /// and the state lane addresses it by that id. An implementation that names it something
        /// else produces an object no recorded value can reach.
        ///
        /// Null is allowed and means it could not be made -- an asset that is not loaded, a prefab
        /// that has been removed. The reconcile counts it rather than throwing: one object that
        /// cannot be rebuilt should not stop the rest of the world from being.
        /// </summary>
        ILiveObject Create(string id);

        /// <summary>
        /// Takes one away. Called with an object this recipe made, once the recording stops listing
        /// it.
        /// </summary>
        void Destroy(ILiveObject instance);
    }

    /// <summary>
    /// The makers a replay can reach, by key.
    ///
    /// A plain static table rather than something scanned from attributes: what can be made depends
    /// on what is loaded -- an external bundle brings its own kinds with it -- so the set has to be
    /// able to change while running.
    /// </summary>
    public static class LiveRecipes
    {
        private static readonly Dictionary<string, ILiveRecipe> _recipes =
            new Dictionary<string, ILiveRecipe>();

        /// <summary>Keys currently registered.</summary>
        public static IReadOnlyCollection<string> keys => _recipes.Keys;

        /// <summary>
        /// Registers a maker under a key. The key is what gets written into a recording, so it has
        /// to mean the same thing in the next run: a prefab guid or a stable name, never something
        /// derived from load order or an instance id.
        /// </summary>
        public static void Register(string key, ILiveRecipe recipe)
        {
            if (string.IsNullOrEmpty(key) || recipe == null) return;

            // Replaced rather than refused: a domain reload re-runs registration, and the second
            // one is the live object. Warned about only when it is a different maker, which is the
            // case that means two things claimed one key.
            if (_recipes.TryGetValue(key, out var existing) && !ReferenceEquals(existing, recipe))
            {
                Debug.LogWarning(
                    $"[RemoteControl] Two makers claim the recipe '{key}'. A recording naming it " +
                    "will be rebuilt by whichever registered last.");
            }

            _recipes[key] = recipe;
        }

        public static void Unregister(string key)
        {
            if (string.IsNullOrEmpty(key)) return;

            _recipes.Remove(key);
        }

        public static bool TryGet(string key, out ILiveRecipe recipe)
        {
            recipe = null;
            return !string.IsNullOrEmpty(key) && _recipes.TryGetValue(key, out recipe);
        }

        /// <summary>Forgets every maker. For tests, and for tearing a session down.</summary>
        public static void Clear() => _recipes.Clear();
    }

    /// <summary>
    /// Implemented by an object that knows how it was made, so the inventory can record it.
    ///
    /// Asked of the object rather than worked out from its type: two objects of one type can come
    /// from different prefabs, and only the object knows which.
    /// </summary>
    public interface ILiveMadeFromRecipe
    {
        /// <summary>
        /// The key of the maker that produced this, or null for something that was not made by one
        /// -- an object that was in the scene from the start.
        /// </summary>
        string recipeKey { get; }
    }
}
