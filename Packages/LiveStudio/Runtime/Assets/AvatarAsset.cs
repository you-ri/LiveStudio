// Copyright (c) You-Ri, 2026

using System;
using System.Threading.Tasks;

using UnityEngine;

using Newtonsoft.Json.Linq;

using Lilium.RemoteControl;
using Lilium.RemoteControl.LiveScene;

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

            // Capture the delta baseline once the new avatar is ready (even without a preset), so a later
            // "save as preset" records only the user's edits — symmetric with how props baseline on load.
            _CaptureDefaultsThenRestoreOnReady(null);

            // The avatar swap itself is driven by AvatarController; completion is observed by the
            // manager via IAvatarService.onAvatarChanged. Mark loaded optimistically.
            AvatarService.Load(kAvatarServiceId, filePath);
            isLoaded = true;
            objectId = _ResolveControllerObjectId();
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
            _CaptureDefaultsThenRestoreOnReady(preset.state);

            AvatarService.Load(kAvatarServiceId, source);
            isLoaded = true;
            objectId = _ResolveControllerObjectId();
        }

        // Resolves the exposed id of the GameObject wrapper that hosts the shared AvatarController, so the
        // remote app can open the avatar's GameObject (transform + components, including the Avatar
        // component) in its detail pane — symmetric with how a prop entry points at its own wrapper.
        // The AvatarController's GameObject is a persistent scene object already exposed as an
        // ExposedGameObjectWithTransform, so this only reads the existing wrapper (no new wrapper is made).
        // Returns null if no controller / wrapper is present (e.g. the default avatar with no scene wrapper).
        private static string _ResolveControllerObjectId()
        {
            if (!(SingletonService<IAvatarService>.subject is Component controller)) return null;
            var go = controller.gameObject;
            foreach (var handle in ExposedObjectRegistry.instances)
            {
                if (!handle.hasId) continue;
                // The GameObject wrapper's target is an ExposedUnityObjectBase referencing the GameObject,
                // distinct from the AvatarController component handle (raw component target) on the same GO.
                if (handle.target is ExposedUnityObjectBase proxy
                    && proxy.reference is GameObject wrappedGo
                    && wrappedGo == go)
                {
                    return handle.id;
                }
            }
            return null;
        }

        // Registers a one-shot onAvatarChanged handler that, once the freshly loaded avatar is ready,
        // captures the AvatarController GameObject's delta baseline BEFORE applying any saved state, so a
        // later "save as preset" captures only the user's edits. When savedState is non-empty it is then
        // reapplied: the unified { wrapper, components } envelope via AssetStateSnapshot, or a bare
        // AvatarController snapshot (no wrapper/components, from an earlier build) via _RestoreAvatarState.
        // The handler is self-removing.
        private static void _CaptureDefaultsThenRestoreOnReady(string savedState)
        {
            var service = SingletonService<IAvatarService>.subject;
            if (service == null) return;

            Action handler = null;
            handler = () =>
            {
                service.onAvatarChanged -= handler;
                var controllerGO = (service as Component)?.gameObject;
                if (controllerGO == null) return;

                // Baseline = the avatar's values right after load, before any preset/user override.
                AssetStateSnapshot.CaptureDefaults(controllerGO);

                if (!string.IsNullOrEmpty(savedState))
                {
                    if (_LooksLikeEnvelope(savedState))
                        AssetStateSnapshot.Restore(savedState, controllerGO);
                    else
                        _RestoreAvatarState(savedState); // bare AvatarController snapshot (earlier build)
                }

                // Finally, apply any deferred live-scene entries for the avatar wrapper (queued during the
                // restore if the wrapper was not registered yet). Ordered LAST so persisted overrides land
                // on top of the defaults/preset without polluting the delta baseline. Usually a no-op — the
                // AvatarController's GameObject is a persistent scene object already bound in the restore.
                var wrapperId = _ResolveControllerObjectId();
                if (!string.IsNullOrEmpty(wrapperId))
                    LiveScenePendingStore.ApplyFor(wrapperId, DefaultExposedObjectResolver.Instance);
            };
            service.onAvatarChanged += handler;
        }

        // True if savedState is the unified envelope ({ wrapper / components }); false for a bare
        // AvatarController snapshot from an earlier build (restored via _RestoreAvatarState).
        private static bool _LooksLikeEnvelope(string json)
        {
            if (string.IsNullOrEmpty(json)) return false;
            try
            {
                var root = JObject.Parse(json);
                return root["wrapper"] != null || root["components"] != null;
            }
            catch { return false; }
        }

        // Restores a bare AvatarController state snapshot (from an earlier build) directly onto the
        // live controller.
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
