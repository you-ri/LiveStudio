// Copyright (c) You-Ri, 2026

using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Lilium.RemoteControl
{
    /// <summary>
    /// Static registry mapping asset GUIDs to loaded <see cref="UnityEngine.Object"/> assets.
    /// Components that expose asset references through <see cref="AssetSelectorAttribute"/>
    /// register their candidate assets here (typically in OnEnable) with GUIDs baked at edit
    /// time, so asset references can be serialized as GUIDs and resolved back at runtime,
    /// where AssetDatabase is unavailable. Also backs <c>GET /live/asset?guid=...</c>.
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

        // The registry only knows the keys it has been handed, and some assets are not registered until
        // something opens the file holding them. A listing therefore has to give the owning layer a chance
        // to populate first. Same shape and lifetime as _nameFallback above: injected once by the layer
        // that owns the key scheme, so this core type stays free of it.
        static System.Func<Task> _catalogPrewarm;
        static System.Func<string, Task<Object[]>> _groupExpander;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Clear()
        {
            _assets.Clear();
            _guids.Clear();
        }

        /// <summary>
        /// Registers a fallback that derives a display name for a key the registry cannot resolve to a
        /// live Object (returns null when it does not recognize the key). Lets a higher layer teach the
        /// generic <c>GET /live/asset</c> resolver about its own key scheme (e.g. LiveStudio's
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

        /// <summary>
        /// Registers the step that makes lazily-registered assets visible to a full listing (e.g. LiveStudio
        /// opening every asset pack once so its members enter the registry). Called before the catalog is
        /// read by <c>GET /live/assets?type=...</c>. Without it a listing simply reports what is already
        /// registered, which is the correct answer for an app that registers everything up front.
        /// </summary>
        public static void SetCatalogPrewarm(System.Func<Task> prewarm) => _catalogPrewarm = prewarm;

        /// <summary>
        /// Registers the resolver for one *group* of assets — a file or bundle that holds several, such as
        /// a LiveStudio asset pack. Given the group's key it opens it and returns its members (which also
        /// registers them), or null when it does not recognize the key. Backs
        /// <c>GET /live/assets?group=...</c>, which lets a client drill into one group without paying the
        /// catalog-wide prewarm.
        /// </summary>
        public static void SetGroupExpander(System.Func<string, Task<Object[]>> expander) => _groupExpander = expander;

        /// <summary>
        /// Runs the registered prewarm, if any. Must be started on the main thread — the work behind it
        /// typically touches Unity APIs — and awaited off it so the server does not stall.
        /// </summary>
        public static Task PrewarmCatalogAsync()
            => _catalogPrewarm != null ? _catalogPrewarm() : Task.CompletedTask;

        /// <summary>
        /// Opens the group named by <paramref name="groupKey"/> and returns its members, or null when no
        /// expander is registered or it does not recognize the key. Same threading rule as
        /// <see cref="PrewarmCatalogAsync"/>.
        /// </summary>
        public static Task<Object[]> ExpandGroupAsync(string groupKey)
            => _groupExpander != null ? _groupExpander(groupKey) : Task.FromResult<Object[]>(null);

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
        /// skipped. Backs the type-filtered <c>GET /live/assets?type=...</c> listing. Must be called on the
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
