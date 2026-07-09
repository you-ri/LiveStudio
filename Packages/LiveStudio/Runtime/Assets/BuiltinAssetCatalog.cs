// Copyright (c) You-Ri, 2026

using System;

using UnityEngine;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Baked catalog of app-embedded (built-in) assets that ship inside the app under a <c>Resources</c>
    /// folder. It bridges edit time — where <c>AssetDatabase</c> can read each asset's GUID and Resources
    /// load path — to runtime, where neither is available: <c>BuiltinAssetCatalogBuilder</c> scans the
    /// Resources folders at edit time and writes the entries here, and <see cref="BuiltinAssetRegistry"/>
    /// reads them at runtime to register each asset in <see cref="Lilium.RemoteControl.AssetRegistry"/> and
    /// to list it on the project's asset page.
    ///
    /// The asset GUID is kept as the key (rather than a new <c>res:</c> scheme) so a reference selected from
    /// a built-in asset serializes byte-identically to a baked scene reference: an existing live scene keeps
    /// resolving after an asset is moved into a Resources folder, since moving a Unity asset preserves its
    /// GUID.
    /// </summary>
    public class BuiltinAssetCatalog : ScriptableObject
    {
        /// <summary>The fixed Resources name this catalog is loaded by at runtime (see <see cref="BuiltinAssetRegistry"/>).</summary>
        public const string kResourcesName = "BuiltinAssetCatalog";

        /// <summary>One built-in <see cref="AnimationClip"/> baked from a Resources asset.</summary>
        [Serializable]
        public struct AnimationClipEntry
        {
            /// <summary>
            /// The asset GUID (a sub-asset uses the compound <c>guid:localId</c> key), under which the clip
            /// is registered in <see cref="Lilium.RemoteControl.AssetRegistry"/> and referenced by a selector.
            /// </summary>
            public string guid;

            /// <summary>The <c>Resources.Load</c> path (Resources-relative, no extension) used to load the clip at runtime.</summary>
            public string resourcesPath;

            /// <summary>The clip's display name, baked so the catalog entry can be listed without loading it.</summary>
            public string name;
        }

        [SerializeField]
        private AnimationClipEntry[] _animationClips = Array.Empty<AnimationClipEntry>();

        /// <summary>The baked built-in animation clips (never null).</summary>
        public AnimationClipEntry[] animationClips
        {
            get => _animationClips ?? Array.Empty<AnimationClipEntry>();
            set => _animationClips = value ?? Array.Empty<AnimationClipEntry>();
        }
    }
}
