// Copyright (c) You-Ri, 2026

using System;
using System.Collections.Generic;
using System.IO;

using UnityEditor;
using UnityEngine;

namespace Lilium.LiveStudio.Editor
{
    /// <summary>
    /// Bakes the <see cref="BuiltinAssetCatalog"/> from the assets under the project's <c>Resources</c>
    /// folders. At runtime <c>AssetDatabase</c> is unavailable, so each built-in asset's GUID and
    /// <c>Resources.Load</c> path are captured here, at edit time, and read back by
    /// <see cref="BuiltinAssetRegistry"/>.
    ///
    /// Which asset types are baked comes from <see cref="BuiltinAssetTypeRegistry"/>, not from this class,
    /// so a package that adds a built-in kind gets its Resources assets baked without touching the baker.
    /// The catalog is rebuilt automatically once per editor load (which is also what picks up a newly
    /// registered kind, since no asset import announces one) and whenever an asset of a registered kind
    /// under a <c>Resources</c> folder changes; it can also be rebuilt on demand from
    /// <c>Assets ▸ Lilium Live Studio ▸ Rebuild Built-in Asset Catalog</c>. Every path is a no-op unless the
    /// baked set actually changed, so the repeated rebuilds never churn the asset.
    ///
    /// Only main assets are baked: <c>Resources.Load</c> addresses the main asset by path, so an asset
    /// embedded as a sub-asset of another file (e.g. a clip inside a model) cannot be loaded that way and
    /// is skipped.
    /// </summary>
    public static class BuiltinAssetCatalogBuilder
    {
        // Where the baked catalog lives so Resources.Load(kResourcesName) finds it at runtime. A project
        // asset (it lists that project's own Resources assets), not shipped with the package.
        const string kCatalogAssetPath = "Assets/Resources/" + BuiltinAssetCatalog.kResourcesName + ".asset";
        const string kResourcesMarker = "/Resources/";

        // Rebuild once per editor load, deferred past every [InitializeOnLoadMethod] so kinds registered by
        // other packages are already in the registry (their registration order relative to ours is
        // undefined). This is also what heals a catalog baked by an older format or an older kind set.
        [InitializeOnLoadMethod]
        static void _RebuildOnLoad() => EditorApplication.delayCall += Rebuild;

        [MenuItem("Assets/Lilium Live Studio/Rebuild Built-in Asset Catalog")]
        public static void Rebuild()
        {
            var entries = _Scan();

            var catalog = AssetDatabase.LoadAssetAtPath<BuiltinAssetCatalog>(kCatalogAssetPath);
            if (catalog == null)
            {
                // Nothing to bake and no catalog yet: leave the project clean (no empty asset).
                if (entries.Length == 0) return;

                Directory.CreateDirectory(Path.GetDirectoryName(kCatalogAssetPath));
                catalog = ScriptableObject.CreateInstance<BuiltinAssetCatalog>();
                catalog.entries = entries;
                AssetDatabase.CreateAsset(catalog, kCatalogAssetPath);
                AssetDatabase.SaveAssets();
                BuiltinAssetRegistry.Reload();
                Debug.Log($"[LiveStudio] Built-in asset catalog created with {entries.Length} asset(s).");
                return;
            }

            // Only rewrite when the baked set actually changed, so we do not churn the asset (and re-trigger
            // the postprocessor) on every import or editor load.
            if (_Equal(catalog.entries, entries)) return;

            catalog.entries = entries;
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            BuiltinAssetRegistry.Reload();
            Debug.Log($"[LiveStudio] Built-in asset catalog rebuilt with {entries.Length} asset(s).");
        }

