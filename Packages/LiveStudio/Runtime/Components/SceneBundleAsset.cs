// Copyright (c) You-Ri, 2026

using System;
using System.Threading.Tasks;

using UnityEngine.SceneManagement;

using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// A scene-bundle asset (<c>*.scene.lsb</c>) managed by <see cref="ExternalAssetManager"/>. Loads
    /// the bundle's single scene additively through <see cref="SceneBundleLoader"/> and owns the
    /// loaded bundle until unloaded.
    ///
    /// Additive and avatar-independent: many scenes can be loaded at once and an avatar swap does not
    /// affect them. Scene-specific orchestration (the active scene, the bootstrap/persistent entry)
    /// lives in <see cref="WorldManager"/>, which drives selection through this asset's
    /// <see cref="scene"/> handle. No exposed wrapper is created, so the <c>objectId</c> /
    /// <c>state</c> snapshot machinery of <see cref="AssetBase"/> is unused here.
    /// </summary>
    [Serializable]
    [ExposedClass("SceneBundleAsset", Category = "Asset", Icon = "public")]
    public class SceneBundleAsset : AssetBase
    {
        public override bool isExclusive => false;
        public override bool reloadsOnAvatarChange => false;

        /// <summary>
        /// True when this scene is the active scene. Persisted so the saved active scene is
        /// reactivated on restore once it has loaded. Written by <see cref="WorldManager"/> when the
        /// active scene changes; only one scene asset is active at a time (an invariant the manager
        /// enforces).
        /// </summary>
        [ExposedField]
        public bool isActive;

        // The loaded bundle + scene, owned until unload. Held only at runtime.
        [NonSerialized]
        private LoadedSceneBundle _loaded;

        /// <summary>The loaded scene handle, or <c>default</c> when not loaded. Used by the manager to
        /// set the active scene without looking the scene up by (possibly colliding) name.</summary>
        public Scene scene => _loaded != null ? _loaded.scene : default;

        /// <summary>True when a valid scene is currently loaded.</summary>
        public bool hasScene => _loaded != null && _loaded.scene.IsValid() && _loaded.scene.isLoaded;

        public override async Task LoadAsync(AssetLoadContext context)
        {
            var loader = new SceneBundleLoader();
            var loaded = await loader.LoadAsync(filePath);
            if (loaded == null)
            {
                // Reflect the failure back as disabled so the UI is not stuck "on".
                enabled = false;
                isLoaded = false;
                return;
            }

            _loaded = loaded;
            isLoaded = true;
        }

        public override void Unload(AssetLoadContext context)
        {
            // UnloadAsync is genuinely asynchronous (SceneManager.UnloadSceneAsync), but the base
            // Unload contract is synchronous; fire-and-forget the teardown. Restoring the active scene
            // to the bootstrap scene after an active scene unloads is handled by WorldManager.
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
