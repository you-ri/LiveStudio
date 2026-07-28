// Copyright (c) You-Ri, 2026

using System.Collections.Generic;
using System.Threading.Tasks;

using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

using Lilium.RemoteControl;
using Lilium.RemoteControl.LiveScene;
using Lilium.RemoteControl.UI;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// The scene page's "+" factory, extended to also list every instanceable prop — built-in Resources
    /// props and external <c>*.prop.lsb</c> bundle props alike — and spawn a fresh copy of one into the
    /// scene on demand. A prop is added exactly the way a Camera is: a new instance per click, persisted by
    /// <c>@prefab</c> (its <see cref="IInstantiableProp.instanceKey"/>), so the same prop can be added any
    /// number of times (multiple instances), unlike toggling a single shared asset entry.
    ///
    /// Source-agnostic: it enumerates <see cref="ExternalAssetManager"/>'s prop assets through
    /// <see cref="IInstantiableProp"/> and obtains each prefab via
    /// <see cref="IInstantiableProp.LoadInstancePrefabAsync"/>, so it never special-cases the source. The
    /// static factory entries (Camera / GLTF Model / ...) are inherited unchanged from
    /// <see cref="StandardObjectFactory"/>; only the props are appended. Lives in LiveStudio because it
    /// reaches the asset manager, which the generic RemoteControl UI layer does not depend on (mirrors
    /// <see cref="LiveCameraFactory"/>).
    /// </summary>
    [System.Serializable]
    [MovedFrom(false, null, null, "BuiltinPropObjectFactory")]
    public class PropObjectFactory : StandardObjectFactory
    {
        // Access level applied to every prop entry. The prop list carries no per-entry level and props are
        // curated content, so one shared level is enough (raise it to Experimental / Development to stage a
        // rollout). The scene page itself is already access-gated.
        [SerializeField]
        private AccessLevel _propAccessLevel = AccessLevel.Public;

        // Number of inherited static factory entries; props occupy the indices after these.
        private int _StaticCount => factories?.Length ?? 0;

        protected override string[] GetObjectNames()
        {
            var baseNames = base.GetObjectNames();
            var props = _InstanceableProps();
            if (props.Count == 0) return baseNames;

            var result = new string[baseNames.Length + props.Count];
            System.Array.Copy(baseNames, result, baseNames.Length);
            for (int i = 0; i < props.Count; i++) result[baseNames.Length + i] = props[i].name;
            return result;
        }

        protected override int[] GetObjectAccessLevels()
        {
            var baseLevels = base.GetObjectAccessLevels();
            var props = _InstanceableProps();
            if (props.Count == 0) return baseLevels;

            var result = new int[baseLevels.Length + props.Count];
            System.Array.Copy(baseLevels, result, baseLevels.Length);
            for (int i = 0; i < props.Count; i++) result[baseLevels.Length + i] = (int)_propAccessLevel;
            return result;
        }

        protected override object[] GetObjects()
        {
            var baseObjects = base.GetObjects();
            var props = _InstanceableProps();
            if (props.Count == 0) return baseObjects;

            var result = new object[baseObjects.Length + props.Count];
            System.Array.Copy(baseObjects, result, baseObjects.Length);
            for (int i = 0; i < props.Count; i++) result[baseObjects.Length + i] = props[i];
            return result;
        }

        public override void CreateObject(int index)
        {
            int baseCount = _StaticCount;
            if (index < baseCount)
            {
                base.CreateObject(index);
                return;
            }

            var props = _InstanceableProps();
            int local = index - baseCount;
            if (local < 0 || local >= props.Count)
            {
                Debug.LogWarning($"[LiveStudio] PropObjectFactory.CreateObject: invalid index {index} (static={baseCount}, props={props.Count}).");
                return;
            }
            _ = _CreatePropInstanceAsync(props[local]);
        }

        // A prop's prefab loads asynchronously (a bundle read for an external prop; a completed task for a
        // built-in), so the spawn runs as a fire-and-forget task. The remote app refetches the object list
        // after the CreateObject call, so no return value is needed.
        private async Task _CreatePropInstanceAsync(AssetBase asset)
        {
            if (!(asset is IInstantiableProp instanceable)) return;

            var prefab = await instanceable.LoadInstancePrefabAsync();
            if (prefab == null)
            {
                Debug.LogError($"[LiveStudio] PropObjectFactory: could not load prop prefab for '{asset.name}'.");
                return;
            }

            if (_container == null)
            {
#if UNITY_2022_3_OR_NEWER
                var host = UnityEngine.Object.FindFirstObjectByType<RemoteControlBehaviour>();
#else
                var host = UnityEngine.Object.FindObjectOfType<RemoteControlBehaviour>();
#endif
                _container = host != null ? host.objectContainer : null;
                if (_container == null)
                {
                    Debug.LogError("[LiveStudio] PropObjectFactory: LiveObjectContainer not found.");
                    return;
                }
            }

            Lilium.RemoteControl.GameObjectUtility.SetCurrentUndoGroup("Create Object");
            Lilium.RemoteControl.GameObjectUtility.RecordObjectUndo(_container.host, "Create Object");

            // Object.Instantiate (not InstantiatePrefabWithUndo) so a bundle-loaded prop prefab — which is
            // not an AssetDatabase asset, so PrefabUtility.InstantiatePrefab would return null in edit mode —
            // instantiates uniformly; the "+" is a runtime action, so editor undo of the instance itself is
            // not required (the same path the deferred restore drain uses).
            var instance = UnityEngine.Object.Instantiate(prefab);
            if (instance == null)
            {
                Debug.LogError($"[LiveStudio] PropObjectFactory: failed to instantiate '{prefab.name}'.");
                return;
            }

            // Place the instance in the base scene (where the RemoteControlContainer lives), not the active
            // scene which may be an additively-loaded set-bundle scene that would take it on unload.
            var baseScene = _ResolveBaseScene();
            if (baseScene.IsValid())
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(instance, baseScene);

            // A socket-driven prop (IProp) has its pose overwritten every frame, so expose no transform — the
            // edit surface is the prop's attachment (socket + offset). A free-standing prop keeps an editable
            // transform. The instanceKey is the @prefab key that re-instantiates a copy on restore (built-in
            // via the PrefabRegistry resolver, external via the deferred prefab provider).
            LiveGameObject wrapper = prefab.GetComponent<IProp>() != null
                ? new LiveGameObject(instance)
                : new LiveGameObjectWithTransform(instance);
            wrapper.prefabSourceKey = instanceable.instanceKey;
            wrapper.name = _GenerateUniqueName(prefab.name);
            _container.AddLiveObject(wrapper);
            wrapper.OnEnable();
        }

        // Every prop currently known to the asset manager that can be spawned as scene instances, in the
        // asset-manager's (stable within a session) order so an index stays consistent between the
        // objectNames read and the later CreateObject call.
        private static List<AssetBase> _InstanceableProps()
        {
            var result = new List<AssetBase>();
            var manager = ExternalAssetManager.current;
            if (manager == null) return result;
            var assets = manager.assetsView;
            for (int i = 0; i < assets.Count; i++)
            {
                if (assets[i] is IInstantiableProp p && p.supportsInstancing) result.Add(assets[i]);
            }
            return result;
        }

        // The base scene (a build scene, buildIndex >= 0) whose RemoteControlContainer owns restored /
        // spawned instances, so they share its lifecycle rather than lingering in a set-bundle scene.
        private static UnityEngine.SceneManagement.Scene _ResolveBaseScene()
        {
            var all = RemoteControlContainer.all;
            UnityEngine.SceneManagement.Scene fallback = default;
            bool hasFallback = false;
            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i];
                if (c == null) continue;
                if (!hasFallback) { fallback = c.gameObject.scene; hasFallback = true; }
                if (c.gameObject.scene.buildIndex >= 0) return c.gameObject.scene;
            }
            return fallback;
        }
    }
}
