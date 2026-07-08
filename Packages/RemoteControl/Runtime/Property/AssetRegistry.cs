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

        // Optional fallback that derives a display name for a key this registry cannot resolve to a live
        // Object (e.g. a "file:" key whose bundle is not loaded yet). Set by a higher layer that owns the
        // key scheme's semantics, so this core registry stays generic. NOT cleared with the per-session
        // maps below — it is a one-time wiring, not session state.
        static System.Func<string, string> _nameFallback;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Clear()
        {
            _assets.Clear();
            _guids.Clear();
        }

        /// <summary>
        /// Registers a fallback that derives a display name for a key the registry cannot resolve to a
        /// live Object (returns null when it does not recognize the key). Lets a higher layer teach the
        /// generic <c>GET /api/asset</c> resolver about its own key scheme (e.g. LiveStudio's
        /// <c>file:&lt;path&gt;#&lt;clip&gt;</c>) without this core type depending on it.
        /// </summary>
        public static void SetNameFallback(System.Func<string, string> fallback) => _nameFallback = fallback;

        /// <summary>
        /// Display name for a key: the registered asset's name when loaded, otherwise the fallback's
        /// derived name (or null when neither resolves it). Makes the resolver complete for keys whose
        /// target is not currently loaded.
        /// </summary>
        public static string ResolveDisplayName(string guid)
        {
            if (TryFind(guid, out var asset)) return asset.name;
            return _nameFallback?.Invoke(guid);
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

        /// <summary>
        /// Collects every currently-registered asset whose runtime type name matches
        /// <paramref name="typeName"/> (case-insensitive simple name, e.g. "AnimationClip"), or all live
        /// assets when it is null/empty. Fills <paramref name="results"/> with (key, asset) pairs; keys are
        /// the registered ids (a bare GUID for a baked asset, or a scheme-prefixed reference such as
        /// LiveStudio's <c>file:&lt;path&gt;#&lt;clip&gt;</c>). Destroyed/unloaded (null) Unity objects are
        /// skipped. Backs the type-filtered <c>GET /api/assets?type=...</c> listing. Must be called on the
        /// main thread (reads UnityEngine.Object type/state).
        /// </summary>
        public static void CollectAssets(string typeName, List<KeyValuePair<string, Object>> results)
        {
            if (results == null) return;
            results.Clear();
            bool all = string.IsNullOrEmpty(typeName);
            foreach (var pair in _assets)
            {
                var asset = pair.Value;
                if (asset == null) continue; // destroyed / unloaded
                if (!all && !string.Equals(asset.GetType().Name, typeName, System.StringComparison.OrdinalIgnoreCase))
                    continue;
                results.Add(new KeyValuePair<string, Object>(pair.Key, asset));
            }
        }
    }
}