        // Renders a preview for every built-in prop that has no thumbnail yet, persisting a BundleThumbnail
        // next to the prefab (kept, so it can be replaced via the BundleThumbnail inspector), then rebuilds so
        // the freshly authored thumbnails bake into the catalog. Explicit rather than part of the auto-bake:
        // rendering N prefabs is not something to run on every asset import, and thumbnails are optional — the
        // catalog and the remote app work without them. Only GameObject kinds have a renderable preview.
        [MenuItem("Assets/Lilium Live Studio/Generate Built-in Prop Thumbnails")]
        public static void GenerateBuiltinPropThumbnails()
        {
            // Rebuild first so the catalog reflects the current Resources set (a just-added prop is included).
            Rebuild();

            var catalog = AssetDatabase.LoadAssetAtPath<BuiltinAssetCatalog>(kCatalogAssetPath);
            if (catalog == null)
            {
                Debug.Log("[LiveStudio] No built-in assets to generate thumbnails for.");
                return;
            }

            int rendered = 0;
            foreach (var entry in catalog.entries)
            {
                var descriptor = BuiltinAssetTypeRegistry.Find(entry.type);
                if (descriptor == null || descriptor.assetType != typeof(GameObject)) continue; // props only

                var prefabPath = AssetDatabase.GUIDToAssetPath(entry.guid);
                if (string.IsNullOrEmpty(prefabPath)) continue;

                // Creates the sibling thumbnail and renders the preview only when it is missing/empty; an
                // existing (auto or user-provided) image is preserved.
                if (BundleThumbnailAuthoring.GetOrCreateThumbnailAssetPath(prefabPath) != null) rendered++;
            }

            // Re-bake so the newly authored thumbnail paths are discovered into the catalog.
            Rebuild();
            Debug.Log($"[LiveStudio] Built-in prop thumbnails ready for {rendered} prop(s).");
        }

        // Enumerates every main asset under a Resources folder that a registered kind owns, capturing its
        // kind, GUID, Resources load path and name. Sorted by kind then load path for a deterministic bake
        // (stable diffs).
        static BuiltinAssetCatalog.Entry[] _Scan()
        {
            var entries = new List<BuiltinAssetCatalog.Entry>();
            // An asset can be found by more than one kind's search filter (e.g. a Texture2D under both a
            // Texture and a Texture2D kind); the first registered kind owns it.
            var taken = new HashSet<string>();

            var descriptors = BuiltinAssetTypeRegistry.descriptors;
            for (int d = 0; d < descriptors.Count; d++)
            {
                var descriptor = descriptors[d];
                foreach (var guid in AssetDatabase.FindAssets("t:" + descriptor.assetType.Name))
                {
                    if (taken.Contains(guid)) continue;

                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(path)) continue;
                    // The catalog lives under Resources too; never let a broad kind (one whose type also
                    // covers ScriptableObject) bake the catalog into itself.
                    if (string.Equals(path, kCatalogAssetPath, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!_TryResourcesLoadPath(path, out var loadPath)) continue;

                    // Main assets only: Resources.Load addresses the main asset, so an asset that is a
                    // sub-asset of another file (main asset is the container, not this) cannot be loaded here.
                    var main = AssetDatabase.LoadMainAssetAtPath(path);
                    if (main == null || !descriptor.assetType.IsInstanceOfType(main)) continue;

                    // Content narrowing (e.g. only IProp-rooted prefabs are built-in props). Applied before
                    // taken.Add so a rejected asset stays available to a later kind's filter.
                    if (descriptor.accept != null && !descriptor.accept(main)) continue;

                    taken.Add(guid);
                    entries.Add(new BuiltinAssetCatalog.Entry
                    {
                        type = descriptor.typeName,
                        guid = guid,
                        resourcesPath = loadPath,
                        name = main.name,
                        thumbnailPath = _ResolveThumbnailLoadPath(path),
                    });
                }
            }

            entries.Sort((a, b) =>
            {
                int byType = string.CompareOrdinal(a.type, b.type);
                return byType != 0 ? byType : string.CompareOrdinal(a.resourcesPath, b.resourcesPath);
            });
            return entries.ToArray();
        }

