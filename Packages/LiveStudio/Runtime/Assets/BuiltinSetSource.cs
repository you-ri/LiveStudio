// Copyright (c) You-Ri, 2026

using System;
using System.Collections.Generic;
using System.IO;

using UnityEngine;
using UnityEngine.SceneManagement;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Runtime facade over the project's <see cref="BuiltinSetList"/> declaration — the set counterpart of
    /// <see cref="BuiltinAssetRegistry"/>. It turns each declared scene into a loadable
    /// <see cref="BuiltinSetAsset"/> for <see cref="ExternalAssetManager"/> to list, pre-warms the declared
    /// preview images into <see cref="ThumbnailCache"/>, and answers the two identity questions the asset
    /// needs: which scene path a GUID loads (a moved scene keeps its GUID) and which GUID an old saved
    /// scene path meant.
    ///
    /// A scene is a set only by being declared. Enumerating the build's scene list instead would be
    /// zero-configuration but would offer every non-stage scene as a set — including a second bootstrap
    /// scene, which additively loading would duplicate the whole studio — and could not carry a display
    /// name, a preview, or an identity that survives moving the scene (there is no GUID at runtime).
    /// </summary>
    public static class BuiltinSetSource
    {
        static BuiltinSetList _list;
        static bool _listLoaded;
        static bool _prewarmed;

        /// <summary>
        /// Drops the cached declaration so the next call reloads it. Called by the editor authoring tools
        /// right after the list is edited, so a change takes effect without a domain reload.
        /// </summary>
        public static void Reload()
        {
            _list = null;
            _listLoaded = false;
            _prewarmed = false;
        }

        static BuiltinSetList _List()
        {
            if (!_listLoaded)
            {
                _list = Resources.Load<BuiltinSetList>(BuiltinSetList.kResourcesName);
                _listLoaded = true;
            }
            return _list;
        }

        /// <summary>
        /// Builds a <see cref="BuiltinSetAsset"/> for each declared scene, with its id / name / scene path
        /// populated so <see cref="ExternalAssetManager"/> can list and load it. Empty when the project
        /// declares no built-in sets (the default — a project ships sets only if it says so).
        ///
        /// Entries that cannot become a working set are dropped with an error rather than listed: a scene
        /// missing from the build could never load, and a duplicate display name would make the stage's
        /// recorded state ambiguous (a set is referred to by name, see <see cref="BuiltinSetList.Entry.displayName"/>).
        /// </summary>
        public static IReadOnlyList<AssetBase> GetSets()
        {
            var list = _List();
            if (list == null) return Array.Empty<AssetBase>();

            var entries = list.entries;
            if (entries.Length == 0) return Array.Empty<AssetBase>();

            _PrewarmThumbnails(entries);

            return BuildSets(
                entries,
                _ResolveBaseScenePath(),
                path => SceneUtility.GetBuildIndexByScenePath(path) >= 0);
        }

        /// <summary>
        /// The rules that turn declared entries into listable sets, with the two things that depend on the
        /// running app — which scene is the bootstrap one, and whether a scene is in the build — passed in.
        /// Split out from <see cref="GetSets"/> so the rules can be exercised without a built player or a
        /// Resources asset.
        /// </summary>
        internal static IReadOnlyList<AssetBase> BuildSets(
            IReadOnlyList<BuiltinSetList.Entry> entries, string basePath, Func<string, bool> isInBuild)
        {
            var result = new List<AssetBase>(entries.Count);
            var takenGuids = new HashSet<string>(StringComparer.Ordinal);

            // The bootstrap set occupies a name too — StageManager lists it under its scene's file name —
            // and it is not in `entries`, so reserve it up front. Without this, a declared scene that
            // happens to share the base scene's file name (in another folder, so the path check above lets
            // it through) would list under a name that always resolves to the bootstrap entry instead.
            var takenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                _BootstrapDisplayName(basePath),
            };

            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (string.IsNullOrEmpty(entry.guid) || string.IsNullOrEmpty(entry.scenePath)) continue;

                if (!takenGuids.Add(entry.guid))
                {
                    Debug.LogError($"[LiveStudio] Built-in set declared twice: '{entry.scenePath}'. Ignoring the duplicate.");
                    continue;
                }

                // The bootstrap scene is the studio itself. Offering it as a set would let it load
                // additively on top of itself — a second AvatarController and a duplicate of everything.
                if (string.Equals(entry.scenePath, basePath, StringComparison.OrdinalIgnoreCase))
                {
                    Debug.LogError($"[LiveStudio] Built-in set '{entry.scenePath}' is the app's base scene; it cannot also be a set. Ignoring it.");
                    continue;
                }

                // Only a scene in the build can be loaded by path at runtime. Report it here rather than
                // letting it list and fail on load, so the cause is visible before an operator taps it.
                if (isInBuild != null && !isInBuild(entry.scenePath))
                {
                    Debug.LogError($"[LiveStudio] Built-in set scene is not in the build's scene list: '{entry.scenePath}'. Ignoring it.");
                    continue;
                }

                var name = ResolveName(entry);
                if (!takenNames.Add(name))
                {
                    // Names are how the stage's saved and recorded state refers to a set, so two sets
                    // answering to one name would make a restore pick an arbitrary winner.
                    Debug.LogError($"[LiveStudio] Two built-in sets are named '{name}' ('{entry.scenePath}'). Ignoring the second one.");
                    continue;
                }

                result.Add(new BuiltinSetAsset
                {
                    id = entry.guid,
                    guid = entry.guid,
                    name = name,
                    scenePath = entry.scenePath,
                    filePath = string.Empty,
                    path = string.Empty,
                    enabled = false,
                    isLoaded = false,
                });
            }
            return result;
        }

        // How StageManager labels the bootstrap entry: its scene's file name, or "Studio" before a scene is
        // known. Kept in step with StageManager._CreatePersistentEntry.
        static string _BootstrapDisplayName(string basePath)
        {
            var name = Path.GetFileNameWithoutExtension(basePath ?? string.Empty);
            return string.IsNullOrEmpty(name) ? "Studio" : name;
        }

        /// <summary>The display name declared for <paramref name="entry"/>, falling back to the scene's file name.</summary>
        public static string ResolveName(BuiltinSetList.Entry entry)
            => !string.IsNullOrEmpty(entry.displayName)
                ? entry.displayName
                : Path.GetFileNameWithoutExtension(entry.scenePath ?? string.Empty);

        /// <summary>
        /// Looks up the declared entry for <paramref name="guid"/>. False when the project no longer
        /// declares it — e.g. a live scene saved before the set was removed from the declaration.
        /// </summary>
        public static bool TryFind(string guid, out BuiltinSetList.Entry entry)
        {
            entry = default;
            if (string.IsNullOrEmpty(guid)) return false;

            var list = _List();
            if (list == null) return false;

            var entries = list.entries;
            for (int i = 0; i < entries.Length; i++)
            {
                if (string.Equals(entries[i].guid, guid, StringComparison.Ordinal))
                {
                    entry = entries[i];
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// The GUID of the declared set at <paramref name="scenePath"/>, or null when no declared set sits
        /// there. Used to read a live scene saved before a set's identity became its GUID, where the scene
        /// path was the persisted identity.
        /// </summary>
        public static string ResolveGuidByScenePath(string scenePath)
        {
            if (string.IsNullOrEmpty(scenePath)) return null;

            var list = _List();
            if (list == null) return null;

            var entries = list.entries;
            for (int i = 0; i < entries.Length; i++)
            {
                if (string.Equals(entries[i].scenePath, scenePath, StringComparison.OrdinalIgnoreCase))
                    return entries[i].guid;
            }
            return null;
        }

        // Stores each declared preview image's raw bytes under the same synthetic key an asset's
        // thumbnailCacheKey resolves to, so the image endpoint serves it without touching the scene. Runs
        // once; the images travel with this asset, so nothing extra is loaded here.
        static void _PrewarmThumbnails(BuiltinSetList.Entry[] entries)
        {
            if (_prewarmed) return;
            _prewarmed = true;

            for (int i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                if (string.IsNullOrEmpty(entry.guid)) continue;

                var thumbnail = entry.thumbnail;
                if (thumbnail == null || !thumbnail.HasImage) continue;

                ThumbnailCache.Store(
                    BuiltinAssetRegistry.ThumbnailCacheKey(entry.guid), thumbnail.ImageData, thumbnail.MimeType);
            }
        }

        // The bootstrap scene is build index 0 only in a player launched normally: in the Editor any
        // scene can be played, and a base-scene switch re-points it at runtime. StageManager captures
        // the scene it started in, so ask it first and fall back to index 0 only before it exists.
        static string _ResolveBaseScenePath()
        {
            var stage = StageManager.current;
            if (stage != null && !string.IsNullOrEmpty(stage.persistentScenePath)) return stage.persistentScenePath;

            if (Application.isPlaying)
            {
                var active = SceneManager.GetActiveScene();
                if (active.IsValid() && !string.IsNullOrEmpty(active.path)) return active.path;
            }

            return SceneUtility.GetScenePathByBuildIndex(0);
        }
    }
}
