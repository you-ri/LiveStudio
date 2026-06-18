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
    ///   <see cref="AvatarProp"/>'s socket constraint (the live transform is computed output, not
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

        // *.prop.lsb attaches under the avatar; glTF is a free-standing scene prop.
        private bool _isAvatarAttached => LiveStudioBundle.IsPropBundle(filePath);

        [NonSerialized] private GameObject _instance;
        [NonSerialized] private ExposedGameObject _exposed;
        [NonSerialized] private RemoteControlContainer _container;

        public override async Task LoadAsync(AssetLoadContext context)
        {
            if (_isAvatarAttached)
            {
                await _LoadPropBundleAsync(context);
            }
            else
            {
                _LoadGltf(context);
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

        private async Task _LoadPropBundleAsync(AssetLoadContext context)
        {
            var avatarRoot = context?.avatarRoot;
            if (avatarRoot == null)
            {
                // No avatar yet: stay enabled and retry when the avatar becomes available
                // (the manager re-diffs on avatar change / late service binding).
                return;
            }

            var loader = new PropBundleLoader();
            var instance = await loader.LoadAsync(filePath, avatarRoot);
            if (instance == null)
            {
                // Reflect the failure back as disabled so the UI is not stuck "on".
                enabled = false;
                isLoaded = false;
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
            // is socket-driven, so the authored state lives on AvatarProp, not the GameObject transform.
            _Register(context, new ExposedGameObject(instance), instance);
        }

        // --- Free-standing scene prop (*.glb / *.gltf) ---

        private void _LoadGltf(AssetLoadContext context)
        {
#if UNITY_GLTFAST
            // Build inactive so setting the path does not kick off a load before the wrapper is wired;
            // GltfModel.Start performs a single load once the host is activated below.
            var host = new GameObject(string.IsNullOrEmpty(name) ? "Prop" : name);
            host.SetActive(false);
            var model = host.AddComponent<GltfModel>();
            model.path = filePath;

            // Expose the transform so the user can position the free-standing prop from the remote app.
            _Register(context, new ExposedGameObjectWithTransform(host), host);

            host.SetActive(true);
#else
            Debug.LogError("[LiveStudio] glTF props require the glTFast package (UNITY_GLTFAST define).");
            enabled = false;
            isLoaded = false;
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
                Debug.LogWarning("[LiveStudio] No RemoteControlContainer found; prop loaded but not remote-controllable.");
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
