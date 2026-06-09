// Copyright (c) You-Ri, 2026
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Lilium.VRChatAvatarTransfer.Editor
{
    /// <summary>
    /// 変換済みアバタープレハブを AssetBundle 化し、*.lsavatar ファイルとして出力する。
    /// Studio アプリ (jp.lilium.livestudio) の LsAvatarLoader が実行時に読み込む。
    /// </summary>
    internal static class LsAvatarExporter
    {
        private const string kBundleName = "lsavatar";
        private const string kTempBuildDir = "LsAvatarBuild";

        /// <summary>
        /// prefabAssetPath の AssetBundle を生成し destLsavatarPath にコピーする。成功時 true。
        /// 依存アセットは BuildPipeline により自動収集される。
        /// </summary>
        public static bool Export(string prefabAssetPath, string destLsavatarPath)
        {
            if (string.IsNullOrEmpty(prefabAssetPath) || AssetImporter.GetAtPath(prefabAssetPath) == null)
            {
                VRChatAvatarTransferLog.Error($"Invalid prefab asset path: '{prefabAssetPath}'.");
                return false;
            }

            if (string.IsNullOrEmpty(destLsavatarPath))
            {
                VRChatAvatarTransferLog.Error("Destination .lsavatar path is empty.");
                return false;
            }

            // Assets/ 内に出力すると再インポートされるため、プロジェクト直下の Temp に出力する。
            var tempOutDir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Temp", kTempBuildDir);
            Directory.CreateDirectory(tempOutDir);

            // 明示マップ overload を使い、importer の assetBundleName を汚さない。
            var build = new AssetBundleBuild
            {
                assetBundleName = kBundleName,
                assetNames = new[] { prefabAssetPath },
            };

            // 紫化(マゼンタ)対策: lilToon 等の shader_feature_local バリアントが AssetBundle ビルド時に
            // 「未使用」と判定されて削られないよう、ビルド中だけ URP の StripUnusedVariants を無効化する。
            // パッケージとして任意プロジェクトに導入されても自己完結するよう、ここで一時上書きして必ず戻す。
            AssetBundleManifest manifest;
            using (UrpShaderStrippingOverride.DisableUnusedVariantStripping())
            {
                manifest = BuildPipeline.BuildAssetBundles(
                    tempOutDir,
                    new[] { build },
                    BuildAssetBundleOptions.ChunkBasedCompression,
                    BuildTarget.StandaloneWindows64);
            }

            if (manifest == null)
            {
                VRChatAvatarTransferLog.Error("BuildPipeline.BuildAssetBundles failed (null manifest).");
                _CleanupTemp(tempOutDir);
                return false;
            }

            var producedPath = Path.Combine(tempOutDir, kBundleName);
            if (!File.Exists(producedPath))
            {
                VRChatAvatarTransferLog.Error($"Built AssetBundle not found at '{producedPath}'.");
                _CleanupTemp(tempOutDir);
                return false;
            }

            File.Copy(producedPath, destLsavatarPath, overwrite: true);
            _CleanupTemp(tempOutDir);

            VRChatAvatarTransferLog.Info($"Exported .lsavatar to '{destLsavatarPath}'.");
            return true;
        }

        private static void _CleanupTemp(string tempOutDir)
        {
            if (Directory.Exists(tempOutDir))
            {
                Directory.Delete(tempOutDir, recursive: true);
            }
        }
    }
}
