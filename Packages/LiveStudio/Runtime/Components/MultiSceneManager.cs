// Copyright (c) You-Ri, 2026

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.ComponentModel;

#if UNITY_EDITOR
using UnityEditor;
#endif

using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    [Serializable]
    [ExposedClass]
    public struct SceneInfo
    {
        [ExposedField]
        public string name;

        [ExposedField]
        public int buildIndex;

        [ExposedField]
        public bool isLoaded;

        [ExposedField]
        public bool isActive;
    }

    [Serializable]
    [ExposedClass(Icon = "layers", Category = "Scene")]
    public class MultiSceneManager : IExposedObject
    {
        const string kId = "f8a3b1c7-4e2d-4a9f-b6d5-8c1e3f7a2b90";

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

        public string name { get; set; } = "Scene Manager";

        public ExposedObject exposedObject => ExposedObjectRegistry.FindByTarget(this);

        public string id => kId;

        [NonSerialized]
        [ExposedField, Hide]
        [FormerlyExposedAs("scenes")]
        private SceneInfo[] _scenes = Array.Empty<SceneInfo>();

        [NonSerialized]
        private bool _scenesDirty;

        [NonSerialized]
        private bool _initialized;

        [ExposedProperty]
        public SceneInfo[] scenes
        {
            get => _scenes;
            set => _scenes = value;
        }

        [ExposedProperty]
        public string activeSceneName => SceneManager.GetActiveScene().name;

        public void OnEnable()
        {
            ExposedObjectRegistry.Create<MultiSceneManager>(this, kId);

            ExposedClass.Get<MultiSceneManager>().onPropertyChanged += _OnPropertyChanged;

            SceneManager.sceneLoaded += _OnSceneLoaded;
            SceneManager.sceneUnloaded += _OnSceneUnloaded;
            SceneManager.activeSceneChanged += _OnActiveSceneChanged;

            // 永続化データからシーンを復元
            if (Application.isPlaying && _scenes != null && _scenes.Length > 0)
            {
                _RestoreScenes();
            }

            _SyncScenes();
            _initialized = true;
        }

        public void OnDisable()
        {
            _scenesDirty = false;
            _initialized = false;

            ExposedClass.Get<MultiSceneManager>().onPropertyChanged -= _OnPropertyChanged;

            SceneManager.sceneLoaded -= _OnSceneLoaded;
            SceneManager.sceneUnloaded -= _OnSceneUnloaded;
            SceneManager.activeSceneChanged -= _OnActiveSceneChanged;

            ExposedObjectRegistry.FindByTarget(this)?.Unregister();
        }

        public void OnDispose()
        {
            OnDisable();
        }

        public void Update()
        {
            if (!_scenesDirty) return;
            if (!_initialized) { _scenesDirty = false; return; }
#if UNITY_EDITOR
            if (_isExitingPlayMode) { _scenesDirty = false; return; }
#endif
            _scenesDirty = false;
            _ApplySceneDiff();
        }

        public void Reset()
        {
        }

        /// <summary>
        /// RemoteAppからscenes配列が変更されたときにdirtyフラグを立てる。
        /// 実際のシーン操作はUpdate()で遅延実行する。
        /// </summary>
        private void _OnPropertyChanged(ExposedProperty property, object oldValue)
        {
            if (property.path.Value != nameof(scenes)) return;
            if (!_initialized) return;
            _scenesDirty = true;
        }

        /// <summary>
        /// scenes配列とSceneManagerの状態を比較し、差分を適用する。
        /// </summary>
        private void _ApplySceneDiff()
        {
            // 空名称の要素にデフォルト名を付与
            for (int i = 0; i < _scenes.Length; i++)
            {
                if (string.IsNullOrEmpty(_scenes[i].name))
                {
                    var info = _scenes[i];
                    info.name = _GenerateUniqueSceneName();
                    _scenes[i] = info;
                }
            }

            // scenes配列のシーン名を収集
            var desiredSceneNames = new HashSet<string>();
            for (int i = 0; i < _scenes.Length; i++)
            {
                desiredSceneNames.Add(_scenes[i].name);
            }

            // 現在のSceneManager状態を収集
            var currentSceneNames = new HashSet<string>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded)
                {
                    currentSceneNames.Add(scene.name);
                }
            }

            // 追加: scenes配列にあってSceneManagerにないシーンを作成
            foreach (var sceneName in desiredSceneNames)
            {
                if (!currentSceneNames.Contains(sceneName))
                {
                    SceneManager.CreateScene(sceneName, new CreateSceneParameters(LocalPhysicsMode.None));
                    Debug.Log($"[RemoteControl] Scene created: {sceneName}");
                }
            }

            // 削除: SceneManagerにあってscenes配列にないシーンをアンロード
            foreach (var sceneName in currentSceneNames)
            {
                if (!desiredSceneNames.Contains(sceneName) && SceneManager.sceneCount > 1)
                {
                    var scene = SceneManager.GetSceneByName(sceneName);
                    if (scene.IsValid() && scene.isLoaded)
                    {
                        SceneManager.UnloadSceneAsync(scene);
                        Debug.Log($"[RemoteControl] Scene unloading: {sceneName}");
                    }
                }
            }

            // 実際の状態で配列を再構築
            _SyncScenes();
            _BroadcastScenes();
        }

        /// <summary>
        /// 永続化データからシーンを復元する。OnEnable時に1回だけ呼ばれる。
        /// </summary>
        private void _RestoreScenes()
        {
            for (int i = 0; i < _scenes.Length; i++)
            {
                var sceneName = _scenes[i].name;
                if (string.IsNullOrEmpty(sceneName)) continue;

                var existing = SceneManager.GetSceneByName(sceneName);
                if (!existing.IsValid())
                {
                    SceneManager.CreateScene(sceneName, new CreateSceneParameters(LocalPhysicsMode.None));
                    Debug.Log($"[RemoteControl] Scene restored: {sceneName}");
                }
            }
        }

        private void _OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            _SyncScenes();
            _BroadcastScenes();
        }

        private void _OnSceneUnloaded(Scene scene)
        {
            _SyncScenes();
            _BroadcastScenes();
        }

        private void _OnActiveSceneChanged(Scene previousActiveScene, Scene newActiveScene)
        {
            _SyncScenes();
            _BroadcastScenes();
        }

        private string _GenerateUniqueSceneName()
        {
            const string kBaseName = "New Scene";

            // 既存シーン名を収集
            var existingNames = new HashSet<string>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                existingNames.Add(SceneManager.GetSceneAt(i).name);
            }
            for (int i = 0; i < _scenes.Length; i++)
            {
                if (!string.IsNullOrEmpty(_scenes[i].name))
                {
                    existingNames.Add(_scenes[i].name);
                }
            }

            if (!existingNames.Contains(kBaseName)) return kBaseName;

            for (int n = 1; ; n++)
            {
                var candidate = $"{kBaseName} ({n})";
                if (!existingNames.Contains(candidate)) return candidate;
            }
        }

        private void _SyncScenes()
        {
            int count = SceneManager.sceneCount;
            if (_scenes == null || _scenes.Length != count)
            {
                _scenes = new SceneInfo[count];
            }

            var activeScene = SceneManager.GetActiveScene();

            for (int i = 0; i < count; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                _scenes[i] = new SceneInfo
                {
                    name = scene.name,
                    buildIndex = scene.buildIndex,
                    isLoaded = scene.isLoaded,
                    isActive = scene == activeScene,
                };
            }
        }

        private void _BroadcastScenes()
        {
            var obj = exposedObject;
            if (obj != null)
            {
                ExposedPropertyBroadcast.BroadcastProperty(obj, "scenes");
            }
        }

        [ExposedFunction]
        public void CreateScene(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName))
            {
                Debug.LogError("[RemoteControl] Scene name cannot be empty.");
                return;
            }

            var existingScene = SceneManager.GetSceneByName(sceneName);
            if (existingScene.IsValid())
            {
                Debug.LogWarning($"[RemoteControl] Scene already exists: {sceneName}");
                return;
            }

            SceneManager.CreateScene(sceneName, new CreateSceneParameters(LocalPhysicsMode.None));
            Debug.Log($"[RemoteControl] Scene created: {sceneName}");
        }

        [ExposedFunction]
        public void UnloadScene(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName))
            {
                Debug.LogError("[RemoteControl] Scene name cannot be empty.");
                return;
            }

            var scene = SceneManager.GetSceneByName(sceneName);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.LogWarning($"[RemoteControl] Scene not found or not loaded: {sceneName}");
                return;
            }

            if (SceneManager.sceneCount <= 1)
            {
                Debug.LogError("[RemoteControl] Cannot unload the last scene.");
                return;
            }

            SceneManager.UnloadSceneAsync(scene);
            Debug.Log($"[RemoteControl] Scene unloading: {sceneName}");
        }

        [ExposedFunction]
        public void SetActiveScene(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName))
            {
                Debug.LogError("[RemoteControl] Scene name cannot be empty.");
                return;
            }

            var scene = SceneManager.GetSceneByName(sceneName);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.LogWarning($"[RemoteControl] Scene not found or not loaded: {sceneName}");
                return;
            }

            SceneManager.SetActiveScene(scene);
            Debug.Log($"[RemoteControl] Active scene set to: {sceneName}");
        }
    }
}
