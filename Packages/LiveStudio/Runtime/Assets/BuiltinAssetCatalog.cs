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
    /// The catalog is kind-agnostic: every entry carries the <see cref="Entry.type"/> of the
    /// <see cref="BuiltinAssetTypeDescriptor"/> that owns it, so which asset types are baked is decided by
    /// <see cref="BuiltinAssetTypeRegistry"/> rather than by this format.
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

        /// <summary>One built-in asset baked from a Resources asset.</summary>
        [Serializable]
        public struct Entry
        {
            /// <summary>
            /// The <see cref="BuiltinAssetTypeDescriptor.typeName"/> of the kind that owns this entry (the
            /// asset type's simple name, e.g. <c>AnimationClip</c>). Resolved back through
            /// <see cref="BuiltinAssetTypeRegistry.Find"/> to load the asset and to create its catalog entry.
            /// </summary>
            public string type;

            /// <summary>
            /// The asset GUID (a sub-asset uses the compound <c>guid:localId</c> key), under which the asset
            /// is registered in <see cref="Lilium.RemoteControl.AssetRegistry"/> and referenced by a selector.
            /// </summary>
            public string guid;

            /// <summary>The <c>Resources.Load</c> path (Resources-relative, no extension) used to load the asset at runtime.</summary>
            public string resourcesPath;

            /// <summary>The asset's display name, baked so the catalog entry can be listed without loading it.</summary>
            public string name;

            /// <summary>
            /// The <c>Resources.Load</c> path of an authored <see cref="BundleThumbnail"/> sibling holding this
            /// asset's preview image, or empty when none is authored. Kept separate from
            /// <see cref="resourcesPath"/> so the thumbnail's raw bytes can be pre-warmed into
            /// <see cref="ThumbnailCache"/> at startup without loading the asset itself (a prefab would drag its
            /// whole dependency tree into memory; a thumbnail must not). Empty is the graceful "no preview"
            /// state — the entry still lists and loads, the remote app just shows its icon. New field: appended
            /// last so older baked catalogs deserialize unchanged.
            /// </summary>
            public string thumbnailPath;
        }

        [SerializeField]
        private Entry[] _entries = Array.Empty<Entry>();

        /// <summary>The baked built-in assets, of every registered kind (never null).</summary>
        public Entry[] entries
        {
            get => _entries ?? Array.Empty<Entry>();
            set => _entries = value ?? Array.Empty<Entry>();
        }
    }
}
