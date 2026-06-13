// Copyright (c) You-Ri, 2026
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

            return BundleBuildUtility.Build(kBundleName, new[] { prefabAssetPath }, destPath);
        }
    }
}
