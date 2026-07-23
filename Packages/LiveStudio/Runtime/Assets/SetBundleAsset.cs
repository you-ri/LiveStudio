// Copyright (c) You-Ri, 2026

using System;
using System.Threading.Tasks;

using UnityEngine.SceneManagement;

using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// A set-bundle asset (<c>*.set.lsb</c>) managed by <see cref="ExternalAssetManager"/>. Loads
    /// the bundle's single scene additively through <see cref="SetBundleLoader"/> and owns the
    /// loaded bundle until unloaded.
    ///
    /// Additive and avatar-independent: many sets can be loaded at once and an avatar swap does not
    /// affect them. Set-specific orchestration (the active set, the bootstrap/persistent entry)
    /// lives in <see cref="StageManager"/>, which drives selection through this asset's
    /// <see cref="scene"/> handle. No exposed wrapper is created, so the <c>objectId</c> /
    /// <c>state</c> snapshot machinery of <see cref="AssetBase"/> is unused here.
    /// </summary>
    [Serializable]
    [ExposedClass("SetBundleAsset", Category = "Asset", Icon = "public")]
    public class SetBundleAsset : AssetBase, ISetAsset
    {
        public override bool isExclusive => false;
        public override bool reloadsOnAvatarChange => false;

        /// <summary>
        /// True when this set is the active set. Persisted so the saved active set is
        /// reactivated on restore once it has loaded. Written by <see cref="StageManager"/> when the
        /// active set changes; only one set asset is active at a time (an invariant the manager
        /// enforces).
        /// </summary>
        [ExposedField]
        public bool isActive;

        // ISetAsset.isActive delegates to the exposed field (a field cannot implement the property
        // directly). scene / hasScene below already satisfy the interface implicitly.
        bool ISetAsset.isActive { get => isActive; set => isActive = value; }

        // The loaded bundle + scene, owned until unload. Held only at runtime.
        [NonSerialized]
        private LoadedSetBundle _loaded;

        /// <summary>The loaded scene handle, or <c>default</c> when not loaded. Used by the manager to
        /// set the active scene without looking the scene up by (possibly colliding) name.</summary>
        public Scene scene => _loaded != null ? _loaded.scene : default;

        /// <summary>True when a valid scene is currently loaded.</summary>
        public bool hasScene => _loaded != null && _loaded.scene.IsValid() && _loaded.scene.isLoaded;

        public override async Task LoadAsync(AssetLoadContext context)
        {
            var loader = new SetBundleLoader();
            var loaded = await loader.LoadAsync(filePath);
            if (loaded == null)
            {
                // Reflect the failure back as disabled so the UI is not stuck "on".
                MarkLoadFailed();
                return;
            }

            _loaded = loaded;
            isLoaded = true;
        }

        public override void Unload(AssetLoadContext context)
        {
            // UnloadAsync is genuinely asynchronous (SceneManager.UnloadSceneAsync), but the base
            // Unload contract is synchronous; fire-and-forget the teardown. Restoring the active scene
            // to the bootstrap scene after an active set unloads is handled by StageManager.
            if (_loaded != null)
            {
                _ = _loaded.UnloadAsync();
                _loaded = null;
            }
            isActive = false;
            isLoaded = false;
        }
    }
}
