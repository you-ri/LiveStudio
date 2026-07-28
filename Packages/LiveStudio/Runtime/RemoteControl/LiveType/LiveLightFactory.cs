// Copyright (c) You-Ri, 2026

using System;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
using Lilium.RemoteControl;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Lilium.LiveStudio
{
    [Serializable]
    [MovedFrom(false, null, null, "ExposedLightFactory")]
    public class LiveLightFactory : ILiveObjectFactory
    {
        [SerializeField]
        public GameObject prefab;

        [SerializeField]
        string _prefabGuid;

        [SerializeField]
        AccessLevel _accessLevel;

        public string name => prefab != null ? prefab.name : "";

        public string prefabGuid => _prefabGuid;

        public AccessLevel accessLevel => _accessLevel;

        public ILiveObject Create()
        {
            if (prefab == null)
            {
                Debug.LogError("[Studio] LiveLightFactory.Create: prefab is null.");
                return null;
            }

            var instance = Lilium.RemoteControl.GameObjectUtility.InstantiatePrefabWithUndo(prefab);
            if (instance == null)
            {
                Debug.LogError($"[Studio] LiveLightFactory.Create: failed to instantiate prefab '{prefab.name}'.");
                return null;
            }

            var light = instance.GetComponent<Light>();
            if (light == null)
            {
                Debug.LogError($"[Studio] LiveLightFactory.Create: prefab '{prefab.name}' has no Light component.");
                Lilium.RemoteControl.GameObjectUtility.DestroyWithUndo(instance);
                return null;
            }

            var exposed = new LiveLight(light);
            exposed.prefabSourceKey = _prefabGuid;
            return exposed;
        }

        public void RegisterPrefabs()
        {
            if (prefab == null) return;
            if (string.IsNullOrEmpty(_prefabGuid))
            {
                Debug.LogWarning($"[Studio] LiveLightFactory.RegisterPrefabs: prefab '{prefab.name}' has no guid. Open the containing asset in Inspector to trigger OnValidate.");
                return;
            }
            PrefabRegistry.Register(_prefabGuid, prefab);
        }

        public void Destroy(ILiveObject obj)
        {
            if (obj is LiveUnityObjectBase u && u.reference != null)
            {
                GameObject go = null;
                if (u.reference is GameObject g) go = g;
                else if (u.reference is Component c) go = c.gameObject;

                if (go != null)
                    Lilium.RemoteControl.GameObjectUtility.DestroyWithUndo(go);
            }
        }

#if UNITY_EDITOR
        public void RefreshPrefabKey()
        {
            if (prefab == null)
            {
                _prefabGuid = string.Empty;
                return;
            }
            var path = AssetDatabase.GetAssetPath(prefab);
            _prefabGuid = string.IsNullOrEmpty(path) ? string.Empty : AssetDatabase.AssetPathToGUID(path);
        }
#endif
    }
}
