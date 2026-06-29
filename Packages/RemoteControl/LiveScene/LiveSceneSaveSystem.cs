// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

using Lilium.RemoteControl;
using Lilium.RemoteControl.Dialogs;
using Lilium.RemoteControl.Notification;

namespace Lilium.RemoteControl.LiveScene
{
    /// <summary>
    /// Pure C# scene save/load helper. Used to be a MonoBehaviour; host
    /// <see cref="RemoteControlBehaviour"/> now drives Unity lifecycle
    /// (wantsToQuit / playModeStateChanged / coroutines for dialogs).
    /// </summary>
    public class LiveSceneSaveSystem
    {
        // New live scene files are written with this extension. Loading is extension-agnostic
        // (the file content is read regardless), so legacy ".scene.json" files still open; only
        // the "Save As" default and the editor extension auto-completion use the new value.
        private const string kLiveSceneFileExtension = ".live.json";
        private const string kLegacyFileExtension = ".scene.json";
        private const string kLiveSceneFileDefaultName = "Untitled.live.json";
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

        /// <summary>
        /// Project folder used to locate the per-project startup state file
        /// (<c>{projectPath}/Settings/startup.json</c>). Set by the upper-level application
        /// whenever a project is opened (see project manager). When unset (e.g. RemoteControl
        /// used standalone without a project manager), the state falls back to
        /// <see cref="Application.persistentDataPath"/>.
        /// </summary>
        private static string _stateProjectDirectoryOverride;

        public static void SetStateProjectDirectory(string projectPath)
        {
            _stateProjectDirectoryOverride = projectPath;
        }

        private static string _StateDir() => _stateProjectDirectoryOverride ?? Application.persistentDataPath;

        /// <summary>
        /// True if the path is a live scene file (current ".live.json" or legacy ".scene.json").
        /// Lets a project crawler classify files by path alone, without reading their contents.
        /// </summary>
        public static bool IsLiveSceneFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return false;
            return filePath.EndsWith(kLiveSceneFileExtension, StringComparison.OrdinalIgnoreCase)
                || filePath.EndsWith(kLegacyFileExtension, StringComparison.OrdinalIgnoreCase);
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
        /// Setter persists the value to the per-project startup state file.
        /// </summary>
        public string currentFilePath
        {
            get => _currentFilePath;
            set
            {
                _currentFilePath = value;
                // Record the absolute path into the project's startup state so the next launch
                // (and the BeforeSceneLoad hook) can reopen it. When scene switching is disabled,
                // write empty so the static startup hook bails out and never switches.
                var fullPath = string.IsNullOrEmpty(value) ? "" : _ResolvePath(value);
                StartupStateStore.Write(_StateDir(), _switchSceneOnLoad ? fullPath : "");
            }
        }

        public string currentFullPath => _ResolvePath(_currentFilePath);

        private readonly ExposedObjectContainer _objectContainer;
        private readonly string _defaultFileName;
        private readonly bool _switchSceneOnLoad;
        private string _currentFilePath;
        private string _baselineJson;

        public event Action onResetDataRequested;

        public LiveSceneSaveSystem(ExposedObjectContainer objectContainer, string defaultFileName, bool autoSaveOnQuit = true, bool switchSceneOnLoad = true)
        {
            _objectContainer = objectContainer;
            _defaultFileName = defaultFileName ?? string.Empty;
            this.autoSaveOnQuit = autoSaveOnQuit;
            _switchSceneOnLoad = switchSceneOnLoad;
            // Restore the current scene from the project's startup state. When none is recorded,
            // fall back to the default; a one-time migration imports the legacy PlayerPrefs value.
            _currentFilePath = StartupStateStore.Read(_StateDir())
                ?? _MigrateLegacyScenePath()
                ?? _defaultFileName;
        }

        /// <summary>
        /// Re-resolves <see cref="currentFilePath"/> from the project's startup state, mirroring the
        /// constructor's resolution. Called by the host at play start.
        ///
        /// The state directory is selected by <see cref="_stateProjectDirectoryOverride"/>, which the
        /// upper layer sets only at runtime (a RuntimeInitializeOnLoadMethod that never runs in edit
        /// mode). With "Enter Play Mode Options" disabling Domain Reload, the [ExecuteAlways] host
        /// builds this instance in edit mode, where the override is still unset, so the constructor
        /// read the wrong directory and defaulted the path. That stale instance survives into play, so
        /// without this refresh <see cref="LoadCurrentData"/> would fall back to the default and
        /// overwrite the project's startup.json — losing the scene to reopen on the next launch.
        /// </summary>
        public void RefreshCurrentFilePathFromStartupState()
        {
            _currentFilePath = StartupStateStore.Read(_StateDir()) ?? _defaultFileName;
        }

