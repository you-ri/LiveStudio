// Copyright (c) You-Ri, 2026

using System;
using System.Threading.Tasks;

using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// An avatar asset managed by <see cref="ExternalAssetManager"/>. Handles <c>*.avatar.lsb</c>
    /// (and legacy <c>*.lsavatar</c>) and <c>*.vrm</c> files by delegating to the existing avatar
    /// pipeline through <see cref="AvatarService"/>, which drives the scene's
    /// <c>AvatarController</c> / <c>ExternalAvatarSource</c>.
    ///
    /// Avatars are exclusive: <see cref="AvatarController"/> holds exactly one avatar, so enabling one
    /// avatar asset replaces the current avatar and the manager disables the others (radio selection).
    /// Loading does not create a fresh exposed wrapper — the loaded avatar is already operable through
    /// the scene's AvatarController — so no per-asset state snapshot is taken here.
    /// </summary>
    [Serializable]
    [ExposedClass("AvatarAsset", Category = "Asset", Icon = "person")]
    public class AvatarAsset : AssetBase
    {
        // Selectable service id of the avatar slot to drive. "current" is the id AvatarController
        // registers itself under (SelectableService&lt;IAvatarService&gt;.Register("current", this)).
        private const string kAvatarServiceId = "current";

        public override bool isExclusive => true;

        public override Task LoadAsync(AssetLoadContext context)
        {
            // The avatar swap itself is driven by AvatarController; completion is observed by the
            // manager via IAvatarService.onAvatarChanged. Mark loaded optimistically.
            AvatarService.Load(kAvatarServiceId, filePath);
            isLoaded = true;
            return Task.CompletedTask;
        }

        public override void Unload(AssetLoadContext context)
        {
            // Restore the AvatarController's default avatar. Only invoked when no avatar asset is
            // selected; switching between avatars replaces in place via LoadAsync (no reset between).
            AvatarService.ResetAvatar(kAvatarServiceId);
            isLoaded = false;
        }
    }
}
