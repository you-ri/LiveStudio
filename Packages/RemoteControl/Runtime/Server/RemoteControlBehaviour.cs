// Copyright (c) You-Ri, 2026
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using Lilium.RemoteControl.UI;

namespace Lilium.RemoteControl.Server
{
    /// <summary>
    /// Single MonoBehaviour that owns the full Remote Control runtime: HTTP server,
    /// the ExposedObject container, and scene save/load. Apps register their own
    /// route handlers by subclassing this and overriding the OnRegister*/OnUpdateHandlers hooks.
    /// </summary>
    /// <remarks>
    /// Replaces the four-component combo of <see cref="RemoteControlServerRunner"/>,
    /// <see cref="ExposedObjectContainer"/>, <see cref="RemoteControlProvider"/>, plus the
    /// optional UI add-on.
    /// </remarks>
    [DefaultExecutionOrder(-32760)]
    [ExecuteAlways]
    public class RemoteControlBehaviour : MonoBehaviour
    {
        // --- Serialized configuration ---

        [SerializeField]
        [Tooltip("Server configuration to use")]
        private RemoteControlServerConfig _serverConfig;

        [SerializeReference, Select]
        [ExposedField(persistable = false)]
        public List<IExposedObject> _objects = new List<IExposedObject>();

        // --- Runtime helpers ---

        private ExposedObjectContainer _container;
        private RemoteControlServerRunner _serverRunner;
        private RemoteControlProvider _sceneSave;

        private bool _serverStarted;
        private bool _handlersRegistered;
        private bool _dialogPending;

        // --- Public API ---

        public RemoteControlServerConfig serverConfig => _serverConfig;
        public RemoteControlServerCore server => _serverRunner?.server;
        public ExposedObjectContainer objectContainer => _container;
        public RemoteControlProvider sceneSave => _sceneSave;

        public bool autoSaveOnQuit
        {
            get => _serverConfig != null ? _serverConfig.autoSaveOnQuit : true;
            set
            {
                if (_serverConfig != null) _serverConfig.autoSaveOnQuit = value;
                if (_sceneSave != null) _sceneSave.autoSaveOnQuit = value;
            }
        }

        public string defaultFileName => _serverConfig != null ? _serverConfig.defaultFileName : null;
        public string currentFilePath
        {
            get => _sceneSave?.currentFilePath;
            set { if (_sceneSave != null) _sceneSave.currentFilePath = value; }
        }
        public string currentFullPath => _sceneSave?.currentFullPath;

        // Convenience pass-throughs (callers historically went through RemoteControlProvider).
        public void LoadCurrentData() => _sceneSave?.LoadCurrentData();
        public void LoadCurrentDataFrom(string path) => _sceneSave?.LoadCurrentDataFrom(path);
        public void SaveCurrentData() => _sceneSave?.SaveCurrentData();
        public void SaveCurrentDataTo(string path) => _sceneSave?.SaveCurrentDataTo(path);
        public bool HasUnsavedChanges() => _sceneSave?.HasUnsavedChanges() ?? false;
        public void ClearCurrentData() => _sceneSave?.ClearCurrentData();
        public void RevertAllToDefault() => _sceneSave?.RevertAllToDefault();

        // --- Unity lifecycle ---

        protected virtual void Awake()
        {
            _BuildHelpers();
        }

        protected virtual void OnEnable()
        {
            _BuildHelpers();
            _container.SetName(gameObject.name);
            _container.Initialize();

            if (!Application.isPlaying) return;

            _StartServerAndRegister();

            _sceneSave.OnEnable();

#if UNITY_EDITOR
            UnityEditor.EditorApplication.playModeStateChanged += _OnPlayModeStateChanged;
#else
            Application.wantsToQuit += _OnWantsToQuit;
#endif
        }

        protected virtual void Start()
        {
            // Start runs after all OnEnables of all enabled components, so by now any
            // scene-side targets referenced by ExposedObject items have finished their
            // own Awake/OnEnable. Safe to load the scene JSON.
            if (!Application.isPlaying) return;
            _sceneSave.LoadCurrentData();
        }

        protected virtual void OnDisable()
        {
            if (Application.isPlaying)
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.playModeStateChanged -= _OnPlayModeStateChanged;
#else
                Application.wantsToQuit -= _OnWantsToQuit;
#endif

                _UnregisterHandlersAndStopServer();
                _sceneSave.OnDisable();
            }

            _container?.Shutdown();
        }

        protected virtual void LateUpdate()
        {
            _container?.UpdateObjects();
        }

        protected virtual void Update()
        {
            if (!Application.isPlaying) return;
            if (_handlersRegistered) OnUpdateHandlers();
        }

        protected virtual void OnDestroy()
        {
            if (Application.isPlaying)
                _serverRunner?.ShutdownServer();
        }

        // --- App-level hooks (override to register additional routes) ---

        protected virtual void OnRegisterHandlers(RemoteControlServerCore server) { }
        protected virtual void OnUnregisterHandlers(RemoteControlServerCore server) { }
        protected virtual void OnUpdateHandlers() { }

