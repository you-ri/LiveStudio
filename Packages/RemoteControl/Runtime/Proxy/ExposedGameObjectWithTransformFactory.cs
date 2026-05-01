// Copyright (c) You-Ri, 2026

using System;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Lilium.RemoteControl
{
    [Serializable]
    public class ExposedGameObjectWithTransformFactory : IExposedObjectFactory
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

        public IExposedObject Create()
        {
            if (prefab == null)
            {
                Debug.LogError("[RemoteControl] ExposedGameObjectWithTransformFactory.Create: prefab is null.");
                return null;
            }

            var instance = GameObjectUtility.InstantiatePrefabWithUndo(prefab);
            if (instance == null)
            {
                Debug.LogError($"[RemoteControl] ExposedGameObjectWithTransformFactory.Create: failed to instantiate prefab '{prefab.name}'.");
                return null;
            }

            var exposed = new ExposedGameObjectWithTransform(instance);
            exposed.prefabSourceKey = _prefabGuid;
            return exposed;
        }

        public void RegisterPrefabs()
        {
            if (prefab == null) return;
            if (string.IsNullOrEmpty(_prefabGuid))
            {
                Debug.LogWarning($"[RemoteControl] ExposedGameObjectWithTransformFactory.RegisterPrefabs: prefab '{prefab.name}' has no guid. Open the containing asset in Inspector to trigger OnValidate.");
                return;
            }
            PrefabRegistry.Register(_prefabGuid, prefab);
        }

        public void Destroy(IExposedObject obj)
        {
            if (obj is ExposedUnityObjectBase u && u.reference != null)
            {
                GameObject go = null;
                if (u.reference is GameObject g) go = g;
                else if (u.reference is Component c) go = c.gameObject;

                if (go != null)
                    GameObjectUtility.DestroyWithUndo(go);
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
