// Copyright (c) You-Ri, 2026

using System;
using System.Threading.Tasks;

using UnityEngine;

using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// A catalog entry for an app-embedded (built-in) <see cref="AnimationClip"/> shipped inside the app
    /// under a <c>Resources</c> folder (baked into <see cref="BuiltinAssetCatalog"/>). Like
    /// <see cref="AnimationBundleAsset"/> it is a reference resource, not scene content: it has no
    /// load/unload toggle and instantiates nothing. It exists in the catalog so the clip is listed on the
    /// project's asset page and selectable (e.g. for the avatar body-override slot); the clip itself is
    /// registered in <see cref="AssetRegistry"/> under its GUID by <see cref="BuiltinAssetRegistry"/>.
    ///
    /// Marked <see cref="AssetBase.isBuiltin"/> so the project crawl never prunes it and it is never
    /// persisted — it is always present, re-injected by <see cref="ExternalAssetManager"/> each run.
    /// </summary>
    [Serializable]
    [ExposedClass("BuiltinAnimationAsset", Category = "Asset", Icon = "animation")]
    public class BuiltinAnimationAsset : AssetBase
    {
        // Additive-style (not a single-selection group), though it never actually loads via the toggle.
        public override bool isExclusive => false;

        public override bool isBuiltin => true;

        /// <summary>
        /// No-op "load": a built-in clip holds no scene instance. The clip is registered in
        /// <see cref="AssetRegistry"/> centrally by <see cref="BuiltinAssetRegistry"/>, not by this toggle.
        /// </summary>
        public override Task LoadAsync(AssetLoadContext context)
        {
            isLoaded = false;
            return Task.CompletedTask;
        }

        /// <summary>No-op unload: nothing was instantiated.</summary>
        public override void Unload(AssetLoadContext context)
        {
            isLoaded = false;
        }
    }
}
