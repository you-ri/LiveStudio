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
    /// One entry in <see cref="StageManager.sets"/>: a set bundle (<c>*.set.lsb</c>)
    /// that the user has added. <see cref="enabled"/> is the desired state controlled from the
    /// remote app; <see cref="isLoaded"/> reflects whether the set is actually loaded.
    /// </summary>
    [Serializable]
    [ExposedClass]
    public struct SetBundleEntry
    {
        [ExposedField]
        public string id;

        [ExposedField]
        public string name;

        [ExposedField]
        public string filePath;

        /// <summary>
        /// Desired state. Toggling this loads (true) or unloads (false) the set. Mirrored to the
        /// backing <see cref="SetBundleAsset"/>, which is where the state is actually persisted.
        /// </summary>
        [ExposedField]
        public bool enabled;

        /// <summary>Actual state, projected from the backing <see cref="SetBundleAsset"/>.</summary>
        [ExposedField(persistable = false)]
        public bool isLoaded;

        /// <summary>True when this set is the active set.</summary>
        [ExposedField]
        public bool isActive;

        /// <summary>
        /// True for the bootstrap (initial) set the app loads at startup. This entry is always
        /// present and loaded, and cannot be unloaded or removed; only activation is allowed.
        /// Rebuilt at runtime, so it is not persisted.
        /// </summary>
        [ExposedField(persistable = false)]
        public bool isPersistent;
    }

    /// <summary>
    /// Surfaces the loaded set bundles to the remote app's Stage page and owns the set-specific
    /// concepts the unified asset pipeline does not model: the active set and the bootstrap
    /// (persistent) set.
    ///
    /// The actual load/unload of set bundles is delegated to <see cref="ExternalAssetManager"/>,
    /// where each set is a <see cref="SetBundleAsset"/> alongside props and avatars. This manager keeps
    /// no bundles of its own: <see cref="sets"/> is a runtime-only projection of the SetBundleAsset
    /// entries (plus the synthetic persistent entry), so the persisted source of truth lives entirely
    /// on the assets side. Remote-app edits to an entry's <see cref="SetBundleEntry.enabled"/> flag
    /// are forwarded to the matching SetBundleAsset; activation is applied here through the asset's loaded
    /// scene handle.
    /// </summary>
    [Serializable]
    [ExposedClass(Icon = "public", Category = "Stage")]
    [MovedFrom(false, null, null, "WorldManager")]
    public class StageManager : IExposedObject
    {
        const string kId = "b2f7c9a1-3d4e-4f8a-9c1b-7e2d5a6f8c30";

        // Stable id for the synthetic entry representing the bootstrap / persistent set. A short
        // literal is safe because real bundle entries use path-based ids and never collide with it.
        const string kPersistentSetId = "persistent";

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

        public string name { get; set; } = "Stage Manager";

        public ExposedObjectHandle? exposedObject => ExposedObjectRegistry.FindByTarget(this);

        public string id => kId;

        // Projected view of the set bundles for the remote app's Stage page: the persistent entry
        // followed by each SetBundleAsset in ExternalAssetManager. Not persisted — the SetBundleAsset
        // entries are the persisted source of truth — so it is rebuilt whenever the asset list changes.
        [NonSerialized]
        [ExposedField(persistable = false)]
        private SetBundleEntry[] sets = Array.Empty<SetBundleEntry>();

        // The scene that was active when this manager started (the bootstrap / persistent set).
        // When an active set's scene is unloaded, the active scene is restored to this.
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
            ExposedObjectRegistry.Create<StageManager>(this, kId);
            ExposedClass.Get<StageManager>().onPropertyChanged += _OnPropertyChanged;

            // The active scene at startup is the bootstrap / persistent set. Surface it as the
            // first, non-removable entry so the remote app can see and re-activate it.
            if (Application.isPlaying)
            {
                _persistentScene = SceneManager.GetActiveScene();
            }

            _initialized = true;
            _RebuildSetsView();
        }

        public void OnDisable()
        {
            _initialized = false;

            ExposedClass.Get<StageManager>().onPropertyChanged -= _OnPropertyChanged;

            // ExternalAssetManager owns the loaded set bundles and unloads them in its own teardown;
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
        /// Adds a set bundle and loads it. Intended to be invoked from the remote app after the
        /// user picks a <c>*.set.lsb</c> file. Delegates to <see cref="ExternalAssetManager"/>, which
        /// creates the backing <see cref="SetBundleAsset"/>; the projected view rebuilds on the resulting
        /// assets-changed notification.
        /// </summary>
        [ExposedFunction]
        public void AddSet(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                Debug.LogError("[LiveStudio] Set bundle path cannot be empty.");
                return;
            }

            if (!LiveStudioBundle.IsSetBundle(filePath))
            {
                Debug.LogError($"[LiveStudio] Not a set bundle (*.set.lsb): {filePath}");
                return;
            }

            if (!File.Exists(filePath))
            {
                Debug.LogError($"[LiveStudio] Set bundle file not found: {filePath}");
                return;
            }

            var manager = ExternalAssetManager.current;
            if (manager == null)
            {
                Debug.LogError("[LiveStudio] No ExternalAssetManager in the scene; cannot add a set bundle.");
                return;
            }

            manager.AddAsset(filePath);
        }

        /// <summary>Unloads (if loaded) and removes the entry with the given id.</summary>
        [ExposedFunction]
        public void RemoveSet(string setId)
        {
            if (string.IsNullOrEmpty(setId)) return;

            // The bootstrap set is always present and cannot be removed.
            if (setId == kPersistentSetId) return;

            ExternalAssetManager.current?.RemoveAsset(setId);
        }

        /// <summary>
        /// Makes the loaded set with the given id the active set (lighting/instantiation target).
        /// Only loaded sets can be activated.
        /// </summary>
        [ExposedFunction]
        public void SetActiveSet(string setId)
        {
            if (string.IsNullOrEmpty(setId)) return;

            var manager = ExternalAssetManager.current;

            // Activating the bootstrap set: clear the SetBundleAsset active flags so it becomes active.
            if (setId == kPersistentSetId)
            {
                _ClearSetBundleAssetActiveFlags(manager);
            }
            else
            {
                var asset = manager?.FindAsset(setId) as SetBundleAsset;
                if (asset == null || !asset.hasScene)
                {
                    Debug.LogWarning($"[LiveStudio] Cannot activate a set that is not loaded: {setId}");
                    return;
                }

                // Radio: only the target set asset is flagged active.
                var view = manager.assetsView;
                for (int i = 0; i < view.Count; i++)
                {
                    if (view[i] is SetBundleAsset s) s.isActive = s == asset;
                }
            }

            _ReconcileActiveScene();
            _RebuildSetsView();
        }

        /// <summary>
        /// Forwards remote-app edits of <c>sets</c> (an entry's <see cref="SetBundleEntry.enabled"/>
        /// flag) to the backing <see cref="SetBundleAsset"/>, which drives the actual load/unload.
        /// </summary>
        private void _OnPropertyChanged(ExposedProperty property, object oldValue)
        {
            if (!_initialized) return;
            if (!property.PathContains(nameof(sets))) return;
            _TransferEnabledToAssets();
        }

        // Pushes each non-persistent entry's desired enabled state onto its SetBundleAsset. SetAssetEnabled
        // is a no-op when unchanged, so mirroring the whole list on any edit stays cheap.
        private void _TransferEnabledToAssets()
        {
            var manager = ExternalAssetManager.current;
            if (manager == null) return;
            for (int i = 0; i < sets.Length; i++)
            {
                if (sets[i].isPersistent) continue;
                manager.SetAssetEnabled(sets[i].id, sets[i].enabled);
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
            _RebuildSetsView();
        }

        /// <summary>
        /// Brings the actual active scene in line with the desired one: the loaded SetBundleAsset flagged
        /// <see cref="SetBundleAsset.isActive"/>, or the bootstrap scene when none is. A set flagged
        /// active but not yet loaded is skipped until it loads (a later assets-changed fires this again).
        /// </summary>
        private void _ReconcileActiveScene()
        {
            if (!Application.isPlaying) return;

            var current = SceneManager.GetActiveScene();
            var desired = _FindActiveSetBundleAsset();

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

        // The loaded SetBundleAsset that should be the active set, or null when the bootstrap set is.
        private SetBundleAsset _FindActiveSetBundleAsset()
        {
            var manager = ExternalAssetManager.current;
            if (manager == null) return null;
            var view = manager.assetsView;
            for (int i = 0; i < view.Count; i++)
            {
                if (view[i] is SetBundleAsset s && s.isActive && s.hasScene) return s;
            }
            return null;
        }

        private static bool _AnySetBundleAssetActive(ExternalAssetManager manager)
        {
            if (manager == null) return false;
            var view = manager.assetsView;
            for (int i = 0; i < view.Count; i++)
            {
                if (view[i] is SetBundleAsset s && s.isActive) return true;
            }
            return false;
        }

        private static void _ClearSetBundleAssetActiveFlags(ExternalAssetManager manager)
        {
            if (manager == null) return;
            var view = manager.assetsView;
            for (int i = 0; i < view.Count; i++)
            {
                if (view[i] is SetBundleAsset s) s.isActive = false;
            }
        }

        /// <summary>
        /// Rebuilds <c>sets</c> from the current state: the persistent entry first, then each
        /// <see cref="SetBundleAsset"/> in <see cref="ExternalAssetManager"/> projected to a
        /// <see cref="SetBundleEntry"/>.
        /// </summary>
        private void _RebuildSetsView()
        {
            var manager = ExternalAssetManager.current;

            var list = new List<SetBundleEntry>();
            if (Application.isPlaying && _persistentScene.IsValid())
            {
                list.Add(_CreatePersistentEntry(manager));
            }

            if (manager != null)
            {
                var view = manager.assetsView;
                for (int i = 0; i < view.Count; i++)
                {
                    if (!(view[i] is SetBundleAsset s)) continue;
                    list.Add(new SetBundleEntry
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

            sets = list.ToArray();
            _Broadcast();
        }

        // The bootstrap set is active whenever no set bundle is flagged active.
        private SetBundleEntry _CreatePersistentEntry(ExternalAssetManager manager)
        {
            var sceneName = _persistentScene.IsValid() ? _persistentScene.name : null;
            return new SetBundleEntry
            {
                id = kPersistentSetId,
                name = string.IsNullOrEmpty(sceneName) ? "Studio" : sceneName,
                filePath = string.Empty,
                enabled = true,
                isLoaded = true,
                isActive = !_AnySetBundleAssetActive(manager),
                isPersistent = true,
            };
        }

        private void _Broadcast()
        {
            // Pass the target instance (not the nullable handle): the (object) overload resolves the
            // handle via the registry. Passing an ExposedObjectHandle? would box to object and fail
            // the registry lookup.
            ExposedPropertyBroadcast.BroadcastProperty(this, "sets");
        }
    }
}
