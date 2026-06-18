// Copyright (c) You-Ri, 2026

using System;
using System.Collections.Generic;
using System.IO;

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

        /// <summary>
        /// Desired state. Toggling this loads (true) or unloads (false) the scene. Mirrored to the
        /// backing <see cref="SceneBundleAsset"/>, which is where the state is actually persisted.
        /// </summary>
        [ExposedField]
        public bool enabled;

        /// <summary>Actual state, projected from the backing <see cref="SceneBundleAsset"/>.</summary>
        [ExposedField(persistable = false)]
        public bool isLoaded;

        /// <summary>True when this scene is the active scene.</summary>
        [ExposedField]
        public bool isActive;

        /// <summary>
        /// True for the bootstrap (initial) scene the app loads at startup. This entry is always
        /// present and loaded, and cannot be unloaded or removed; only activation is allowed.
        /// Rebuilt at runtime, so it is not persisted.
        /// </summary>
        [ExposedField(persistable = false)]
        public bool isPersistent;
    }

    /// <summary>
    /// Surfaces the loaded scene bundles to the remote app's Worlds page and owns the scene-specific
    /// concepts the unified asset pipeline does not model: the active scene and the bootstrap
    /// (persistent) scene.
    ///
    /// The actual load/unload of scene bundles is delegated to <see cref="ExternalAssetManager"/>,
    /// where each scene is a <see cref="SceneBundleAsset"/> alongside props and avatars. This manager keeps
    /// no bundles of its own: <see cref="scenes"/> is a runtime-only projection of the SceneBundleAsset
    /// entries (plus the synthetic persistent entry), so the persisted source of truth lives entirely
    /// on the assets side. Remote-app edits to an entry's <see cref="SceneBundleEntry.enabled"/> flag
    /// are forwarded to the matching SceneBundleAsset; activation is applied here through the asset's loaded
    /// scene handle.
    /// </summary>
    [Serializable]
    [ExposedClass(Icon = "public", Category = "Scene")]
    [MovedFrom(false, null, null, "RuntimeSceneManager")]
    public class WorldManager : IExposedObject
    {
        const string kId = "b2f7c9a1-3d4e-4f8a-9c1b-7e2d5a6f8c30";

        // Stable id for the synthetic entry representing the bootstrap / persistent scene. A short
        // literal is safe because real bundle entries use path-based ids and never collide with it.
        const string kPersistentSceneId = "persistent";

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

        // Projected view of the scene bundles for the remote app's Worlds page: the persistent entry
        // followed by each SceneBundleAsset in ExternalAssetManager. Not persisted — the SceneBundleAsset entries
        // are the persisted source of truth — so it is rebuilt whenever the asset list changes.
        [NonSerialized]
        [ExposedField(persistable = false)]
        private SceneBundleEntry[] scenes = Array.Empty<SceneBundleEntry>();

        // The scene that was active when this manager started (the bootstrap / persistent scene).
        // When an active bundle scene is unloaded, the active scene is restored to this.
        [NonSerialized]
        private Scene _persistentScene;

        [NonSerialized]
        private bool _initialized;

        // The ExternalAssetManager we are subscribed to (null when not subscribed). The manager may not
        // exist yet at OnEnable (load order), so subscription is deferred to Update. Holding the
        // instance — rather than re-reading the singleton — lets us unsubscribe cleanly even if the
        // manager is torn down before us.
        [NonSerialized]
        private ExternalAssetManager _subscribedManager;

        public void OnEnable()
        {
            ExposedObjectRegistry.Create<WorldManager>(this, kId);
            ExposedClass.Get<WorldManager>().onPropertyChanged += _OnPropertyChanged;

            // The active scene at startup is the bootstrap / persistent scene. Surface it as the
            // first, non-removable entry so the remote app can see and re-activate it.
            if (Application.isPlaying)
            {
                _persistentScene = SceneManager.GetActiveScene();
            }

            _initialized = true;
            _RebuildScenesView();
        }

        public void OnDisable()
        {
            _initialized = false;

            ExposedClass.Get<WorldManager>().onPropertyChanged -= _OnPropertyChanged;

            // ExternalAssetManager owns the loaded scene bundles and unloads them in its own teardown;
            // here we only drop our subscription.
            if (_subscribedManager != null)
            {
                _subscribedManager.onAssetsChanged -= _OnAssetsChanged;
                _subscribedManager = null;
            }

            ExposedObjectRegistry.FindByTarget(this)?.Unregister();
        }

        public void OnDispose()
        {
            OnDisable();
        }

        public void Update()
        {
            if (!_initialized) return;
            if (!Application.isPlaying) return;
#if UNITY_EDITOR
            if (_isExitingPlayMode) return;
#endif
            _EnsureSubscribed();
        }

        public void Reset()
        {
        }

        /// <summary>
        /// Adds a scene bundle and loads it. Intended to be invoked from the remote app after the
        /// user picks a <c>*.scene.lsb</c> file. Delegates to <see cref="ExternalAssetManager"/>, which
        /// creates the backing <see cref="SceneBundleAsset"/>; the projected view rebuilds on the resulting
        /// assets-changed notification.
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

            var manager = ExternalAssetManager.current;
            if (manager == null)
            {
                Debug.LogError("[LiveStudio] No ExternalAssetManager in the scene; cannot add a scene bundle.");
                return;
            }

            manager.AddAsset(filePath);
        }

        /// <summary>Unloads (if loaded) and removes the entry with the given id.</summary>
        [ExposedFunction]
        public void RemoveScene(string sceneId)
        {
            if (string.IsNullOrEmpty(sceneId)) return;

            // The bootstrap scene is always present and cannot be removed.
            if (sceneId == kPersistentSceneId) return;

            ExternalAssetManager.current?.RemoveAsset(sceneId);
        }

        /// <summary>
        /// Makes the loaded scene with the given id the active scene (lighting/instantiation target).
        /// Only loaded scenes can be activated.
        /// </summary>
        [ExposedFunction]
        public void SetActiveScene(string sceneId)
        {
            if (string.IsNullOrEmpty(sceneId)) return;

            var manager = ExternalAssetManager.current;

            // Activating the bootstrap scene: clear the SceneBundleAsset active flags so it becomes active.
            if (sceneId == kPersistentSceneId)
            {
                _ClearSceneBundleAssetActiveFlags(manager);
            }
            else
            {
                var asset = manager?.FindAsset(sceneId) as SceneBundleAsset;
                if (asset == null || !asset.hasScene)
                {
                    Debug.LogWarning($"[LiveStudio] Cannot activate a scene that is not loaded: {sceneId}");
                    return;
                }

                // Radio: only the target scene asset is flagged active.
                var view = manager.assetsView;
                for (int i = 0; i < view.Count; i++)
                {
                    if (view[i] is SceneBundleAsset s) s.isActive = s == asset;
                }
            }

            _ReconcileActiveScene();
            _RebuildScenesView();
        }

        /// <summary>
        /// Forwards remote-app edits of <c>scenes</c> (an entry's <see cref="SceneBundleEntry.enabled"/>
        /// flag) to the backing <see cref="SceneBundleAsset"/>, which drives the actual load/unload.
        /// </summary>
        private void _OnPropertyChanged(ExposedProperty property, object oldValue)
        {
            if (!_initialized) return;
            if (!property.PathContains(nameof(scenes))) return;
            _TransferEnabledToAssets();
        }

        // Pushes each non-persistent entry's desired enabled state onto its SceneBundleAsset. SetAssetEnabled
        // is a no-op when unchanged, so mirroring the whole list on any edit stays cheap.
        private void _TransferEnabledToAssets()
        {
            var manager = ExternalAssetManager.current;
            if (manager == null) return;
            for (int i = 0; i < scenes.Length; i++)
            {
                if (scenes[i].isPersistent) continue;
                manager.SetAssetEnabled(scenes[i].id, scenes[i].enabled);
            }
        }

        private void _EnsureSubscribed()
        {
            if (_subscribedManager != null) return;
            var manager = ExternalAssetManager.current;
            if (manager == null) return;
            manager.onAssetsChanged += _OnAssetsChanged;
            _subscribedManager = manager;
            // Catch up on any load/unload that completed before we subscribed.
            _OnAssetsChanged();
        }

        // The asset list changed (load/unload completed, entry added/removed): reconcile the active
        // scene and rebuild the projected view.
        private void _OnAssetsChanged()
        {
            _ReconcileActiveScene();
            _RebuildScenesView();
        }

        /// <summary>
        /// Brings the actual active scene in line with the desired one: the loaded SceneBundleAsset flagged
        /// <see cref="SceneBundleAsset.isActive"/>, or the bootstrap scene when none is. A scene flagged
        /// active but not yet loaded is skipped until it loads (a later assets-changed fires this again).
        /// </summary>
        private void _ReconcileActiveScene()
        {
            if (!Application.isPlaying) return;

            var current = SceneManager.GetActiveScene();
            var desired = _FindActiveSceneBundleAsset();

            if (desired != null)
            {
                if (desired.scene != current)
                {
                    SceneManager.SetActiveScene(desired.scene);
                }
            }
            else if (_persistentScene.IsValid() && _persistentScene.isLoaded && current != _persistentScene)
            {
                SceneManager.SetActiveScene(_persistentScene);
            }
        }

        // The loaded SceneBundleAsset that should be the active scene, or null when the bootstrap scene is.
        private SceneBundleAsset _FindActiveSceneBundleAsset()
        {
            var manager = ExternalAssetManager.current;
            if (manager == null) return null;
            var view = manager.assetsView;
            for (int i = 0; i < view.Count; i++)
            {
                if (view[i] is SceneBundleAsset s && s.isActive && s.hasScene) return s;
            }
            return null;
        }

        private static bool _AnySceneBundleAssetActive(ExternalAssetManager manager)
        {
            if (manager == null) return false;
            var view = manager.assetsView;
            for (int i = 0; i < view.Count; i++)
            {
                if (view[i] is SceneBundleAsset s && s.isActive) return true;
            }
            return false;
        }

        private static void _ClearSceneBundleAssetActiveFlags(ExternalAssetManager manager)
        {
            if (manager == null) return;
            var view = manager.assetsView;
            for (int i = 0; i < view.Count; i++)
            {
                if (view[i] is SceneBundleAsset s) s.isActive = false;
            }
        }

        /// <summary>
        /// Rebuilds <c>scenes</c> from the current state: the persistent entry first, then each
        /// <see cref="SceneBundleAsset"/> in <see cref="ExternalAssetManager"/> projected to a
        /// <see cref="SceneBundleEntry"/>.
        /// </summary>
        private void _RebuildScenesView()
        {
            var manager = ExternalAssetManager.current;

            var list = new List<SceneBundleEntry>();
            if (Application.isPlaying && _persistentScene.IsValid())
            {
                list.Add(_CreatePersistentEntry(manager));
            }

            if (manager != null)
            {
                var view = manager.assetsView;
                for (int i = 0; i < view.Count; i++)
                {
                    if (!(view[i] is SceneBundleAsset s)) continue;
                    list.Add(new SceneBundleEntry
                    {
                        id = s.id,
                        name = s.name,
                        filePath = s.filePath,
                        enabled = s.enabled,
                        isLoaded = s.isLoaded,
                        isActive = s.isActive,
                        isPersistent = false,
                    });
                }
            }

            scenes = list.ToArray();
            _Broadcast();
        }

        // The bootstrap scene is active whenever no bundle scene is flagged active.
        private SceneBundleEntry _CreatePersistentEntry(ExternalAssetManager manager)
        {
            var sceneName = _persistentScene.IsValid() ? _persistentScene.name : null;
            return new SceneBundleEntry
            {
                id = kPersistentSceneId,
                name = string.IsNullOrEmpty(sceneName) ? "Studio" : sceneName,
                filePath = string.Empty,
                enabled = true,
                isLoaded = true,
                isActive = !_AnySceneBundleAssetActive(manager),
                isPersistent = true,
            };
        }

        private void _Broadcast()
        {
            // Pass the target instance (not the nullable handle): the (object) overload resolves the
            // handle via the registry. Passing an ExposedObjectHandle? would box to object and fail
            // the registry lookup.
            ExposedPropertyBroadcast.BroadcastProperty(this, "scenes");
        }
    }
}
