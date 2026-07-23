// Copyright (c) You-Ri, 2026

using UnityEngine.SceneManagement;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// An <see cref="AssetBase"/> that owns an additively-loaded Unity scene presented as a "set" on the
    /// Stage page and orchestrated by <see cref="StageManager"/> (active set, warps). Implemented by
    /// <see cref="SetBundleAsset"/> (scene from a <c>*.set.lsb</c> AssetBundle) and
    /// <see cref="BuiltinSetAsset"/> (scene from the app's built-in scene list). StageManager reconciles
    /// through this interface rather than a concrete type, so both set sources share one path; the base
    /// <see cref="AssetBase"/> supplies id / name / enabled / isLoaded.
    ///
    /// The <see cref="scene"/> handle is also the single source of truth for "is this scene a set scene?"
    /// — <see cref="ExternalAssetManager"/> uses it to keep a prop out of a set scene, replacing the old
    /// build-index heuristic (a built-in set scene has a real build index, unlike a bundle set scene at -1).
    /// </summary>
    public interface ISetAsset
    {
        /// <summary>True when this set is the active set (the lighting / warp target). Persisted per set.</summary>
        bool isActive { get; set; }

        /// <summary>True when a valid scene is currently loaded.</summary>
        bool hasScene { get; }

        /// <summary>The loaded scene handle, or <c>default</c> when not loaded.</summary>
        Scene scene { get; }
    }
}
