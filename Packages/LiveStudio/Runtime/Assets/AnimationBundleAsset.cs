// Copyright (c) You-Ri, 2026

using System;
using System.Threading.Tasks;

using UnityEngine;

using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// A catalog entry for a <c>*.anim.lsb</c> animation bundle managed by
    /// <see cref="ExternalAssetManager"/>. Unlike a prop or avatar, an animation bundle is a reference
    /// resource, not scene content: it has no load/unload toggle and instantiates nothing. It exists in
    /// the catalog so its clips can be listed and selected (e.g. for the avatar body-override slot); the
    /// clips themselves are loaded on demand — and registered in <see cref="AssetRegistry"/> under their
    /// <c>file:&lt;relative-path&gt;#&lt;clipName&gt;</c> keys — by <see cref="AnimationBundleLoader"/> the
    /// first time a clip in the bundle is requested.
    ///
    /// Two-level discovery keeps the project crawl content-free: the crawl lists <c>*.anim.lsb</c> files
    /// as these entries by path alone (no bundle is opened), and only the bundle a user actually drills
    /// into is opened, to enumerate its clip names (<see cref="GetClipNamesAsync"/>).
    /// </summary>
    [Serializable]
    [ExposedClass("AnimationBundleAsset", Category = "Asset", Icon = "animation")]
    public class AnimationBundleAsset : AssetBase
    {
        // Additive-style (not a single-selection group), though it never actually loads via the toggle.
        public override bool isExclusive => false;

        /// <summary>
        /// The project-relative bundle path used to build clip keys (<see cref="ExternalAssetKey"/>).
        /// Uses the persisted <see cref="AssetBase.path"/> when set, otherwise derives it from the
        /// absolute <see cref="AssetBase.filePath"/>, so a freshly-crawled entry (whose <c>path</c> is not
        /// populated until the scene is saved) still builds the exact keys the selector persists.
        /// </summary>
        public string relativePath =>
            !string.IsNullOrEmpty(path) ? path : PropPreset.Relativize(filePath, ProjectManager.projectPath);

        /// <summary>
        /// No-op "load": an animation bundle holds no scene instance, so enabling it does nothing but mark
        /// the entry loaded (guarding the manager's diff against re-invoking this every frame). Clips are
        /// loaded lazily by <see cref="AnimationBundleLoader"/> when referenced, not by this toggle.
        /// </summary>
        public override Task LoadAsync(AssetLoadContext context)
        {
            isLoaded = true;
            return Task.CompletedTask;
        }

        /// <summary>No-op unload: nothing was instantiated. Session clip cleanup is handled centrally.</summary>
        public override void Unload(AssetLoadContext context)
        {
            isLoaded = false;
        }

        /// <summary>
        /// Loads the bundle (once, cached) and returns its clip names — the second level of discovery,
        /// run only when a user drills into this bundle in a selector. Empty on failure.
        /// </summary>
        public async Task<string[]> GetClipNamesAsync()
        {
            var clips = await AnimationBundleLoader.LoadClipsAsync(filePath, relativePath);
            if (clips == null || clips.Length == 0) return Array.Empty<string>();

            var names = new string[clips.Length];
            for (int i = 0; i < clips.Length; i++) names[i] = clips[i] != null ? clips[i].name : string.Empty;
            return names;
        }
    }
}
