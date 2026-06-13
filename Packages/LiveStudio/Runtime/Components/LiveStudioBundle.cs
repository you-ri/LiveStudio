// Copyright (c) You-Ri, 2026

using System;
using System.IO;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Shared definitions for LiveStudio bundle files (AssetBundle based).
    ///
    /// LiveStudio distributes content through AssetBundles using compound extensions
    /// so the payload kind is encoded in the file name itself:
    /// <list type="bullet">
    ///   <item><c>*.scene.lsb</c> — a scene bundle (one Unity scene, loaded additively).</item>
    ///   <item><c>*.avatar.lsb</c> — an avatar bundle (a single root prefab).</item>
    ///   <item><c>*.lsavatar</c> — legacy avatar bundle, kept for input compatibility.</item>
    /// </list>
    ///
    /// Because both <c>.scene.lsb</c> and <c>.avatar.lsb</c> end in <c>.lsb</c>,
    /// <see cref="Path.GetExtension(string)"/> cannot tell them apart. Detection therefore
    /// matches the compound suffix of the file name, not just the last extension.
    /// </summary>
    public static class LiveStudioBundle
    {
        /// <summary>Suffix of a scene bundle file (case-insensitive).</summary>
        public const string SceneExtension = ".scene.lsb";

        /// <summary>Suffix of an avatar bundle file (case-insensitive).</summary>
        public const string AvatarExtension = ".avatar.lsb";

        /// <summary>Legacy avatar bundle suffix. Accepted on input, no longer produced on export.</summary>
        public const string LegacyAvatarExtension = ".lsavatar";

        /// <summary>True if <paramref name="path"/> names a scene bundle (<c>*.scene.lsb</c>).</summary>
        public static bool IsSceneBundle(string path) => _HasSuffix(path, SceneExtension);

        /// <summary>
        /// True if <paramref name="path"/> names an avatar bundle, either the current
        /// <c>*.avatar.lsb</c> form or the legacy <c>*.lsavatar</c> form.
        /// </summary>
        public static bool IsAvatarBundle(string path)
            => _HasSuffix(path, AvatarExtension) || _HasSuffix(path, LegacyAvatarExtension);

        private static bool _HasSuffix(string path, string suffix)
            => !string.IsNullOrEmpty(path) && path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
    }
}
