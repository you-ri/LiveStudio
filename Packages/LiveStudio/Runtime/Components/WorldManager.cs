// Copyright (c) You-Ri, 2026

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Scripting.APIUpdating;

#if UNITY_EDITOR
using UnityEditor;
#endif

using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// One entry in <see cref="WorldManager.scenes"/>: a scene bundle (<c>*.scene.lsb</c>)
    /// that the user has added. <see cref="enabled"/> is the desired state controlled from the
    /// remote app; <see cref="isLoaded"/> reflects whether the scene is actually loaded.
    /// </summary>
    [Serializable]
    [ExposedClass]
    public struct SceneBundleEntry
    {
        [ExposedField]
        public string id;

        [ExposedField]
        public string name;

        [ExposedField]
        public string filePath;

        /// <summary>Desired state. Toggling this loads (true) or unloads (false) the scene.</summary>
        [ExposedField]
        public bool enabled;

        /// <summary>Actual state, synced by the manager after a load/unload completes.</summary>
        [ExposedField]
        public bool isLoaded;

        /// <summary>True when this scene is the active scene. Synced by the manager.</summary>
        [ExposedField]
        public bool isActive;
    }

    /// <summary>
    /// Manages a list of scene bundles (<c>*.scene.lsb</c>) at runtime. Each entry can be loaded
    /// additively or unloaded independently, and the list is editable from the remote app.
    ///
    /// Patterned after <see cref="MultiSceneManager"/> (which creates empty in-memory scenes), but
    /// this manager loads real scenes from AssetBundles via <see cref="SceneBundleLoader"/> and owns
    /// the loaded bundles until they are unloaded.
    /// </summary>
    [Serializable]
    [ExposedClass(Icon = "public", Category = "Scene")]
    [MovedFrom(false, null, null, "RuntimeSceneManager")]
    public class WorldManager : IExposedObject, IExposedDeserializeCallback
    {
        const string kId = "b2f7c9a1-3d4e-4f8a-9c1b-7e2d5a6f8c30";

#if UNITY_EDITOR
        private static bool _isExitingPlayMode;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void _InitializeEditor()
        {
            _isExitingPlayMode = false;
            EditorApplication.playModeStateChanged -= _OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += _OnPlayModeStateChanged;
        }

        private static void _OnPlayModeStateChanged(PlayModeStateChange state)
        {
            _isExitingPlayMode = state == PlayModeStateChange.ExitingPlayMode;
        }
#endif

        public string name { get; set; } = "World Manager";

        public ExposedObjectHandle? exposedObject => ExposedObjectRegistry.FindByTarget(this);

        public string id => kId;

        // The list of added scene bundles, exposed as an editable array. The remote app toggles each
        // entry's `enabled` flag (load/unload) and removes entries; persisted to the live scene JSON.
        [NonSerialized]
        [ExposedField]
        private SceneBundleEntry[] scenes = Array.Empty<SceneBundleEntry>();

        // Loaded bundles owned by this manager, keyed by entry id. Held until the scene is unloaded.
        [NonSerialized]
        private readonly Dictionary<string, LoadedSceneBundle> _loaded =
            new Dictionary<string, LoadedSceneBundle>();

        // Entries currently being loaded or unloaded; guards against re-entrant requests.
        [NonSerialized]
        private readonly HashSet<string> _busy = new HashSet<string>();

        // The scene that was active when this manager started (the bootstrap / persistent scene).
        // When an active bundle scene is unloaded, the active scene is restored to this.
        [NonSerialized]
        private Scene _persistentScene;

        // The entry id that should become the active scene once it has finished loading. Captured
        // from a restored live scene's persisted `isActive` flag at startup, cleared after applied.
        [NonSerialized]
        private string _pendingActiveId;

        [NonSerialized]
        private bool _dirty;

        [NonSerialized]
        private bool _initialized;

        public void OnEnable()
        {
            ExposedObjectRegistry.Create<WorldManager>(this, kId);
            ExposedClass.Get<WorldManager>().onPropertyChanged += _OnPropertyChanged;

            // The active scene at startup is the bootstrap / persistent scene.
            if (Application.isPlaying)
            {
                _persistentScene = SceneManager.GetActiveScene();
            }

            // Note: a restored live scene's `scenes` array is not available here. `scenes` is
            // [NonSerialized], so it is empty at OnEnable and only populated later when the live
            // scene JSON is deserialized (RemoteControlBehaviour.Start). The restore — loading the
            // enabled entries and reactivating the saved active scene — is handled in
            // OnAfterExposedDeserialize, which fires right after that array is restored.

            _initialized = true;
        }

        public void OnDisable()
        {
            _dirty = false;
            _initialized = false;

            ExposedClass.Get<WorldManager>().onPropertyChanged -= _OnPropertyChanged;

            // Unload everything we own so leaving the scene does not leak bundles.
            foreach (var loaded in _loaded.Values)
            {
                _ = loaded.UnloadAsync();
            }
            _loaded.Clear();
            _busy.Clear();
            _pendingActiveId = null;

            ExposedObjectRegistry.FindByTarget(this)?.Unregister();
        }

        public void OnDispose()
        {
            OnDisable();
        }

        /// <summary>
        /// Fires right after the <c>scenes</c> array is restored from a saved live scene. The
        /// restored entries carry their saved <see cref="SceneBundleEntry.isLoaded"/> /
        /// <see cref="SceneBundleEntry.isActive"/> flags, but nothing is actually loaded yet, so we
        /// reconcile: schedule the enabled entries to load and remember which one should become the
        /// active scene once it has loaded (applied in <see cref="_LoadEntryAsync"/>).
        /// </summary>
        public void OnAfterExposedDeserialize()
        {
            if (!Application.isPlaying) return;

            for (int i = 0; i < scenes.Length; i++)
            {
                if (!scenes[i].isActive) continue;

                var id = scenes[i].id;
                if (_loaded.TryGetValue(id, out var loaded) &&
                    loaded.scene.IsValid() && loaded.scene.isLoaded)
                {
                    // Already loaded (e.g. a runtime re-sync): activate immediately.
                    SceneManager.SetActiveScene(loaded.scene);
                    _pendingActiveId = null;
                }
                else
                {
                    // Not loaded yet (startup restore): activate once it finishes loading.
                    _pendingActiveId = id;
                }
                break;
            }

            // Load (or unload) entries to match the restored desired state on the next Update.
            _dirty = true;
        }

        public void Update()
        {
            if (!_dirty) return;
            if (!_initialized) { _dirty = false; return; }
#if UNITY_EDITOR
            if (_isExitingPlayMode) { _dirty = false; return; }
#endif
            _dirty = false;
            _ApplySceneDiff();
        }

        public void Reset()
        {
        }

        /// <summary>
        /// Adds a scene bundle and loads it. Intended to be invoked from the remote app after the
        /// user picks a <c>*.scene.lsb</c> file.
        /// </summary>
        [ExposedFunction]
        public void AddScene(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                Debug.LogError("[LiveStudio] Scene bundle path cannot be empty.");
                return;
            }

            if (!LiveStudioBundle.IsSceneBundle(filePath))
            {
                Debug.LogError($"[LiveStudio] Not a scene bundle (*.scene.lsb): {filePath}");
                return;
            }

            if (!File.Exists(filePath))
            {
                Debug.LogError($"[LiveStudio] Scene bundle file not found: {filePath}");
                return;
            }

            var entry = new SceneBundleEntry
            {
                id = Guid.NewGuid().ToString("N"),
                name = _DeriveName(filePath),
                filePath = filePath,
                enabled = true,
                isLoaded = false,
            };

            var list = new List<SceneBundleEntry>(scenes) { entry };
            scenes = list.ToArray();

            _dirty = true;
            _Broadcast();
        }

        /// <summary>Unloads (if loaded) and removes the entry with the given id.</summary>
        [ExposedFunction]
        public void RemoveScene(string sceneId)
        {
            if (string.IsNullOrEmpty(sceneId)) return;

            if (_loaded.TryGetValue(sceneId, out var loaded))
            {
                _loaded.Remove(sceneId);
                _ = _UnloadBundleThenSyncAsync(loaded);
            }

            var list = new List<SceneBundleEntry>(scenes);
            list.RemoveAll(e => e.id == sceneId);
            scenes = list.ToArray();

            _Broadcast();
        }

        /// <summary>
        /// Makes the loaded scene with the given id the active scene (lighting/instantiation target).
        /// Only loaded scenes can be activated.
        /// </summary>
        [ExposedFunction]
        public void SetActiveScene(string sceneId)
        {
            if (string.IsNullOrEmpty(sceneId)) return;

            if (!_loaded.TryGetValue(sceneId, out var loaded))
            {
                Debug.LogWarning($"[LiveStudio] Cannot activate a scene that is not loaded: {sceneId}");
                return;
            }

            if (loaded.scene.IsValid() && loaded.scene.isLoaded)
            {
                SceneManager.SetActiveScene(loaded.scene);
            }

            _SyncActiveStates();
            _Broadcast();
        }

        /// <summary>
        /// Reacts to <c>scenes</c> (or any of its elements) changing from the remote app and
        /// schedules a diff pass on the next <see cref="Update"/>.
        /// </summary>
        private void _OnPropertyChanged(ExposedProperty property, object oldValue)
        {
            if (!_initialized) return;
            if (!property.PathContains(nameof(scenes))) return;
            _dirty = true;
        }

        /// <summary>
        /// Brings the actual loaded scenes in line with the desired <see cref="SceneBundleEntry.enabled"/>
        /// flags: load newly-enabled entries, unload newly-disabled ones, and unload entries that were
        /// removed from the array.
        /// </summary>
        private void _ApplySceneDiff()
        {
            var desiredIds = new HashSet<string>();

            for (int i = 0; i < scenes.Length; i++)
            {
                var entry = scenes[i];
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
                    _ = _UnloadEntryAsync(entry.id);
                }
            }

            // Entries removed from the array while still loaded: unload them.
            var orphans = new List<string>();
            foreach (var id in _loaded.Keys)
            {
                if (!desiredIds.Contains(id) && !_busy.Contains(id))
                {
                    orphans.Add(id);
                }
            }
            foreach (var id in orphans)
            {
                _ = _UnloadEntryAsync(id);
            }
        }

        private async Task _LoadEntryAsync(string sceneId, string filePath)
        {
            _busy.Add(sceneId);
            try
            {
                var loader = new SceneBundleLoader();
                var loaded = await loader.LoadAsync(filePath);
                if (loaded != null)
                {
                    _loaded[sceneId] = loaded;
                    _SetEntryLoaded(sceneId, true);

                    // Restore the persisted active scene once it has finished loading.
                    if (_pendingActiveId == sceneId &&
                        loaded.scene.IsValid() && loaded.scene.isLoaded)
                    {
                        SceneManager.SetActiveScene(loaded.scene);
                        _pendingActiveId = null;
                    }
                }
                else
                {
                    // Load failed: reflect the failure back as disabled so the UI is not stuck "on".
                    _SetEntryEnabled(sceneId, false);
                    _SetEntryLoaded(sceneId, false);
                }
            }
            finally
            {
                _busy.Remove(sceneId);
                _SyncActiveStates();
                _Broadcast();
            }
        }

        private async Task _UnloadEntryAsync(string sceneId)
        {
            _busy.Add(sceneId);
            try
            {
                if (_loaded.TryGetValue(sceneId, out var loaded))
                {
                    _loaded.Remove(sceneId);
                    await _UnloadBundleAsync(loaded);
                }
                _SetEntryLoaded(sceneId, false);
            }
            finally
            {
                _busy.Remove(sceneId);
                _SyncActiveStates();
                _Broadcast();
            }
        }

        /// <summary>
        /// Unloads a bundle and, if it was the active scene, restores the persistent scene as active.
        /// </summary>
        private async Task _UnloadBundleAsync(LoadedSceneBundle loaded)
        {
            bool wasActive = loaded.scene == SceneManager.GetActiveScene();
            await loaded.UnloadAsync();
            if (wasActive && _persistentScene.IsValid() && _persistentScene.isLoaded)
            {
                SceneManager.SetActiveScene(_persistentScene);
            }
        }

        /// <summary>
        /// <see cref="RemoveScene"/> path: unload the bundle (restoring the persistent scene if it was
        /// active) and then resync the active flags. The entry itself is already removed synchronously.
        /// </summary>
        private async Task _UnloadBundleThenSyncAsync(LoadedSceneBundle loaded)
        {
            await _UnloadBundleAsync(loaded);
            _SyncActiveStates();
            _Broadcast();
        }

        /// <summary>Recomputes each entry's <see cref="SceneBundleEntry.isActive"/> from the active scene.</summary>
        private void _SyncActiveStates()
        {
            var active = SceneManager.GetActiveScene();
            for (int i = 0; i < scenes.Length; i++)
            {
                bool isActive = _loaded.TryGetValue(scenes[i].id, out var loaded) && loaded.scene == active;
                if (scenes[i].isActive == isActive) continue;
                var entry = scenes[i];
                entry.isActive = isActive;
                scenes[i] = entry;
            }
        }

        private void _SetEntryLoaded(string sceneId, bool value)
        {
            for (int i = 0; i < scenes.Length; i++)
            {
                if (scenes[i].id != sceneId) continue;
                var entry = scenes[i];
                entry.isLoaded = value;
                scenes[i] = entry;
                return;
            }
        }

        private void _SetEntryEnabled(string sceneId, bool value)
        {
            for (int i = 0; i < scenes.Length; i++)
            {
                if (scenes[i].id != sceneId) continue;
                var entry = scenes[i];
                entry.enabled = value;
                scenes[i] = entry;
                return;
            }
        }

        private void _Broadcast()
        {
            // Pass the target instance (not the nullable handle): the (object) overload resolves the
            // handle via the registry. Passing an ExposedObjectHandle? would box to object and fail
            // the registry lookup.
            ExposedPropertyBroadcast.BroadcastProperty(this, "scenes");
        }

        private static string _DeriveName(string filePath)
        {
            var fileName = Path.GetFileName(filePath);
            if (fileName.EndsWith(LiveStudioBundle.SceneExtension, StringComparison.OrdinalIgnoreCase))
            {
                return fileName.Substring(0, fileName.Length - LiveStudioBundle.SceneExtension.Length);
            }
            return Path.GetFileNameWithoutExtension(fileName);
        }
    }
}
