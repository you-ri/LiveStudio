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
    /// (Resources) — this loads through <see cref="SceneManager"/> by the scene's build path. Only the
    /// <see cref="guid"/> is persisted, exactly as a built-in prop persists its catalog GUID: the path is
    /// re-resolved from the declaration each run, so moving or renaming the scene does not break a saved
    /// live scene.
    ///
    /// Derives from <see cref="SetAssetBase"/> so <see cref="StageManager"/> orchestrates it (active set,
    /// warps) through the same path as a bundle set. Additive and avatar-independent; a single active set
    /// at a time is an invariant StageManager enforces.
    /// </summary>
    [Serializable]
    [LiveClass("BuiltinSetAsset", Category = "Asset", Icon = "public", lane = FrameLane.None)]
    public class BuiltinSetAsset : SetAssetBase
    {
        /// <summary>
        /// The declared scene asset's GUID: this entry's stable identity, and the only field it persists.
        /// Everything else about the set (where the scene sits, what it is called, its preview) is read
        /// back from <see cref="BuiltinSetList"/> under this GUID on the next run.
        /// </summary>
        [LiveField, Hide]
        public string guid;

        /// <summary>
        /// The scene's build path (e.g. <c>Assets/Scenes/Stage.unity</c>), which
        /// <see cref="SceneManager.LoadSceneAsync(string, LoadSceneParameters)"/> loads the scene by.
        /// Deliberately NOT persisted: it is a cache of where the identity currently points, so a scene
        /// moved between sessions still loads. Re-resolved from <see cref="guid"/> when the catalog entry
        /// is (re-)injected.
        /// </summary>
        [LiveField(persistable = false), Hide]
        public string scenePath;

        public override bool isBuiltin => true;

        // No project file: the persisted identity is the scene GUID. A live scene saved before the identity
        // became a GUID persisted the scene path instead (and no guid), so fall back to looking that path up
        // in the declaration — otherwise the restored entry would have no id at all and could neither dedup
        // against the declared entry nor ever load.
        public override string persistentId
            => !string.IsNullOrEmpty(guid) ? guid : BuiltinSetSource.ResolveGuidByScenePath(scenePath);

        // The additively-loaded scene, owned until unload. Held only at runtime.
        [NonSerialized]
        private Scene _loadedScene;

        /// <summary>The loaded scene handle, or <c>default</c> when not loaded.</summary>
        public override Scene scene => _loadedScene;

        /// <summary>True when a valid scene is currently loaded.</summary>
        public override bool hasScene => _loadedScene.IsValid() && _loadedScene.isLoaded;

        /// <summary>
        /// Re-reads the name and scene path from the declaration, so a set renamed or moved in the editor
        /// takes effect on an entry that came back from a saved live scene (which persists only the GUID,
        /// and whose stored name is whatever it was called when it was saved). Also back-fills the GUID for
        /// an entry restored from the older path-based identity. No-op once the declaration no longer lists
        /// this set — the entry then keeps what it was restored with and simply fails to load.
        /// </summary>
        public void RefreshFromDeclaration()
        {
            var id = persistentId;
            if (!BuiltinSetSource.TryFind(id, out var entry)) return;

            guid = entry.guid;
            scenePath = entry.scenePath;
            name = BuiltinSetSource.ResolveName(entry);
        }

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
