// Copyright (c) You-Ri, 2026
using System.Collections.Generic;
using UnityEngine;

using Lilium.RemoteControl;
using Lilium.RemoteControl.Notification;
using Lilium.RemoteControl.RestApi;
using Lilium.RemoteControl.Server;

namespace Lilium.RemoteControl.LiveScene
{
    /// <summary>
    /// Single MonoBehaviour that owns the full Remote Control runtime: HTTP server,
    /// the ExposedObjectHandle container, and scene save/load. Apps register their own
    /// route handlers by subclassing this and overriding the OnRegister*/OnUpdateHandlers hooks.
    /// </summary>
    /// <remarks>
    /// Replaces the four-component combo of <see cref="RemoteControlServerRunner"/>,
    /// <see cref="ExposedObjectContainer"/>, <see cref="LiveSceneSaveSystem"/>, plus the
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

        [SerializeField]
        [Tooltip("Survive base-scene reloads (DontDestroyOnLoad). The host then owns project-scoped " +
                 "objects that must outlive a live-scene switch; scene-scoped objects live in a " +
                 "RemoteControlContainer in the (reloaded) base scene instead.")]
        private bool _persistAcrossScenes;

        [SerializeReference, Select]
        [ExposedField(persistable = false)]
        public List<IExposedObject> _objects = new List<IExposedObject>();

        // --- Runtime helpers ---

        private ExposedObjectContainer _container;
        private RemoteControlServerRunner _serverRunner;
        private LiveSceneSaveSystem _sceneSave;
        private LiveSceneIoHandler _sceneIoHandler;

        private bool _serverStarted;
        private bool _handlersRegistered;
        private bool _dialogPending;

        // --- Persistence (DontDestroyOnLoad) ---

        // The single surviving persistent host (when _persistAcrossScenes). A reloaded base scene
        // brings a duplicate of this component; the duplicate destroys its own GameObject in Awake.
        private static RemoteControlBehaviour _persistentInstance;

        // True on a duplicate host that lost the singleton race: it must no-op every lifecycle method
        // so it neither starts a second server nor tears the surviving host's state down.
        private bool _isDuplicate;

        // True after a load triggered a base-scene switch and we are waiting for the new scene to finish
        // loading to re-run the deserialize. Only meaningful on the persistent host (no new host Start
        // re-enters the load for us).
        private bool _switchPendingReload;

        /// <summary>
        /// Whether this host survives base-scene reloads. Subclasses can force it on regardless of the
        /// serialized field (e.g. an app host that always owns project-scoped objects).
        /// </summary>
        protected virtual bool persistAcrossScenes => _persistAcrossScenes;

        /// <summary>
        /// Raised by a persistent host immediately after a base-scene switch, before the saved data is
        /// re-deserialized. Project-scoped objects that persist across the switch use this to re-sync
        /// with the new scene's per-scene global state (e.g. RenderSettings, the active Scene handle),
        /// which a reload resets. It fires only on a real base-scene switch on a persistent host — never
        /// when a non-persistent object is simply recreated by the reload — so a handler can re-capture
        /// the new scene's defaults without mistaking its own already-applied state for them.
        /// </summary>
        public static event System.Action onBaseSceneReloaded;

        // Reset statics at runtime startup so disabling Domain Reload does not leak the previous play
        // session's host (a stale reference would make the first host of the new session mistake itself
        // for a duplicate) or its subscribers.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void _ResetPersistentStatics()
        {
            _persistentInstance = null;
            onBaseSceneReloaded = null;
        }

        // --- Public API ---

        public RemoteControlServerConfig serverConfig => _serverConfig;
        public RemoteControlServerCore server => _serverRunner?.server;
        public ExposedObjectContainer objectContainer => _container;
        public LiveSceneSaveSystem sceneSave => _sceneSave;

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
        public bool switchSceneOnLoad => _serverConfig != null ? _serverConfig.switchSceneOnLoad : true;
        public string currentFilePath
        {
            get => _sceneSave?.currentFilePath;
            set { if (_sceneSave != null) _sceneSave.currentFilePath = value; }
        }
        public string currentFullPath => _sceneSave?.currentFullPath;

