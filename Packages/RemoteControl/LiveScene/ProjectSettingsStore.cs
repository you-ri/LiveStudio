// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;
using System.IO;

using UnityEngine;

using Newtonsoft.Json.Linq;

using Lilium.RemoteControl;

namespace Lilium.RemoteControl.LiveScene
{
    /// <summary>
    /// Reads and writes per-class project settings files under
    /// <c>{projectPath}/Settings/{ClassName}.settings.json</c>. These hold
    /// <see cref="PersistScope.Project"/>-scoped members that should be shared across every
    /// live scene of the project, so they live in the project folder rather than in a scene file.
    ///
    /// Mirrors <see cref="StartupStateStore"/>'s role/layout and reuses its Settings directory.
    /// </summary>
    public static class ProjectSettingsStore
    {
        private const string kSettingsExtension = ".settings.json";

        /// <summary>Absolute path of <c>{projectPath}/Settings/{className}.settings.json</c>.</summary>
        public static string GetSettingsFilePath(string projectPath, string className)
            => Path.Combine(StartupStateStore.GetStateDir(projectPath), _Sanitize(className) + kSettingsExtension);

        /// <summary>
        /// Writes each <c>{ClassName}.settings.json</c> built by
        /// <see cref="ProjectSettingsSerializer.BuildPerClassJson"/>. No-op when no project folder
        /// is set or there is nothing to write.
        /// </summary>
        public static void WriteAll(string projectPath, Dictionary<string, string> perClassJson)
        {
            if (string.IsNullOrEmpty(projectPath)) return;
            if (perClassJson == null || perClassJson.Count == 0) return;

            var dir = StartupStateStore.GetStateDir(projectPath);
            try
            {
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                foreach (var kv in perClassJson)
                {
                    var path = Path.Combine(dir, _Sanitize(kv.Key) + kSettingsExtension);
                    File.WriteAllText(path, kv.Value);
                }
                _PruneOrphans(dir, perClassJson);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RemoteControl] Failed to write project settings under '{dir}': {ex.Message}");
            }
        }

        // Removes stale settings files after a write:
        // - a file whose class no longer exists (no ExposedClass for its entries' @type), and
        // - a file named after a FORMER class name whose current-name file was just written (a rename
        //   left the old file behind; keeping it would re-apply stale values via [FormerlyExposedAs]).
        // A current class's file that simply was not written this session (its object is not loaded now)
        // is KEPT, so an absent object never loses its project settings. Files we cannot classify
        // (unparseable / no @type) are left untouched.
        private static void _PruneOrphans(string dir, Dictionary<string, string> written)
        {
            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in written) keep.Add(_Sanitize(kv.Key));

            string[] files;
            try { files = Directory.GetFiles(dir, "*" + kSettingsExtension); }
            catch { return; }

            foreach (var file in files)
            {
                var fileName = Path.GetFileName(file);
                var baseName = fileName.Substring(0, fileName.Length - kSettingsExtension.Length);
                if (keep.Contains(baseName)) continue; // just written / refreshed

                var fileType = _ReadEntryType(file);
                if (string.IsNullOrEmpty(fileType)) continue; // unknown shape -> leave it

                var ec = ExposedClass.Find(fileType);
                if (ec == null)
                {
                    _TryDelete(file, $"class '{fileType}' no longer exists");
                }
                else if (!string.Equals(ec.typeName, fileType, StringComparison.Ordinal)
                         && keep.Contains(_Sanitize(ec.typeName)))
                {
                    // fileType is a former name and the current-name file was just written -> stale duplicate.
                    _TryDelete(file, $"renamed '{fileType}' -> '{ec.typeName}' superseded by current file");
                }
                // else: current class whose object is just absent this session -> keep.
            }
        }

        // Reads the @type of the first entry of a settings file (all entries in a file share one class).
        // Returns null when the file cannot be parsed or has no typed entry.
        private static string _ReadEntryType(string file)
        {
            try
            {
                var root = JObject.Parse(File.ReadAllText(file));
                if (!(root["objects"] is JArray arr)) return null;
                foreach (var e in arr)
                {
                    if (e is JObject o)
                    {
                        var t = o["@type"]?.Value<string>();
                        if (!string.IsNullOrEmpty(t)) return t;
                    }
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        private static void _TryDelete(string file, string reason)
        {
            try
            {
                File.Delete(file);
                Debug.Log($"[RemoteControl] Removed orphan project settings '{Path.GetFileName(file)}' ({reason}).");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RemoteControl] Failed to remove orphan project settings '{file}': {ex.Message}");
            }
        }

        /// <summary>
        /// Applies every <c>*.settings.json</c> in the project's Settings directory to the live
        /// objects. Globbing means class names need not be known up front, and added/removed
        /// classes are handled automatically. No-op when the directory is missing.
        /// </summary>
        public static void ApplyAll(string projectPath, IExposedObjectResolver resolver)
        {
            if (string.IsNullOrEmpty(projectPath) || resolver == null) return;

            var dir = StartupStateStore.GetStateDir(projectPath);
            if (!Directory.Exists(dir)) return;

            string[] files;
            try
            {
                files = Directory.GetFiles(dir, "*" + kSettingsExtension);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RemoteControl] Failed to enumerate project settings under '{dir}': {ex.Message}");
                return;
            }

            foreach (var file in files)
            {
                try
                {
                    var json = File.ReadAllText(file);
                    ProjectSettingsSerializer.ApplyJson(json, resolver);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[RemoteControl] Failed to apply project settings '{file}': {ex.Message}");
                }
            }
        }

        /// <summary>Removes the settings file for a class if present.</summary>
        public static void Delete(string projectPath, string className)
        {
            if (string.IsNullOrEmpty(projectPath)) return;
            var filePath = GetSettingsFilePath(projectPath, className);
            if (File.Exists(filePath)) File.Delete(filePath);
        }

        // typeName は通常安全な識別子だが、念のためパス区切り等の不正文字を '_' へ置換する。
        private static string _Sanitize(string className)
        {
            if (string.IsNullOrEmpty(className)) return "_";
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                className = className.Replace(c, '_');
            }
            return className;
        }
    }
}
