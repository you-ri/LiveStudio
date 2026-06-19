// Copyright (c) You-Ri, 2026

using System;
using System.IO;

using UnityEngine;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// File format helpers for object presets (<c>*.preset.json</c>).
    ///
    /// A preset is a lightweight, human-readable JSON file that records how to recreate a loaded
    /// exposed object plus the parameter state to reapply afterwards. It carries no mesh. The
    /// recreation info lives under a <c>target</c> descriptor whose <c>strategy</c> selects how the
    /// object is rebuilt; today the only strategy is <c>asset</c> (load a source prop / avatar file and
    /// reapply state), but the descriptor is intentionally open so future strategies (factory-created
    /// objects, apply-to-existing, ...) can be added without changing the format. Presets live in the
    /// project folder and are crawled as loadable entries, so a user can adjust an object in the live
    /// scene, save it as a named preset, and recall the configuration later.
    ///
    /// <para>Structure (v2, asset strategy):</para>
    /// <code>
    /// {
    ///   "format": "object.preset",
    ///   "version": 2,
    ///   "name": "&lt;display name&gt;",
    ///   "target": {
    ///     "strategy": "asset",
    ///     "kind": "prop",              // or "avatar" — which concrete asset to recreate
    ///     "source": "Props/foo.glb",   // relative to the preset file when possible, else absolute
    ///     "sourceKind": "glb"           // redundant kind hint for validation
    ///   },
    ///   "state": { ... AssetStateSnapshot-shaped state (prop: delta, avatar: full) ... }
    /// }
    /// </code>
    /// Legacy <c>prop.preset</c> files (root-level <c>source</c>) are still read for back-compat.
    /// </summary>
    public static class PropPreset
    {
        /// <summary>Compound suffix of a preset file (case-insensitive).</summary>
        public const string Extension = ".preset.json";

        /// <summary>
        /// Format discriminator stored in the file. v2 wraps the recreation info in a
        /// <c>target</c> descriptor so additional strategies can be added later; the legacy
        /// <see cref="LegacyPropFormatId"/> (root-level <c>source</c>) is still read for back-compat.
        /// </summary>
        public const string FormatId = "object.preset";

        /// <summary>Legacy format id (prop presets written before the generic target descriptor).</summary>
        public const string LegacyPropFormatId = "prop.preset";

        /// <summary>Recreation strategy: load an external source asset (prop / avatar) and reapply state.</summary>
        public const string StrategyAsset = "asset";

        /// <summary>Current file format version.</summary>
        public const int Version = 2;

        /// <summary>Which concrete asset kind a preset recreates (drives PropAsset vs AvatarAsset on crawl).</summary>
        public enum AssetKind { Prop, Avatar }

        /// <summary>Parsed contents of a preset file.</summary>
        public struct PresetData
        {
            /// <summary>Display name.</summary>
            public string name;

            /// <summary>Source asset reference (relative to the preset file, or absolute).</summary>
            public string source;

            /// <summary>Source kind hint (e.g. <c>prop.lsb</c> / <c>glb</c> / <c>vrm</c> / <c>avatar.lsb</c>).</summary>
            public string sourceKind;

            /// <summary>Concrete asset kind to instantiate when loading the preset.</summary>
            public AssetKind kind;

            /// <summary>Serialized <see cref="AssetStateSnapshot"/>-shaped state (delta for props, full for avatars), or null/empty.</summary>
            public string state;
        }

        /// <summary>True if <paramref name="path"/> names a prop preset (<c>*.preset.json</c>).</summary>
        public static bool IsPresetFile(string path)
            => !string.IsNullOrEmpty(path) && path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Returns a short kind hint for a source asset path (<c>prop.lsb</c> / <c>glb</c> /
        /// <c>gltf</c> / <c>avatar.lsb</c> / <c>vrm</c> / <c>lsavatar</c>), or the plain extension
        /// without the dot as a fallback.
        /// </summary>
        public static string GetSourceKind(string sourcePath)
        {
            if (string.IsNullOrEmpty(sourcePath)) return "";
            if (LiveStudioBundle.IsPropBundle(sourcePath)) return "prop.lsb";
            if (sourcePath.EndsWith(".glb", StringComparison.OrdinalIgnoreCase)) return "glb";
            if (sourcePath.EndsWith(".gltf", StringComparison.OrdinalIgnoreCase)) return "gltf";
            if (sourcePath.EndsWith(".vrm", StringComparison.OrdinalIgnoreCase)) return "vrm";
            // The legacy *.lsavatar suffix must be checked before IsAvatarBundle, which also matches it.
            if (sourcePath.EndsWith(LiveStudioBundle.LegacyAvatarExtension, StringComparison.OrdinalIgnoreCase)) return "lsavatar";
            if (LiveStudioBundle.IsAvatarBundle(sourcePath)) return "avatar.lsb";
            return Path.GetExtension(sourcePath).TrimStart('.').ToLowerInvariant();
        }

        /// <summary>
        /// Reads a preset file and returns which concrete asset kind it recreates, defaulting to
        /// <see cref="AssetKind.Prop"/> on any read/parse failure (legacy prop-preset behavior).
        /// Used during crawl to pick PropAsset vs AvatarAsset for a <c>*.preset.json</c> entry.
        /// </summary>
        public static AssetKind PeekKind(string filePath)
        {
            try
            {
                if (TryParse(File.ReadAllText(filePath), out var data)) return data.kind;
            }
            catch { /* fall through to default */ }
            return AssetKind.Prop;
        }

        /// <summary>
        /// Builds the preset file JSON for an asset-strategy preset. <paramref name="stateJson"/> is the
        /// <see cref="AssetStateSnapshot"/>-shaped string (delta for props, full snapshot for avatars);
        /// it is embedded as a parsed object so the file stays readable. The recreation info is wrapped
        /// in a <c>target</c> descriptor so future strategies can extend the format.
        /// </summary>
        public static string BuildJson(AssetKind kind, string name, string source, string sourceKind, string stateJson)
        {
            JToken state;
            if (string.IsNullOrEmpty(stateJson))
            {
                state = new JObject();
            }
            else
            {
                try { state = JToken.Parse(stateJson); }
                catch { state = new JObject(); }
            }

            var target = new JObject
            {
                ["strategy"] = StrategyAsset,
                ["kind"] = kind == AssetKind.Avatar ? "avatar" : "prop",
                ["source"] = source ?? "",
                ["sourceKind"] = sourceKind ?? "",
            };

            var root = new JObject
            {
                ["format"] = FormatId,
                ["version"] = Version,
                ["name"] = name ?? "",
                ["target"] = target,
                ["state"] = state,
            };
            return root.ToString(Formatting.Indented);
        }

        /// <summary>
        /// Parses preset file JSON. Accepts both the v2 <c>object.preset</c> format (with a
        /// <c>target</c> descriptor) and the legacy <c>prop.preset</c> format (root-level
        /// <c>source</c>/<c>sourceKind</c>, always a prop). Returns false (and logs) if the content is
        /// unparseable, is not a preset, or targets a newer format version.
        /// </summary>
        public static bool TryParse(string json, out PresetData data)
        {
            data = default;
            if (string.IsNullOrEmpty(json))
            {
                Debug.LogError("[LiveStudio] Preset is empty.");
                return false;
            }

            JObject root;
            try { root = JObject.Parse(json); }
            catch (Exception e)
            {
                Debug.LogError($"[LiveStudio] Failed to parse preset: {e.Message}");
                return false;
            }

            var format = root["format"]?.Value<string>();
            data.name = root["name"]?.Value<string>() ?? "";
            data.state = root["state"] is JObject stateObj ? stateObj.ToString(Formatting.None) : null;

            // Legacy prop preset: source/sourceKind at the root, always a prop.
            if (string.Equals(format, LegacyPropFormatId, StringComparison.Ordinal))
            {
                data.source = root["source"]?.Value<string>() ?? "";
                data.sourceKind = root["sourceKind"]?.Value<string>() ?? "";
                data.kind = AssetKind.Prop;
                return true;
            }

            if (!string.Equals(format, FormatId, StringComparison.Ordinal))
            {
                Debug.LogError($"[LiveStudio] Not a preset (format='{format}').");
                return false;
            }

            var version = root["version"]?.Value<int>() ?? 0;
            if (version > Version)
            {
                Debug.LogError($"[LiveStudio] Preset version {version} is newer than supported ({Version}).");
                return false;
            }

            var target = root["target"] as JObject;
            data.source = target?["source"]?.Value<string>() ?? "";
            data.sourceKind = target?["sourceKind"]?.Value<string>() ?? "";
            data.kind = string.Equals(target?["kind"]?.Value<string>(), "avatar", StringComparison.Ordinal)
                ? AssetKind.Avatar
                : AssetKind.Prop;
            return true;
        }

        /// <summary>
        /// Replaces characters not allowed in file names with '_', returning a non-empty fallback when
        /// the input reduces to nothing.
        /// </summary>
        public static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "preset";
            var invalid = Path.GetInvalidFileNameChars();
            var chars = name.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (Array.IndexOf(invalid, chars[i]) >= 0) chars[i] = '_';
            }
            var result = new string(chars).Trim();
            return string.IsNullOrEmpty(result) ? "preset" : result;
        }

        /// <summary>
        /// Returns <paramref name="absolutePath"/> expressed relative to <paramref name="baseDir"/>
        /// using forward slashes, so a preset survives moving the project folder. Falls back to the
        /// absolute path when a relative path cannot be formed (e.g. a different drive).
        /// Implemented with <see cref="Uri"/> for Unity 2021 (.NET Standard 2.0) compatibility, where
        /// <c>Path.GetRelativePath</c> is unavailable.
        /// </summary>
        public static string Relativize(string absolutePath, string baseDir)
        {
            if (string.IsNullOrEmpty(absolutePath) || string.IsNullOrEmpty(baseDir)) return absolutePath;
            try
            {
                var baseUri = new Uri(_WithTrailingSeparator(baseDir));
                var fileUri = new Uri(absolutePath);
                if (baseUri.Scheme != fileUri.Scheme) return absolutePath; // different drive/scheme
                var relative = Uri.UnescapeDataString(baseUri.MakeRelativeUri(fileUri).ToString());
                return string.IsNullOrEmpty(relative) ? absolutePath : relative.Replace('\\', '/');
            }
            catch
            {
                return absolutePath;
            }
        }

        /// <summary>
        /// Resolves a stored <c>source</c> reference to an absolute path. Rooted paths are returned as
        /// is; relative paths are resolved against <paramref name="baseDir"/> (the preset file's folder).
        /// </summary>
        public static string ResolveSource(string source, string baseDir)
        {
            if (string.IsNullOrEmpty(source)) return source;
            if (Path.IsPathRooted(source)) return source;
            if (string.IsNullOrEmpty(baseDir)) return source;
            return Path.GetFullPath(Path.Combine(baseDir, source));
        }

        private static string _WithTrailingSeparator(string dir)
        {
            if (dir.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ||
                dir.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal))
            {
                return dir;
            }
            return dir + Path.DirectorySeparatorChar;
        }
    }
}
