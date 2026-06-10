// Studio app は Unity6 (VirgoMotionStudio) からのみビルドする。Build Profile API は
// Unity 6.0+ 専用のため、Unity 2022.3 を要する VRChat 系プロジェクトに本パッケージを
// 追加してもコンパイルが通るよう全体をガードする (2022.3 では空コンパイル単位)。
#if UNITY_6000_0_OR_NEWER
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.Build.Profile;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Lilium.LiveStudio.Virgo
{
    /// <summary>
    /// Studio App のコマンドラインビルド用クラス
    /// Usage: Unity.exe -executeMethod Lilium.LiveStudio.Virgo.BuildStudioApp.Build
    /// </summary>
    public static class BuildStudioApp
    {
        private const string kStudioBuildProfilePath = "Assets/Settings/Build Profiles/App(Windows).asset";
        private const string kStudioBuildProfileDevPath = "Assets/Settings/Build Profiles/App(Windows,Development).asset";
        // 出力先ルート（"Builds/<プロジェクトフォルダ名>" を導出する）。exe 名もプロジェクト名から決まる。
        private const string kBuildsRootFolder = "Builds";
        private const string kStudioPackageName = "jp.lilium.livestudio.virgo";
        // Tools~ を持つパッケージ群。すべての Tools~ を build/Tools/ にマージコピーする。
        private static readonly string[] kToolsSourcePackages = { "jp.lilium.remotecontrol", "jp.lilium.livestudio.virgo" };
        // ビルド中の一時出力先（再帰コピー問題を回避）
        private const string kTempBuildFolder = "Temp/StudioBuild";

        /// <summary>
        /// コマンドラインからStudio Appをビルドするメソッド（Release Build）
        /// Usage: Unity.exe -executeMethod Lilium.LiveStudio.Virgo.BuildStudioApp.Build
        /// </summary>
        public static void Build()
        {
            BuildInternal(kStudioBuildProfilePath, "Release", "", exitOnComplete: true);
        }

        /// <summary>
        /// コマンドラインからStudio AppをDevelopmentビルドするメソッド
        /// Usage: Unity.exe -executeMethod Lilium.LiveStudio.Virgo.BuildStudioApp.BuildDevelopment
        /// </summary>
        public static void BuildDevelopment()
        {
            BuildInternal(kStudioBuildProfileDevPath, "Development", "_Dev", exitOnComplete: true);
        }

        /// <summary>
        /// エディタ内からStudio Appをビルドするメソッド（Release Build）
        /// </summary>
        public static void BuildFromEditor()
        {
            BuildInternal(kStudioBuildProfilePath, "Release", "", exitOnComplete: false);
        }

        /// <summary>
        /// エディタ内からStudio AppをDevelopmentビルドするメソッド
        /// </summary>
        public static void BuildDevelopmentFromEditor()
        {
            BuildInternal(kStudioBuildProfileDevPath, "Development", "_Dev", exitOnComplete: false);
        }

        /// <summary>
        /// 内部ビルドメソッド
        /// </summary>
        /// <param name="profilePath">Build Profileのパス</param>
        /// <param name="buildType">ビルドタイプ（"Release" or "Development"）</param>
        /// <param name="outputSuffix">出力フォルダのサフィックス</param>
        /// <param name="exitOnComplete">ビルド完了後にEditorを終了するか（コマンドラインビルド用）</param>
        private static void BuildInternal(string profilePath, string buildType, string outputSuffix, bool exitOnComplete)
        {
            Debug.Log($"[Studio] ===== Build Studio App Started ({buildType}) =====");

            // Studio Build Profileをロード
            var studioProfile = AssetDatabase.LoadAssetAtPath<BuildProfile>(profilePath);
            if (studioProfile == null)
            {
                Debug.LogError($"[Studio] Build Profile not found: {profilePath}");
                if (exitOnComplete) EditorApplication.Exit(1);
                return;
            }

            Debug.Log($"[Studio] Loaded Build Profile: {studioProfile.name}");
            Debug.Log($"[Studio] Build Type: {buildType}");

            // パス設定。出力先はプロジェクトフォルダ名から導出する
            // （例: VirgoMotionStudio → Builds/VirgoMotionStudio、VirgoMotionStudioVRC → Builds/VirgoMotionStudioVRC）。
            string projectPath = Path.GetDirectoryName(Application.dataPath);
            string virgoRoot = Path.GetDirectoryName(projectPath); // Virgo
            string projectName = Path.GetFileName(projectPath);
            string exeName = projectName + ".exe";

            // 最終出力先（Developmentの場合は "_Dev" サフィックス付き）
            string finalOutputDir = Path.Combine(virgoRoot, kBuildsRootFolder, projectName + outputSuffix);
            string finalOutputPath = Path.Combine(finalOutputDir, exeName);

            // 一時ビルド先（プロジェクト内のTempフォルダ）
            string tempBuildDir = Path.Combine(projectPath, kTempBuildFolder);
            string tempOutputPath = Path.Combine(tempBuildDir, exeName);

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
            foreach (var scene in studioProfile.scenes)
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
                if (exitOnComplete) EditorApplication.Exit(1);
                return;
            }

            // batchmode では起動時のアクティブシーンが未保存(path="")になる。lilToon の OnPreprocessBuild は
            // ビルドシーンを走査後に元シーンを EditorSceneManager.OpenScene(startScenePath) で復元するが、
            // path="" だと ArgumentException を投げる。すると lilToon のシェーダバリアント最適化が中断し、
            // 全バリアントがコンパイルされてビルドが極端に遅くなる。実シーンを開いて回避する
            // （エディタ実行時は既にシーンが開いているので何もしない）。
            if (string.IsNullOrEmpty(UnityEngine.SceneManagement.SceneManager.GetActiveScene().path))
            {
                UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scenes[0], UnityEditor.SceneManagement.OpenSceneMode.Single);
                Debug.Log($"[Studio] Opened scene '{scenes[0]}' (batchmode) to avoid lilToon empty-active-scene build error.");
            }

            // ビルドオプションを設定（一時フォルダに出力）
            var buildOptions = new BuildPlayerWithProfileOptions
            {
                buildProfile = studioProfile,
                locationPathName = tempOutputPath,
                options = BuildOptions.None
            };

            Debug.Log($"[Studio] Profile: {buildOptions.buildProfile.name}, BuildType: {buildType}");
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

                    // 各パッケージの Tools~ フォルダを出力先にマージコピー（Toolsサブフォルダは除外して再帰コピーを防ぐ）
                    string toolsDestDir = Path.Combine(finalOutputDir, "Tools");
                    var excludeFolders = new HashSet<string> { "Tools" };
                    foreach (var packageName in kToolsSourcePackages)
                    {
                        var packageInfo = PackageInfo.FindForPackageName(packageName);
                        if (packageInfo == null)
                        {
                            Debug.LogWarning($"[Studio] Package not found: {packageName}");
                            continue;
                        }
                        string toolsSourceDir = Path.Combine(packageInfo.resolvedPath, "Tools~");
                        if (!Directory.Exists(toolsSourceDir))
                        {
                            continue;
                        }
                        Debug.Log($"[Studio] Copying Tools folder from: {toolsSourceDir}");
                        CopyDirectory(toolsSourceDir, toolsDestDir, excludeFolders);
                        Debug.Log($"[Studio] Tools folder copied to: {toolsDestDir}");
                    }

                    Debug.Log("[Studio] ===== BUILD SUCCEEDED =====");
                    Debug.Log($"[Studio] Output: {finalOutputPath}");
                    Debug.Log($"[Studio] Size: {report.summary.totalSize / (1024 * 1024):F2} MB");
                    Debug.Log($"[Studio] Time: {report.summary.totalTime.TotalSeconds:F1} seconds");

                    // エディタ内ビルドの場合は出力先フォルダを開く
                    if (!exitOnComplete)
                    {
                        EditorUtility.RevealInFinder(finalOutputPath);
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[Studio] Failed to copy build output: {ex.Message}");
                    if (exitOnComplete) EditorApplication.Exit(1);
                    return;
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

                if (exitOnComplete) EditorApplication.Exit(0);
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

                if (exitOnComplete) EditorApplication.Exit(1);
            }
        }

        /// <summary>
        /// ディレクトリを再帰的にコピー
        /// </summary>
        private static void CopyDirectory(string sourceDir, string destDir, HashSet<string> excludeFolderNames = null)
        {
            Directory.CreateDirectory(destDir);

            foreach (var file in Directory.GetFiles(sourceDir))
            {
                string destFile = Path.Combine(destDir, Path.GetFileName(file));
                File.Copy(file, destFile, true);
            }

            foreach (var dir in Directory.GetDirectories(sourceDir))
            {
                string dirName = Path.GetFileName(dir);

                // 除外フォルダ名に一致する場合はスキップ
                if (excludeFolderNames != null && excludeFolderNames.Contains(dirName))
                {
                    continue;
                }

                string destSubDir = Path.Combine(destDir, dirName);
                CopyDirectory(dir, destSubDir, excludeFolderNames);
            }
        }
    }
}
#endif
