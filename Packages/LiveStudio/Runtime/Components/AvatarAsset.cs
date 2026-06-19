// Copyright (c) You-Ri, 2026

using System;
using System.IO;
using System.Threading.Tasks;

using UnityEngine;

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
    /// Loading does not create a fresh exposed wrapper — the loaded avatar is already operable through
    /// the scene's AvatarController — so preset state is captured/restored on that controller rather
    /// than a per-asset wrapper.
    /// </summary>
    [Serializable]
    [ExposedClass("AvatarAsset", Category = "Asset", Icon = "person")]
    public class AvatarAsset : AssetBase
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

        public override bool isExclusive => true;

        public override Task LoadAsync(AssetLoadContext context)
        {
            if (_isPreset)
            {
                _LoadPreset();
                return Task.CompletedTask;
            }

            // The avatar swap itself is driven by AvatarController; completion is observed by the
            // manager via IAvatarService.onAvatarChanged. Mark loaded optimistically.
            AvatarService.Load(kAvatarServiceId, filePath);
            isLoaded = true;
            return Task.CompletedTask;
        }

        // Reads the preset, resolves its referenced source avatar, arranges to reapply the saved
        // AvatarController state once the new avatar is ready, then requests the avatar load.
        private void _LoadPreset()
        {
            string json;
            try { json = File.ReadAllText(filePath); }
            catch (Exception e)
            {
                Debug.LogError($"[LiveStudio] Failed to read avatar preset '{filePath}': {e.Message}");
                enabled = false;
                isLoaded = false;
                return;
            }

            if (!PropPreset.TryParse(json, out var preset))
            {
                enabled = false;
                isLoaded = false;
                return;
            }

            var source = PropPreset.ResolveSource(preset.source, Path.GetDirectoryName(filePath));
            if (string.IsNullOrEmpty(source) || !File.Exists(source))
            {
                Debug.LogError($"[LiveStudio] Avatar preset source not found: '{preset.source}' (from '{filePath}').");
                enabled = false;
                isLoaded = false;
                return;
            }

            _resolvedSourcePath = source;

            // Reapply the saved AvatarController state after the new avatar becomes ready. The
            // AvatarController instance persists across swaps; only its driven avatar changes, so the
            // saved override arrays apply to the freshly loaded (same source) avatar. The handler is
            // one-shot and self-removing.
            var service = SingletonService<IAvatarService>.subject;
            if (service != null && !string.IsNullOrEmpty(preset.state))
            {
                var savedState = preset.state;
                Action handler = null;
                handler = () =>
                {
                    service.onAvatarChanged -= handler;
                    _RestoreAvatarState(savedState);
                };
                service.onAvatarChanged += handler;
            }

            AvatarService.Load(kAvatarServiceId, source);
            isLoaded = true;
        }

        // Restores a captured AvatarController state snapshot onto the live controller.
        private static void _RestoreAvatarState(string stateJson)
        {
            var controller = SingletonService<IAvatarService>.subject as Component;
            if (controller == null) return;
            var handle = ExposedObjectRegistry.FindByTarget(controller);
            if (handle.HasValue) ExposedObjectSnapshot.Restore(stateJson, handle.Value);
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
