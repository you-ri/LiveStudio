using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.Build.Profile;

using Lilium.LiveStudio;
namespace Lilium.LiveStudio.Virgo
{
    /// <summary>
    /// Fusion App のコマンドラインビルド用クラス
    /// Usage: Unity.exe -executeMethod Lilium.LiveStudio.Virgo.BuildFusionApp.Build
    /// </summary>
    public static class BuildFusionApp
    {
        private const string kFusionBuildProfilePath = "Assets/Settings/Build Profiles/Fusion(Windows Server).asset";
        private const string kFusionOutputFolder = "LiveStudio/Packages/LiveStudioVirgo/Tools~/VirgoMotionFusion";
        private const string kFusionExeName = "VirgoMotionFusion.exe";
        // ビルド中の一時出力先（パッケージフォルダ外に配置して再帰コピー問題を回避）
        private const string kTempBuildFolder = "Temp/FusionBuild";

        /// <summary>
        /// コマンドラインからFusion Appをビルドするメソッド
        /// </summary>
        public static void Build()
        {
            Debug.Log("[Studio] ===== Build Fusion App Started =====");

            // ProductNameを上書き（ビルド後に元に戻す）
            var originalProductName = PlayerSettings.productName;
            PlayerSettings.productName = "VirgoMotionFusion";
            Debug.Log($"[Studio] ProductName set to: {PlayerSettings.productName}");

            int exitCode = 1;
            try
            {
                exitCode = BuildInternal();
            }
            finally
            {
                // 元に戻す
                PlayerSettings.productName = originalProductName;
                Debug.Log($"[Studio] ProductName restored to: {PlayerSettings.productName}");
            }

            EditorApplication.Exit(exitCode);
        }

        private static int BuildInternal()
        {
            // Fusion Build Profileをロード
            var fusionProfile = AssetDatabase.LoadAssetAtPath<BuildProfile>(kFusionBuildProfilePath);
            if (fusionProfile == null)
            {
                Debug.LogError($"[Studio] Build Profile not found: {kFusionBuildProfilePath}");
                return 1;
            }

            Debug.Log($"[Studio] Loaded Build Profile: {fusionProfile.name}");

            // パス設定
            string projectPath = Path.GetDirectoryName(Application.dataPath); // VirgoMotionStudio
            string virgoRoot = Path.GetDirectoryName(projectPath); // Virgo

            // 最終出力先（パッケージ内）
            string finalOutputDir = Path.Combine(virgoRoot, kFusionOutputFolder);
            string finalOutputPath = Path.Combine(finalOutputDir, kFusionExeName);

            // 一時ビルド先（プロジェクト内のTempフォルダ、パッケージ外）
            // Unityのビルドプロセス中にパッケージフォルダ内のファイルが再帰的にコピーされる問題を回避
            string tempBuildDir = Path.Combine(projectPath, kTempBuildFolder);
            string tempOutputPath = Path.Combine(tempBuildDir, kFusionExeName);

            Debug.Log($"[Studio] Temp build path: {tempOutputPath}");
            Debug.Log($"[Studio] Final output path: {finalOutputPath}");

            // 一時ビルドフォルダをクリーンアップして作成
            if (Directory.Exists(tempBuildDir))
            {
                Directory.Delete(tempBuildDir, true);
            }
            Directory.CreateDirectory(tempBuildDir);
            Debug.Log($"[Studio] Created temp build directory: {tempBuildDir}");

            // シーンリストを取得
            var scenes = new List<string>();
            foreach (var scene in fusionProfile.scenes)
            {
                if (scene.enabled)
                {
                    scenes.Add(scene.path);
                    Debug.Log($"[Studio] Scene: {scene.path}");
                }
            }

            if (scenes.Count == 0)
            {
                Debug.LogError("[Studio] No scenes found in build profile!");
                return 1;
            }

            // ビルドオプションを設定（一時フォルダに出力）
            var buildOptions = new BuildPlayerWithProfileOptions 
            {
                buildProfile = fusionProfile,
                locationPathName = tempOutputPath,
                options = BuildOptions.None
            };

            Debug.Log($"[Studio] Profile: {buildOptions.buildProfile.name}, Subtarget: Server");
            Debug.Log("[Studio] Starting build...");

            // ビルド実行
            var report = BuildPipeline.BuildPlayer(buildOptions);

            // 結果を出力
            if (report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                Debug.Log("[Studio] Build completed, copying to final destination...");

                // 最終出力先にコピー
                try
                {
                    // 既存の出力先をクリーンアップ
                    if (Directory.Exists(finalOutputDir))
                    {
                        Directory.Delete(finalOutputDir, true);
                    }

                    // ディレクトリごとコピー
                    CopyDirectory(tempBuildDir, finalOutputDir);

                    // 不要なToolsフォルダを削除（ビルド時に誤ってコピーされる）
                    string unnecessaryToolsDir = Path.Combine(finalOutputDir, "Tools");
                    if (Directory.Exists(unnecessaryToolsDir))
                    {
                        Directory.Delete(unnecessaryToolsDir, true);
                        Debug.Log($"[Studio] Removed unnecessary Tools folder: {unnecessaryToolsDir}");
                    }

                    Debug.Log("[Studio] ===== BUILD SUCCEEDED =====");
                    Debug.Log($"[Studio] Output: {finalOutputPath}");
                    Debug.Log($"[Studio] Size: {report.summary.totalSize / (1024 * 1024):F2} MB");
                    Debug.Log($"[Studio] Time: {report.summary.totalTime.TotalSeconds:F1} seconds");
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[Studio] Failed to copy build output: {ex.Message}");
                    return 1;
                }
                finally
                {
                    // 一時フォルダをクリーンアップ
                    if (Directory.Exists(tempBuildDir))
                    {
                        try { Directory.Delete(tempBuildDir, true); }
                        catch { /* ignore cleanup errors */ }
                    }
                }

                return 0;
            }
            else
            {
                Debug.LogError("[Studio] ===== BUILD FAILED =====");
                Debug.LogError($"[Studio] Result: {report.summary.result}");
                Debug.LogError($"[Studio] Errors: {report.summary.totalErrors}");
                foreach (var step in report.steps)
                {
                    foreach (var message in step.messages)
                    {
                        if (message.type == LogType.Error)
                        {
                            Debug.LogError($"[Studio] {message.content}");
                        }
                    }
                }

                // 一時フォルダをクリーンアップ
                if (Directory.Exists(tempBuildDir))
                {
                    try { Directory.Delete(tempBuildDir, true); }
                    catch { /* ignore cleanup errors */ }
                }

                return 1;
            }
        }

        /// <summary>
        /// ディレクトリを再帰的にコピー
        /// </summary>
        private static void CopyDirectory(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);

            foreach (var file in Directory.GetFiles(sourceDir))
            {
                string destFile = Path.Combine(destDir, Path.GetFileName(file));
                File.Copy(file, destFile, true);
            }

            foreach (var dir in Directory.GetDirectories(sourceDir))
            {
                string destSubDir = Path.Combine(destDir, Path.GetFileName(dir));
                CopyDirectory(dir, destSubDir);
            }
        }
    }
}