        // Convenience pass-throughs (callers historically went through LiveSceneSaveSystem).
        // A base-scene switch arms the deferred re-deserialize on a persistent host: the runtime
        // live-scene-open path (LiveSceneManager.LoadScene) flows through LoadCurrentDataFrom here, so
        // the flag must be set here too, not only in Start().
        public void LoadCurrentData()
        {
            bool switched = _sceneSave?.LoadCurrentData() ?? false;
            if (switched && persistAcrossScenes) _switchPendingReload = true;
        }
        public void LoadCurrentDataFrom(string path, bool forceBaseSceneReload = false)
        {
            bool switched = _sceneSave?.LoadCurrentDataFrom(path, forceBaseSceneReload) ?? false;
            if (switched && persistAcrossScenes) _switchPendingReload = true;
        }
        public void SaveCurrentData() => _sceneSave?.SaveCurrentData();
        public void SaveCurrentDataTo(string path) => _sceneSave?.SaveCurrentDataTo(path);
        public bool HasUnsavedChanges() => _sceneSave?.HasUnsavedChanges() ?? false;
        public void ClearCurrentData() => _sceneSave?.ClearCurrentData();
        public void RevertAllToDefault() => _sceneSave?.RevertAllToDefault();
        public void ResetAllToDefault() => _sceneSave?.ResetAllToDefault();

        /// <summary>
        /// Saves to the current path, or opens the platform's "Save As" picker when there is none.
        /// Returns false only when the user cancels that picker.
        /// </summary>
        public bool TrySaveOrPrompt()
        {
            if (_sceneSave == null) return false;
#if UNITY_EDITOR
            return _sceneSave.TrySaveOrPromptEditor();
#else
            return _sceneSave.TrySaveOrPromptRuntime();
#endif
        }

        /// <summary>
        /// Arms the deferred re-deserialize for a base-scene reload that is triggered programmatically
        /// (e.g. New Scene) rather than through <see cref="LoadCurrentData"/>. Without this, a persistent
        /// host's <see cref="_OnSceneLoaded"/> bails out and never fires <see cref="onBaseSceneReloaded"/>,
        /// so project-scoped state (loaded props/avatar, lighting) is left untouched by the reload.
        /// No-op on a non-persistent host (its replacement re-enters the load in Start) and in edit mode.
        /// </summary>
        public void PrepareBaseSceneReload()
        {
            if (persistAcrossScenes && Application.isPlaying) _switchPendingReload = true;
        }

        // --- Unity lifecycle ---

        protected virtual void Awake()
        {
            if (_EnsurePersistenceRegistration()) return; // duplicate destroyed itself
            _BuildHelpers();
        }

        // Registers this host as the surviving persistent instance (DontDestroyOnLoad), or marks it a
        // duplicate to destroy. Returns true when this is a duplicate the caller must bail out of.
        // Idempotent and a no-op in edit mode / when persistence is off.
        //
        // Called from BOTH Awake and OnEnable: with Enter Play Mode Options "Disable Scene Reload" and
        // [ExecuteAlways], Awake only runs in edit mode (isPlaying == false), so on play-enter the
        // registration must happen in OnEnable, which does re-run. Duplicate detection still leans on
        // Awake — a runtime base-scene reload (the only source of a duplicate) runs the full
        // Awake/OnEnable lifecycle, and DestroyImmediate in Awake (before any OnEnable) is what stops
        // the duplicate's sibling RemoteControlContainer from registering colliding objects.
        private bool _EnsurePersistenceRegistration()
        {
            if (!persistAcrossScenes || !Application.isPlaying) return false;
            if (_persistentInstance == this) return false; // already the registered instance

            if (_persistentInstance != null)
            {
                // A reloaded base scene re-instantiated the persistent host. Destroy this duplicate so it
                // never starts a second server or re-registers the project-scoped objects (whose fixed ids
                // would collide with the surviving instance's, then unregister them on teardown). The
                // scene-scoped objects live in a separate base-scene GameObject, so this host carries none
                // to lose.
                _isDuplicate = true;
                DestroyImmediate(gameObject);
                return true;
            }

            _persistentInstance = this;
            DontDestroyOnLoad(gameObject);
            return false;
        }

        protected virtual void OnEnable()
        {
            if (_isDuplicate) return;
            // Play-enter registration when Awake did not re-run (Disable Scene Reload); a no-op when Awake
            // already registered this instance. Bails if this turned out to be a duplicate.
            if (_EnsurePersistenceRegistration()) return;

            _BuildHelpers();
            _container.SetName(gameObject.name);

            // Merge RemoteControlContainers already present (e.g. additively-loaded worlds), then
            // subscribe so containers that enable/disable later stay in sync.
            var containers = RemoteControlContainer.all;
            for (int i = 0; i < containers.Count; i++)
            {
                var c = containers[i];
                if (c != null) _container.AddSource(c._objects, c);
            }
            RemoteControlContainer.onRegistered += _OnContainerRegistered;
            RemoteControlContainer.onUnregistered += _OnContainerUnregistered;

            _container.Initialize();

            if (!Application.isPlaying) return;

            _StartServerAndRegister();

            _sceneSave.OnEnable();

            // The project-directory override that selects the startup-state directory is set by the
            // upper layer only at runtime (BeforeSceneLoad), so an instance built in edit mode (which
            // happens under [ExecuteAlways] when Domain Reload is disabled) resolved its current scene
            // path against the wrong directory. Re-resolve it now that the override is authoritative,
            // before Start() runs the load — otherwise the load falls back to the default and
            // overwrites the project's startup.json, losing the scene to reopen next launch.
            _sceneSave.RefreshCurrentFilePathFromStartupState();

            // A persistent host owns the load flow across base-scene switches: there is no new host in
            // the reloaded scene to re-enter LoadCurrentData, so we re-run it ourselves once the new
            // scene has finished loading (see _OnSceneLoaded).
            if (persistAcrossScenes)
                UnityEngine.SceneManagement.SceneManager.sceneLoaded += _OnSceneLoaded;

            // Gates a quit asked for over REST. In the Editor this is the only cancellable point in
            // that path (ExitingPlayMode is not), so the prompt reaches every surface there too.
            QuitApiHandler.onQuitRequesting += _ShouldAllowQuit;

#if UNITY_EDITOR
            UnityEditor.EditorApplication.playModeStateChanged += _OnPlayModeStateChanged;
#else
            Application.wantsToQuit += _OnWantsToQuit;
#endif
        }

