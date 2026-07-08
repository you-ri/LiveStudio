// Copyright (c) You-Ri, 2026

using System;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Builds and parses the string keys that identify individual assets loaded from external project
    /// files, so an <see cref="AssetSelectorAttribute"/> reference can round-trip through the live scene
    /// and resolve back at runtime (where AssetDatabase is unavailable).
    ///
    /// Two key spaces share one string slot (the AssetSelector <c>@guid</c> field), distinguished by a
    /// prefix:
    /// <list type="bullet">
    ///   <item>A bare Unity GUID (optionally <c>guid:localId</c>) — an in-app asset baked at edit time
    ///   and registered by its owning component. These keys carry no prefix, so existing live scenes
    ///   stay byte-identical.</item>
    ///   <item><c>file:&lt;project-relative-path&gt;#&lt;clipName&gt;</c> — one asset inside an external
    ///   bundle (e.g. a clip in a <c>*.anim.lsb</c>). The path is project-relative (forward slashes) so a
    ///   saved scene survives moving the project folder; the clip name selects one asset within the bundle.</item>
    /// </list>
    ///
    /// Registration in <see cref="Lilium.RemoteControl.AssetRegistry"/> uses these same keys, so the
    /// registry doubles as the unified resolver for both key spaces.
    /// </summary>
    public static class ExternalAssetKey
    {
        /// <summary>Prefix marking a key that addresses an asset inside an external project file.</summary>
        public const string FilePrefix = "file:";

        // Separates the bundle path from the asset (clip) name within a file key.
        private const char kMemberSeparator = '#';

        /// <summary>True when <paramref name="key"/> addresses an external file asset (has the file prefix).</summary>
        public static bool IsFileKey(string key)
            => !string.IsNullOrEmpty(key) && key.StartsWith(FilePrefix, StringComparison.Ordinal);

        /// <summary>
        /// Builds the key for a clip inside a bundle: <c>file:&lt;relativePath&gt;#&lt;clipName&gt;</c>.
        /// <paramref name="relativePath"/> is the bundle path relative to the project folder (forward
        /// slashes). Returns null when either part is empty.
        /// </summary>
        public static string BuildClipKey(string relativePath, string clipName)
        {
            if (string.IsNullOrEmpty(relativePath) || string.IsNullOrEmpty(clipName)) return null;
            return FilePrefix + relativePath + kMemberSeparator + clipName;
        }

        /// <summary>
        /// Parses a file key into its bundle-relative path and member (clip) name. Splits on the first
        /// <c>#</c> after the prefix. Returns false for a null / non-file key or one missing the member
        /// separator.
        /// </summary>
        public static bool TryParseClipKey(string key, out string relativePath, out string clipName)
        {
            relativePath = null;
            clipName = null;
            if (!IsFileKey(key)) return false;

            var body = key.Substring(FilePrefix.Length);
            int sep = body.IndexOf(kMemberSeparator);
            if (sep <= 0 || sep >= body.Length - 1) return false;

            relativePath = body.Substring(0, sep);
            clipName = body.Substring(sep + 1);
            return true;
        }
    }
}