        // One-time migration of the pre-startup.json PlayerPrefs state. Earlier builds stored the
        // current scene under "RemoteControl_ScenePath_<defaultFileName>" (relative) and mirrored
        // the absolute path to "RemoteControl_LastScenePath". If startup.json has no scene yet but a
        // legacy value exists, adopt it, write it into startup.json, and delete the legacy keys so
        // the migration runs only once. Returns the migrated path, or null when nothing to migrate.
        private string _MigrateLegacyScenePath()
        {
            const string kLegacyPrefsPrefix = "RemoteControl_ScenePath_";
            const string kLegacyLastScenePathKey = "RemoteControl_LastScenePath";

            var legacyKey = kLegacyPrefsPrefix + _defaultFileName;
            var legacy = PlayerPrefs.GetString(legacyKey, "");
            // No usable legacy value (missing, or just the default the constructor would pick anyway).
            if (string.IsNullOrEmpty(legacy) || legacy == _defaultFileName)
            {
                PlayerPrefs.DeleteKey(legacyKey);
                PlayerPrefs.DeleteKey(kLegacyLastScenePathKey);
                return null;
            }

            var fullPath = _ResolvePath(legacy);
            StartupStateStore.Write(_StateDir(), _switchSceneOnLoad ? fullPath : "");
            PlayerPrefs.DeleteKey(legacyKey);
            PlayerPrefs.DeleteKey(kLegacyLastScenePathKey);
            PlayerPrefs.Save();
            return legacy;
        }

        // The startup base-scene switch (a BeforeSceneLoad hook that redirects to the live scene
        // recorded in startup.json) lives in the upper LiveStudio layer (StartupSceneSwitcher),
        // because locating the state directory requires the project-path knowledge that this
        // generic layer deliberately does not own.

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

        /// <summary>
        /// Loads the current scene file. Returns <c>true</c> when a base-scene switch was triggered
        /// (the deserialize is then deferred until the new scene loads), <c>false</c> when the data was
        /// deserialized in place.
        /// </summary>
        public bool LoadCurrentData()
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

            // If the file targets a different Unity base scene than the one currently active, switch
            // first. The deserialize is deferred until the new scene loads: a non-persistent host is
            // recreated by the reload and re-enters this in its Start(); a persistent host re-enters it
            // from its sceneLoaded handler. This unifies the order "read JSON -> switch scene ->
            // deserialize" for both startup loads (startup.json-backed) and explicit LoadScene calls.
            // When switching is disabled, skip the switch but still deserialize the saved property
            // values into the current scene.
            if (_switchSceneOnLoad && _TrySwitchBaseScene(fullPath)) return true;

            _LoadFrom(fullPath);
            return false;
        }

        /// <summary>
        /// Sets <see cref="currentFilePath"/> then loads it. Returns <c>true</c> when a base-scene
        /// switch was triggered (see <see cref="LoadCurrentData"/>).
        /// </summary>
        public bool LoadCurrentDataFrom(string filePath)
        {
            if (_objectContainer == null) return false;

            currentFilePath = filePath;
            // Delegate to LoadCurrentData so the base-scene switch path is exercised
            // identically to the startup load path.
            return LoadCurrentData();
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

            var baseSceneName = LiveSceneSerializer.ExtractBaseSceneName(json);
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

            var baseSceneName = _ResolveBaseSceneName();
            var json = LiveSceneSerializer.BuildLiveSceneJson(_objectContainer, baseSceneName);
            System.IO.File.WriteAllText(fullPath, json);
            _baselineJson = json;

            // Project scope のメンバーは live.json から除外済みなので、ここで
            // {projectPath}/Settings/{クラス名}.settings.json へ別途書き出す。
            _SaveProjectSettings();

            Debug.Log($"[RemoteControl] Live scene saved to '{fullPath}'");

            // Let connected remote apps know the save completed (shown as a toast there).
            RemoteNotificationSystem.Show(
                LocalizationSystem.Translate("NOTIFY_LIVESCENE_SAVED"),
                RemoteNotificationSystem.Type.Success,
                icon: "save");
        }

        // Writes every Project-scoped member into per-class {projectPath}/Settings/{ClassName}.settings.json.
        // Resolves the same object set BuildLiveSceneJson uses (main container + merged sources).
        private void _SaveProjectSettings()
        {
            if (_objectContainer == null) return;
            var projectPath = _stateProjectDirectoryOverride;
            if (string.IsNullOrEmpty(projectPath)) return;

            var allObjects = new List<IExposedObject>(_objectContainer.EnumerateAllObjects());
            var objects = ExposedObjectGraph.ResolveExposedObjects(allObjects, _objectContainer);
            var perClass = ProjectSettingsSerializer.BuildPerClassJson(objects, _objectContainer);
            ProjectSettingsStore.WriteAll(projectPath, perClass);
        }

