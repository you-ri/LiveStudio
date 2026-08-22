// Copyright (c) You-Ri, 2026
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

using Lilium.RemoteControl.Editor;
using Lilium.RemoteControl.Server;

namespace Lilium.RemoteControl
{
    /// <summary>
    /// Lists the editor's REST server configurations and lets each one be created, started,
    /// stopped and removed.
    ///
    /// A config is a <see cref="RemoteControlServerConfig"/> asset; the running server itself is
    /// owned by <see cref="RemoteControlServerManager"/> and keyed by port, so a config can exist
    /// with no server behind it. That gives each row three states - not created, stopped, running
    /// - and the buttons differ per state.
    ///
    /// The manager's state changes without telling anyone (a server can also be started from
    /// code), so the rows poll it on a timer. Only the state-dependent parts are refreshed;
    /// rebuilding the rows would fight with a port being typed into.
    /// </summary>
    public class RemoteControlServerWindow : EditorWindow
    {
        // How often the rows re-read RemoteControlServerManager. Slow enough to be free, fast
        // enough that a server started from elsewhere shows up as a live window.
        private const long kStatePollMs = 500;

        private const string kStyleSheet = "Editor/RemoteControlServerWindow/RemoteControlServerWindow.uss";
        private const string kConfigFolder = "Assets/Settings/RemoteControl";

        private RemoteControlServerSettings _settings;

        private VisualElement _list;
        private HelpBox _emptyHelp;

        // One entry per drawn config, so the poll can refresh the state-dependent elements
        // without rebuilding the rows.
        private readonly List<ServerRow> _rows = new List<ServerRow>();

        private class ServerRow
        {
            public RemoteControlServerConfig config;
            public Label status;
            public IntegerField port;
            public Toggle cors;
            public Label url;
            public Button run;
            public Button remove;
        }

        [MenuItem("Window/Lilium Remote Control/Remote Control Server")]
        public static void ShowWindow()
        {
            var window = GetWindow<RemoteControlServerWindow>("Remote Control Server");
            window.Show();
        }

        [InitializeOnLoadMethod]
        private static void AutoStartServer()
        {
            // Start the servers marked as editor-resident as soon as the editor comes up.
            var settings = RemoteControlServerSettings.GetOrCreate();

            foreach (var config in settings.serverConfigs)
            {
                // The list can hold broken references (assets deleted since), so allow null.
                if (config == null || !config.runningInEditor) continue;
                if (RemoteControlServerManager.HasServer(config.port)) continue;

                var server = RemoteControlServerManager.GetOrCreateServer(config.port, config);
                if (server != null)
                {
                    RemoteControlServerManager.StartServer(config.port);
                    Debug.Log($"[Studio] Auto-started server on port {config.port}");
                }
            }
        }

        private void CreateGUI()
        {
            _settings = RemoteControlServerSettings.GetOrCreate();

            var root = rootVisualElement;
            RemoteControlEditorStyles.Apply(root, kStyleSheet);
            root.AddToClassList(RemoteControlEditorStyles.kColumn);

            var add = new Button(_AddServer) { text = "+ Add New Server" };
            add.AddToClassList("rcs-add-button");
            add.AddToClassList(RemoteControlEditorStyles.kAccent);
            root.Add(add);

            var title = new Label("Server List");
            title.AddToClassList(RemoteControlEditorStyles.kTitle);
            title.AddToClassList("rcs-list-title");
            root.Add(title);

            _emptyHelp = new HelpBox("No servers configured. Click '+ Add New Server' to create one.", HelpBoxMessageType.Info);
            _emptyHelp.AddToClassList(RemoteControlEditorStyles.kHelp);
            root.Add(_emptyHelp);

            var scroll = new ScrollView();
            scroll.AddToClassList(RemoteControlEditorStyles.kScroll);
            _list = scroll.contentContainer;
            root.Add(scroll);

            _RebuildList();
            root.schedule.Execute(_RefreshStates).Every(kStatePollMs);
        }

        // --- List ---

        private void _RebuildList()
        {
            _rows.Clear();
            _list.Clear();

            foreach (var config in _settings.serverConfigs)
            {
                // The referenced asset may not exist in this project (came from another one, or
                // was deleted).
                if (config == null) continue;
                _list.Add(_MakeServerCard(config));
            }

            _emptyHelp.style.display = _rows.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _RefreshStates();
        }

