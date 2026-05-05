// Copyright (c) You-Ri, 2026
using System;
using UnityEngine;

using Lilium.RemoteControl.UI;

namespace Lilium.RemoteControl
{
    /// <summary>
    /// Pure C# scene save/load helper. Used to be a MonoBehaviour; host
    /// <see cref="Lilium.RemoteControl.Server.RemoteControlBehaviour"/> now drives Unity lifecycle
    /// (wantsToQuit / playModeStateChanged / coroutines for dialogs).
    /// </summary>
    public class RemoteControlProvider
    {
        private const string kSceneFileExtension = ".scene.json";
        private const string kSceneFileDefaultName = "Untitled.scene.json";
        private const string kSceneFileDefaultSubDir = "Virgo Motion/Saved";

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
            }
        }

        public string currentFullPath => _ResolvePath(_currentFilePath);

        private readonly ExposedObjectContainer _objectContainer;
        private readonly string _defaultFileName;
        private readonly string _prefsKey;
        private string _currentFilePath;
        private string _baselineJson;

        public event Action onResetDataRequested;

        public RemoteControlProvider(ExposedObjectContainer objectContainer, string defaultFileName, bool autoSaveOnQuit = true)
        {
            _objectContainer = objectContainer;
            _defaultFileName = defaultFileName ?? string.Empty;
            this.autoSaveOnQuit = autoSaveOnQuit;
            _prefsKey = "RemoteControl_ScenePath_" + _defaultFileName;
            _currentFilePath = PlayerPrefs.GetString(_prefsKey, _defaultFileName);
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
            var fullPath = currentFullPath;
            if (_currentFilePath != _defaultFileName && System.IO.File.Exists(fullPath))
            {
                _LoadFrom(fullPath);
                return;
            }
            currentFilePath = _defaultFileName;
            _LoadFrom(_ResolvePath(_defaultFileName));
        }

        public void LoadCurrentDataFrom(string filePath)
        {
            if (_objectContainer == null) return;

            currentFilePath = filePath;
            _LoadFrom(_ResolvePath(filePath));
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

            var json = ExposedSceneSerializer.BuildSceneJson(_objectContainer);
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

            var objects = ExposedSceneSerializer.ResolveExposedObjects(
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

            // Cache the post-load state as the baseline for HasUnsavedChanges.
            _baselineJson = ExposedSceneSerializer.BuildSceneJson(_objectContainer);
        }
    }
}
