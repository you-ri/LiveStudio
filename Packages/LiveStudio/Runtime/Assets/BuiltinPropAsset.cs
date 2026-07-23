// Copyright (c) You-Ri, 2026

using System;
using System.Threading.Tasks;

using UnityEngine;

using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// A loadable app-embedded (built-in) prop: a prefab shipped inside the app under a <c>Resources</c>
    /// folder whose root carries an <see cref="IProp"/> component. Baked into
    /// <see cref="BuiltinAssetCatalog"/> and injected by <see cref="ExternalAssetManager"/> so the end
    /// user's own prefab loads and unloads exactly like an external <c>*.prop.lsb</c> avatar prop — no
    /// AssetBundle build step, just "drop it under Resources and add a prop component".
    ///
    /// Unlike the reference-only <see cref="BuiltinAssetBase"/> kinds (e.g. an animation clip), this
    /// instantiates scene content, so it extends <see cref="AssetBase"/> directly and is
    /// <see cref="AssetBase.isPersistable"/> (its enabled/loaded state and objectId are written to the
    /// live scene, so its saved parameter overrides reattach on restore). The catalog entry itself is
    /// still re-injected each run (<see cref="AssetBase.isBuiltin"/>); only the in-use state persists,
    /// keyed by <see cref="persistentId"/> (the catalog GUID). The instantiation and remote-control
    /// wiring are shared with <see cref="PropAsset"/> through <see cref="LoadedProp"/>.
    /// </summary>
    [Serializable]
    [ExposedClass("BuiltinPropAsset", Category = "Asset", Icon = "deployed_code")]
    public class BuiltinPropAsset : AssetBase
    {
        /// <summary>
        /// The catalog GUID this entry loads. The stable identity persisted to the live scene (the runtime
        /// <see cref="AssetBase.id"/> is reconstructed from it on restore) and the key
        /// <see cref="BuiltinAssetRegistry.LoadPrefab"/> resolves the Resources prefab by.
        /// </summary>
        [ExposedField, Hide]
        public string guid;

        public override bool isExclusive => false;

        // Built-in props carry an IProp root, so they live under the avatar and must be reloaded onto the
        // new avatar when it is swapped (same as an external *.prop.lsb avatar prop).
        public override bool reloadsOnAvatarChange => true;

        public override bool isBuiltin => true;

        // Not backed by a project file: restore id from the persisted GUID so the re-injected catalog
        // entry and the persisted one dedup to a single entry.
        public override string persistentId => guid;

        // The loaded instance and its source-agnostic registration/teardown (shared with PropAsset).
        [NonSerialized] private readonly LoadedProp _loaded = new LoadedProp();

        public override Task LoadAsync(AssetLoadContext context)
        {
            var avatarRoot = context?.avatarRoot;
            if (avatarRoot == null)
            {
                // No avatar yet: stay enabled and retry when the avatar becomes available (the manager
                // re-diffs on avatar change / late service binding), like an external avatar prop.
                return Task.CompletedTask;
            }

            var prefab = BuiltinAssetRegistry.LoadPrefab(guid);
            if (prefab == null)
            {
                Debug.LogError($"[LiveStudio] Built-in prop prefab not found in Resources (guid '{guid}').");
                MarkLoadFailed();
                return Task.CompletedTask;
            }

            var instance = UnityEngine.Object.Instantiate(prefab, avatarRoot, worldPositionStays: false);
            instance.name = prefab.name;

            // Wrap WITHOUT an exposed transform: the pose is socket-driven by the IProp component, so the
            // authored state lives there, not on the GameObject transform — identical to an external
            // *.prop.lsb avatar prop. LoadAsync is synchronous (Resources.Load), so there is no concurrent
            // half-load window and no duplicate-instance guard is needed.
            _loaded.Register(context, new ExposedGameObject(instance), instance, this);
            return Task.CompletedTask;
        }

        public override void Unload(AssetLoadContext context)
        {
            // Snapshot current parameter values before destroying so they reapply on the next load.
            _loaded.Capture(this);
            _loaded.Destroy();
            isLoaded = false;
        }

        public override void CaptureState() => _loaded.Capture(this);
    }
}