        protected virtual void Start()
        {
            // Start runs after all OnEnables of all enabled components, so by now any
            // scene-side targets referenced by ExposedObjectHandle items have finished their
            // own Awake/OnEnable. Safe to load the scene JSON.
            if (_isDuplicate) return;
            if (!Application.isPlaying) return;

            // The pass-through arms the deferred re-deserialize when a base-scene switch is triggered
            // (persistent host); a non-persistent host is destroyed by the reload and its replacement
            // re-enters Start.
            LoadCurrentData();
        }

        protected virtual void OnDisable()
        {
            if (_isDuplicate) return;

            RemoteControlContainer.onRegistered -= _OnContainerRegistered;
            RemoteControlContainer.onUnregistered -= _OnContainerUnregistered;

            if (persistAcrossScenes)
                UnityEngine.SceneManagement.SceneManager.sceneLoaded -= _OnSceneLoaded;

            if (Application.isPlaying)
            {
                QuitApiHandler.onQuitRequesting -= _ShouldAllowQuit;

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
            if (_persistentInstance == this) _persistentInstance = null;

            if (_isDuplicate) return;

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

        // --- RemoteControlContainer discovery (objects from other scenes) ---

        private void _OnContainerRegistered(RemoteControlContainer container)
        {
            if (container == null) return;
            _container.AddSource(container._objects, container);
            _container.InitializeSource(container);
        }

        // Re-deserialize after a base-scene switch on a persistent host. There is no new host in the
        // reloaded scene to re-enter the load, so the surviving host drives it once the new scene's
        // objects are available.
        private void _OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            // Only a base-scene switch (Single) needs a re-deserialize; additive set bundles keep the
            // active live scene. Act only when a prior load actually requested the switch.
            if (mode != UnityEngine.SceneManagement.LoadSceneMode.Single) return;
            if (!_switchPendingReload) return;
            _switchPendingReload = false;

            // Let persistent project-scoped objects re-sync with the new scene's per-scene global state
            // first (RenderSettings, active Scene handle), while RenderSettings still holds the new
            // scene's authored values — before the deserialize below applies any saved overrides on top.
            onBaseSceneReloaded?.Invoke();

            // The new base scene's RemoteControlContainer registered during its OnEnable (before this
            // callback), so the host container now resolves the new scene's objects. The active scene
            // now matches the saved baseSceneName, so LoadCurrentData deserializes in place without
            // switching again.
            _sceneSave.LoadCurrentData();
        }

        private void _OnContainerUnregistered(RemoteControlContainer container)
        {
            if (container == null) return;
            _container.ShutdownSource(container);
            _container.RemoveSource(container);
        }

        private void _BuildHelpers()
        {
            if (_container == null)
                _container = new ExposedObjectContainer(gameObject.name, _objects, this);
            if (_serverRunner == null)
                _serverRunner = new RemoteControlServerRunner(_serverConfig, _container);
            if (_sceneSave == null)
                _sceneSave = new LiveSceneSaveSystem(_container, defaultFileName, autoSaveOnQuit, switchSceneOnLoad);
        }

        private void _StartServerAndRegister()
        {
            if (_serverStarted) return;

            _serverRunner.StartServer();
            var srv = _serverRunner.server;
            if (srv == null) return;

            _serverStarted = true;

            // Built-in scene import/export handler. Registered here (not via the virtual
            // OnRegisterHandlers hook) so subclasses that override the hook without calling
            // base still get scene I/O. ExposedObjectHandler no longer claims these routes.
            if (_sceneIoHandler == null) _sceneIoHandler = new LiveSceneIoHandler(srv);
            srv.RegisterRoute(_sceneIoHandler);

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
                // UnregisterRoute calls handler.Cleanup() internally.
                srv.UnregisterRoute(_sceneIoHandler);
                _sceneIoHandler = null;
                _handlersRegistered = false;
            }
            // Note: server itself stays alive; OnDestroy handles ShutdownServer.
            _serverStarted = false;
        }

