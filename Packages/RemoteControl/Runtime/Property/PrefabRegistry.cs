// Copyright (c) You-Ri, 2026

using System.Collections.Generic;
using UnityEngine;

namespace Lilium.RemoteControl
{
    /// <summary>
    /// Asset GUID から Prefab GameObject を検索・インスタンス化するための静的レジストリ。
    /// インスタンス追跡は IExposedObject.prefabSourceKey が担う。
    /// </summary>
    public static class PrefabRegistry
    {
        // asset guid -> prefab GameObject
        private static readonly Dictionary<string, GameObject> _registry
            = new Dictionary<string, GameObject>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void _Clear()
        {
            _registry.Clear();
        }

        public static void Register(string guid, GameObject prefab)
        {
            if (prefab == null) return;
            if (string.IsNullOrEmpty(guid))
            {
                Debug.LogWarning($"[RemoteControl] PrefabRegistry.Register: empty guid for prefab '{prefab.name}'. Skipped.");
                return;
            }
            _registry[guid] = prefab;
        }

        public static bool TryFind(string guid, out GameObject prefab)
        {
            if (string.IsNullOrEmpty(guid))
            {
                prefab = null;
                return false;
            }
            return _registry.TryGetValue(guid, out prefab);
        }

        /// <summary>
        /// Asset GUID からインスタンスを生成する。
        /// </summary>
        public static GameObject Instantiate(string guid)
        {
            if (!TryFind(guid, out var prefab))
            {
                Debug.LogWarning($"[RemoteControl] Prefab not found (guid='{guid}').");
                return null;
            }
            return GameObject.Instantiate(prefab);
        }
    }
}
