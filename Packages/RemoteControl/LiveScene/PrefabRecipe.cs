// Copyright (c) You-Ri, 2026
using System.Collections.Generic;
using UnityEngine;
using Lilium.RemoteControl.Frames;

namespace Lilium.RemoteControl.LiveScene
{
    /// <summary>
    /// Rebuilds a prefab-made live object for a replay, from the same key a saved scene writes as
    /// <c>@prefab</c>.
    ///
    /// The keys are prefab guids and the set of them is open -- a project's own prefabs, the built-in
    /// catalogue, whatever an external bundle brings -- so these are resolved on demand through
    /// <see cref="LiveRecipes.RegisterResolver"/> rather than registered one by one. A key resolves
    /// exactly when <see cref="PrefabRegistry"/> can find the prefab, which is the same condition
    /// that decides whether a saved scene can restore it.
    ///
    /// Deliberately built on the scene-restore path rather than beside it: an object a save can
    /// rebuild and an object a recording can rebuild have to be the same object, and two ways of
    /// making it would be two answers that drift apart.
    /// </summary>
    public sealed class PrefabRecipe : ILiveRecipe
    {
        private readonly string _prefabKey;

        private PrefabRecipe(string prefabKey)
        {
            _prefabKey = prefabKey;
        }

        /// <summary>The prefab guid this makes instances of. What a recording carries as the key.</summary>
        public string prefabKey => _prefabKey;

        /// <inheritdoc/>
        public ILiveObject Create(string id, string typeName)
        {
            if (!PrefabRegistry.TryFind(_prefabKey, out var prefab) || prefab == null)
            {
                // The bundle holding it is not loaded yet. Null rather than an exception: the
                // reconcile counts it and the object appears once the asset does.
                return null;
            }

            return LiveSceneSerializer.InstantiatePrefabInstance(prefab, _prefabKey, id, typeName);
        }

        /// <inheritdoc/>
        public void Destroy(ILiveObject instance)
        {
            if (instance == null) return;

            // The same order the delete button uses: let the object take itself down, take it out of
            // the container, then take away what it was wrapping. Skipping the container leaves an
            // entry pointing at a destroyed object, which reads as a live object that answers nothing.
            instance.OnDispose();
            LiveSceneSerializer.ForgetPrefabInstance(instance);

            if (!(instance is LiveUnityObjectBase wrapper) || wrapper.reference == null) return;

            var go = wrapper.reference as GameObject ?? (wrapper.reference as Component)?.gameObject;
            if (go != null) GameObjectUtility.Destroy(go);
        }

        /// <summary>
        /// Makes prefab keys resolvable as recipes, so a replay can stand up anything a save could.
        ///
        /// Registered at startup rather than by whoever owns a prefab: the answer is the same for
        /// every key, and one resolver is what keeps it that way as catalogues come and go.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Install()
        {
            _byKey.Clear();
            LiveRecipes.RegisterResolver(_Resolve);
        }

#if UNITY_EDITOR
        // The recorder also runs in an editor that is not playing, where the runtime hook never fires.
        [UnityEditor.InitializeOnLoadMethod]
        private static void _InstallInEditor() => Install();
#endif

        // One recipe per key, kept so the reconcile compares the same maker across frames (it holds
        // on to what made each object, and a fresh instance every lookup would defeat that).
        private static readonly Dictionary<string, PrefabRecipe> _byKey =
            new Dictionary<string, PrefabRecipe>();

        private static ILiveRecipe _Resolve(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (_byKey.TryGetValue(key, out var recipe)) return recipe;

            // Only for a key that names a prefab something can actually find. Handing back a recipe
            // for any key at all would turn "nothing knows how to make this" into "it was made and
            // then failed", which counts as a different thing in the reconcile.
            if (!PrefabRegistry.TryFind(key, out var prefab) || prefab == null) return null;

            recipe = new PrefabRecipe(key);
            _byKey[key] = recipe;
            return recipe;
        }
    }
}
