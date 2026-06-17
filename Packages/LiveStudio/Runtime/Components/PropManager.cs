// Copyright (c) You-Ri, 2026

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using UnityEngine;

using Lilium.RemoteControl;
using Lilium.RemoteControl.LiveScene;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// One entry in <see cref="PropManager.props"/>: an avatar prop bundle (<c>*.prop.lsb</c>)
    /// the user has added. <see cref="enabled"/> is the desired state controlled from the remote app;
    /// <see cref="isLoaded"/> reflects whether the prop is actually loaded onto the current avatar.
    /// </summary>
    [Serializable]
    [ExposedClass]
    public struct PropEntry
    {
        [ExposedField]
        public string id;

        [ExposedField]
        public string name;

        [ExposedField]
        public string filePath;

        /// <summary>Desired state. Toggling loads (true) / unloads (false) the prop. Persisted.</summary>
        [ExposedField]
        public bool enabled;

        /// <summary>Actual state, synced after a load/unload completes. Not persisted.</summary>
        [ExposedField(persistable = false)]
        public bool isLoaded;

        /// <summary>
        /// id of the loaded prop's exposed object (the <c>GameObjectWithTransform</c> wrapper),
        /// so the remote app can open its property editor. Empty while unloaded. Not persisted.
        /// </summary>
        [ExposedField(persistable = false)]
        public string objectId;
    }

    /// <summary>
    /// Manages a list of avatar prop bundles (<c>*.prop.lsb</c>) at runtime, patterned after
    /// <see cref="WorldManager"/>. Each entry can be loaded or unloaded independently from the remote
    /// app. A loaded prop is instantiated as a child of the current avatar (so its <see cref="AvatarProp"/>
    /// bridges the avatar's parameters and follows a bone), and is wrapped in an
    /// <see cref="ExposedGameObjectWithTransform"/> added to a <see cref="RemoteControlContainer"/> so
    /// it (and its prop component) are operable from the remote app.
    ///
    /// Props live under the avatar, so swapping the avatar destroys them; this manager listens for
    /// <see cref="IAvatarService.onAvatarChanged"/> and reloads the enabled props onto the new avatar.
    /// </summary>
    [Serializable]
    [ExposedClass(Icon = "deployed_code", Category = "Avatar")]
    public class PropManager : IExposedObject, IExposedDeserializeCallback
    {
        const string kId = "c3e8a1d2-5b6f-4a90-8c2d-1f7e9a4b6d50";

        public string name { get; set; } = "Prop Manager";

        public ExposedObjectHandle? exposedObject => ExposedObjectRegistry.FindByTarget(this);

        public string id => kId;

        // Added prop bundles, exposed as an editable array. The remote app toggles each entry's
        // `enabled` flag (load/unload) and removes entries; persisted to the live scene JSON.
        [NonSerialized]
        [ExposedField]
        private PropEntry[] props = Array.Empty<PropEntry>();

        // A loaded prop owned by this manager.
        private sealed class LoadedProp
        {
            public GameObject instance;
            public ExposedGameObjectWithTransform exposed;
            public RemoteControlContainer container;
        }

        [NonSerialized]
        private readonly Dictionary<string, LoadedProp> _loaded = new Dictionary<string, LoadedProp>();

        // Entries currently loading/unloading; guards against re-entrant requests.
        [NonSerialized]
        private readonly HashSet<string> _busy = new HashSet<string>();

        [NonSerialized]
        private IAvatarService _avatarService;

        [NonSerialized]
        private bool _dirty;

        [NonSerialized]
        private bool _initialized;

        [NonSerialized]
        private bool _restorePending;

        public void OnEnable()
        {
            ExposedObjectRegistry.Create<PropManager>(this, kId);
            ExposedClass.Get<PropManager>().onPropertyChanged += _OnPropertyChanged;

            if (Application.isPlaying)
            {
                _restorePending = true;
                _avatarService = SingletonService<IAvatarService>.subject;
                if (_avatarService != null) _avatarService.onAvatarChanged += _OnAvatarChanged;
            }

            _initialized = true;
        }

        public void OnDisable()
        {
            _dirty = false;
            _initialized = false;

            ExposedClass.Get<PropManager>().onPropertyChanged -= _OnPropertyChanged;
            if (_avatarService != null) _avatarService.onAvatarChanged -= _OnAvatarChanged;
            _avatarService = null;

            _UnloadAll();

            ExposedObjectRegistry.FindByTarget(this)?.Unregister();
        }

        public void OnDispose()
        {
            OnDisable();
        }

        /// <summary>Fires after the <c>props</c> array is restored from a saved live scene; schedules a diff.</summary>
        public void OnAfterExposedDeserialize()
        {
            if (!Application.isPlaying) return;
            if (!_restorePending) return;
            _dirty = true;
        }

        public void Update()
        {
            _restorePending = false;

            // The avatar service may not have existed at OnEnable (load order). Bind late so prop
            // reloads track avatar swaps.
            if (Application.isPlaying && _avatarService == null)
            {
                _avatarService = SingletonService<IAvatarService>.subject;
                if (_avatarService != null)
                {
                    _avatarService.onAvatarChanged += _OnAvatarChanged;
                    _dirty = true; // a new avatar is available; (re)load enabled props onto it.
                }
            }

            if (!_dirty) return;
            if (!_initialized) { _dirty = false; return; }
            _dirty = false;
            _ApplyDiff();
        }

        public void Reset()
        {
        }

        /// <summary>
        /// Adds a prop bundle and loads it onto the current avatar. Invoked from the remote app after
        /// the user picks a <c>*.prop.lsb</c> file.
        /// </summary>
        [ExposedFunction]
        public void AddProp(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                Debug.LogError("[LiveStudio] Prop bundle path cannot be empty.");
                return;
            }
            if (!LiveStudioBundle.IsPropBundle(filePath))
            {
                Debug.LogError($"[LiveStudio] Not a prop bundle (*.prop.lsb): {filePath}");
                return;
            }
            if (!File.Exists(filePath))
            {
                Debug.LogError($"[LiveStudio] Prop bundle file not found: {filePath}");
                return;
            }

            var entry = new PropEntry
            {
                id = Guid.NewGuid().ToString("N"),
                name = _DeriveName(filePath),
                filePath = filePath,
                enabled = true,
                isLoaded = false,
            };

            var list = new List<PropEntry>(props) { entry };
            props = list.ToArray();

            _dirty = true;
            _Broadcast();
        }

        /// <summary>Unloads (if loaded) and removes the entry with the given id.</summary>
        [ExposedFunction]
        public void RemoveProp(string propId)
        {
            if (string.IsNullOrEmpty(propId)) return;

            if (_loaded.TryGetValue(propId, out var loaded))
            {
                _loaded.Remove(propId);
                _DestroyLoaded(loaded);
            }

            var list = new List<PropEntry>(props);
            list.RemoveAll(e => e.id == propId);
            props = list.ToArray();

            _Broadcast();
        }

        private void _OnPropertyChanged(ExposedProperty property, object oldValue)
        {
            if (!_initialized) return;
            if (!property.PathContains(nameof(props))) return;
            _dirty = true;
        }

        // The avatar was swapped: its child props were destroyed with it. Drop the dead loaded entries
        // and re-diff so the enabled props are reloaded onto the new avatar.
        private void _OnAvatarChanged()
        {
            _UnloadAll();
            for (int i = 0; i < props.Length; i++)
            {
                if (!props[i].isLoaded && string.IsNullOrEmpty(props[i].objectId)) continue;
                _SetEntryLoaded(props[i].id, false);
                _SetEntryObjectId(props[i].id, string.Empty);
            }
            _dirty = true;
        }

        /// <summary>
        /// Brings the actual loaded props in line with the desired <see cref="PropEntry.enabled"/> flags.
        /// </summary>
        private void _ApplyDiff()
        {
            var desiredIds = new HashSet<string>();

            for (int i = 0; i < props.Length; i++)
            {
                var entry = props[i];
                if (string.IsNullOrEmpty(entry.id)) continue;
                desiredIds.Add(entry.id);

                if (_busy.Contains(entry.id)) continue;

                bool loaded = _loaded.ContainsKey(entry.id);
                if (entry.enabled && !loaded)
                {
                    _ = _LoadEntryAsync(entry.id, entry.filePath);
                }
                else if (!entry.enabled && loaded)
                {
                    _UnloadEntry(entry.id);
                }
            }

            // Entries removed from the array while still loaded: unload them.
            var orphans = new List<string>();
            foreach (var id in _loaded.Keys)
            {
                if (!desiredIds.Contains(id) && !_busy.Contains(id)) orphans.Add(id);
            }
            foreach (var id in orphans) _UnloadEntry(id);
        }

        private async Task _LoadEntryAsync(string propId, string filePath)
        {
            var avatarRoot = _AvatarRoot();
            if (avatarRoot == null)
            {
                // No avatar yet: keep the entry enabled and retry when the avatar becomes available
                // (Update binds the service / _OnAvatarChanged re-diffs).
                return;
            }

            _busy.Add(propId);
            try
            {
                var loader = new PropBundleLoader();
                var instance = await loader.LoadAsync(filePath, avatarRoot);
                if (instance != null)
                {
                    var container = _ResolveContainer();
                    ExposedGameObjectWithTransform exposed = null;
                    if (container != null)
                    {
                        // Wrap the prop so the remote app can control its transform and prop component.
                        // The source list is already initialized by the host, so call OnEnable manually.
                        exposed = new ExposedGameObjectWithTransform(instance);
                        container._objects.Add(exposed);
                        exposed.OnEnable();
                    }
                    else
                    {
                        Debug.LogWarning("[LiveStudio] No RemoteControlContainer found; prop loaded but not remote-controllable.");
                    }

                    _loaded[propId] = new LoadedProp { instance = instance, exposed = exposed, container = container };
                    _SetEntryLoaded(propId, true);
                    // 詳細ペーン (remote app) がプロパティ編集できるよう、exposed ラッパの id を entry に持たせる。
                    _SetEntryObjectId(propId, exposed != null ? exposed.id : string.Empty);
                }
                else
                {
                    _SetEntryEnabled(propId, false);
                    _SetEntryLoaded(propId, false);
                }
            }
            finally
            {
                _busy.Remove(propId);
                _Broadcast();
            }
        }

        private void _UnloadEntry(string propId)
        {
            if (_loaded.TryGetValue(propId, out var loaded))
            {
                _loaded.Remove(propId);
                _DestroyLoaded(loaded);
            }
            _SetEntryLoaded(propId, false);
            _SetEntryObjectId(propId, string.Empty);
            _Broadcast();
        }

        private void _UnloadAll()
        {
            foreach (var loaded in _loaded.Values) _DestroyLoaded(loaded);
            _loaded.Clear();
            _busy.Clear();
        }

        // Unregisters the exposed wrapper from its container and destroys the prop instance.
        private void _DestroyLoaded(LoadedProp loaded)
        {
            if (loaded == null) return;
            if (loaded.exposed != null)
            {
                loaded.exposed.OnDisable();
                if (loaded.container != null) loaded.container._objects.Remove(loaded.exposed);
            }
            if (loaded.instance != null) UnityEngine.Object.Destroy(loaded.instance);
        }

        // Current avatar root transform, or null if no avatar is loaded.
        private Transform _AvatarRoot()
        {
            var service = SingletonService<IAvatarService>.subject;
            var target = service?.target;
            return target != null ? target.transform : null;
        }

        // The RemoteControlContainer this manager lives in (so loaded props join the same container);
        // falls back to the first registered container.
        private RemoteControlContainer _ResolveContainer()
        {
            var all = RemoteControlContainer.all;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] != null && all[i]._objects.Contains(this)) return all[i];
            }
            return all.Count > 0 ? all[0] : null;
        }

        private void _SetEntryLoaded(string propId, bool value)
        {
            for (int i = 0; i < props.Length; i++)
            {
                if (props[i].id != propId) continue;
                var entry = props[i];
                entry.isLoaded = value;
                props[i] = entry;
                return;
            }
        }

        private void _SetEntryObjectId(string propId, string value)
        {
            for (int i = 0; i < props.Length; i++)
            {
                if (props[i].id != propId) continue;
                var entry = props[i];
                entry.objectId = value;
                props[i] = entry;
                return;
            }
        }

        private void _SetEntryEnabled(string propId, bool value)
        {
            for (int i = 0; i < props.Length; i++)
            {
                if (props[i].id != propId) continue;
                var entry = props[i];
                entry.enabled = value;
                props[i] = entry;
                return;
            }
        }

        private void _Broadcast()
        {
            ExposedPropertyBroadcast.BroadcastProperty(this, "props");
        }

        private static string _DeriveName(string filePath)
        {
            var fileName = Path.GetFileName(filePath);
            if (fileName.EndsWith(LiveStudioBundle.PropExtension, StringComparison.OrdinalIgnoreCase))
            {
                return fileName.Substring(0, fileName.Length - LiveStudioBundle.PropExtension.Length);
            }
            return Path.GetFileNameWithoutExtension(fileName);
        }
    }
}
