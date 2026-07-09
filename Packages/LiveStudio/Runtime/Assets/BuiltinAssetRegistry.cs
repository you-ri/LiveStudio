// Copyright (c) You-Ri, 2026

using System;
using System.Collections.Generic;

using UnityEngine;

using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Runtime facade over the baked <see cref="BuiltinAssetCatalog"/>: loads the catalog from
    /// <c>Resources</c>, registers each built-in asset in <see cref="AssetRegistry"/> under its GUID so a
    /// selector reference (e.g. the avatar body-override slot) resolves it where <c>AssetDatabase</c> is
    /// unavailable, and builds the <see cref="AssetBase"/> catalog entries <see cref="ExternalAssetManager"/>
    /// injects into the project asset list.
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
        /// Loads and registers every built-in asset in the catalog into <see cref="AssetRegistry"/> (by
        /// GUID). Idempotent: once a catalog has been processed, subsequent calls return immediately. A
        /// missing catalog is not marked done (so a later call retries once one is baked); a missing
        /// Resources asset for an entry is skipped (logged) so one bad entry never blocks the rest.
        /// </summary>
        public static void EnsureRegistered()
        {
            if (_registered) return;

            var catalog = _Catalog();
            if (catalog == null) return; // nothing baked yet; retry on a later call (see Reload).
            _registered = true;

            var clips = catalog.animationClips;
            for (int i = 0; i < clips.Length; i++)
            {
                var entry = clips[i];
                if (string.IsNullOrEmpty(entry.guid) || string.IsNullOrEmpty(entry.resourcesPath)) continue;
                if (AssetRegistry.TryFind(entry.guid, out _)) continue; // already registered (e.g. by a scene component).

                var clip = Resources.Load<AnimationClip>(entry.resourcesPath);
                if (clip == null)
                {
                    Debug.LogWarning($"[LiveStudio] Built-in animation clip not found in Resources: '{entry.resourcesPath}'.");
                    continue;
                }
                AssetRegistry.Register(entry.guid, clip);
            }
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

            var clips = catalog.animationClips;
            var result = new List<AssetBase>(clips.Length);
            for (int i = 0; i < clips.Length; i++)
            {
                var entry = clips[i];
                if (string.IsNullOrEmpty(entry.guid)) continue;
                result.Add(new BuiltinAnimationAsset
                {
                    id = entry.guid,
                    name = string.IsNullOrEmpty(entry.name) ? entry.guid : entry.name,
                    filePath = string.Empty,
                    path = string.Empty,
                    enabled = false,
                    isLoaded = false,
                    objectId = string.Empty,
                });
            }
            return result;
        }
    }
}
