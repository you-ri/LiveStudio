// Copyright (c) You-Ri, 2026

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using UnityEditor;
using UnityEngine;

namespace Lilium.LiveStudio.Editor
{
    /// <summary>
    /// Builds an AssetBundle from one or more selected assets and writes it as a <c>*.pack.lsb</c> file that
    /// the runtime <see cref="PackBundleLoader"/> reads. Unlike the prop / avatar exporters (a single root
    /// prefab of a known kind), a pack holds a curated set of loose assets of ANY type — clips, materials,
    /// audio, … — so a whole set ships as one file; each member is later addressed by
    /// <c>file:&lt;relative-path&gt;#&lt;assetName&gt;</c>.
    ///
    /// Because members are addressed by name within the pack, member names must be unique across the whole
    /// pack regardless of type; the export is rejected when two selected assets share a name. The pack's
    /// internal id (CAB-...) is salted with a hash of ALL member GUIDs, so it is stable for a given set and
    /// does not shift when the set's first member changes.
    /// </summary>
    public static class PackBundleExporter
    {
        private const string kBundleName = "pack";
        private const string kExtension = ".pack.lsb";
        private const string kMenuPath = "Assets/Lilium Live Studio/Export Asset Pack (.pack.lsb)";

        /// <summary>
        /// Builds an asset pack from <paramref name="assetPaths"/> (main assets of standalone files) and
        /// copies it to <paramref name="destPath"/>. Returns true on success.
        /// </summary>
        public static bool Export(IReadOnlyList<string> assetPaths, string destPath)
        {
            if (assetPaths == null || assetPaths.Count == 0)
            {
                Debug.LogError("[LiveStudio] No assets to pack.");
                return false;
            }

            // Reject duplicate member names up front: members are addressed by name in the pack, so two
            // with the same name would collide into one key and only one would ever resolve. The check is
            // type-agnostic because the key carries no type either.
            var seenNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var path in assetPaths)
            {
                var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                if (asset == null)
                {
                    Debug.LogError($"[LiveStudio] Not a loadable asset: '{path}'.");
                    return false;
                }
                if (!seenNames.Add(asset.name))
                {
                    Debug.LogError($"[LiveStudio] Duplicate member name '{asset.name}': names in an asset pack must be unique. Rename one and re-export.");
                    return false;
                }
            }

            var token = _MemberToken(assetPaths);
            return BundleBuildUtility.Build(kBundleName, _ToArray(assetPaths), destPath, token);
        }

        // A stable token derived from every member's GUID (sorted, so member order does not change it), so
        // the pack's internal id is unique per member set and reproducible across re-exports.
        private static string _MemberToken(IReadOnlyList<string> assetPaths)
        {
            var guids = new List<string>(assetPaths.Count);
            foreach (var path in assetPaths)
            {
                var guid = AssetDatabase.AssetPathToGUID(path);
                guids.Add(string.IsNullOrEmpty(guid) ? path : guid);
            }
            guids.Sort(StringComparer.Ordinal);

            var sb = new StringBuilder();
            for (int i = 0; i < guids.Count; i++)
            {
                if (i > 0) sb.Append('|');
                sb.Append(guids[i]);
            }
            return BundleBuildUtility.StableToken(sb.ToString());
        }

        private static string[] _ToArray(IReadOnlyList<string> list)
        {
            var arr = new string[list.Count];
            for (int i = 0; i < list.Count; i++) arr[i] = list[i];
            return arr;
        }

        [MenuItem(kMenuPath)]
        private static void _ExportSelectedAssets()
        {
            var assetPaths = _SelectedAssetPaths();
            if (assetPaths.Count == 0)
            {
                Debug.LogError("[LiveStudio] Select one or more assets in the Project window first.");
                return;
            }

            // Default name: the single member's name, or a generic pack name for several.
            var defaultBase = assetPaths.Count == 1
                ? Path.GetFileNameWithoutExtension(assetPaths[0])
                : "AssetPack";
            var savePath = EditorUtility.SaveFilePanel("Export Asset Pack", "", defaultBase + kExtension, "lsb");
            if (string.IsNullOrEmpty(savePath)) return;

            // SaveFilePanel only handles the final extension, so guarantee the compound suffix.
            if (!savePath.EndsWith(kExtension, StringComparison.OrdinalIgnoreCase))
            {
                savePath = Path.ChangeExtension(savePath, null);
                if (savePath.EndsWith(".pack", StringComparison.OrdinalIgnoreCase))
                {
                    savePath = savePath.Substring(0, savePath.Length - ".pack".Length);
                }
                savePath += kExtension;
            }

            Export(assetPaths, savePath);
        }

        [MenuItem(kMenuPath, validate = true)]
        private static bool _ValidateExportSelectedAssets() => _SelectedAssetPaths().Count > 0;

        // The asset paths of the selected packable assets. Sub-assets of another file (e.g. a clip inside an
        // FBX) are skipped: bundling one would pull in the whole containing asset, not just the member.
        // Folders and scenes are skipped too — a scene produces a streamed scene bundle, which cannot be
        // mixed with loose assets in one file.
        private static List<string> _SelectedAssetPaths()
        {
            var paths = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var obj in Selection.objects)
            {
                if (obj == null) continue;
                if (obj is SceneAsset) continue;
                var path = AssetDatabase.GetAssetPath(obj);
                if (string.IsNullOrEmpty(path)) continue;
                if (AssetDatabase.IsValidFolder(path)) continue;
                if (!AssetDatabase.IsMainAsset(obj)) continue; // skip sub-assets (e.g. FBX sub-clips)
                if (seen.Add(path)) paths.Add(path);
            }
            return paths;
        }
    }
}
