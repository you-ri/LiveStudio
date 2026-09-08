// Copyright (c) You-Ri, 2026

using UnityEngine.SceneManagement;

using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Shared base for the set-kind assets managed by <see cref="ExternalAssetManager"/>: the file-backed
    /// <see cref="SetBundleAsset"/> (<c>*.set.lsb</c>) and the app-embedded <see cref="BuiltinSetAsset"/>
    /// (a scene shipped inside the build). It carries what is common to every set source — the active flag
    /// and the fact that a set is a free-standing scene an avatar swap does not disturb — while each
    /// subclass keeps its own source resolution and load path. How a set is orchestrated (the active set,
    /// the bootstrap entry, warps) lives in <see cref="StageManager"/>, which reconciles through
    /// <see cref="ISetAsset"/> rather than any concrete type.
    ///
    /// Deliberately NOT marked <c>[LiveClass]</c> (like <see cref="AssetBase"/>): it is abstract and the
    /// polymorphic <c>@type</c> discriminator only ever names a concrete subclass. Its purpose on the wire
    /// is instead as a stable grouping key — the client discovers, via each concrete type's
    /// <c>baseTypes</c> in <c>GET /live/types</c>, that <c>SetBundleAsset</c> and <c>BuiltinSetAsset</c>
    /// both derive from <c>SetAssetBase</c>, so it can list "every set asset" by base type without
    /// enumerating each concrete <c>@type</c>. <see cref="ISetAsset"/> cannot serve that purpose: the
    /// inheritance chain published on the wire walks base classes only, never interfaces.
    /// </summary>
    public abstract class SetAssetBase : AssetBase, ISetAsset
    {
        /// <summary>
        /// True when this set is the active set (the lighting / warp target). Persisted so the saved active
        /// set is reactivated on restore once it has loaded. Written by <see cref="StageManager"/>; only one
        /// set asset is active at a time (an invariant the manager enforces).
        /// </summary>
        [LiveField]
        public bool isActive;

        // ISetAsset.isActive delegates to the exposed field (a field cannot implement the property
        // directly).
        bool ISetAsset.isActive { get => isActive; set => isActive = value; }

        /// <summary>The loaded scene handle, or <c>default</c> when not loaded.</summary>
        public abstract Scene scene { get; }

        /// <summary>True when a valid scene is currently loaded.</summary>
        public abstract bool hasScene { get; }

        // A set is a free-standing scene, not something parented under the avatar, so swapping the avatar
        // never invalidates it. True for every set source.
        public override bool reloadsOnAvatarChange => false;
    }
}
