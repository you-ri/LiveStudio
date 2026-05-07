using UnityEngine;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using System.IO;

#if !FUSION_APP

using Lilium.LiveStudio;
namespace Lilium.LiveStudio.Virgo.Editor
{
    /// <summary>
    /// ビルド後にTools~フォルダの内容をビルド出力にコピーするポストプロセッサー
    /// </summary>
    public class BuildPostProcessor : IPostprocessBuildWithReport
    {
        private const string kEditorPrefsLastBuildPathKey = "Lilium.LiveStudio.Virgo.LastBuildPath";

        public int callbackOrder => 0;

        public void OnPostprocessBuild(BuildReport report)
        {
            string buildPath = report.summary.outputPath;

            // ビルドパスを保存
            EditorPrefs.SetString(kEditorPrefsLastBuildPathKey, buildPath);

            Debug.Log($"[Studio] Build completed: {buildPath}");

            // Tools~フォルダをコピー
            CopyToolsFolder(buildPath);
        }

        /// <summary>
        /// Tools~フォルダの内容をビルド出力にコピー
        /// </summary>
        private void CopyToolsFolder(string buildPath)
        {
            // パッケージのTools~フォルダのパス
            // PackageManager APIを使用してローカルパッケージの実際のパスを取得
            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/jp.lilium.livestudio.virgo");
            if (packageInfo == null)
            {
                Debug.LogError("[Studio] Could not find package: jp.lilium.livestudio.virgo");
                return;
            }
            string packagePath = packageInfo.resolvedPath;
            string toolsSourcePath = Path.Combine(packagePath, "Tools~");

            if (!Directory.Exists(toolsSourcePath))
            {
                Debug.LogWarning($"[Studio] Tools~ folder not found: {toolsSourcePath}");
                return;
            }

            // ビルド出力のToolsフォルダのパス
            // 実行ファイルと同じディレクトリにToolsフォルダを作成
            string buildDirectory = Path.GetDirectoryName(buildPath);
            string toolsDestPath = Path.Combine(buildDirectory, "Tools");

            try
            {
                // 既存のToolsフォルダを削除
                if (Directory.Exists(toolsDestPath))
                {
                    Directory.Delete(toolsDestPath, true);
                }

                // Tools~フォルダをコピー
                CopyDirectory(toolsSourcePath, toolsDestPath);

                Debug.Log($"[Studio] Tools folder copied successfully to: {toolsDestPath}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Studio] Failed to copy Tools folder: {e.Message}");
            }
        }

        /// <summary>
        /// ディレクトリを再帰的にコピー
        /// </summary>
        private void CopyDirectory(string sourceDir, string destDir)
        {
            // コピー先ディレクトリを作成
            Directory.CreateDirectory(destDir);

            // ファイルをコピー
            foreach (string file in Directory.GetFiles(sourceDir))
            {
                string fileName = Path.GetFileName(file);
                string destFile = Path.Combine(destDir, fileName);
                File.Copy(file, destFile, true);
            }

            // サブディレクトリを再帰的にコピー
            foreach (string subDir in Directory.GetDirectories(sourceDir))
            {
                string dirName = Path.GetFileName(subDir);
                string destSubDir = Path.Combine(destDir, dirName);
                CopyDirectory(subDir, destSubDir);
            }
        }
        

    }
}
#endif