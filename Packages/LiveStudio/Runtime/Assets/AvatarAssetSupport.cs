// Copyright (c) You-Ri, 2026

using System;

using UnityEngine;

using Newtonsoft.Json.Linq;

using Lilium.RemoteControl;
using Lilium.RemoteControl.LiveScene;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Shared avatar-load plumbing used by both the file-backed <see cref="AvatarAsset"/> and the
    /// app-embedded <see cref="BuiltinAvatarAsset"/>. Both drive the single shared
    /// <see cref="AvatarController"/> through <see cref="AvatarService"/>, so "capture the delta baseline
    /// once the new avatar is ready" and "resolve the controller's exposed wrapper id" are identical
    /// regardless of whether the avatar came from a project file or from a Resources prefab.
    /// </summary>
    internal static class AvatarAssetSupport
    {
        /// <summary>
        /// Resolves the exposed id of the GameObject wrapper that hosts the shared AvatarController, so the
        /// remote app can open the avatar's GameObject (transform + components, including the Avatar
        /// component) in its detail pane — symmetric with how a prop entry points at its own wrapper.
        /// The AvatarController's GameObject is a persistent scene object already exposed as an
        /// ExposedGameObjectWithTransform, so this only reads the existing wrapper (no new wrapper is made).
        /// Returns null if no controller / wrapper is present (e.g. the default avatar with no scene wrapper).
        /// </summary>
        internal static string ResolveControllerObjectId()
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

        /// <summary>
        /// Registers a one-shot onAvatarChanged handler that, once the freshly loaded avatar is ready,
        /// captures the AvatarController GameObject's delta baseline BEFORE applying any saved state, so a
        /// later "save as preset" captures only the user's edits. When <paramref name="savedState"/> is
        /// non-empty it is then reapplied: the unified { wrapper, components } envelope via
        /// AssetStateSnapshot, or a bare AvatarController snapshot (no wrapper/components, from an earlier
        /// build) via <see cref="_RestoreAvatarState"/>. The handler is self-removing.
        /// </summary>
        internal static void CaptureDefaultsThenRestoreOnReady(string savedState)
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
                var wrapperId = ResolveControllerObjectId();
                if (!string.IsNullOrEmpty(wrapperId))
                    LiveScenePendingStore.ApplyFor(wrapperId, DefaultExposedObjectResolver.Instance);

                // The load is complete and everything applied above came from disk, not from the user.
                // Re-baseline the dirty tracking (delta baseline untouched) so an avatar that finishes
                // loading after the scene restore is not reported as an unsaved change at quit time.
                AssetStateSnapshot.MarkAllClean(controllerGO);
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
    }
}
