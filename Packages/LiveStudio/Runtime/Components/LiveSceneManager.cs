// Copyright (c) You-Ri, 2026

using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    [ExposedClass(Icon = "settings")]
    public static class LiveSceneManager
    {
        [ExposedProperty, Hide]
        public static string scenePath
        {
            get
            {
#if UNITY_2022_3_OR_NEWER
                var provider = Object.FindFirstObjectByType<Lilium.RemoteControl.LiveScene.RemoteControlBehaviour>();
#else
                var provider = Object.FindObjectOfType<Lilium.RemoteControl.LiveScene.RemoteControlBehaviour>();
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

        [ExposedFunction(label = "LIVESCENE_OPEN_SAVE_FOLDER"), Hide]
        public static void OpenSaveFolder()
        {
            // 現在開いているプロジェクトフォルダ (既定保存先) を開く。未オープン時はベースディレクトリ。
            var path = !string.IsNullOrEmpty(ProjectManager.projectPath)
                ? ProjectManager.projectPath
                : SavedPaths.baseDirectory;
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

        [ExposedFunction(label = "LIVESCENE_SAVE_SCENE"), Hide]
        public static void SaveScene(string filePath = null)
        {
            var providers = Object.FindObjectsOfType<Lilium.RemoteControl.LiveScene.RemoteControlBehaviour>();
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

        [ExposedFunction(label = "LIVESCENE_LOAD_SCENE"), Hide]
        public static void LoadScene(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return;

            // 起動時 (RemoteControlBehaviour.Start → LoadCurrentData) と同じパスを通すため、
            // ここでは provider に filePath だけ伝え、シーン切替/デシリアライズの判断は
            // provider 内部 (LoadCurrentData → _TrySwitchBaseScene → _LoadFrom) に委ねる。
            var providers = Object.FindObjectsOfType<Lilium.RemoteControl.LiveScene.RemoteControlBehaviour>();
            foreach (var provider in providers)
            {
                provider.LoadCurrentDataFrom(filePath);
            }
        }

        [ExposedFunction(label = "LIVESCENE_NEW_SCENE"), Hide]
        public static void NewScene(string sceneName = null)
        {
            var providers = Object.FindObjectsOfType<Lilium.RemoteControl.LiveScene.RemoteControlBehaviour>();
            foreach (var provider in providers)
            {
                provider.currentFilePath = "";
                // 空の新規シーンには上書きするファイルが無いので、ロード済みの prop/avatar や
                // camera 等の exposed 値が前のシーンのまま残る。明示的に既定へ戻し（per-asset の
                // enabled も false に戻るので _ApplyDiff が再ロードしない）、ベースシーン再ロードで
                // onBaseSceneReloaded を発火させて GameObject の破棄と project-scoped 状態の再同期を行う。
                provider.RevertAllToDefault();
                provider.PrepareBaseSceneReload();
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
    }
}
