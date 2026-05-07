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

        // Tools~ を持つパッケージ群。すべての Tools~ を build/Tools/ にマージコピーする。
        private static readonly string[] kToolsSourcePackages = { "jp.lilium.remotecontrol", "jp.lilium.livestudio.virgo" };

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
        /// 各パッケージの Tools~ フォルダの内容をビルド出力にマージコピー
        /// </summary>
        private void CopyToolsFolder(string buildPath)
        {
            string buildDirectory = Path.GetDirectoryName(buildPath);
            string toolsDestPath = Path.Combine(buildDirectory, "Tools");

            try
            {
                // 既存のToolsフォルダを削除
                if (Directory.Exists(toolsDestPath))
                {
                    Directory.Delete(toolsDestPath, true);
                }

                bool copiedAny = false;
                foreach (var packageName in kToolsSourcePackages)
                {
                    var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForPackageName(packageName);
                    if (packageInfo == null)
                    {
                        Debug.LogWarning($"[Studio] Package not found: {packageName}");
                        continue;
                    }

                    string toolsSourcePath = Path.Combine(packageInfo.resolvedPath, "Tools~");
                    if (!Directory.Exists(toolsSourcePath))
                    {
                        continue;
                    }

                    CopyDirectory(toolsSourcePath, toolsDestPath);
                    Debug.Log($"[Studio] Tools folder copied from {toolsSourcePath} to: {toolsDestPath}");
                    copiedAny = true;
                }

                if (!copiedAny)
                {
                    Debug.LogWarning("[Studio] No Tools~ folder found in any source package.");
                }
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