        // Resolves the Resources.Load path of an authored thumbnail sibling for the asset at assetPath, or
        // empty when none is usable. Discovery only — never renders here, so the frequent auto-bake stays
        // cheap and side-effect free; a preview is generated on demand by GenerateBuiltinPropThumbnails.
        // A thumbnail must sit under a Resources folder (so it loads at runtime) and actually hold an image;
        // an empty result is the graceful "no preview" state.
        static string _ResolveThumbnailLoadPath(string assetPath)
        {
            var thumbnailAssetPath = BundleThumbnailAuthoring.GetThumbnailAssetPath(assetPath);
            var thumbnail = AssetDatabase.LoadAssetAtPath<BundleThumbnail>(thumbnailAssetPath);
            if (thumbnail == null || !thumbnail.HasImage) return string.Empty;
            if (!_TryResourcesLoadPath(thumbnailAssetPath, out var loadPath)) return string.Empty;
            return loadPath;
        }

        // Maps an asset path under a Resources folder to its Resources.Load path (relative to the last
        // "Resources" segment, extension stripped). Returns false when the asset is not under Resources.
        static bool _TryResourcesLoadPath(string assetPath, out string loadPath)
        {
            loadPath = null;
            var p = assetPath.Replace('\\', '/');
            int idx = p.LastIndexOf(kResourcesMarker, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return false;

            var after = p.Substring(idx + kResourcesMarker.Length); // e.g. "Poses/Sit_Normal_Boy.anim"
            int dot = after.LastIndexOf('.');
            loadPath = dot >= 0 ? after.Substring(0, dot) : after;
            return !string.IsNullOrEmpty(loadPath);
        }

        static bool _Equal(BuiltinAssetCatalog.Entry[] a, BuiltinAssetCatalog.Entry[] b)
        {
            if (a == null || b == null) return a == b;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i].type != b[i].type || a[i].guid != b[i].guid ||
                    a[i].resourcesPath != b[i].resourcesPath || a[i].name != b[i].name ||
                    a[i].thumbnailPath != b[i].thumbnailPath)
                    return false;
            }
            return true;
        }

        // Auto-rebuilds the catalog when an asset of a registered kind under a Resources folder is imported,
        // deleted or moved. Imports are filtered by asset type rather than by extension, so a kind never has
        // to enumerate the extensions its type can come from — and writing the catalog *.asset itself does
        // not retrigger a rebuild loop, since no kind owns a BuiltinAssetCatalog.
        class Postprocessor : AssetPostprocessor
        {
            static void OnPostprocessAllAssets(
                string[] imported, string[] deleted, string[] moved, string[] movedFrom)
            {
                // The removed paths cannot be typed any more (the asset is gone from the database), so they
                // fall back to a path test: any Resources path but the catalog itself. A rebuild that finds
                // nothing changed writes nothing, so the occasional false positive is free.
                if (_TouchesTyped(imported) || _TouchesTyped(moved) ||
                    _TouchesResources(deleted) || _TouchesResources(movedFrom))
                {
                    // Defer past the import that triggered us so the AssetDatabase is settled.
                    EditorApplication.delayCall += Rebuild;
                }
            }

            static bool _TouchesTyped(string[] paths)
            {
                if (paths == null) return false;
                for (int i = 0; i < paths.Length; i++)
                {
                    if (!_UnderResources(paths[i])) continue;
                    var type = AssetDatabase.GetMainAssetTypeAtPath(paths[i]);
                    if (BuiltinAssetTypeRegistry.FindForAssetType(type) != null) return true;
                }
                return false;
            }

            static bool _TouchesResources(string[] paths)
            {
                if (paths == null) return false;
                for (int i = 0; i < paths.Length; i++)
                {
                    if (!_UnderResources(paths[i])) continue;
                    if (string.Equals(paths[i], kCatalogAssetPath, StringComparison.OrdinalIgnoreCase)) continue;
                    return true;
                }
                return false;
            }

            static bool _UnderResources(string path)
                => !string.IsNullOrEmpty(path) &&
                   path.IndexOf(kResourcesMarker, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
