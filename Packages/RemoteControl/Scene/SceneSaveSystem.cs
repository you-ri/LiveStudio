// Copyright (c) You-Ri, 2026
using System;
using UnityEngine;
using UnityEngine.SceneManagement;

using Lilium.RemoteControl;
using Lilium.RemoteControl.UI;

namespace Lilium.RemoteControl.Scene
{
    /// <summary>
    /// Pure C# scene save/load helper. Used to be a MonoBehaviour; host
    /// <see cref="RemoteControlBehaviour"/> now drives Unity lifecycle
    /// (wantsToQuit / playModeStateChanged / coroutines for dialogs).
    /// </summary>
    public class SceneSaveSystem
    {
        private const string kSceneFileExtension = ".scene.json";
        private const string kSceneFileDefaultName = "Untitled.scene.json";
        private const string kSceneFileDefaultSubDir = "Virgo Motion/Saved";

        // Fixed PlayerPrefs key that mirrors the absolute path of the most recently used
        // scene file. Used by the BeforeSceneLoad startup hook to read the file without
        // needing access to a per-app defaultFileName (no RemoteControlBehaviour exists yet
        // at that point).
        private const string kLastScenePathKey = "RemoteControl_LastScenePath";

        /// <summary>
        /// Optional directory override for the "Save As" dialog. Set by the upper-level
        /// application at startup via <see cref="SetSaveAsDefaultDirectory"/>.
        /// </summary>
        private static string _saveAsDefaultDirectoryOverride;

        public static void SetSaveAsDefaultDirectory(string absolutePath)
        {
            _saveAsDefaultDirectoryOverride = absolutePath;
        }

        public ExposedObjectContainer objectContainer => _objectContainer;
        public string defaultFileName => _defaultFileName;
        public bool autoSaveOnQuit { get; set; }

        /// <summary>
        /// True after a "let me quit" path has been confirmed. The host's wantsToQuit handler
        /// reads this to bypass the unsaved-changes dialog.
        /// </summary>
        public bool allowQuit { get; set; }

        /// <summary>
        /// Current scene file path. Relative paths resolve against persistentDataPath.
        /// Setter persists the value to PlayerPrefs.
        /// </summary>
        public string currentFilePath
        {
            get => _currentFilePath;
            set
            {
                _currentFilePath = value;
                PlayerPrefs.SetString(_prefsKey, value ?? "");
                // Mirror the absolute path into a fixed key so the BeforeSceneLoad hook
                // can read it on next launch without knowing the per-app defaultFileName.
                var fullPath = string.IsNullOrEmpty(value) ? "" : _ResolvePath(value);
                PlayerPrefs.SetString(kLastScenePathKey, fullPath);
            }
        }

        public string currentFullPath => _ResolvePath(_currentFilePath);

        private readonly ExposedObjectContainer _objectContainer;
        private readonly string _defaultFileName;
        private readonly string _prefsKey;
        private string _currentFilePath;
        private string _baselineJson;

        public event Action onResetDataRequested;

        public SceneSaveSystem(ExposedObjectContainer objectContainer, string defaultFileName, bool autoSaveOnQuit = true)
        {
            _objectContainer = objectContainer;
            _defaultFileName = defaultFileName ?? string.Empty;
            this.autoSaveOnQuit = autoSaveOnQuit;
            _prefsKey = "RemoteControl_ScenePath_" + _defaultFileName;
            _currentFilePath = PlayerPrefs.GetString(_prefsKey, _defaultFileName);
        }

        // --- Startup hook ---

        /// <summary>
        /// Runs before the first scene is loaded. If the most recently used scene file
        /// (mirrored to <see cref="kLastScenePathKey"/>) targets a different Unity scene
        /// than the one Unity is about to load, redirect to that scene up-front.
        /// This avoids switching scenes after the HTTP server has already started, which
        /// would race with in-flight requests and produce ObjectDisposedException noise.
        /// All error paths are silent: fall through to the normal startup scene.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void _SwitchBaseSceneOnStartup()
        {
            if (!Application.isPlaying) return;

            var fullPath = PlayerPrefs.GetString(kLastScenePathKey, "");
            if (string.IsNullOrEmpty(fullPath)) return;
            if (!System.IO.File.Exists(fullPath)) return;

            string baseSceneName;
            try
            {
                var json = System.IO.File.ReadAllText(fullPath);
                baseSceneName = ExposedSceneSerializer.ExtractBaseSceneName(json);
            }
            catch
            {
                return;
            }
            if (string.IsNullOrEmpty(baseSceneName)) return;

            int count = SceneManager.sceneCountInBuildSettings;
            if (count == 0) return;

            // Skip if the scene Unity is about to load (build index 0 among enabled scenes)
            // already matches the saved baseSceneName.
            var initialName = System.IO.Path.GetFileNameWithoutExtension(SceneUtility.GetScenePathByBuildIndex(0));
            if (initialName == baseSceneName) return;

            for (int i = 0; i < count; i++)
            {
                var path = SceneUtility.GetScenePathByBuildIndex(i);
                if (System.IO.Path.GetFileNameWithoutExtension(path) == baseSceneName)
                {
                    SceneManager.LoadScene(i);
                    return;
                }
            }
        }

