// Copyright (c) You-Ri, 2026

using System.Collections.Generic;
using UnityEngine;

namespace Lilium.RemoteControl
{
    /// <summary>
    /// Asset GUID から Prefab GameObject を検索・インスタンス化するための静的レジストリ。
    /// インスタンス追跡は ILiveObject.prefabSourceKey が担う。
    /// </summary>
    public static class PrefabRegistry
    {
        // asset guid -> prefab GameObject
        private static readonly Dictionary<string, GameObject> _registry
            = new Dictionary<string, GameObject>();

        // Optional fallback used when a guid is not in _registry: lets a lazily-loaded source (e.g. a
        // Resources-backed built-in catalog) resolve a prefab on demand instead of pre-registering — and
        // still be found by @prefab restore after a restart. Not cleared by _Clear (the dictionary is
        // per-run, but the resolver is a stable function the owner registers once).
        private static System.Func<string, GameObject> _resolver;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void _Clear()
        {
            _registry.Clear();
        }

        /// <summary>
        /// Registers a fallback that resolves a prefab from its asset GUID when it is not pre-registered via
        /// <see cref="Register"/>. Consulted by <see cref="TryFind"/> / <see cref="Instantiate"/> on a miss,
        /// so a prefab kept out of memory until first use still resolves for @prefab restore.
        /// </summary>
        public static void RegisterResolver(System.Func<string, GameObject> resolver)
        {
            _resolver = resolver;
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
            if (_registry.TryGetValue(guid, out prefab)) return true;
            if (_resolver != null)
            {
                prefab = _resolver(guid);
                return prefab != null;
            }
            prefab = null;
            return false;
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
