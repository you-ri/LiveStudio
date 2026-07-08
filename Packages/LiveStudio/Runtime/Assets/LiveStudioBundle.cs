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
    ///   <item><c>*.set.lsb</c> — a set bundle (one Unity scene, loaded additively onto the stage).</item>
    ///   <item><c>*.scene.lsb</c> — legacy set bundle, kept for input compatibility.</item>
    ///   <item><c>*.avatar.lsb</c> — an avatar bundle (a single root prefab).</item>
    ///   <item><c>*.prop.lsb</c> — a prop bundle (a single root prefab; avatar prop or stage prop).</item>
    ///   <item><c>*.lsavatar</c> — legacy avatar bundle, kept for input compatibility.</item>
    /// </list>
    ///
    /// Because both <c>.set.lsb</c> and <c>.avatar.lsb</c> end in <c>.lsb</c>,
    /// <see cref="Path.GetExtension(string)"/> cannot tell them apart. Detection therefore
    /// matches the compound suffix of the file name, not just the last extension.
    /// </summary>
    public static class LiveStudioBundle
    {
        /// <summary>Suffix of a set bundle file (case-insensitive).</summary>
        public const string SetExtension = ".set.lsb";

        /// <summary>Legacy set bundle suffix (formerly "scene bundle"). Accepted on input for backward
        /// compatibility, no longer produced on export.</summary>
        public const string LegacySetExtension = ".scene.lsb";

        /// <summary>Suffix of an avatar bundle file (case-insensitive).</summary>
        public const string AvatarExtension = ".avatar.lsb";

        /// <summary>Suffix of a prop bundle file (case-insensitive).</summary>
        public const string PropExtension = ".prop.lsb";

        /// <summary>
        /// Suffix of an animation bundle file (case-insensitive). Unlike the prop / avatar bundles
        /// (a single root prefab), an animation bundle packs one or more <see cref="UnityEngine.AnimationClip"/>
        /// assets so a curated set of clips (e.g. a dance pack) ships as one file. Individual clips are
        /// addressed by <c>file:&lt;relative-path&gt;#&lt;clipName&gt;</c> (see <see cref="ExternalAssetKey"/>).
        /// </summary>
        public const string AnimationExtension = ".anim.lsb";

        /// <summary>Legacy avatar bundle suffix. Accepted on input, no longer produced on export.</summary>
        public const string LegacyAvatarExtension = ".lsavatar";

        /// <summary>
        /// True if <paramref name="path"/> names a set bundle, either the current <c>*.set.lsb</c>
        /// form or the legacy <c>*.scene.lsb</c> form.
        /// </summary>
        public static bool IsSetBundle(string path)
            => _HasSuffix(path, SetExtension) || _HasSuffix(path, LegacySetExtension);

        /// <summary>True if <paramref name="path"/> names a prop bundle (<c>*.prop.lsb</c>).</summary>
        public static bool IsPropBundle(string path) => _HasSuffix(path, PropExtension);

        /// <summary>True if <paramref name="path"/> names an animation bundle (<c>*.anim.lsb</c>).</summary>
        public static bool IsAnimationBundle(string path) => _HasSuffix(path, AnimationExtension);

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
