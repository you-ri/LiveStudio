// Copyright (c) You-Ri, 2026

using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    [LiveClass(Icon = "settings")]
    public static class LiveSceneManager
    {
        [LiveProperty, Hide]
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
        [LiveProperty, Hide]
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

        [LiveFunction(label = "LIVESCENE_OPEN_SAVE_FOLDER"), Hide]
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

        [LiveFunction(label = "LIVESCENE_SAVE_SCENE"), Hide]
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

        [LiveFunction(label = "LIVESCENE_LOAD_SCENE"), Hide]
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

        /// <summary>True when any host reports edits made since the last load or save.</summary>
        internal static bool HasUnsavedChanges()
        {
            var providers = Object.FindObjectsOfType<Lilium.RemoteControl.LiveScene.RemoteControlBehaviour>();
            foreach (var provider in providers)
            {
                if (provider.HasUnsavedChanges()) return true;
            }
            return false;
        }

        /// <summary>
        /// Saves every host, prompting for a path where one has none. Returns false when the user
        /// cancels a picker, in which case the scene is still unsaved.
        /// </summary>
        internal static bool TrySaveOrPrompt()
        {
            var providers = Object.FindObjectsOfType<Lilium.RemoteControl.LiveScene.RemoteControlBehaviour>();
            foreach (var provider in providers)
            {
                if (!provider.TrySaveOrPrompt()) return false;
            }
            return true;
        }

        /// <summary>
        /// Opens a live scene the way a fresh app launch does, for the project-open path. An empty
        /// <paramref name="filePath"/> opens a new, unsaved scene (the project records no scene yet).
        ///
        /// <see cref="LoadScene"/> / <see cref="NewScene"/> deserialize on top of whatever is currently
        /// live, and only reload the base Unity scene when the file targets a different one — fine
        /// within a project, but switching projects leaks the previous project's state: the scene file
        /// is a delta, so every value it omits keeps the old project's value; loaded props/avatars stay
        /// loaded; and Project-scoped settings live outside the scene file entirely. Resetting to the
        /// captured defaults and forcing the base-scene reload reproduces the startup state.
        /// </summary>
        internal static void OpenProjectScene(string filePath)
        {
            bool isNewScene = string.IsNullOrEmpty(filePath);

            var providers = Object.FindObjectsOfType<Lilium.RemoteControl.LiveScene.RemoteControlBehaviour>();
            foreach (var provider in providers)
            {
                provider.ResetAllToDefault();

                if (isNewScene)
                {
                    provider.currentFilePath = "";
                    provider.PrepareBaseSceneReload();
                }
                else
                {
                    provider.LoadCurrentDataFrom(filePath, forceBaseSceneReload: true);
                }
            }

            // A new scene has no file to read a base scene from, so reload the active one (the load
            // path above drives the reload itself).
            if (isNewScene) _SwitchBaseScene(null);

            // Remote apps show the scene name from scenePath, but a project switch changes it without
            // any request of theirs (and may run long after the OpenProject call returns, once the
            // unsaved-changes confirmation is answered), so push the new value instead of leaving them
            // to poll for it. currentFilePath is already set above; the pending base-scene reload does
            // not change it.
            LivePropertyBroadcast.BroadcastStaticProperty(typeof(LiveSceneManager), nameof(scenePath));
        }

        [LiveFunction(label = "LIVESCENE_NEW_SCENE"), Hide]
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
