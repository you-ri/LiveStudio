// Copyright (c) You-Ri, 2026

using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Lilium.LiveStudio.Editor
{
    /// <summary>
    /// Builds an AssetBundle from a prefab and writes it as a <c>*.prop.lsb</c> file that
    /// the runtime <c>PropBundleLoader</c> loads. Used for avatar props and stage props.
    /// The actual build is delegated to the shared <see cref="BundleBuildUtility"/>.
    /// </summary>
    public static class PropBundleExporter
    {
        private const string kBundleName = "prop";
        private const string kExtension = ".prop.lsb";
        private const string kMenuPath = "Assets/Lilium Live Studio/Export Prop Bundle (.prop.lsb)";

        /// <summary>
        /// Builds the AssetBundle of <paramref name="prefabAssetPath"/> and copies it to
        /// <paramref name="destPath"/>. Returns true on success. Dependencies are collected
        /// automatically by the build pipeline.
        /// </summary>
        public static bool Export(string prefabAssetPath, string destPath)
        {
            if (string.IsNullOrEmpty(prefabAssetPath) || AssetImporter.GetAtPath(prefabAssetPath) == null)
            {
                Debug.LogError($"[LiveStudio] Invalid prefab asset path: '{prefabAssetPath}'.");
                return false;
            }

            // Pack a BundleThumbnail alongside the prefab so the remote app can show a thumbnail for this
            // .prop.lsb. The asset is persistent (kept next to the prefab) so the user can replace the
            // auto-rendered preview with their own image; it is reused on re-export.
            var thumbnailPath = BundleThumbnailAuthoring.GetOrCreateThumbnailAssetPath(prefabAssetPath);
            var assetNames = string.IsNullOrEmpty(thumbnailPath)
                ? new[] { prefabAssetPath }
                : new[] { prefabAssetPath, thumbnailPath };
            return BundleBuildUtility.Build(kBundleName, assetNames, destPath);
        }

        [MenuItem(kMenuPath)]
        private static void _ExportSelectedPrefab()
        {
            var assetPath = AssetDatabase.GetAssetPath(Selection.activeObject);
            if (string.IsNullOrEmpty(assetPath))
            {
                Debug.LogError("[LiveStudio] Select a prefab asset in the Project window first.");
                return;
            }

            var defaultName = $"{Path.GetFileNameWithoutExtension(assetPath)}{kExtension}";
            var savePath = EditorUtility.SaveFilePanel("Export Prop Bundle", "", defaultName, "lsb");
            if (string.IsNullOrEmpty(savePath)) return;

            // SaveFilePanel only handles the final extension, so guarantee the compound suffix.
            if (!savePath.EndsWith(kExtension, StringComparison.OrdinalIgnoreCase))
            {
                savePath = Path.ChangeExtension(savePath, null);
                if (savePath.EndsWith(".prop", StringComparison.OrdinalIgnoreCase))
                {
                    savePath = savePath.Substring(0, savePath.Length - ".prop".Length);
                }
                savePath += kExtension;
            }

            Export(assetPath, savePath);
        }

        [MenuItem(kMenuPath, validate = true)]
        private static bool _ValidateExportSelectedPrefab()
        {
            var prefabType = PrefabUtility.GetPrefabAssetType(Selection.activeObject);
            return prefabType != PrefabAssetType.NotAPrefab && prefabType != PrefabAssetType.MissingAsset;
        }
    }
}