        public bool HasUnsavedChanges()
            => LiveSceneSerializer.HasChanges(_objectContainer, _baselineJson);

        public void ClearCurrentData()
        {
            var fullPath = _ResolvePath(_currentFilePath);
            _currentFilePath = _defaultFileName;
            StartupStateStore.Delete(_StateDir());
            _baselineJson = null;

            if (System.IO.File.Exists(fullPath))
                System.IO.File.Delete(fullPath);
        }

        /// <summary>
        /// Reverts every dirty property in every contained ExposedObjectHandle back to its captured
        /// default. Used at editor Play-mode exit to restore ScriptableObject state.
        /// </summary>
        public void RevertAllToDefault()
        {
            if (_objectContainer == null) return;

            var objects = ExposedObjectGraph.ResolveExposedObjects(
                new List<IExposedObject>(_objectContainer.EnumerateAllObjects()), _objectContainer);

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
                extension: kLiveSceneFileExtension);

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
                kLiveSceneFileExtension.TrimStart('.'));
            if (string.IsNullOrEmpty(savePath))
            {
                Debug.Log("[Debug][RemoteControl] Save As dialog cancelled (Editor)");
                return false;
            }
            // EditorUtility.SaveFilePanel only auto-completes a single extension, so we add the
            // compound suffix manually. A legacy ".scene.json" name (overwriting an existing file)
            // is kept as-is; otherwise the new ".live.json" extension is appended.
            if (!savePath.EndsWith(kLiveSceneFileExtension, StringComparison.OrdinalIgnoreCase)
                && !savePath.EndsWith(kLegacyFileExtension, StringComparison.OrdinalIgnoreCase))
                savePath += kLiveSceneFileExtension;
            SaveCurrentDataTo(savePath);
            return true;
        }
#endif

        private string _GetSaveAsDefaultName()
        {
            if (!string.IsNullOrEmpty(_defaultFileName)) return _defaultFileName;
            return kLiveSceneFileDefaultName;
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

            string fileJson = null;
            if (System.IO.File.Exists(fullPath))
            {
                fileJson = System.IO.File.ReadAllText(fullPath);
                LiveSceneSerializer.LiveSceneFromJson(fileJson, _objectContainer);
            }

            // Project scope のメンバーは scene ファイルに含まれないため、シーン適用後に
            // {projectPath}/Settings/*.settings.json を冪等適用する。シーンロードのたびに走るので
            // シーン切替を跨いで Project 設定が保たれる。Scene/Project はキーが互いに素なので順序は無害。
            if (!string.IsNullOrEmpty(_stateProjectDirectoryOverride))
            {
                ProjectSettingsStore.ApplyAll(_stateProjectDirectoryOverride, _objectContainer);
            }

            // Use the loaded file's content verbatim as the dirty baseline. The file was written by a
            // previous BuildLiveSceneJson save and already holds the COMPLETE saved state — including
            // entries for assets (e.g. props) that finish loading asynchronously after this point.
            // Re-snapshotting the live container here would capture it before those async loads complete,
            // so the quit-time snapshot would always look "more populated" and falsely report unsaved
            // changes (the dialog appeared on every launch→quit, even right after a clean save).
            // HasChanges tolerates version / formatting differences, so comparing against the raw file is
            // safe. When no file exists (a brand-new scene), fall back to a freshly built empty baseline.
            _baselineJson = fileJson
                ?? LiveSceneSerializer.BuildLiveSceneJson(_objectContainer, _ResolveBaseSceneName());
        }

        // A Unity scene counts as a "base scene" only when it is registered in build settings.
        // Scenes loaded from AssetBundles (.set.lsb sets) have buildIndex == -1 and must
        // never be recorded as baseSceneName — sets are calibrated on top of a bundled base.
        private static bool _IsBuildScene(Scene scene)
            => scene.buildIndex >= 0 && scene.buildIndex < SceneManager.sceneCountInBuildSettings;

        // Resolve the baseSceneName to persist. Prefer the active scene when it is a build scene;
        // otherwise fall back to the first loaded build scene (the active scene is an additive
        // world bundle on top of it). Returns null when no build scene is loaded, in which case
        // the serializer omits the field.
        private static string _ResolveBaseSceneName()
        {
            var active = SceneManager.GetActiveScene();
            if (_IsBuildScene(active)) return active.name;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (s.isLoaded && _IsBuildScene(s)) return s.name;
            }
            return null;
        }
    }
}
