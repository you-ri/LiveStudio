// Copyright (c) You-Ri, 2026

using System;

using UnityEngine;

using Lilium.RemoteControl;
using Lilium.RemoteControl.LiveScene;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// The runtime state of a single loaded prop instance (the GameObject, its exposed wrapper and the
    /// container it registers into) plus the source-agnostic wiring that surrounds any prop load:
    /// re-keying the wrapper to the owner's persisted <see cref="AssetBase.objectId"/>, registering it
    /// in the base-scene container, applying saved state and deferred live-scene entries, and tearing it
    /// all down on unload.
    ///
    /// Extracted from <see cref="PropAsset"/> so a prop can be instantiated from any source — an external
    /// <c>*.prop.lsb</c> AssetBundle (<see cref="PropAsset"/>) or an app-embedded Resources prefab
    /// (<see cref="BuiltinPropAsset"/>) — while sharing one registration/teardown path. The caller owns
    /// obtaining and instantiating the prefab and choosing the wrapper (with or without an exposed
    /// transform); everything from registration onward lives here.
    /// </summary>
    internal sealed class LoadedProp
    {
        private GameObject _instance;
        private LiveGameObject _exposed;
        private RemoteControlContainer _container;

        /// <summary>The loaded instance, or null when nothing is loaded.</summary>
        public GameObject instance => _instance;

        /// <summary>True while a prop instance is loaded (used to discard a duplicate concurrent load).</summary>
        public bool hasInstance => _instance != null;

        /// <summary>
        /// Wraps the loaded instance, re-keys the wrapper to the owner's persisted objectId (assigning a
        /// fresh one when none exists yet) so the remote app's reference survives unload/reload, registers
        /// it in the container, and reapplies saved state. Sets <see cref="AssetBase.isLoaded"/> on the
        /// owner. Mirrors the former <c>PropAsset._Register</c> verbatim, only parameterized by owner.
        /// </summary>
        public void Register(AssetLoadContext context, LiveGameObject exposed, GameObject instance, AssetBase owner)
        {
            var container = context?.container;
            if (container == null)
            {
                Debug.LogWarning("[LiveStudio] No base-scene RemoteControlContainer found; prop loaded but not remote-controllable.");
            }
            else
            {
                if (string.IsNullOrEmpty(owner.objectId)) owner.objectId = Guid.NewGuid().ToString();
                exposed.ReplaceId(owner.objectId);
                container._objects.Add(exposed);
                // The container's source list is already initialized by the host, so call OnEnable manually.
                exposed.OnEnable();
            }

            _instance = instance;
            _exposed = exposed;
            _container = container;
            owner.isLoaded = true;

            // Record the freshly loaded values as the delta baseline (the source asset's defaults)
            // BEFORE reapplying saved state, so a later "save as preset" captures only the user's edits.
            AssetStateSnapshot.CaptureDefaults(_exposed, _instance);
            AssetStateSnapshot.Restore(owner.state, _exposed, _instance);

            // Finally, apply this prop's deferred live-scene entries (its top-level diff, queued during
            // the scene restore because the prop had not loaded yet). Ordered LAST — after CaptureDefaults
            // and the state carrier — so the persisted overrides land on top without polluting the delta
            // baseline used by "save as preset". Keyed by objectId, the id the wrapper was just re-keyed to.
            if (!string.IsNullOrEmpty(owner.objectId))
                LiveScenePendingStore.ApplyFor(owner.objectId, DefaultLiveObjectResolver.Instance);

            // The load is complete and everything applied above came from disk, not from the user.
            // Re-baseline the dirty tracking (delta baseline untouched) so a prop that finishes loading
            // after the scene restore is not reported as an unsaved change at quit time.
            AssetStateSnapshot.MarkAllClean(_exposed, _instance);
        }

        /// <summary>Refreshes the owner's <see cref="AssetBase.state"/> from the live instance (no-op when unloaded).</summary>
        public void Capture(AssetBase owner)
        {
            if (_instance == null) return;
            var json = AssetStateSnapshot.Capture(_exposed, _instance);
            if (!string.IsNullOrEmpty(json)) owner.state = json;
        }

        /// <summary>Serializes the loaded instance's current parameter delta (vs the source defaults), or null when unloaded.</summary>
        public string CaptureDelta()
        {
            if (_instance == null) return null;
            return AssetStateSnapshot.CaptureDelta(_exposed, _instance);
        }

        /// <summary>
        /// Unregisters the exposed wrapper from its container and destroys the prop instance, dropping
        /// any exposed-object handles created for the root's components so the registry retains no
        /// dangling targets after the GameObject is destroyed.
        /// </summary>
        public void Destroy()
        {
            if (_exposed != null)
            {
                _exposed.OnDisable();
                if (_container != null) _container._objects.Remove(_exposed);
            }
            if (_instance != null)
            {
                foreach (var comp in _instance.GetComponents<Component>())
                {
                    if (comp == null || !LiveClass.Has(comp.GetType())) continue;
                    LiveObjectRegistry.FindByTarget(comp)?.Unregister();
                }
                UnityEngine.Object.Destroy(_instance);
            }
            _instance = null;
            _exposed = null;
            _container = null;
        }
    }
}
