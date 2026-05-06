// Copyright (c) You-Ri, 2026

using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    [ExposedClass(Icon = "settings")]
    public static class ProjectManager
    {
        [ExposedProperty, Hide]
        public static string[] qualityNames => QualitySettings.names;

        [ExposedProperty, Hide]
        public static string scenePath
        {
            get
            {
#if UNITY_2022_3_OR_NEWER
                var provider = Object.FindFirstObjectByType<Lilium.RemoteControl.Server.RemoteControlBehaviour>();
#else
                var provider = Object.FindObjectOfType<Lilium.RemoteControl.Server.RemoteControlBehaviour>();
#endif
                if (provider == null) return "";
                // currentFilePath が空の場合はシーンが未保存状態なので空文字を返す
                return string.IsNullOrEmpty(provider.currentFilePath) ? "" : provider.currentFullPath;
            }
        }

        // ビルド設定 (enabled なシーンのみ) を SceneInfo[] として返す。
        // RemoteApp が NewScene のダイアログでベースシーン候補一覧を表示するために使用。
        // ビルド済みアプリでも動作するよう EditorBuildSettings は使わず SceneUtility 経由で取得する。
        [ExposedProperty, Hide]
        public static SceneInfo[] availableScenes
        {
            get
            {
                int count = SceneManager.sceneCountInBuildSettings;
                var result = new SceneInfo[count];
                var activeName = SceneManager.GetActiveScene().name;
                for (int i = 0; i < count; i++)
                {
                    var path = SceneUtility.GetScenePathByBuildIndex(i);
                    var sceneName = Path.GetFileNameWithoutExtension(path);
                    var loaded = SceneManager.GetSceneByBuildIndex(i);
                    result[i] = new SceneInfo
                    {
                        name = sceneName,
                        buildIndex = i,
                        isLoaded = loaded.IsValid() && loaded.isLoaded,
                        isActive = sceneName == activeName,
                    };
                }
                return result;
            }
        }

        [ExposedField, Hide]
        [FormerlyExposedAs("quality")]
        private static string _quality;

        [Section("high_quality", "SECTION_QUALITY_TITLE", "SECTION_QUALITY_SUBTITLE")]
        [ExposedProperty]
        [StringSelector(nameof(qualityNames))]
        public static string quality
        {
            get => _quality;
            set
            {
                _quality = value;
                SetQuality(value);
            }
        }

        public static int currentQualityIndex => QualitySettings.GetQualityLevel();

        // Sync the shadow field with QualitySettings on startup so the initial
        // getter value reflects the active quality level (and the JSON baseline
        // matches reality for dirty detection).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void _InitializeQuality()
        {
            _quality = QualitySettings.names[QualitySettings.GetQualityLevel()];
        }

        // Convention-based static deserialize callback fired by ExposedClass when
        // the owning ExposedObject's target is null (static class). Re-applies
        // _quality to QualitySettings since the shadow field write bypasses the
        // property setter.
        public static void OnAfterExposedDeserialize()
        {
            if (!string.IsNullOrEmpty(_quality)) SetQuality(_quality);
        }

        [ExposedFunction(label = "PROJECT_OPEN_SAVE_FOLDER"), Hide]
        public static void OpenSaveFolder()
        {
            var path = SavedPaths.EnsureSceneDirectory();
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true,
                };
                System.Diagnostics.Process.Start(psi);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[Studio] Failed to open save folder '{path}': {ex.Message}");
            }
        }

        [ExposedFunction(label = "PROJECT_SAVE_SCENE"), Hide]
        public static void SaveScene(string filePath = null)
        {
            var providers = Object.FindObjectsOfType<Lilium.RemoteControl.Server.RemoteControlBehaviour>();
            foreach (var provider in providers)
            {
                if (string.IsNullOrEmpty(filePath))
                {
                    provider.SaveCurrentData();
                }
                else
                {
                    provider.SaveCurrentDataTo(filePath);
                }
            }
        }

        [ExposedFunction(label = "PROJECT_LOAD_SCENE"), Hide]
        public static void LoadScene(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return;

            // 起動時 (RemoteControlBehaviour.Start → LoadCurrentData) と同じパスを通すため、
            // ここでは provider に filePath だけ伝え、シーン切替/デシリアライズの判断は
            // provider 内部 (LoadCurrentData → _TrySwitchBaseScene → _LoadFrom) に委ねる。
            var providers = Object.FindObjectsOfType<Lilium.RemoteControl.Server.RemoteControlBehaviour>();
            foreach (var provider in providers)
            {
                provider.LoadCurrentDataFrom(filePath);
            }
        }

        [ExposedFunction(label = "PROJECT_NEW_SCENE"), Hide]
        public static void NewScene(string sceneName = null)
        {
            var providers = Object.FindObjectsOfType<Lilium.RemoteControl.Server.RemoteControlBehaviour>();
            foreach (var provider in providers)
            {
                provider.currentFilePath = "";
            }
            _SwitchBaseScene(sceneName);
        }

        // ベース Unity シーンを切り替える内部ヘルパ。
        // sceneName が null/空または見つからない場合は、現アクティブシーンを再ロードする。
        private static void _SwitchBaseScene(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName))
            {
                var activeScene = SceneManager.GetActiveScene();
                SceneManager.LoadScene(activeScene.buildIndex);
                return;
            }

            int count = SceneManager.sceneCountInBuildSettings;
            for (int i = 0; i < count; i++)
            {
                var path = SceneUtility.GetScenePathByBuildIndex(i);
                if (Path.GetFileNameWithoutExtension(path) == sceneName)
                {
                    SceneManager.LoadScene(i);
                    return;
                }
            }

            Debug.LogWarning($"[Studio] Scene '{sceneName}' not found in build settings. Falling back to active scene.");
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        public static void SetQuality(string name)
        {
            var names = QualitySettings.names;
            for (int i = 0; i < names.Length; i++)
            {
                if (names[i] == name)
                {
                    QualitySettings.SetQualityLevel(i, true);
                    return;
                }
            }

            Debug.LogError($"[Studio] Quality level '{name}' not found. Available: {string.Join(", ", names)}");
        }
    }
}