        // --- Lifecycle (called by host MonoBehaviour) ---

        public void OnEnable()
        {
            RemoteControlService.onResetData += _OnResetData;
        }

        public void OnDisable()
        {
            RemoteControlService.onResetData -= _OnResetData;
        }

        private void _OnResetData()
        {
            ClearCurrentData();
            // The data has been wiped; the host should be allowed to quit without a dialog.
            allowQuit = true;
            onResetDataRequested?.Invoke();
            Application.Quit();
        }

        // --- Save / Load ---

        public void LoadCurrentData()
        {
            string fullPath;
            if (_currentFilePath != _defaultFileName && System.IO.File.Exists(currentFullPath))
            {
                fullPath = currentFullPath;
            }
            else
            {
                currentFilePath = _defaultFileName;
                fullPath = _ResolvePath(_defaultFileName);
            }

            // If the file targets a different Unity base scene than the one currently active,
            // switch first and let the new scene's RemoteControlBehaviour.Start() re-enter
            // LoadCurrentData(). This unifies the order "read JSON -> switch scene -> deserialize"
            // for both startup loads (PlayerPrefs-backed) and explicit LoadScene calls.
            if (_TrySwitchBaseScene(fullPath)) return;

            _LoadFrom(fullPath);
        }

        public void LoadCurrentDataFrom(string filePath)
        {
            if (_objectContainer == null) return;

            currentFilePath = filePath;
            // Delegate to LoadCurrentData so the base-scene switch path is exercised
            // identically to the startup load path.
            LoadCurrentData();
        }

        /// <summary>
        /// Reads <c>baseSceneName</c> from the file. If it differs from the active Unity scene
        /// and is registered in build settings, calls <see cref="SceneManager.LoadScene(int)"/>
        /// and returns <c>true</c>; the new scene's RemoteControlBehaviour will re-trigger the load.
        /// Returns <c>false</c> when no switch is needed (legacy file, same scene, or scene not in build).
        /// </summary>
        private static bool _TrySwitchBaseScene(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath) || !System.IO.File.Exists(fullPath)) return false;
            string json;
            try
            {
                json = System.IO.File.ReadAllText(fullPath);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RemoteControl] Failed to read '{fullPath}' for base-scene check: {ex.Message}");
                return false;
            }

            var baseSceneName = ExposedSceneSerializer.ExtractBaseSceneName(json);
            if (string.IsNullOrEmpty(baseSceneName)) return false;
            if (baseSceneName == SceneManager.GetActiveScene().name) return false;

            int count = SceneManager.sceneCountInBuildSettings;
            for (int i = 0; i < count; i++)
            {
                var path = SceneUtility.GetScenePathByBuildIndex(i);
                if (System.IO.Path.GetFileNameWithoutExtension(path) == baseSceneName)
                {
                    SceneManager.LoadScene(i);
                    return true;
                }
            }

            Debug.LogWarning($"[RemoteControl] Base scene '{baseSceneName}' not found in build settings. Loading data into current active scene.");
            return false;
        }

        public void SaveCurrentData()
        {
            if (string.IsNullOrEmpty(_currentFilePath))
            {
                Debug.LogWarning("[RemoteControl] SaveCurrentData: current file path is empty. Use SaveCurrentDataTo or set currentFilePath first.");
                return;
            }
            SaveCurrentDataTo(_currentFilePath);
        }

        public void SaveCurrentDataTo(string filePath)
        {
            if (_objectContainer == null) return;
            if (string.IsNullOrEmpty(filePath))
            {
                Debug.LogWarning("[RemoteControl] SaveCurrentDataTo: empty file path.");
                return;
            }

            currentFilePath = filePath;

            var fullPath = _ResolvePath(filePath);
            var dir = System.IO.Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                System.IO.Directory.CreateDirectory(dir);

            var baseSceneName = SceneManager.GetActiveScene().name;
            var json = ExposedSceneSerializer.BuildSceneJson(_objectContainer, baseSceneName);
            System.IO.File.WriteAllText(fullPath, json);
            _baselineJson = json;
        }

        public bool HasUnsavedChanges()
            => ExposedSceneSerializer.HasChanges(_objectContainer, _baselineJson);

        public void ClearCurrentData()
        {
            var fullPath = _ResolvePath(_currentFilePath);
            _currentFilePath = _defaultFileName;
            PlayerPrefs.DeleteKey(_prefsKey);
            _baselineJson = null;

            if (System.IO.File.Exists(fullPath))
                System.IO.File.Delete(fullPath);
        }

        /// <summary>
        /// Reverts every dirty property in every contained ExposedObject back to its captured
        /// default. Used at editor Play-mode exit to restore ScriptableObject state.
        /// </summary>
        public void RevertAllToDefault()
        {
            if (_objectContainer == null) return;

            var objects = ExposedObjectGraph.ResolveExposedObjects(
                _objectContainer.objects, _objectContainer);

            foreach (var obj in objects)
            {
                var dirtyProps = obj.GetDirtyProperties();
                if (dirtyProps.Count == 0) continue;

                // Use FromJson rather than Revert(path) because FromJson uses SetValueRaw,
                // which can write through read-only component arrays containing
                // ScriptableObject values.
                var defaultJson = ExposedObjectDefaultRegistry.GetDefaults(obj);
                if (defaultJson != null)
                {
                    ExposedPropertySerializer.FromJson(
                        defaultJson.ToString(), obj, _objectContainer, captureDefaults: false);
                }
            }
        }

        /// <summary>
        /// Saves to the current path if one is set; otherwise opens a "Save As" dialog.
        /// Returns true on success or false if the user cancels.
        /// Build / runtime version (uses native dialogs).
        /// </summary>
        public bool TrySaveOrPromptRuntime()
        {
            if (!string.IsNullOrEmpty(_currentFilePath))
            {
                SaveCurrentData();
                return true;
            }

            string savePath = SaveFileDialog.Show(
                title: LocalizationSystem.Translate("DIALOG_SAVE_AS_TITLE"),
                initialDirectory: GetSaveAsDefaultDirectory(),
                defaultFileName: _GetSaveAsDefaultName(),
                extension: kSceneFileExtension);

            if (string.IsNullOrEmpty(savePath))
            {
                Debug.Log("[Debug][RemoteControl] Save As dialog cancelled");
                return false;
            }

            SaveCurrentDataTo(savePath);
            return true;
        }

