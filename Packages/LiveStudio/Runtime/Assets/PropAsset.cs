// Copyright (c) You-Ri, 2026

using System;
using System.Threading.Tasks;

using UnityEngine;

using Lilium.RemoteControl;
using Lilium.RemoteControl.LiveScene;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// A prop asset managed by <see cref="ExternalAssetManager"/>. Handles two payload kinds chosen
    /// by file extension:
    /// <list type="bullet">
    ///   <item><c>*.prop.lsb</c> — an avatar prop: instantiated under the current avatar and wrapped
    ///   WITHOUT an exposed transform, because its pose is bone-driven every frame by
    ///   <see cref="Prop"/>'s socket constraint (the live transform is computed output, not
    ///   authored state). Reloaded onto the new avatar when the avatar is swapped.</item>
    ///   <item><c>*.glb</c> / <c>*.gltf</c> — a free-standing scene prop: loaded via
    ///   <see cref="GltfModel"/> into a host object at the scene root and wrapped WITH an exposed
    ///   transform so the user can position it. Unaffected by avatar swaps.</item>
    /// </list>
    /// </summary>
    [Serializable]
    [LiveClass("PropAsset", Category = "Asset", Icon = "deployed_code")]
    public class PropAsset : AssetBase, IInstantiableProp
    {
        /// <summary>
        /// A preset is a note the app wrote and may delete; the bundle a preset points at is not ours
        /// (both arrive as this kind, but they come from different places).
        /// </summary>
        public override bool isAppOwnedFile => _isPreset;

        // Only avatar props (*.prop.lsb) live under the avatar and must be reloaded on an avatar swap.
        public override bool reloadsOnAvatarChange => _isAvatarAttached;

        // A *.preset.json entry loads a referenced source asset; everything else is the source itself.
        private bool _isPreset => PropPreset.IsPresetFile(filePath);

        // The asset actually loaded: the resolved source for a preset, otherwise filePath itself.
        // Null for a preset that has not been loaded yet (so suffix checks return false until loaded).
        private string _effectiveSourcePath => _isPreset ? _resolvedSourcePath : filePath;

        // *.prop.lsb attaches under the avatar; glTF is a free-standing scene prop.
        private bool _isAvatarAttached => LiveStudioBundle.IsPropBundle(_effectiveSourcePath);

        // The loaded prop instance and its source-agnostic registration/teardown (shared with
        // BuiltinPropAsset). Held only at runtime.
        [NonSerialized] private readonly LoadedProp _loaded = new LoadedProp();

        // Absolute source asset path resolved while loading a preset; null for a direct prop entry.
        [NonSerialized] private string _resolvedSourcePath;

        /// <summary>True if this entry is a preset (<c>*.preset.json</c>) referencing a source asset.</summary>
        internal bool isPreset => _isPreset;

        /// <summary>
        /// The source asset path this prop represents: the resolved referenced asset for a preset,
        /// otherwise the entry's own file path. Used when saving a new preset from a loaded prop.
        /// </summary>
        internal string sourceFilePath => _effectiveSourcePath;

        /// <summary>Serializes the loaded prop's current parameter delta (vs the source defaults).</summary>
        internal string CaptureDeltaState() => _loaded.CaptureDelta();

        public override async Task LoadAsync(AssetLoadContext context)
        {
            if (_isPreset)
            {
                await _LoadPresetAsync(context);
                return;
            }

            if (_isAvatarAttached)
            {
                await _LoadPropBundleAsync(context, filePath);
            }
            else
            {
                _LoadGltf(context, filePath);
            }
        }

        // --- Preset (*.preset.json) ---

        // Reads the preset, resolves its referenced source asset, seeds the live state from the saved
        // delta the first time, then delegates to the matching source loader. Once a live edit has been
        // captured into `state` (e.g. across an unload/reload), that captured state wins over the delta.
        private async Task _LoadPresetAsync(AssetLoadContext context)
        {
            if (!TryReadPreset("prop", out var preset, out var source)) return;

            _resolvedSourcePath = source;
            if (string.IsNullOrEmpty(state)) state = preset.state;

            if (LiveStudioBundle.IsPropBundle(source))
            {
                await _LoadPropBundleAsync(context, source);
            }
            else
            {
                _LoadGltf(context, source);
            }
        }

        public override void Unload(AssetLoadContext context)
        {
            // Snapshot current parameter values before destroying so they reapply on the next load.
            _loaded.Capture(this);
            _loaded.Destroy();
            isLoaded = false;
        }

        public override void CaptureState() => _loaded.Capture(this);

        // --- IInstantiableProp: spawn as scene instances from the live scene "+" ---

        // Only *.prop.lsb avatar props are re-instantiable prefabs. A free-standing glTF prop is covered by
        // the GLTF Model host, and a preset references a source rather than being one, so both are excluded.
        public bool supportsInstancing => _isAvatarAttached && !_isPreset;

        // The portable, project-relative reference is the @prefab key; on restore the deferred prefab
        // provider resolves it back to this asset via ExternalAssetManager.FindAssetByReference.
        public string instanceKey
        {
            get
            {
                var source = sourceFilePath;
                if (string.IsNullOrEmpty(source)) return source;
                var relative = PropPreset.Relativize(source, ProjectManager.projectPath);
                return string.IsNullOrEmpty(relative) ? source : relative;
            }
        }

        public Task<GameObject> LoadInstancePrefabAsync() => PropBundleLoader.GetPrefabAsync(sourceFilePath);

        // --- Avatar prop (*.prop.lsb) ---

        private async Task _LoadPropBundleAsync(AssetLoadContext context, string sourcePath)
        {
            var avatarRoot = context?.avatarRoot;
            if (avatarRoot == null)
            {
                // No avatar yet: stay enabled and retry when the avatar becomes available
                // (the manager re-diffs on avatar change / late service binding).
                return;
            }

            var loader = new PropBundleLoader();
            var instance = await loader.LoadAsync(sourcePath, avatarRoot);
            if (instance == null)
            {
                // Reflect the failure back as disabled so the UI is not stuck "on".
                MarkLoadFailed();
                return;
            }

            if (_loaded.hasInstance)
            {
                // A concurrent load already registered this prop (e.g. an avatar swap cleared busy
                // mid-load); discard the duplicate instead of leaking it.
                UnityEngine.Object.Destroy(instance);
                return;
            }

            // Wrap so the remote app can control the prop component. No transform is exposed: the pose
            // is socket-driven, so the authored state lives on Prop, not the GameObject transform.
            _loaded.Register(context, new LiveGameObject(instance), instance, this);
        }

        // --- Free-standing scene prop (*.glb / *.gltf) ---

        private void _LoadGltf(AssetLoadContext context, string sourcePath)
        {
#if UNITY_GLTFAST
            // Build inactive so setting the path does not kick off a load before the wrapper is wired;
            // GltfModel.Start performs a single load once the host is activated below.
            var host = new GameObject(string.IsNullOrEmpty(name) ? "Prop" : name);
            host.SetActive(false);
            var model = host.AddComponent<GltfModel>();
            model.path = sourcePath;

            // Place the free-standing prop in the base scene (where its RemoteControlContainer lives), not
            // the active scene which may be a set bundle. new GameObject() lands in the active scene, so
            // move it explicitly; the GameObject is a root here, which MoveGameObjectToScene requires.
            var baseContainer = context?.container;
            if (baseContainer != null)
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(host, baseContainer.gameObject.scene);

            // Expose the transform so the user can position the free-standing prop from the remote app.
            _loaded.Register(context, new LiveGameObjectWithTransform(host), host, this);

            host.SetActive(true);
#else
            Debug.LogError("[LiveStudio] glTF props require the glTFast package (UNITY_GLTFAST define).");
            MarkLoadFailed();
#endif
        }

    }
}
