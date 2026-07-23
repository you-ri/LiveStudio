// Copyright (c) You-Ri, 2026

using System;
using System.Collections.Generic;

using UnityEngine;

using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Runtime facade over the baked <see cref="BuiltinAssetCatalog"/>: loads the catalog from
    /// <c>Resources</c>, registers each reference-only built-in asset in <see cref="AssetRegistry"/> under
    /// its GUID so a selector reference (e.g. the avatar body-override slot) resolves it where
    /// <c>AssetDatabase</c> is unavailable, and builds the <see cref="AssetBase"/> catalog entries
    /// <see cref="ExternalAssetManager"/> injects into the project asset list. A loadable built-in (e.g. a
    /// prop) is not pre-registered here — it is loaded on demand via <see cref="LoadPrefab"/> when enabled.
    ///
    /// Kind-agnostic: how a baked entry is loaded and what catalog entry it becomes comes from its
    /// <see cref="BuiltinAssetTypeDescriptor"/> (resolved by <see cref="BuiltinAssetCatalog.Entry.type"/>),
    /// so a new built-in asset kind needs no change here.
    ///
    /// Registration is eager and idempotent: it runs once at play start (after <see cref="AssetRegistry"/>
    /// is cleared) and once at editor load (so non-play REST lists the assets), plus on demand as a cheap
    /// no-op once done. Eager registration matters for scene restore — a persisted body-override reference
    /// must resolve synchronously when the live scene deserializes, before any async load.
    /// </summary>
    public static class BuiltinAssetRegistry
    {
        static BuiltinAssetCatalog _catalog;
        static bool _catalogLoaded;
        static bool _registered;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#endif
        static void _Init()
        {
            // AssetRegistry is cleared at SubsystemRegistration (before this), so re-register every play.
            _registered = false;
            EnsureRegistered();
        }

        static BuiltinAssetCatalog _Catalog()
        {
            if (!_catalogLoaded)
            {
                _catalog = Resources.Load<BuiltinAssetCatalog>(BuiltinAssetCatalog.kResourcesName);
                _catalogLoaded = true;
            }
            return _catalog;
        }

        /// <summary>
        /// Drops the cached catalog and registration flag so the next call reloads and re-registers. Called
        /// by the editor baker right after (re)writing the catalog, so freshly baked entries take effect
        /// without a domain reload.
        /// </summary>
        public static void Reload()
        {
            _catalog = null;
            _catalogLoaded = false;
            _registered = false;
            EnsureRegistered();
        }

        /// <summary>
        /// Loads and registers every reference-only built-in asset in the catalog into
        /// <see cref="AssetRegistry"/> (by GUID). Loadable kinds (isReference=false, e.g. props) are skipped
        /// — they are loaded on demand, not pre-registered. Idempotent: once a catalog has been processed,
        /// subsequent calls return immediately. A missing catalog is not marked done (so a later call
        /// retries once one is baked); an entry whose kind is not registered, or whose Resources asset is
        /// missing, is skipped (logged) so one bad entry never blocks the rest.
        /// </summary>
        public static void EnsureRegistered()
        {
            if (_registered) return;

            var catalog = _Catalog();
            if (catalog == null) return; // nothing baked yet; retry on a later call (see Reload).
            _registered = true;

            var entries = catalog.entries;
            for (int i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                if (string.IsNullOrEmpty(entry.guid) || string.IsNullOrEmpty(entry.resourcesPath)) continue;
                if (AssetRegistry.TryFind(entry.guid, out _)) continue; // already registered (e.g. by a scene component).

                // Unknown kinds are reported here only — this runs once, while GetAssets re-runs on every
                // project crawl and would turn the same bad entry into a log flood.
                var descriptor = BuiltinAssetTypeRegistry.Find(entry.type);
                if (descriptor == null)
                {
                    Debug.LogWarning(
                        $"[LiveStudio] No built-in asset kind registered for type '{entry.type}' ('{entry.resourcesPath}'). Entry skipped.");
                    continue;
                }

                // Reference-only kinds (e.g. animation clips) are pre-loaded and registered so a selector
                // resolves them by GUID. A loadable kind (e.g. a prop) must not be force-loaded here — it
                // would drag every built-in prop's dependency tree into memory at startup; it is loaded on
                // demand when enabled (see BuiltinPropAsset / LoadPrefab).
                if (!descriptor.isReference) continue;

                var asset = Resources.Load(entry.resourcesPath, descriptor.assetType);
                if (asset == null)
                {
                    Debug.LogWarning($"[LiveStudio] Built-in {entry.type} not found in Resources: '{entry.resourcesPath}'.");
                    continue;
                }
                AssetRegistry.Register(entry.guid, asset);
            }
        }

        /// <summary>
        /// Loads the built-in prefab baked under <paramref name="guid"/> from Resources, or null when the
        /// guid is unknown or the Resources asset is missing. Looked up in the catalog by GUID (not by a
        /// stored Resources path) so a loadable built-in asset need not persist its path — a renamed or
        /// moved asset still resolves as long as its GUID is unchanged. Used by <see cref="BuiltinPropAsset"/>
        /// when it is enabled.
        /// </summary>
        public static GameObject LoadPrefab(string guid)
        {
            if (string.IsNullOrEmpty(guid)) return null;

            var catalog = _Catalog();
            if (catalog == null) return null;

            var entries = catalog.entries;
            for (int i = 0; i < entries.Length; i++)
            {
                if (!string.Equals(entries[i].guid, guid, StringComparison.Ordinal)) continue;
                var resourcesPath = entries[i].resourcesPath;
                if (string.IsNullOrEmpty(resourcesPath)) return null;
                return Resources.Load<GameObject>(resourcesPath);
            }
            return null;
        }

        /// <summary>
        /// Builds the <see cref="AssetBase"/> catalog entries for every built-in asset so
        /// <see cref="ExternalAssetManager"/> can list them on the project asset page. Ids and names come
        /// from the baked data, so no asset is loaded just to enumerate the list. Empty when nothing is baked.
        /// </summary>
        public static IReadOnlyList<AssetBase> GetAssets()
        {
            var catalog = _Catalog();
            if (catalog == null) return Array.Empty<AssetBase>();

            var entries = catalog.entries;
            var result = new List<AssetBase>(entries.Length);
            for (int i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                if (string.IsNullOrEmpty(entry.guid)) continue;

                var descriptor = BuiltinAssetTypeRegistry.Find(entry.type);
                if (descriptor == null) continue; // unknown kind; already reported by EnsureRegistered.

                var asset = descriptor.create(entry);
                if (asset == null) continue;

                asset.id = entry.guid;
                asset.name = string.IsNullOrEmpty(entry.name) ? entry.guid : entry.name;
                asset.filePath = string.Empty;
                asset.path = string.Empty;
                asset.enabled = false;
                asset.isLoaded = false;
                asset.objectId = string.Empty;
                result.Add(asset);
            }
            return result;
        }
    }
}
