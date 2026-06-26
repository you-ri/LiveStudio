// Copyright (c) You-Ri, 2026
using System;
using System.IO;
using UnityEngine;
using Newtonsoft.Json.Linq;

namespace Lilium.RemoteControl.LiveScene
{
    /// <summary>
    /// Reads and writes a startup state file (<c>{stateDir}/Settings/startup.json</c>) that
    /// records which live scene was last in use. The store is directory-based and carries no
    /// notion of a "project": callers pass whichever directory owns the state (the upper-level
    /// project manager passes the open project folder; standalone callers pass
    /// <see cref="Application.persistentDataPath"/>). A scene inside the state directory is
    /// stored relative to it; a scene outside is stored as an absolute path.
    /// </summary>
    public static class StartupStateStore
    {
        private const string kStateSubDir = "Settings";
        private const string kStartupFileName = "startup.json";

        private const string kFormatIdentifier = "jp.lilium.livestudio.startup";
        private const int kCurrentFormatVersion = 1;

        /// <summary>Absolute path of <c>{projectPath}/Settings/startup.json</c>.</summary>
        public static string GetStartupFilePath(string projectPath)
            => Path.Combine(GetStateDir(projectPath), kStartupFileName);

        /// <summary>Absolute path of <c>{projectPath}/Settings</c>.</summary>
        public static string GetStateDir(string projectPath)
            => Path.Combine(projectPath ?? string.Empty, kStateSubDir);

        /// <summary>
        /// Reads the recorded scene path and resolves it to an absolute path. A path stored
        /// relative resolves against <paramref name="projectPath"/>; an absolute stored path is
        /// returned as-is. Returns <c>null</c> when the file is missing, unreadable, or records
        /// no scene (a brand-new / unsaved scene).
        /// </summary>
        public static string Read(string projectPath)
        {
            if (string.IsNullOrEmpty(projectPath)) return null;

            var filePath = GetStartupFilePath(projectPath);
            if (!File.Exists(filePath)) return null;

            string scenePath;
            try
            {
                var json = File.ReadAllText(filePath);
                var root = JObject.Parse(json);
                scenePath = (string)root["scenePath"];
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RemoteControl] Failed to read startup state '{filePath}': {ex.Message}");
                return null;
            }

            if (string.IsNullOrEmpty(scenePath)) return null;
            if (Path.IsPathRooted(scenePath)) return scenePath;
            return Path.Combine(projectPath, scenePath);
        }

        /// <summary>
        /// Records the current scene. A scene inside the project folder is stored as a path
        /// relative to it (so the project stays portable); a scene outside the project is stored
        /// as an absolute path. An empty <paramref name="sceneFullPath"/> records "no scene".
        /// </summary>
        public static void Write(string projectPath, string sceneFullPath)
        {
            if (string.IsNullOrEmpty(projectPath)) return;

            var dir = GetStateDir(projectPath);
            var scenePath = _ToStoredPath(projectPath, sceneFullPath);

            var root = new JObject
            {
                ["format"] = kFormatIdentifier,
                ["formatVersion"] = kCurrentFormatVersion,
                ["scenePath"] = scenePath,
            };

            try
            {
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, kStartupFileName), root.ToString());
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RemoteControl] Failed to write startup state under '{dir}': {ex.Message}");
            }
        }

        /// <summary>Removes the startup state file if present.</summary>
        public static void Delete(string projectPath)
        {
            if (string.IsNullOrEmpty(projectPath)) return;
            var filePath = GetStartupFilePath(projectPath);
            if (File.Exists(filePath)) File.Delete(filePath);
        }

        // Stores paths inside the project as project-relative (forward slashes for portability),
        // and paths outside the project as their original absolute form. Empty stays empty.
        private static string _ToStoredPath(string projectPath, string sceneFullPath)
        {
            if (string.IsNullOrEmpty(sceneFullPath)) return string.Empty;
            if (!Path.IsPathRooted(sceneFullPath)) return sceneFullPath.Replace('\\', '/');

            var projectFull = Path.GetFullPath(projectPath).TrimEnd('/', '\\');
            var sceneFull = Path.GetFullPath(sceneFullPath);

            // Unity 2021 has no Path.GetRelativePath; relativize by prefix match instead.
            var prefix = projectFull + Path.DirectorySeparatorChar;
            if (sceneFull.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return sceneFull.Substring(prefix.Length).Replace('\\', '/');

            return sceneFull;
        }
    }
}
