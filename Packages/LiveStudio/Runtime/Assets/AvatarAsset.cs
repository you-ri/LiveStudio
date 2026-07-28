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
    /// <c>AvatarController</c> / <c>ExternalAvatarSource</c>. Also handles avatar <c>*.preset.json</c>
    /// entries, which reference a source avatar file plus a saved <c>AvatarController</c> state to
    /// reapply once the avatar is ready.
    ///
    /// Avatars are exclusive: <see cref="AvatarController"/> holds exactly one avatar, so enabling one
    /// avatar asset replaces the current avatar and the manager disables the others (radio selection).
    /// Loading does not instantiate a wrapper of its own; instead the entry's
    /// <see cref="AssetBase.objectId"/> is pointed at the existing exposed GameObject wrapper that hosts
    /// the shared AvatarController, so the remote app opens that GameObject (transform + components,
    /// including the Avatar component) in its detail pane — symmetric with how a prop entry points at
    /// its own wrapper. Preset state is still captured/restored on the AvatarController.
    /// </summary>
    [Serializable]
    [LiveClass("AvatarAsset", Category = "Asset", Icon = "person")]
    public class AvatarAsset : AvatarAssetBase
    {
        // Selectable service id of the avatar slot to drive. "current" is the id AvatarController
        // registers itself under (SelectableService&lt;IAvatarService&gt;.Register("current", this)).
        private const string kAvatarServiceId = "current";

        // A *.preset.json entry loads a referenced source avatar; everything else is the source itself.
        private bool _isPreset => PropPreset.IsPresetFile(filePath);

        // Absolute source avatar path resolved while loading a preset; null for a direct avatar entry.
        [NonSerialized] private string _resolvedSourcePath;

        /// <summary>True if this entry is a preset (<c>*.preset.json</c>) referencing a source avatar.</summary>
        internal bool isPreset => _isPreset;

        /// <summary>
        /// The source avatar path this entry represents: the resolved referenced avatar for a preset,
        /// otherwise the entry's own file path. Used when saving a new preset from the active avatar.
        /// </summary>
        internal string sourceFilePath => _isPreset ? _resolvedSourcePath : filePath;

        public override Task LoadAsync(AssetLoadContext context)
        {
            if (_isPreset)
            {
                _LoadPreset();
                return Task.CompletedTask;
            }

            // Capture the delta baseline once the new avatar is ready (even without a preset), so a later
            // "save as preset" records only the user's edits — symmetric with how props baseline on load.
            AvatarAssetSupport.CaptureDefaultsThenRestoreOnReady(null);

            // The avatar swap itself is driven by AvatarController; completion is observed by the
            // manager via IAvatarService.onAvatarChanged. Mark loaded optimistically.
            AvatarService.Load(kAvatarServiceId, filePath);
            isLoaded = true;
            objectId = AvatarAssetSupport.ResolveControllerObjectId();
            return Task.CompletedTask;
        }

        // Reads the preset, resolves its referenced source avatar, arranges to reapply the saved
        // AvatarController state once the new avatar is ready, then requests the avatar load.
        private void _LoadPreset()
        {
            if (!TryReadPreset("avatar", out var preset, out var source)) return;

            _resolvedSourcePath = source;

            // Once the new avatar is ready: capture the delta baseline, then reapply the saved state on
            // top. The AvatarController instance persists across swaps; only its driven avatar changes, so
            // the saved overrides apply to the freshly loaded (same source) avatar.
            AvatarAssetSupport.CaptureDefaultsThenRestoreOnReady(preset.state);

            AvatarService.Load(kAvatarServiceId, source);
            isLoaded = true;
            objectId = AvatarAssetSupport.ResolveControllerObjectId();
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
