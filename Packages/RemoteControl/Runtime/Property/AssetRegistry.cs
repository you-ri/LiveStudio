// Copyright (c) You-Ri, 2026

using System.Collections.Generic;
using UnityEngine;

namespace Lilium.RemoteControl
{
    /// <summary>
    /// Static registry mapping asset GUIDs to loaded <see cref="UnityEngine.Object"/> assets.
    /// Components that expose asset references through <see cref="AssetSelectorAttribute"/>
    /// register their candidate assets here (typically in OnEnable) with GUIDs baked at edit
    /// time, so asset references can be serialized as GUIDs and resolved back at runtime,
    /// where AssetDatabase is unavailable. Also backs <c>GET /api/asset?guid=...</c>.
    /// </summary>
    public static class AssetRegistry
    {
        // asset guid -> asset
        static readonly Dictionary<string, Object> _assets = new Dictionary<string, Object>();

        // asset -> guid (reverse lookup for serialization)
        static readonly Dictionary<Object, string> _guids = new Dictionary<Object, string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Clear()
        {
            _assets.Clear();
            _guids.Clear();
        }

        public static void Register(string guid, Object asset)
        {
            if (asset == null) return;
            if (string.IsNullOrEmpty(guid))
            {
                Debug.LogWarning($"[RemoteControl] AssetRegistry.Register: empty guid for asset '{asset.name}'. Skipped.");
                return;
            }
            _assets[guid] = asset;
            _guids[asset] = guid;
        }

        public static bool TryFind(string guid, out Object asset)
        {
            asset = null;
            if (string.IsNullOrEmpty(guid)) return false;
            return _assets.TryGetValue(guid, out asset) && asset != null;
        }

        public static bool TryFindGuid(Object asset, out string guid)
        {
            guid = null;
            if (asset == null) return false;
            return _guids.TryGetValue(asset, out guid);
        }
    }
}
