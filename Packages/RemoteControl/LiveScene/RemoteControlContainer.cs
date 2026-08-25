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
    /// It is also the scene side of <see cref="LiveClassAsset"/>: it implements the standard
    /// <see cref="IExposedPropertyTable"/> (the same mechanism PlayableDirector uses) to map the
    /// assets' binding keys to actual scene objects, and turns each resolved binding into a runtime
    /// <see cref="LiveBinding"/> added to its own object list. Attribute-less exposure is a base
    /// capability of a container rather than an add-on, so it lives here instead of in a companion
    /// component: the object list the bindings go into is this one, and the enable/disable that
    /// applies and drops them is this one's.
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
    public class RemoteControlContainer : MonoBehaviour, IExposedPropertyTable
    {
        [SerializeReference, Select]
        [LiveField(persistable = false)]
        public List<ILiveObject> _objects = new List<ILiveObject>();

        public IReadOnlyList<ILiveObject> objects => _objects;

        // --- Live class assets (attribute-less exposure) ---

        [SerializeField, FormerlySerializedAs("_presets")]
        [Tooltip("Live class assets applied while this container is enabled")]
        private List<LiveClassAsset> _assets = new List<LiveClassAsset>();

        [Serializable]
        private struct SceneReference
        {
            public string key;
            public UnityEngine.Object value;
        }

        // Direct object references serialized in the scene, so bindings survive renames and
        // hierarchy moves. The same asset can be used in multiple scenes; each scene's container
        // supplies its own objects for the same keys, and because the key is also the LiveObject
        // id, persisted values (live.json) stay stable across scenes.
        [SerializeField]
        private List<SceneReference> _references = new List<SceneReference>();

        // Runtime wrappers created from the resolved bindings.
        //
        // ⚠ Deliberately NOT _objects: that list is [SerializeReference], so a wrapper put there
        // is written into the scene file the next time it is saved — and [ExecuteAlways] means the
        // wrappers exist while merely editing. They come back as empty LiveBinding entries that
        // nothing removes, one more set per save. This list is runtime-only and is handed to the
        // host as a source of its own.
        private readonly List<ILiveObject> _bindingObjects = new List<ILiveObject>();

        /// <summary>
        /// The runtime binding wrappers, merged by the host <c>RemoteControlBehaviour</c> as
        /// a source separate from <see cref="_objects"/>. The list instance is stable for the life
        /// of the component, so the host can hold it by reference.
        /// </summary>
        public List<ILiveObject> bindingObjects => _bindingObjects;

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
            if (_all.Contains(this)) return;

            // Apply before announcing: the host merges _objects by reference and initializes it as
            // soon as it hears about the container, so the bindings have to be in it by then.
            _ApplyAssets();

            _all.Add(this);
            onRegistered?.Invoke(this);
        }

        protected virtual void OnDisable()
        {
            if (_all.Remove(this))
                onUnregistered?.Invoke(this);

            // After the host has dropped this source, so it never enumerates a list that is being
            // emptied. Any live-scene save has to have happened before the container is disabled —
            // an unregistered type is not serialized, so saving afterwards would drop its values.
            _UnapplyAssets();
        }

        // --- IExposedPropertyTable ---

        public void SetReferenceValue(PropertyName id, UnityEngine.Object value)
        {
            for (int i = 0; i < _references.Count; i++)
            {
                if (new PropertyName(_references[i].key) == id)
                {
                    _references[i] = new SceneReference { key = _references[i].key, value = value };
                    return;
                }
            }
            _references.Add(new SceneReference { key = _PropertyNameToKey(id), value = value });
        }

        // PropertyName.ToString() is "name:id" (e.g. "c1cf41ce-...:-1566981294") in the editor;
        // storing it verbatim would hash a different string on lookup. Strip the trailing ":id".
        // Only the editor adds new entries (the table is scene-serialized), so the name part is
        // always available here.
        private static string _PropertyNameToKey(PropertyName id)
        {
            var s = id.ToString();
            int i = s.LastIndexOf(':');
            return i >= 0 ? s.Substring(0, i) : s;
        }

        public UnityEngine.Object GetReferenceValue(PropertyName id, out bool idValid)
        {
            for (int i = 0; i < _references.Count; i++)
            {
                if (new PropertyName(_references[i].key) == id)
                {
                    idValid = true;
                    return _references[i].value;
                }
            }
            idValid = false;
            return null;
        }

        public void ClearReferenceValue(PropertyName id)
        {
            for (int i = 0; i < _references.Count; i++)
            {
                if (new PropertyName(_references[i].key) == id)
                {
                    _references.RemoveAt(i);
                    return;
                }
            }
        }

        /// <summary>String-keyed convenience over the PropertyName API.</summary>
        public UnityEngine.Object ResolveKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            var value = GetReferenceValue(new PropertyName(key), out var valid);
            return valid ? value : null;
        }

        // --- Live class asset apply / unapply ---

        /// <summary>
        /// Re-applies the assets after they or the reference table changed
        /// (called by the Live Class Asset editor window).
        /// </summary>
        public void Reload()
        {
            _UnapplyAssets();
            _ApplyAssets();
        }

        private void _ApplyAssets()
        {
            if (_assets.Count == 0) return;
            if (!_IsLiveInstance()) return;

            // Every type first: a binding cannot get its registry handle until the LiveClass of its
            // target type exists, and one asset may well bind an instance of a type another declares.
            foreach (var asset in _assets)
            {
                if (asset == null) continue;
                LiveClassAssetSystem.RegisterTypes(asset);
            }

            foreach (var asset in _assets)
            {
                if (asset == null) continue;
                foreach (var entry in asset.bindings)
                {
                    if (entry == null || string.IsNullOrEmpty(entry.key)) continue;
                    var target = ResolveKey(entry.key);
                    if (target == null)
                    {
                        // Unbound in this scene — not an error: the same asset may be shared by
                        // scenes that only bind a subset of its entries.
                        continue;
                    }

                    _bindingObjects.Add(new LiveBinding(entry.key, target));
                }
            }
        }

        private void _UnapplyAssets()
        {
            for (int i = 0; i < _bindingObjects.Count; i++)
            {
                _bindingObjects[i]?.OnDisable();
            }
            _bindingObjects.Clear();

            // Only after the instances are gone: a type still carrying live bindings (another
            // container's, or ours a moment ago) is kept registered by the system.
            foreach (var asset in _assets)
            {
                if (asset == null) continue;
                LiveClassAssetSystem.UnregisterTypes(asset);
            }
        }

        // A prefab opened in Prefab Mode instantiates its contents in a scene of its own, and
        // [ExecuteAlways] runs OnEnable there too. Applying would register live objects under the
        // asset's binding keys — the very ids the scene instance uses — so the two would collide
        // for as long as the prefab stays open. Same story for an instance living on an asset.
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
