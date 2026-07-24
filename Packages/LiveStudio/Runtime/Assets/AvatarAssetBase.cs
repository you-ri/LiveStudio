// Copyright (c) You-Ri, 2026

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Shared base for the avatar-kind assets managed by <see cref="ExternalAssetManager"/>: the
    /// file-backed <see cref="AvatarAsset"/> (<c>*.vrm</c> / <c>*.avatar.lsb</c> / <c>*.preset.json</c>)
    /// and the app-embedded <see cref="BuiltinAvatarAsset"/> (a Resources prefab). It carries only what is
    /// common to every avatar source — chiefly that an avatar is <see cref="AssetBase.isExclusive"/> (the
    /// <c>AvatarController</c> holds exactly one avatar, radio selection) — while each subclass keeps its
    /// own source resolution and load path. The actual avatar-ready plumbing (delta-baseline capture,
    /// controller-wrapper objectId resolution) is shared separately through <see cref="AvatarAssetSupport"/>.
    ///
    /// Deliberately NOT marked <c>[ExposedClass]</c> (like <see cref="AssetBase"/>): it is abstract and the
    /// polymorphic <c>@type</c> discriminator only ever names a concrete subclass. Its purpose on the wire
    /// is instead as a stable grouping key — the client discovers, via each concrete type's
    /// <c>baseTypes</c> in <c>GET /exposed/types</c>, that <c>AvatarAsset</c> and <c>BuiltinAvatarAsset</c>
    /// both derive from <c>AvatarAssetBase</c>, so it can list "every avatar asset" by base type without
    /// enumerating each concrete <c>@type</c>.
    /// </summary>
    public abstract class AvatarAssetBase : AssetBase
    {
        // Avatars are exclusive: enabling one replaces the current avatar and the manager disables the
        // others (radio selection). Common to every avatar source, so it lives here.
        public override bool isExclusive => true;
    }
}
