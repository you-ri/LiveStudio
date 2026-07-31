// Copyright (c) You-Ri, 2026

using UnityEditor;
// MainToolbarButton (6.3+) and EditorToolbarButton (2021.2+) both live here.
using UnityEditor.Toolbars;
using UnityEngine;
using UnityEngine.UIElements;

using Lilium.RemoteControl.LiveScene;
using Lilium.RemoteControl.Server;

namespace Lilium.RemoteControl.Editor
{
    /// <summary>
    /// Start/stop of this project's Remote Control server from the editor toolbar, mirroring the
    /// Unreal editor's server toggle (<c>FLiliumRemoteControlEditorModule</c>). The server runs on its
    /// own threads, so it serves the remote app just as well with the editor merely open as it does
    /// in play mode - which is exactly what this button is for.
    /// </summary>
    [InitializeOnLoad]
    static class RemoteControlServerToggle
    {
        static bool _running;

        /// <summary>Raised on the main thread whenever <see cref="IsRunning"/> changes.</summary>
        public static event System.Action stateChanged;

        /// <summary>True while any server registered with <see cref="RemoteControlServerManager"/> is listening.</summary>
        public static bool IsRunning => RemoteControlServerManager.IsAnyServerRunning();

        /// <summary>
        /// True while the running application owns the server, which is when this toggle only reports.
        /// </summary>
        public static bool IsOwnedByPlayMode => EditorApplication.isPlayingOrWillChangePlaymode;

        static RemoteControlServerToggle()
        {
            // Play mode and the editor auto-start bring the server up without going through this
            // toggle, so the state is polled rather than reported. The check is a walk over a handful
            // of dictionary entries and allocates nothing.
            _running = IsRunning;
            EditorApplication.update -= _Poll;
            EditorApplication.update += _Poll;

            // Entering/leaving play mode changes who owns the server, which the button reports even
            // when the running state itself does not change.
            EditorApplication.playModeStateChanged -= _OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += _OnPlayModeStateChanged;
        }

        public static void Toggle()
        {
            // RemoteControlBehaviour starts the server on play and registers the app's own routes on
            // it (avatar, camera, ...). Those routes are lost with the server instance and nothing
            // here can put them back, so a stop/start from the toolbar would leave the running app
            // with a half-wired server. Stay an indicator instead.
            if (IsOwnedByPlayMode)
            {
                Debug.Log("[RemoteControl] The running application owns the server while in play mode. " +
                          "Stop play mode to start or stop it from the toolbar.");
                return;
            }

            // Stopping tears the server down rather than parking it: HttpServerCore.StopServer cleans
            // up every registered route, so a kept instance would come back up answering nothing.
            if (IsRunning) RemoteControlServerManager.RemoveAllServers();
            else _Start();

            // Report the new state now instead of leaving the button stale until the next editor frame.
            _Poll();
        }

        /// <summary>The port of a listening server, or -1 when none is. For the tooltip only.</summary>
        public static int GetRunningPort()
        {
            foreach (var pair in RemoteControlServerManager.servers)
            {
                if (pair.Value?.server?.IsRunning == true) return pair.Key;
            }
            return -1;
        }

        static void _Poll()
        {
            bool running = IsRunning;
            if (running == _running) return;

            _running = running;
            stateChanged?.Invoke();
        }

        static void _OnPlayModeStateChanged(PlayModeStateChange state)
        {
            stateChanged?.Invoke();
        }

        static void _Start()
        {
            bool anyRunning = false;

            // The hosts in the loaded scenes come first: their config is the one the app really
            // serves, and their container is what makes an editor-mode server worth talking to
            // (RemoteControlBehaviour registers its live objects in edit mode as well - it only skips
            // starting the server there).
            var hosts = UnityEngine.Object.FindObjectsByType<RemoteControlBehaviour>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < hosts.Length; i++)
            {
                anyRunning |= _StartFor(hosts[i].serverConfig, hosts[i].objectContainer);
            }

            // Then whatever the Remote Control Server window has registered, for projects that
            // configure their servers there instead of hosting one in the scene. Never creates the
            // settings asset - a toolbar click must not author assets.
            var settings = RemoteControlServerSettings.Find();
            if (settings != null)
            {
                var configs = settings.serverConfigs;
                for (int i = 0; i < configs.Count; i++)
                {
                    anyRunning |= _StartFor(configs[i], null);
                }
            }

            if (!anyRunning)
            {
                Debug.LogWarning("[RemoteControl] No server could be started. Add a RemoteControlBehaviour " +
                                 "to the scene, or configure a server in Window > Lilium Remote Control > Remote Control Server.");
            }
        }