        // --- Quit / Play-mode dialog handling ---

        /// <summary>
        /// Decides whether a quit may proceed, raising the unsaved-changes prompt (on every surface)
        /// and returning false while it is up. The answer restarts the quit itself.
        ///
        /// Two callers share this. In a player, <see cref="Application.wantsToQuit"/> gates every quit.
        /// In the Editor there is no such veto — a remote quit forces play mode off and the resulting
        /// ExitingPlayMode cannot be aborted — so <see cref="QuitApiHandler.onQuitRequesting"/> gates
        /// the remote path before it gets that far, making both behave the same.
        /// </summary>
        private bool _ShouldAllowQuit()
        {
            if (_isDuplicate || _sceneSave == null) return true;

            bool hasUnsaved = _sceneSave.HasUnsavedChanges();
            Debug.Log($"[Debug][RemoteControl] quit gate: allowQuit={_sceneSave.allowQuit} " +
                      $"hasUnsaved={hasUnsaved} autoSave={_sceneSave.autoSaveOnQuit} dialogPending={_dialogPending}");

            if (_sceneSave.allowQuit)
            {
                Debug.Log("[Debug][RemoteControl] quit gate -> true (allowQuit)");
                return true;
            }
            if (!hasUnsaved)
            {
                Debug.Log("[Debug][RemoteControl] quit gate -> true (no unsaved changes)");
                return true;
            }

            if (_sceneSave.autoSaveOnQuit)
            {
                _sceneSave.SaveCurrentData();
                _sceneSave.allowQuit = true;
                Debug.Log("[Debug][RemoteControl] quit gate -> true (autoSave)");
                return true;
            }

            // The prompt goes up on every surface (OS dialog + every connected remote app) and answers
            // back asynchronously, so we abort this quit and let the answer start a new one. Refusing
            // here is also what stops Unity tearing down underneath the prompt.
            if (!_dialogPending)
            {
                _dialogPending = true;
                RemoteConfirmSystem.Ask(RemoteConfirmSystem.Request.UnsavedChanges(), _OnQuitConfirmAnswered);
            }
            Debug.Log("[Debug][RemoteControl] quit gate -> false (dialog pending)");
            return false;
        }

        // Runs on the main thread once any surface answers the unsaved-changes prompt.
        private void _OnQuitConfirmAnswered(RemoteConfirmSystem.Choice choice)
        {
            Debug.Log($"[Debug][RemoteControl] dialog answered: result={choice}");

            switch (choice)
            {
                case RemoteConfirmSystem.Choice.Yes:
                    if (!TrySaveOrPrompt())
                    {
                        // The user cancelled the file picker; abort the quit so the app stays open.
                        _dialogPending = false;
                        return;
                    }
                    break;
                case RemoteConfirmSystem.Choice.No:
                    // Quit without saving.
                    break;
                case RemoteConfirmSystem.Choice.Cancel:
                default:
                    // The user wants to keep the app running; release the dialog gate so the next
                    // quit can show the prompt again.
                    _dialogPending = false;
                    return;
            }

            // allowQuit lets the gate through on the way back in, and (in the Editor) keeps
            // _OnPlayModeStateChanged from asking a second time.
            _sceneSave.allowQuit = true;
            Application.Quit();
#if UNITY_EDITOR
            // Application.Quit() does nothing in play mode; stopping is what actually quits.
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }

#if !UNITY_EDITOR
        private bool _OnWantsToQuit() => _ShouldAllowQuit();
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

            // The one prompt that is NOT mirrored to the remote apps. ExitingPlayMode is synchronous
            // and cannot be aborted, so there is no way to hold play mode open while an asynchronous
            // answer comes back — DisplayDialog blocking right here is the only thing that works.
            // Cancel is omitted for the same reason: we can only ask Save vs Don't Save.
            // Stopping play from a remote app does go through the mirrored path
            // (QuitApiHandler.onQuitRequesting -> _ShouldAllowQuit), which sets allowQuit and returns
            // above, so this is reached only when the developer presses Stop in the Editor.
            bool save = UnityEditor.EditorUtility.DisplayDialog(
                LocalizationSystem.Translate("DIALOG_UNSAVED_CHANGES_TITLE"),
                LocalizationSystem.Translate("DIALOG_UNSAVED_CHANGES_MESSAGE"),
                LocalizationSystem.Translate("DIALOG_SAVE"),
                LocalizationSystem.Translate("DIALOG_DONT_SAVE"));
            if (save) _sceneSave.TrySaveOrPromptEditor();
        }
#endif

    }
}