        private VisualElement _MakeServerCard(RemoteControlServerConfig config)
        {
            var row = new ServerRow { config = config };

            var card = new VisualElement();
            card.AddToClassList(RemoteControlEditorStyles.kCard);

            var header = new VisualElement();
            header.AddToClassList(RemoteControlEditorStyles.kRow);
            header.AddToClassList("rcs-card-header");
            // Shown for identification only - the config is edited through the fields below.
            var configField = new ObjectField { objectType = typeof(RemoteControlServerConfig), allowSceneObjects = false };
            configField.AddToClassList("rcs-config-field");
            configField.SetValueWithoutNotify(config);
            configField.SetEnabled(false);
            header.Add(configField);
            row.status = new Label();
            row.status.AddToClassList("rcs-status");
            header.Add(row.status);
            card.Add(header);

            // Committed on Enter / focus loss rather than per keystroke: every commit writes the
            // asset back to disk.
            row.port = new IntegerField("Port") { isDelayed = true };
            row.port.SetValueWithoutNotify(config.port);
            row.port.RegisterValueChangedCallback(evt => _Commit(config, () => config.port = evt.newValue));
            card.Add(row.port);

            row.cors = new Toggle("Enable CORS");
            row.cors.SetValueWithoutNotify(config.enableCors);
            row.cors.RegisterValueChangedCallback(evt => _Commit(config, () => config.enableCors = evt.newValue));
            card.Add(row.cors);

            var runningInEditor = new Toggle("Running in Editor");
            runningInEditor.SetValueWithoutNotify(config.runningInEditor);
            runningInEditor.RegisterValueChangedCallback(evt => _Commit(config, () => config.runningInEditor = evt.newValue));
            card.Add(runningInEditor);

            row.url = new Label();
            row.url.AddToClassList("rcs-url");
            row.url.AddToClassList(RemoteControlEditorStyles.kSubtle);
            card.Add(row.url);

            var buttons = new VisualElement();
            buttons.AddToClassList("rcs-buttons");
            row.run = _MakeButton(string.Empty, () => _ToggleServer(config));
            buttons.Add(row.run);
            row.remove = _MakeButton("Remove Server", () => RemoteControlServerManager.RemoveServer(config.port));
            row.remove.AddToClassList(RemoteControlEditorStyles.kDanger);
            buttons.Add(row.remove);
            var delete = _MakeButton("Delete Config", () => _DeleteConfig(config));
            delete.AddToClassList("rcs-button--delete");
            delete.AddToClassList(RemoteControlEditorStyles.kDanger);
            buttons.Add(delete);
            card.Add(buttons);

            _rows.Add(row);
            return card;
        }

        private static Button _MakeButton(string text, System.Action onClick)
        {
            var button = new Button(onClick) { text = text };
            button.AddToClassList("rcs-button");
            return button;
        }

        // --- State poll ---

        /// <summary>
        /// Re-reads the manager for every row. Only touches the state-dependent parts, so a field
        /// being edited is never overwritten from under the cursor.
        /// </summary>
        private void _RefreshStates()
        {
            foreach (var row in _rows)
            {
                if (row.config == null) continue;
                bool hasServer = RemoteControlServerManager.HasServer(row.config.port);
                bool isRunning = RemoteControlServerManager.IsServerRunning(row.config.port);

                row.status.text = hasServer ? (isRunning ? "● Running" : "○ Stopped") : "○ Not Created";
                row.status.EnableInClassList(RemoteControlEditorStyles.kSuccess, isRunning);
                row.status.EnableInClassList(RemoteControlEditorStyles.kWarning, hasServer && !isRunning);
                row.status.EnableInClassList(RemoteControlEditorStyles.kSubtle, !hasServer);

                // The port and CORS are baked into the server when it is created, so they can
                // only be edited while none is listening on that port.
                bool editable = !hasServer || !isRunning;
                row.port.SetEnabled(editable);
                row.cors.SetEnabled(editable);

                row.url.text = isRunning ? $"http://localhost:{row.config.port}/" : string.Empty;
                row.url.style.display = isRunning ? DisplayStyle.Flex : DisplayStyle.None;

                row.run.text = isRunning ? "Stop Server" : (hasServer ? "Start" : "Start Server");
                row.run.EnableInClassList(RemoteControlEditorStyles.kSuccess, !isRunning);
                row.run.EnableInClassList(RemoteControlEditorStyles.kWarning, isRunning);
                row.remove.style.display = hasServer ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        // --- Actions ---

        private void _ToggleServer(RemoteControlServerConfig config)
        {
            if (RemoteControlServerManager.IsServerRunning(config.port))
            {
                RemoteControlServerManager.StopServer(config.port);
                return;
            }
            if (!RemoteControlServerManager.HasServer(config.port)
                && RemoteControlServerManager.GetOrCreateServer(config.port, config) == null)
            {
                return;
            }
            RemoteControlServerManager.StartServer(config.port);
        }

        private void _Commit(RemoteControlServerConfig config, System.Action edit)
        {
            edit();
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
        }

        private void _AddServer()
        {
            int newPort = 3002;
            while (_settings.serverConfigs.Any(c => c != null && c.port == newPort))
            {
                newPort++;
            }

            var newConfig = CreateInstance<RemoteControlServerConfig>();
            newConfig.port = newPort;
            newConfig.enableCors = true;
            newConfig.runningInEditor = false;

            if (!AssetDatabase.IsValidFolder("Assets/Settings"))
            {
                AssetDatabase.CreateFolder("Assets", "Settings");
            }
            if (!AssetDatabase.IsValidFolder(kConfigFolder))
            {
                AssetDatabase.CreateFolder("Assets/Settings", "RemoteControl");
            }

            AssetDatabase.CreateAsset(newConfig, $"{kConfigFolder}/RemoteControl_Port{newPort}.asset");
            AssetDatabase.SaveAssets();

            _settings.serverConfigs.Add(newConfig);
            EditorUtility.SetDirty(_settings);
            AssetDatabase.SaveAssets();

            _RebuildList();
        }

        private void _DeleteConfig(RemoteControlServerConfig config)
        {
            if (!EditorUtility.DisplayDialog("Confirm", $"Delete server configuration for port {config.port}?", "Yes", "No"))
            {
                return;
            }

            if (RemoteControlServerManager.HasServer(config.port))
            {
                RemoteControlServerManager.RemoveServer(config.port);
            }

            string assetPath = AssetDatabase.GetAssetPath(config);
            if (!string.IsNullOrEmpty(assetPath))
            {
                AssetDatabase.DeleteAsset(assetPath);
            }

            _settings.serverConfigs.Remove(config);
            EditorUtility.SetDirty(_settings);
            AssetDatabase.SaveAssets();

            // Rebuilding here would destroy the button whose click is still being dispatched.
            rootVisualElement.schedule.Execute(_RebuildList);
        }
    }
}
