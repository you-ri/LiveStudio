// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

using Lilium.RemoteControl;

namespace Lilium.RemoteControl.LiveScene
{
    /// <summary>
    /// Holds a list of remote-controllable <see cref="ILiveObject"/> instances for the scene it
    /// lives in, and registers itself in a static registry so a <c>RemoteControlBehaviour</c>
    /// (which owns the single HTTP server) can discover and merge those objects.
    ///
    /// It is also the scene side of <see cref="LiveClassAsset"/>: while it is enabled, the assets
    /// it lists have their type declarations registered, which is what makes members of those types
    /// exposed without attributes. Attribute-less exposure is a base capability of a container
    /// rather than an add-on, so it lives here instead of in a companion component: the enable /
    /// disable that applies and drops the declarations is this one's.
    ///
    /// Which *instances* are exposed is said by this component's own object list, not by the asset:
    /// a <c>LiveComponent</c> / <c>LiveAsset</c> entry names a scene object and carries the id it
    /// is exposed under. A declaration alone already covers every component of a GameObject in the
    /// list; an entry of its own is for giving some other object an id.
    /// </summary>
    /// <remarks>
    /// This component carries remote control across scene boundaries: place it in an additively
    /// loaded set (e.g. a .set.lsb bundle) and its objects become listable, resolvable,
    /// saveable and operable through the host behaviour's server. It owns no server of its own.
    ///
    /// The same applies to the assets: a set bundle carries its own container, so its exposure
    /// declarations and their scene references travel with it and apply on load / drop on unload
    /// without anything else having to know about them.
    /// </remarks>
    [DefaultExecutionOrder(-32760)]
    [ExecuteAlways]
    public class RemoteControlContainer : MonoBehaviour
    {
        [SerializeReference, Select]
        [LiveField(persistable = false)]
        public List<ILiveObject> _objects = new List<ILiveObject>();

        public IReadOnlyList<ILiveObject> objects => _objects;

        // --- Live class assets (attribute-less exposure) ---

        [SerializeField, FormerlySerializedAs("_presets")]
        [Tooltip("Live class assets applied while this container is enabled")]
        private List<LiveClassAsset> _assets = new List<LiveClassAsset>();

        /// <summary>Live class assets applied by this container. Editable from the Live Class Asset window.</summary>
        public List<LiveClassAsset> assets => _assets;

        // --- Static registry (discovery for the host RemoteControlBehaviour) ---

        private static readonly List<RemoteControlContainer> _all = new List<RemoteControlContainer>();

        /// <summary>All containers currently enabled, in registration order.</summary>
        public static IReadOnlyList<RemoteControlContainer> all => _all;

        /// <summary>Raised after a container has been added to <see cref="all"/>.</summary>
        public static event Action<RemoteControlContainer> onRegistered;

        /// <summary>Raised after a container has been removed from <see cref="all"/>.</summary>
        public static event Action<RemoteControlContainer> onUnregistered;

        // Reset static state at runtime startup so disabling Domain Reload does not leak the
        // previous play session's containers or host subscriptions.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void _ClearStatics()
        {
            _all.Clear();
            onRegistered = null;
            onUnregistered = null;
        }

        protected virtual void OnEnable()
        {
            // A container that is not a live scene instance stays out of the registry entirely:
            // announcing it would hand the host an object list whose entries claim the very ids
            // the real scene instance uses. See _IsLiveInstance.
            if (!_IsLiveInstance()) return;
            if (_all.Contains(this)) return;

            // Apply before announcing: the host initializes the object list as soon as it hears
            // about the container, and an entry cannot get its registry handle until the LiveClass
            // of its target type exists.
            _ApplyAssets();

            _all.Add(this);
            onRegistered?.Invoke(this);
        }

        protected virtual void OnDisable()
        {
            if (_all.Remove(this))
                onUnregistered?.Invoke(this);

            // After the host has dropped this source, so the object entries have released their
            // handles before the types those handles hold are unregistered. Any live-scene save has
            // to have happened before the container is disabled — an unregistered type is not
            // serialized, so saving afterwards would drop its values.
            _UnapplyAssets();
        }

        // --- Live class asset apply / unapply ---

        /// <summary>
        /// Re-applies the assets after they changed (called by the Live Class Asset editor window).
        /// </summary>
        public void Reload()
        {
            _UnapplyAssets();
            _ApplyAssets();
        }

        private void _ApplyAssets()
        {
            foreach (var asset in _assets)
            {
                if (asset == null) continue;
                LiveClassAssetSystem.RegisterTypes(asset);
            }
        }

        private void _UnapplyAssets()
        {
            // A type still carrying exposed instances (another container's, or ours until the host
            // shut this source down a moment ago) is kept registered by the system.
            foreach (var asset in _assets)
            {
                if (asset == null) continue;
                LiveClassAssetSystem.UnregisterTypes(asset);
            }
        }

        // A prefab opened in Prefab Mode instantiates its contents in a scene of its own, and
        // [ExecuteAlways] runs OnEnable there too. Registering would expose live objects under the
        // ids its object entries carry — the very ids the scene instance uses — so the two would
        // collide for as long as the prefab stays open. Same story for an instance living on an
        // asset.
        private bool _IsLiveInstance()
        {
#if UNITY_EDITOR
            if (UnityEditor.EditorUtility.IsPersistent(this)) return false;

            var stage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && stage.scene == gameObject.scene) return false;
#endif
            return true;
        }
    }
}
