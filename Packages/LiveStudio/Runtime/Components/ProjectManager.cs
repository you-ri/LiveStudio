// Copyright (c) You-Ri, 2026

using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Lilium.RemoteControl;

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

        // Restore the persisted project path at runtime start (works with Domain Reload disabled).
        // The crawl itself runs once ExternalAssetManager is ready (see OnAssetManagerReady).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void _Initialize()
        {
            _projectPath = PlayerPrefs.GetString(kProjectPathKey, "");
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
