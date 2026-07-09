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
    /// <see cref="BuiltinAssetRegistry"/>. The catalog is rebuilt automatically whenever a relevant asset
    /// under a <c>Resources</c> folder changes (see the <see cref="AssetPostprocessor"/> below), and can be
    /// rebuilt on demand from <c>Assets ▸ Lilium Live Studio ▸ Rebuild Built-in Asset Catalog</c>.
    ///
    /// Only standalone <see cref="AnimationClip"/> assets (<c>*.anim</c>, whose main asset is the clip) are
    /// baked for now: <c>Resources.Load</c> addresses the main asset by path, so a clip embedded as a
    /// sub-asset of a model file cannot be loaded that way and is skipped.
    /// </summary>
    public static class BuiltinAssetCatalogBuilder
    {
        // Where the baked catalog lives so Resources.Load(kResourcesName) finds it at runtime. A project
        // asset (it lists that project's own Resources clips), not shipped with the package.
        const string kCatalogAssetPath = "Assets/Resources/" + BuiltinAssetCatalog.kResourcesName + ".asset";
        const string kResourcesMarker = "/Resources/";
        const string kAnimExtension = ".anim";

        [MenuItem("Assets/Lilium Live Studio/Rebuild Built-in Asset Catalog")]
        public static void Rebuild()
        {
            var entries = _ScanAnimationClips();

            var catalog = AssetDatabase.LoadAssetAtPath<BuiltinAssetCatalog>(kCatalogAssetPath);
            if (catalog == null)
            {
                // Nothing to bake and no catalog yet: leave the project clean (no empty asset).
                if (entries.Length == 0) return;

                Directory.CreateDirectory(Path.GetDirectoryName(kCatalogAssetPath));
                catalog = ScriptableObject.CreateInstance<BuiltinAssetCatalog>();
                catalog.animationClips = entries;
                AssetDatabase.CreateAsset(catalog, kCatalogAssetPath);
                AssetDatabase.SaveAssets();
                BuiltinAssetRegistry.Reload();
                Debug.Log($"[LiveStudio] Built-in asset catalog created with {entries.Length} animation clip(s).");
                return;
            }

            // Only rewrite when the baked set actually changed, so we do not churn the asset (and re-trigger
            // the postprocessor) on every import.
            if (_Equal(catalog.animationClips, entries)) return;

            catalog.animationClips = entries;
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            BuiltinAssetRegistry.Reload();
            Debug.Log($"[LiveStudio] Built-in asset catalog rebuilt with {entries.Length} animation clip(s).");
        }

        // Enumerates every standalone AnimationClip (*.anim) under any Resources folder, capturing its GUID,
        // Resources load path and name. Sorted by load path for a deterministic bake (stable diffs).
        static BuiltinAssetCatalog.AnimationClipEntry[] _ScanAnimationClips()
        {
            var entries = new List<BuiltinAssetCatalog.AnimationClipEntry>();

            foreach (var guid in AssetDatabase.FindAssets("t:AnimationClip"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path)) continue;
                if (!_TryResourcesLoadPath(path, out var loadPath)) continue;

                // Main-asset clips only: Resources.Load addresses the main asset, so a clip that is a
                // sub-asset of a model file (main asset is the model, not the clip) cannot be loaded here.
                var main = AssetDatabase.LoadMainAssetAtPath(path);
                if (!(main is AnimationClip clip)) continue;

                entries.Add(new BuiltinAssetCatalog.AnimationClipEntry
                {
                    guid = guid,
                    resourcesPath = loadPath,
                    name = clip.name,
                });
            }

            entries.Sort((a, b) => string.CompareOrdinal(a.resourcesPath, b.resourcesPath));
            return entries.ToArray();
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

        static bool _Equal(BuiltinAssetCatalog.AnimationClipEntry[] a, BuiltinAssetCatalog.AnimationClipEntry[] b)
        {
            if (a == null || b == null) return a == b;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i].guid != b[i].guid || a[i].resourcesPath != b[i].resourcesPath || a[i].name != b[i].name)
                    return false;
            }
            return true;
        }

        // Auto-rebuilds the catalog when an *.anim under a Resources folder is imported, deleted or moved.
        // Filtered to *.anim so writing the catalog *.asset itself does not retrigger a rebuild loop.
        class Postprocessor : AssetPostprocessor
        {
            static void OnPostprocessAllAssets(
                string[] imported, string[] deleted, string[] moved, string[] movedFrom)
            {
                if (_Touches(imported) || _Touches(deleted) || _Touches(moved) || _Touches(movedFrom))
                {
                    // Defer past the import that triggered us so the AssetDatabase is settled.
                    EditorApplication.delayCall += Rebuild;
                }
            }

            static bool _Touches(string[] paths)
            {
                if (paths == null) return false;
                for (int i = 0; i < paths.Length; i++)
                {
                    var p = paths[i];
                    if (string.IsNullOrEmpty(p)) continue;
                    if (p.IndexOf(kResourcesMarker, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (p.EndsWith(kAnimExtension, StringComparison.OrdinalIgnoreCase)) return true;
                }
                return false;
            }
        }
    }
}
