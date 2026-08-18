// Copyright (c) You-Ri, 2026

using System;
using System.Threading.Tasks;

using UnityEngine;
using UnityEngine.SceneManagement;

using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// A built-in set: a Unity scene shipped inside the app (present in the build's scene list) that loads
    /// additively as a "set", exactly like an external <c>*.set.lsb</c> (<see cref="SetBundleAsset"/>) but
    /// with no bundle and no bake step. The set list is discovered at runtime from the build's scene list
    /// (see <see cref="BuiltinSetSource"/>), so shipping a stage scene is nothing more than adding it to
    /// the build — no marker asset or Resources folder, unlike a built-in prop.
    ///
    /// Scenes cannot be loaded from a <c>Resources</c> folder, so — unlike <see cref="BuiltinPropAsset"/>
    /// (Resources) — this loads through <see cref="SceneManager"/> by the scene's build path. The path is
    /// the stable identity persisted to the live scene (there is no runtime GUID without a bake), and the
    /// runtime <see cref="AssetBase.id"/> is reconstructed from it on restore.
    ///
    /// Implements <see cref="ISetAsset"/> so <see cref="StageManager"/> orchestrates it (active set, warps)
    /// through the same path as a bundle set. Additive and avatar-independent; a single active set at a
    /// time is an invariant StageManager enforces.
    /// </summary>
    [Serializable]
    [LiveClass("BuiltinSetAsset", Category = "Asset", Icon = "public")]
    public class BuiltinSetAsset : AssetBase, ISetAsset
    {
        /// <summary>
        /// The scene's build path (e.g. <c>Assets/Scenes/Stage.unity</c>). The stable identity persisted to
        /// the live scene (the runtime <see cref="AssetBase.id"/> is reconstructed from it) and the handle
        /// <see cref="SceneManager.LoadSceneAsync(string, LoadSceneParameters)"/> loads the scene by.
        /// </summary>
        [LiveField, Hide]
        public string scenePath;

        /// <summary>
        /// True when this set is the active set. Persisted so the saved active set is reactivated on
        /// restore once it has loaded. Written by <see cref="StageManager"/>.
        /// </summary>
        [LiveField]
        public bool isActive;

        public override bool reloadsOnAvatarChange => false;
        public override bool isBuiltin => true;

        // No project file: restore the runtime id from the persisted scene path so the re-enumerated
        // built-in set and the persisted one dedup to a single entry.
        public override string persistentId => scenePath;

        // The additively-loaded scene, owned until unload. Held only at runtime.
        [NonSerialized]
        private Scene _loadedScene;

        /// <summary>The loaded scene handle, or <c>default</c> when not loaded.</summary>
        public Scene scene => _loadedScene;

        /// <summary>True when a valid scene is currently loaded.</summary>
        public bool hasScene => _loadedScene.IsValid() && _loadedScene.isLoaded;

        // ISetAsset.isActive delegates to the exposed field (a field cannot implement the property
        // directly). scene / hasScene satisfy the interface implicitly.
        bool ISetAsset.isActive { get => isActive; set => isActive = value; }

        public override async Task LoadAsync(AssetLoadContext context)
        {
            if (hasScene) { isLoaded = true; return; }
            if (string.IsNullOrEmpty(scenePath))
            {
                Debug.LogError("[LiveStudio] Built-in set has no scene path.");
                MarkLoadFailed();
                return;
            }

            // Capture the exact Scene handle for this load. Names can collide, so we never look the
            // scene up by name afterwards — we hold the handle captured here (mirrors SetBundleLoader).
            Scene captured = default;
            void OnSceneLoaded(Scene s, LoadSceneMode mode) => captured = s;
            SceneManager.sceneLoaded += OnSceneLoaded;
            try
            {
                await SetBundleLoader.AwaitOperation(
                    SceneManager.LoadSceneAsync(scenePath, new LoadSceneParameters(LoadSceneMode.Additive)));
            }
            finally
            {
                SceneManager.sceneLoaded -= OnSceneLoaded;
            }

            if (!captured.IsValid())
            {
                Debug.LogError($"[LiveStudio] Failed to load built-in set scene (not in the build's scene list?): '{scenePath}'.");
                MarkLoadFailed();
                return;
            }

            _loadedScene = captured;
            isLoaded = true;
        }

        public override void Unload(AssetLoadContext context)
        {
            // UnloadSceneAsync is genuinely asynchronous but the base Unload contract is synchronous;
            // fire-and-forget the teardown. Restoring the active scene to the bootstrap scene after an
            // active set unloads is handled by StageManager.
            if (hasScene) _ = SceneManager.UnloadSceneAsync(_loadedScene);
            _loadedScene = default;
            isActive = false;
            isLoaded = false;
        }
    }
}