        // Starts the server for one configuration, reusing an instance already registered for that
        // port so two hosts sharing a port (or a server the editor auto-start brought up) end up with
        // one server rather than a second one failing to bind. Returns whether it is listening.
        static bool _StartFor(RemoteControlServerConfig config, LiveObjectContainer container)
        {
            if (config == null) return false;

            int port = config.port;
            var server = RemoteControlServerManager.GetOrCreateServer(port, config, container);
            if (server == null) return false;

            if (!server.IsRunning) RemoteControlServerManager.StartServer(port);
            return RemoteControlServerManager.IsServerRunning(port);
        }
    }

    /// <summary>The toolbar icon shared by every editor-version branch below.</summary>
    static class RemoteControlServerToolbarIcon
    {
        // The same Material Symbols "devices" artwork the Unreal editor toggle uses.
        public const string path = "Packages/jp.lilium.remotecontrol/Editor/Icons/devices.png";

        public static string Tooltip(bool running)
        {
            int port = running ? RemoteControlServerToggle.GetRunningPort() : -1;
            string state = running
                ? (port >= 0 ? $"Remote Control server running (port {port})." : "Remote Control server running.")
                : "Remote Control server stopped.";

            if (RemoteControlServerToggle.IsOwnedByPlayMode) return $"{state} Play mode owns it.";
            return running ? $"{state} Click to stop." : $"{state} Click to start.";
        }
    }

#if UNITY_6000_3_OR_NEWER
    // Unity 6.3+ exposes an official main-toolbar element API.
    [InitializeOnLoad]
    static class RemoteControlServerToolbarButton
    {
        const string kToolbarButtonId = "Lilium.RemoteControl/ServerToolbarButton";

        static MainToolbarButton _button;
        static Texture2D _runningIcon;

        static RemoteControlServerToolbarButton()
        {
            RemoteControlServerToggle.stateChanged -= _OnStateChanged;
            RemoteControlServerToggle.stateChanged += _OnStateChanged;
        }

        [MainToolbarElement(kToolbarButtonId,
            defaultDockPosition = MainToolbarDockPosition.Left,
            defaultDockIndex = 10)]
        static MainToolbarButton CreateButton()
        {
            _button = new MainToolbarButton(
                _BuildContent(RemoteControlServerToggle.IsRunning),
                RemoteControlServerToggle.Toggle);
            return _button;
        }

        /// <summary>
        /// A <see cref="MainToolbarElement"/> is a content descriptor rather than a VisualElement, so
        /// the running state cannot be shown by tinting a child Image the way the older path does.
        /// The tint is baked into a second icon and swapped into the element's content instead.
        /// </summary>
        static MainToolbarContent _BuildContent(bool running)
        {
            if (running && _runningIcon == null)
            {
                _runningIcon = ToolbarIcon.CreateTinted(RemoteControlServerToolbarIcon.path, ToolbarIcon.runningTint);
            }

            var icon = running && _runningIcon != null
                ? _runningIcon
                : ToolbarIcon.Load(RemoteControlServerToolbarIcon.path);
            return new MainToolbarContent(null, icon, RemoteControlServerToolbarIcon.Tooltip(running));
        }

        static void _OnStateChanged()
        {
            if (_button == null) return;

            _button.content = _BuildContent(RemoteControlServerToggle.IsRunning);
            MainToolbar.Refresh(kToolbarButtonId);
        }
    }
#else
    // Unity 2021.2 - 6.2 have no public toolbar-extension API, so the button is hosted through the
    // reflection-based MainToolbarHook. A native EditorToolbarButton is injected so its
    // background/hover exactly match the other main-toolbar buttons.
    [InitializeOnLoad]
    static class RemoteControlServerToolbarButton
    {
        static EditorToolbarButton _button;
        static Image _icon;

        static RemoteControlServerToolbarButton()
        {
            EditorApplication.delayCall += _Register;
        }

        static void _Register()
        {
            // Own Image child (instead of EditorToolbarButton.icon) so the tint is fully controllable.
            _icon = new Image
            {
                image = ToolbarIcon.Load(RemoteControlServerToolbarIcon.path),
                scaleMode = ScaleMode.ScaleToFit
            };
            _icon.style.width = 16;
            _icon.style.height = 16;
            _icon.style.alignSelf = Align.Center;

            _button = new EditorToolbarButton(string.Empty, RemoteControlServerToggle.Toggle);
            // Center the icon on both axes; the empty text element otherwise leaves it top-aligned.
            _button.style.alignItems = Align.Center;
            _button.style.justifyContent = Justify.Center;
            _button.Add(_icon);

            _UpdateState();
            RemoteControlServerToggle.stateChanged -= _UpdateState;
            RemoteControlServerToggle.stateChanged += _UpdateState;

            // Left of the Remote app button (order 11).
            MainToolbarHook.AddLeftElement(_button, order: 10);
        }

        static void _UpdateState()
        {
            bool running = RemoteControlServerToggle.IsRunning;
            _icon.tintColor = running ? ToolbarIcon.runningTint : ToolbarIcon.idleTint;
            _button.tooltip = RemoteControlServerToolbarIcon.Tooltip(running);
        }
    }
#endif
}
