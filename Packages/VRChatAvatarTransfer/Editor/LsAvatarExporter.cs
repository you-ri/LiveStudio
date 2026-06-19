// Copyright (c) You-Ri, 2026
using System;
using System.IO;

using UnityEditor;

using Lilium.LiveStudio.Editor;

namespace Lilium.VRChatAvatarTransfer.Editor
{
    /// <summary>
    /// 変換済みアバタープレハブを AssetBundle 化し、*.avatar.lsb ファイルとして出力する。
    /// Studio アプリ (jp.lilium.livestudio) の LsAvatarLoader が実行時に読み込む。
    /// バンドルのビルドは共有の <see cref="BundleBuildUtility"/> に委譲する。
    /// </summary>
    internal static class LsAvatarExporter
    {
        private const string kBundleName = "avatar";
        private const string kExtension = ".avatar.lsb";
        private const string kMenuPath = "Assets/Lilium Live Studio/Export Avatar Bundle (.avatar.lsb)";

        /// <summary>
        /// prefabAssetPath の AssetBundle を生成し destPath にコピーする。成功時 true。
        /// 依存アセットは BuildPipeline により自動収集される。
        /// </summary>
        public static bool Export(string prefabAssetPath, string destPath)
        {
            if (string.IsNullOrEmpty(prefabAssetPath) || AssetImporter.GetAtPath(prefabAssetPath) == null)
            {
                VRChatAvatarTransferLog.Error($"Invalid prefab asset path: '{prefabAssetPath}'.");
                return false;
            }

            // Pack a BundleThumbnail alongside the prefab so the remote app can show a thumbnail for this
            // .avatar.lsb. The asset is persistent (kept next to the prefab) so the user can replace the
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
                VRChatAvatarTransferLog.Error("Select a prefab asset in the Project window first.");
                return;
            }

            var defaultName = $"{Path.GetFileNameWithoutExtension(assetPath)}{kExtension}";
            var savePath = EditorUtility.SaveFilePanel("Export Avatar Bundle", "", defaultName, "lsb");
            if (string.IsNullOrEmpty(savePath)) return;

            // SaveFilePanel は最終拡張子しか扱えないため、複合サフィックスを保証する。
            if (!savePath.EndsWith(kExtension, StringComparison.OrdinalIgnoreCase))
            {
                savePath = Path.ChangeExtension(savePath, null);
                if (savePath.EndsWith(".avatar", StringComparison.OrdinalIgnoreCase))
                {
                    savePath = savePath.Substring(0, savePath.Length - ".avatar".Length);
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
