// Copyright (c) You-Ri, 2026

using System;

using UnityEngine;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// The project's declaration of which scenes shipped inside the app are offered as built-in sets —
    /// the opt-in list <see cref="BuiltinSetSource"/> enumerates at runtime. Authored by hand (one entry
    /// per set scene, through the asset's inspector), unlike <see cref="BuiltinAssetCatalog"/>, which is
    /// baked automatically from whatever sits under a <c>Resources</c> folder.
    ///
    /// Declaring is deliberately explicit rather than "every scene in the build is a set": a build also
    /// carries scenes that are not stages (a bootstrap scene, a scene loaded additively for its own
    /// purposes), and offering one of those as a set would let it be loaded on top of itself. A scene not
    /// listed here is simply not a set.
    ///
    /// A scene cannot live under <c>Resources</c>, so this asset does instead: it is loaded by the fixed
    /// name <see cref="kResourcesName"/> and carries what runtime needs — the scene's GUID (the stable
    /// identity persisted to a live scene), its build path (how <c>SceneManager</c> loads it), an optional
    /// display name and an optional preview image. The GUID / path pair is filled in by the inspector as
    /// the scene is dropped in, and re-resolved from the GUID when the scene is moved or renamed.
    /// </summary>
    public class BuiltinSetList : ScriptableObject
    {
        /// <summary>The fixed Resources name this declaration is loaded by at runtime.</summary>
        public const string kResourcesName = "BuiltinSetList";

        /// <summary>One declared built-in set.</summary>
        [Serializable]
        public struct Entry
        {
            /// <summary>
            /// The scene asset's GUID: the stable identity persisted to the live scene, and what the entry
            /// is keyed and de-duplicated by. Survives moving or renaming the scene, unlike
            /// <see cref="scenePath"/>.
            /// </summary>
            public string guid;

            /// <summary>
            /// The scene's build path (e.g. <c>Assets/Sets/Room.unity</c>), which
            /// <c>SceneManager.LoadSceneAsync</c> loads by. Derived from <see cref="guid"/> at edit time and
            /// re-derived whenever the scene moves, so it is a cache of the identity, never the identity.
            /// </summary>
            public string scenePath;

            /// <summary>
            /// The name shown to the operator, or empty to use the scene's file name. ⚠ This name is what
            /// the stage's recorded state refers a set by, so renaming a declared set is as breaking as
            /// renaming a set bundle's file: takes and live scenes saved under the old name no longer find it.
            /// </summary>
            public string displayName;

            /// <summary>
            /// Optional preview image shown on the set's card, or null for none (the remote app then shows
            /// the set icon). A <see cref="BundleThumbnail"/> rather than a <c>Texture2D</c> so the bytes are
            /// served over REST as-is — the same shape a bundle's packed thumbnail takes.
            /// </summary>
            public BundleThumbnail thumbnail;
        }

        [SerializeField]
        private Entry[] _entries = Array.Empty<Entry>();

        /// <summary>The declared built-in sets, in display order (never null).</summary>
        public Entry[] entries
        {
            get => _entries ?? Array.Empty<Entry>();
            set => _entries = value ?? Array.Empty<Entry>();
        }
    }
}