#if UNITY_EDITOR
        /// <summary>
        /// Saves to the current path if one is set; otherwise opens the editor's Save File panel.
        /// Returns true on success or false if the user cancels.
        /// </summary>
        public bool TrySaveOrPromptEditor()
        {
            if (!string.IsNullOrEmpty(_currentFilePath))
            {
                SaveCurrentData();
                return true;
            }

            string savePath = UnityEditor.EditorUtility.SaveFilePanel(
                LocalizationSystem.Translate("DIALOG_SAVE_AS_TITLE"),
                GetSaveAsDefaultDirectory(),
                _GetSaveAsDefaultName(),
                kSceneFileExtension.TrimStart('.'));
            if (string.IsNullOrEmpty(savePath))
            {
                Debug.Log("[Debug][RemoteControl] Save As dialog cancelled (Editor)");
                return false;
            }
            // EditorUtility.SaveFilePanel only auto-completes a single extension, so we add the
            // compound suffix manually.
            if (!savePath.EndsWith(kSceneFileExtension, StringComparison.OrdinalIgnoreCase))
                savePath += kSceneFileExtension;
            SaveCurrentDataTo(savePath);
            return true;
        }
#endif

        private string _GetSaveAsDefaultName()
        {
            if (!string.IsNullOrEmpty(_defaultFileName)) return _defaultFileName;
            return kSceneFileDefaultName;
        }

        /// <summary>
        /// Default directory for the "Save As" dialog. Honors a directory registered via
        /// <see cref="SetSaveAsDefaultDirectory"/>; otherwise falls back to MyDocuments/Virgo Motion/Saved.
        /// Creates the directory if it doesn't exist.
        /// </summary>
        public static string GetSaveAsDefaultDirectory()
        {
            string dir;
            string fallback;
            if (!string.IsNullOrEmpty(_saveAsDefaultDirectoryOverride))
            {
                dir = _saveAsDefaultDirectoryOverride;
                fallback = _saveAsDefaultDirectoryOverride;
            }
            else
            {
                var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                if (string.IsNullOrEmpty(docs))
                    return Application.persistentDataPath;
                dir = System.IO.Path.Combine(docs, kSceneFileDefaultSubDir);
                fallback = docs;
            }

            try
            {
                if (!System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RemoteControl] Failed to create default save directory '{dir}': {ex.Message}");
                return fallback;
            }
            return dir;
        }

        private string _ResolvePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return System.IO.Path.Combine(Application.persistentDataPath, _defaultFileName);

            if (System.IO.Path.IsPathRooted(path))
                return path;

            return System.IO.Path.Combine(Application.persistentDataPath, path);
        }

        private void _LoadFrom(string fullPath)
        {
            if (_objectContainer == null) return;

            if (System.IO.File.Exists(fullPath))
            {
                var json = System.IO.File.ReadAllText(fullPath);
                ExposedSceneSerializer.SceneFromJson(json, _objectContainer);
            }

            // Cache the post-load state as the baseline for HasUnsavedChanges. The baseline
            // must use the same baseSceneName the next save will write so an unchanged scene
            // does not appear dirty on round-trip.
            _baselineJson = ExposedSceneSerializer.BuildSceneJson(_objectContainer, SceneManager.GetActiveScene().name);
        }
    }
}
