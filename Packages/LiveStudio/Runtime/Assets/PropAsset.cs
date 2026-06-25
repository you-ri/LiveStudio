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
    [ExposedClass("PropAsset", Category = "Asset", Icon = "deployed_code")]
    public class PropAsset : AssetBase
    {
        public override bool isExclusive => false;

        // Only avatar props (*.prop.lsb) live under the avatar and must be reloaded on an avatar swap.
        public override bool reloadsOnAvatarChange => _isAvatarAttached;

        // A *.preset.json entry loads a referenced source asset; everything else is the source itself.
        private bool _isPreset => PropPreset.IsPresetFile(filePath);

        // The asset actually loaded: the resolved source for a preset, otherwise filePath itself.
        // Null for a preset that has not been loaded yet (so suffix checks return false until loaded).
        private string _effectiveSourcePath => _isPreset ? _resolvedSourcePath : filePath;

        // *.prop.lsb attaches under the avatar; glTF is a free-standing scene prop.
        private bool _isAvatarAttached => LiveStudioBundle.IsPropBundle(_effectiveSourcePath);

        [NonSerialized] private GameObject _instance;
        [NonSerialized] private ExposedGameObject _exposed;
        [NonSerialized] private RemoteControlContainer _container;

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
        internal string CaptureDeltaState()
        {
            if (_instance == null) return null;
            return AssetStateSnapshot.CaptureDelta(_exposed, _instance);
        }

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
            CaptureState();
            _DestroyLoaded();
            isLoaded = false;
        }

        public override void CaptureState()
        {
            if (_instance == null) return;
            var json = AssetStateSnapshot.Capture(_exposed, _instance);
            if (!string.IsNullOrEmpty(json)) state = json;
        }

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

            if (_instance != null)
            {
                // A concurrent load already registered this prop (e.g. an avatar swap cleared busy
                // mid-load); discard the duplicate instead of leaking it.
                UnityEngine.Object.Destroy(instance);
                return;
            }

            // Wrap so the remote app can control the prop component. No transform is exposed: the pose
            // is socket-driven, so the authored state lives on Prop, not the GameObject transform.
            _Register(context, new ExposedGameObject(instance), instance);
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
            _Register(context, new ExposedGameObjectWithTransform(host), host);

            host.SetActive(true);
#else
            Debug.LogError("[LiveStudio] glTF props require the glTFast package (UNITY_GLTFAST define).");
            MarkLoadFailed();
#endif
        }

        // --- Shared registration / teardown ---

        // Wraps the loaded instance, re-keys the wrapper to the persisted objectId so the remote app's
        // reference survives unload/reload, registers it in the container, and reapplies saved state.
        private void _Register(AssetLoadContext context, ExposedGameObject exposed, GameObject instance)
        {
            var container = context?.container;
            if (container == null)
            {
                Debug.LogWarning("[LiveStudio] No base-scene RemoteControlContainer found; prop loaded but not remote-controllable.");
            }
            else
            {
                if (string.IsNullOrEmpty(objectId)) objectId = Guid.NewGuid().ToString();
                exposed.ReplaceId(objectId);
                container._objects.Add(exposed);
                // The container's source list is already initialized by the host, so call OnEnable manually.
                exposed.OnEnable();
            }

            _instance = instance;
            _exposed = exposed;
            _container = container;
            isLoaded = true;

            // Record the freshly loaded values as the delta baseline (the source asset's defaults)
            // BEFORE reapplying saved state, so a later "save as preset" captures only the user's edits.
            AssetStateSnapshot.CaptureDefaults(_exposed, _instance);
            AssetStateSnapshot.Restore(state, _exposed, _instance);
        }

        // Unregisters the exposed wrapper from its container and destroys the prop instance, dropping
        // any exposed-object handles created for the root's components so the registry retains no
        // dangling targets after the GameObject is destroyed.
        private void _DestroyLoaded()
        {
            if (_exposed != null)
            {
                _exposed.OnDisable();
                if (_container != null) _container._objects.Remove(_exposed);
            }
            if (_instance != null)
            {
                foreach (var comp in _instance.GetComponents<Component>())
                {
                    if (comp == null || !ExposedClass.Has(comp.GetType())) continue;
                    ExposedObjectRegistry.FindByTarget(comp)?.Unregister();
                }
                UnityEngine.Object.Destroy(_instance);
            }
            _instance = null;
            _exposed = null;
            _container = null;
        }
    }
}
