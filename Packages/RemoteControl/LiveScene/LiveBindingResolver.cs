// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;
using UnityEngine;

using Lilium.RemoteControl;

namespace Lilium.RemoteControl.LiveScene
{
    /// <summary>
    /// Scene-side counterpart of <see cref="LiveBindingPreset"/> assets: implements the standard
    /// <see cref="IExposedPropertyTable"/> (the same mechanism PlayableDirector uses) to map the
    /// preset's binding keys to actual scene objects, and turns each resolved binding into a
    /// runtime <see cref="LiveBinding"/> registered through the normal LiveObject flow.
    ///
    /// The reference table holds direct object references serialized in the scene, so bindings
    /// survive renames and hierarchy moves. The same preset asset can be used in multiple scenes;
    /// each scene's resolver supplies its own objects for the same keys, and because the key is
    /// also the LiveObject id, persisted values (live.json) stay stable across scenes.
    /// </summary>
    [AddComponentMenu("Lilium/Remote Control/Live Binding Resolver")]
    [DefaultExecutionOrder(-32700)]
    [ExecuteAlways]
    public class LiveBindingResolver : MonoBehaviour, IExposedPropertyTable
    {
        [SerializeField]
        private List<LiveBindingPreset> _presets = new List<LiveBindingPreset>();

        [Serializable]
        private struct SceneReference
        {
            public string key;
            public UnityEngine.Object value;
        }

        [SerializeField]
        private List<SceneReference> _references = new List<SceneReference>();

        // Runtime wrappers created from the resolved bindings, and the host object list they
        // were injected into (RemoteControlBehaviour/Container) for listing + persistence.
        private readonly List<LiveBinding> _active = new List<LiveBinding>();
        private List<ILiveObject> _hostList;

        /// <summary>Preset assets registered by this resolver. Editable from the Live Binding panel.</summary>
        public List<LiveBindingPreset> presets => _presets;

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

        // --- Lifecycle ---

        protected virtual void OnEnable()
        {
            _RegisterAll();
        }

        protected virtual void OnDisable()
        {
            _UnregisterAll();
        }

        /// <summary>
        /// Re-registers everything after the presets or the reference table changed
        /// (called by the Live Binding editor panel).
        /// </summary>
        public void Reload()
        {
            _UnregisterAll();
            _RegisterAll();
        }

        private void _RegisterAll()
        {
            foreach (var preset in _presets)
            {
                if (preset == null) continue;
                LiveBindingSystem.RegisterTypes(preset);
            }

            foreach (var preset in _presets)
            {
                if (preset == null) continue;
                foreach (var entry in preset.bindings)
                {
                    if (entry == null || string.IsNullOrEmpty(entry.key)) continue;
                    var target = ResolveKey(entry.key);
                    if (target == null)
                    {
                        // Unbound in this scene — not an error: the same preset may be shared by
                        // scenes that only bind a subset of its entries.
                        continue;
                    }

                    // Look the host list up lazily so a resolver with nothing to expose stays silent.
                    _hostList ??= _FindHostList();

                    var binding = new LiveBinding(entry.key, target);
                    _active.Add(binding);
                    _hostList?.Add(binding);
                }
            }
        }

        private void _UnregisterAll()
        {
            foreach (var binding in _active)
            {
                binding.OnDisable();
                _hostList?.Remove(binding);
            }
            _active.Clear();
            _hostList = null;
        }

        // The wrappers must live in a RemoteControlBehaviour/Container object list to be listed
        // (/live/objects) and persisted (live-scene save). Prefer a host on this GameObject, then
        // any in the loaded scenes. Without a host the bindings still register in the registry
        // (operable by id) but are not listed or persisted.
        private List<ILiveObject> _FindHostList()
        {
            var localContainer = GetComponent<RemoteControlContainer>();
            if (localContainer != null) return localContainer._objects;
            var localBehaviour = GetComponent<RemoteControlBehaviour>();
            if (localBehaviour != null) return localBehaviour._objects;

            var behaviour = FindObjectsByType<RemoteControlBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID);
            if (behaviour.Length > 0) return behaviour[0]._objects;
            var containers = FindObjectsByType<RemoteControlContainer>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID);
            if (containers.Length > 0) return containers[0]._objects;

            Debug.LogWarning("[RemoteControl] LiveBindingResolver found no RemoteControlBehaviour/RemoteControlContainer; bindings will not be listed or persisted.");
            return null;
        }
    }
}