        /// <summary>
        /// Hook for derived classes to register routes that need the server but should not
        /// be exposed via OnRegisterHandlers (e.g. UI). Called immediately after the server
        /// starts, before <see cref="OnRegisterHandlers"/>.
        /// </summary>
        protected virtual void OnPreRegisterHandlers(RemoteControlServerCore server) { }
        protected virtual void OnPreUnregisterHandlers(RemoteControlServerCore server) { }

        // --- Internals ---

        private void _BuildHelpers()
        {
            if (_container == null)
                _container = new ExposedObjectContainer(gameObject.name, _objects, this);
            if (_serverRunner == null)
                _serverRunner = new RemoteControlServerRunner(_serverConfig, _container);
            if (_sceneSave == null)
                _sceneSave = new RemoteControlProvider(_container, defaultFileName, autoSaveOnQuit);
        }

        private void _StartServerAndRegister()
        {
            if (_serverStarted) return;

            _serverRunner.StartServer();
            var srv = _serverRunner.server;
            if (srv == null) return;

            _serverStarted = true;

            OnPreRegisterHandlers(srv);
            OnRegisterHandlers(srv);
            _handlersRegistered = true;
        }

        private void _UnregisterHandlersAndStopServer()
        {
            var srv = _serverRunner?.server;
            if (_handlersRegistered && srv != null)
            {
                OnUnregisterHandlers(srv);
                OnPreUnregisterHandlers(srv);
                _handlersRegistered = false;
            }
            // Note: server itself stays alive; OnDestroy handles ShutdownServer.
            _serverStarted = false;
        }

        // --- Quit / Play-mode dialog handling ---

#if !UNITY_EDITOR
        private bool _OnWantsToQuit()
        {
            bool hasUnsaved = _sceneSave.HasUnsavedChanges();
            Debug.Log($"[Debug][RemoteControl] wantsToQuit: allowQuit={_sceneSave.allowQuit} " +
                      $"hasUnsaved={hasUnsaved} autoSave={_sceneSave.autoSaveOnQuit} dialogPending={_dialogPending}");

            if (_sceneSave.allowQuit)
            {
                Debug.Log("[Debug][RemoteControl] wantsToQuit -> true (allowQuit)");
                return true;
            }
            if (!hasUnsaved)
            {
                Debug.Log("[Debug][RemoteControl] wantsToQuit -> true (no unsaved changes)");
                return true;
            }

            if (_sceneSave.autoSaveOnQuit)
            {
                _sceneSave.SaveCurrentData();
                _sceneSave.allowQuit = true;
                Debug.Log("[Debug][RemoteControl] wantsToQuit -> true (autoSave)");
                return true;
            }

            // Showing a MessageBox from inside wantsToQuit lets Unity continue tearing down in
            // parallel, so the dialog gets dismissed immediately. Defer one frame via coroutine.
            if (!_dialogPending)
            {
                _dialogPending = true;
                StartCoroutine(_ShowDialogAndQuit());
            }
            Debug.Log("[Debug][RemoteControl] wantsToQuit -> false (dialog pending)");
            return false;
        }

        private IEnumerator _ShowDialogAndQuit()
        {
            yield return null; // exit the wantsToQuit context

            string title = LocalizationSystem.Translate("DIALOG_UNSAVED_CHANGES_TITLE");
            string message = LocalizationSystem.Translate("DIALOG_UNSAVED_CHANGES_MESSAGE");
            string okLabel = LocalizationSystem.Translate("DIALOG_OK");
            string cancelLabel = LocalizationSystem.Translate("DIALOG_CANCEL");

            bool save = ConfirmDialog.Show(title, message, okLabel, cancelLabel);
            Debug.Log($"[Debug][RemoteControl] dialog answered: save={save}, calling Application.Quit() again");
            if (save && !_sceneSave.TrySaveOrPromptRuntime())
            {
                _dialogPending = false;
                yield break;
            }
            _sceneSave.allowQuit = true;
            Application.Quit();
        }
#endif

#if UNITY_EDITOR
        private void _OnPlayModeStateChanged(UnityEditor.PlayModeStateChange state)
        {
            if (state != UnityEditor.PlayModeStateChange.ExitingPlayMode) return;
            if (_sceneSave.allowQuit) return;
            if (!_sceneSave.HasUnsavedChanges()) return;

            _sceneSave.allowQuit = true;

            if (_sceneSave.autoSaveOnQuit)
            {
                _sceneSave.SaveCurrentData();
                return;
            }

            // ExitingPlayMode runs synchronously, so DisplayDialog reliably blocks here.
            bool save = UnityEditor.EditorUtility.DisplayDialog(
                LocalizationSystem.Translate("DIALOG_UNSAVED_CHANGES_TITLE"),
                LocalizationSystem.Translate("DIALOG_UNSAVED_CHANGES_MESSAGE"),
                LocalizationSystem.Translate("DIALOG_OK"),
                LocalizationSystem.Translate("DIALOG_CANCEL"));
            if (save) _sceneSave.TrySaveOrPromptEditor();
        }
#endif

    }
}
