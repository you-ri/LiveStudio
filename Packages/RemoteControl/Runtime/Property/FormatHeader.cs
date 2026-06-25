// Copyright (c) You-Ri, 2026

using UnityEngine;

using Newtonsoft.Json.Linq;

namespace Lilium.RemoteControl
{
    /// <summary>
    /// Shared "format header" for the project's JSON document formats (live scene, project settings,
    /// object presets). Centralizes the <c>{ "format", "formatVersion" }</c> header and the single
    /// versioning policy so every format reads and writes it identically.
    ///
    /// <para>Versioning policy (defined in one place):</para>
    /// <list type="bullet">
    ///   <item><b>Write</b> — each format keeps its own <c>format</c> id string and its current version
    ///   constant; this helper writes them as the first two fields, preserving byte-for-byte output.</item>
    ///   <item><b>Read</b> — the <c>format</c> string is informational and NOT validated, so legacy ids
    ///   still load. A missing <c>formatVersion</c> is treated as the minimum supported (pre-header
    ///   legacy files). A version below <c>minVersion</c> is rejected; a version above
    ///   <c>currentVersion</c> is read best-effort (warn and continue), since the document shape is
    ///   stable and unknown fields are ignored.</item>
    ///   <item><b>Incompatible changes</b> — introduce a NEW <c>format</c> id rather than relying on a
    ///   version bump, so a reader that must hard-reject can do so by id at its call site.</item>
    /// </list>
    /// </summary>
    public static class FormatHeader
    {
        /// <summary>JSON key holding the format identifier string.</summary>
        public const string FormatKey = "format";

        /// <summary>JSON key holding the integer format version.</summary>
        public const string VersionKey = "formatVersion";

        /// <summary>
        /// Writes <paramref name="formatId"/> and <paramref name="version"/> as the first two properties
        /// of <paramref name="root"/> (call before adding the rest, to preserve field order).
        /// </summary>
        public static void Write(JObject root, string formatId, int version)
        {
            if (root == null) return;
            root[FormatKey] = formatId;
            root[VersionKey] = version;
        }

        /// <summary>
        /// Reads and validates the format version under the shared policy. Returns false (and logs an
        /// error) only when the file is too old to read; a newer version logs a warning but returns true
        /// (best-effort). A missing version is treated as <paramref name="minVersion"/>.
        /// <paramref name="label"/> names the format in log messages (e.g. "Scene", "Preset").
        /// </summary>
        public static bool TryReadVersion(JObject root, string label, int currentVersion, int minVersion, out int version)
        {
            version = root?[VersionKey]?.Value<int>() ?? minVersion;
            if (version < minVersion)
            {
                Debug.LogError($"[RemoteControl] {label} format version {version} is older than the minimum supported ({minVersion}).");
                return false;
            }
            if (version > currentVersion)
            {
                Debug.LogWarning($"[RemoteControl] {label} format version {version} is newer than supported ({currentVersion}); reading best-effort.");
            }
            return true;
        }
    }
}
