// Copyright (c) You-Ri, 2026

using System;
using System.Threading.Tasks;

using UnityEngine;

using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// A catalog entry for a <c>*.pack.lsb</c> asset pack managed by <see cref="ExternalAssetManager"/>.
    /// Unlike a prop or avatar, a pack is a reference resource, not scene content: it has no load/unload
    /// toggle and instantiates nothing. It exists in the catalog so its members can be listed and selected
    /// (e.g. an animation clip for the avatar body-override slot); the members themselves are loaded on
    /// demand — and registered in <see cref="AssetRegistry"/> under their
    /// <c>file:&lt;relative-path&gt;#&lt;assetName&gt;</c> keys — by <see cref="PackBundleLoader"/> the
    /// first time a member of the pack is requested.
    ///
    /// A pack carries no payload kind in its name, so what it holds is known only once it is opened. That
    /// is deliberate: two-level discovery keeps the project crawl content-free — the crawl lists
    /// <c>*.pack.lsb</c> files as these entries by path alone (no pack is opened), and only the pack a user
    /// actually drills into is opened, to enumerate its member names (<see cref="GetMemberNamesAsync"/>).
    /// </summary>
    [Serializable]
    [LiveClass("PackBundleAsset", Category = "Asset", Icon = "animation")]
    [FormerlyNamedAs("AnimationBundleAsset")]
    public class PackBundleAsset : AssetBase
    {
        // Additive-style (not a single-selection group), though it never actually loads via the toggle.
        public override bool isExclusive => false;

        /// <summary>
        /// The project-relative pack path used to build member keys (<see cref="ExternalAssetKey"/>).
        /// Uses the persisted <see cref="AssetBase.path"/> when set, otherwise derives it from the
        /// absolute <see cref="AssetBase.filePath"/>, so a freshly-crawled entry (whose <c>path</c> is not
        /// populated until the scene is saved) still builds the exact keys the selector persists.
        /// </summary>
        public string relativePath =>
            !string.IsNullOrEmpty(path) ? path : PropPreset.Relativize(filePath, ProjectManager.projectPath);

        /// <summary>
        /// No-op "load": a pack holds no scene instance, so enabling it does nothing but mark the entry
        /// loaded (guarding the manager's diff against re-invoking this every frame). Members are loaded
        /// lazily by <see cref="PackBundleLoader"/> when referenced, not by this toggle.
        /// </summary>
        public override Task LoadAsync(AssetLoadContext context)
        {
            isLoaded = true;
            return Task.CompletedTask;
        }

        /// <summary>No-op unload: nothing was instantiated. Session member cleanup is handled centrally.</summary>
        public override void Unload(AssetLoadContext context)
        {
            isLoaded = false;
        }

        /// <summary>
        /// Loads the pack (once, cached) and returns its member names — the second level of discovery, run
        /// only when a user drills into this pack in a selector. Empty on failure.
        /// </summary>
        public async Task<string[]> GetMemberNamesAsync()
        {
            var members = await PackBundleLoader.LoadMembersAsync(filePath, relativePath);
            if (members == null || members.Length == 0) return Array.Empty<string>();

            var names = new string[members.Length];
            for (int i = 0; i < members.Length; i++)
            {
                names[i] = members[i] != null ? members[i].name : string.Empty;
            }
            return names;
        }
    }
}
