// Copyright (c) You-Ri, 2026

using System;
using System.IO;
using UnityEngine;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Resolves paths under the open project's hidden ".livestudio" folder — local-only, regenerable
    /// data that is never part of the handed-over project. This is the counterpart of the visible
    /// "Settings" folder (the deliverable): anything that should not travel with the project lives here.
    /// Currently hosts the thumbnail cache (".livestudio/cache/thumbnails"); a "temp" folder and other
    /// caches can be added the same way.
    /// </summary>
    public static class ProjectPaths
    {
        public const string kHiddenDir = ".livestudio";
        public const string kCacheDir = "cache";
        public const string kThumbnailsDir = "thumbnails";

        /// <summary>
        /// Absolute path of "{projectPath}/.livestudio/cache/thumbnails", or null when no project is
        /// open. Does not create the directory.
        /// </summary>
        public static string GetThumbnailCacheDir()
        {
            var projectPath = ProjectManager.projectPath;
            if (string.IsNullOrEmpty(projectPath)) return null;
            return Path.Combine(projectPath, kHiddenDir, kCacheDir, kThumbnailsDir);
        }

        /// <summary>
        /// Same as <see cref="GetThumbnailCacheDir"/> but creates the directory. Returns null when no
        /// project is open or the directory cannot be created.
        /// </summary>
        public static string EnsureThumbnailCacheDir()
        {
            var dir = GetThumbnailCacheDir();
            if (string.IsNullOrEmpty(dir)) return null;
            try
            {
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Studio] Failed to create cache directory '{dir}': {ex.Message}");
                return null;
            }
            return dir;
        }

        /// <summary>True when <paramref name="path"/> lies inside any ".livestudio" folder.</summary>
        public static bool IsInsideHiddenDir(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            var needle = "/" + kHiddenDir + "/";
            return path.Replace('\\', '/').IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
