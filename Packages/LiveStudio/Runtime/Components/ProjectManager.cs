// Copyright (c) You-Ri, 2026

using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Lilium.RemoteControl;
using Lilium.RemoteControl.LiveScene;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Tracks a single "project folder" and feeds its contents into <see cref="ExternalAssetManager"/>.
    /// Opening a project crawls the folder by path/extension only (never reading file contents): every
    /// supported file — props / avatars / scene bundles and live scenes (*.live.json / *.scene.json) —
    /// is registered as a path-only, disabled entry (loaded lazily, or opened in the live-scene case).
    /// The folder path is persisted to PlayerPrefs and the last project is re-opened automatically on
    /// startup.
    /// </summary>
    [ExposedClass(Icon = "folder")]
    public static class ProjectManager
    {
        // PlayerPrefs key mirroring the absolute project folder path (machine-global, not per-scene).
        private const string kProjectPathKey = "RemoteControl_ProjectPath";

        private static string _projectPath = "";

        /// <summary>Absolute path of the currently open project folder, or empty if none.</summary>
        [ExposedProperty, Hide]
        public static string projectPath => _projectPath;

        /// <summary>
        /// Display name of the project: the open folder's name, or the configured initial name
        /// (<see cref="LiveStudioProjectSettings.defaultProjectName"/>) when no project is open.
        /// </summary>
        [ExposedProperty, Hide]
        public static string projectName
        {
            get
            {
                if (!string.IsNullOrEmpty(_projectPath))
                {
                    var name = Path.GetFileName(_projectPath.TrimEnd('/', '\\'));
                    if (!string.IsNullOrEmpty(name)) return name;
                }

                var configured = LiveStudioProjectSettings.Instance?.defaultProjectName;
                return string.IsNullOrEmpty(configured) ? "Untitled" : configured;
            }
        }

        // Restore the persisted project path at runtime start (works with Domain Reload disabled).
        // When nothing is persisted (first launch) the initial project folder
        // Documents/<brand>/<DefaultProject> is created and opened. The crawl itself runs once
        // ExternalAssetManager is ready (see OnAssetManagerReady).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void _Initialize()
        {
            _projectPath = PlayerPrefs.GetString(kProjectPathKey, "");
            if (string.IsNullOrEmpty(_projectPath))
            {
                _projectPath = _EnsureInitialProjectPath();
            }

            // 開いているプロジェクトフォルダを Scene (live scene) の既定保存先に合わせる。
            _ApplySaveDirectory();
        }

        // Resolves the initial project folder Documents/<brand>/<DefaultProject> and creates it.
        // The default project is recomputed from the configured name each launch (not persisted);
        // only an explicit OpenProject persists a path.
        private static string _EnsureInitialProjectPath()
        {
            var name = LiveStudioProjectSettings.Instance?.defaultProjectName;
            if (string.IsNullOrEmpty(name)) name = "Untitled";
            return SavedPaths.EnsureProjectDirectory(name);
        }

        // Points the live-scene Save As dialog at the currently open project folder.
        private static void _ApplySaveDirectory()
        {
            if (!string.IsNullOrEmpty(_projectPath))
            {
                LiveSceneSaveSystem.SetSaveAsDefaultDirectory(_projectPath);
            }
        }

        /// <summary>
        /// Opens (or switches to) a project folder: persists the path and crawls it. Invoked from the
        /// remote app after the user picks a folder.
        /// </summary>
        [ExposedFunction(label = "PROJECT_OPEN_FOLDER"), Hide]
        public static void OpenProject(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath))
            {
                Debug.LogError("[LiveStudio] Project folder path cannot be empty.");
                return;
            }
            if (!Directory.Exists(folderPath))
            {
                Debug.LogError($"[LiveStudio] Project folder not found: {folderPath}");
                return;
            }

            _projectPath = folderPath;
            PlayerPrefs.SetString(kProjectPathKey, folderPath);
            PlayerPrefs.Save();

            _ApplySaveDirectory();
            _Crawl();
        }

        /// <summary>Re-scans the current project folder (e.g. after files were added on disk).</summary>
        [ExposedFunction(label = "PROJECT_RECRAWL"), Hide]
        public static void RecrawlProject()
        {
            _Crawl();
        }

        /// <summary>
        /// Called by <see cref="ExternalAssetManager"/> once it is ready (after the live scene, if any,
        /// has been restored), so the restored project folder's assets are merged in on startup and
        /// re-merged after opening a live scene.
        /// </summary>
        internal static void OnAssetManagerReady()
        {
            if (string.IsNullOrEmpty(_projectPath)) return;
            _Crawl();
        }

        // Walks the project folder by path/extension only — file contents are never read here — and
        // registers every supported file into ExternalAssetManager (path-only, disabled / dedup-synced).
        private static void _Crawl()
        {
            if (string.IsNullOrEmpty(_projectPath) || !Directory.Exists(_projectPath)) return;

            var assetPaths = new List<string>();
            foreach (var path in Directory.EnumerateFiles(_projectPath, "*", SearchOption.AllDirectories))
            {
                if (ExternalAssetManager.IsSupportedAssetFile(path)) assetPaths.Add(path);
                // Any other file is ignored; its contents are never read.
            }

            // current may be null if no manager exists yet; OnAssetManagerReady re-runs the crawl later.
            ExternalAssetManager.current?.RegisterDiscoveredAssets(assetPaths);
        }
    }
}
