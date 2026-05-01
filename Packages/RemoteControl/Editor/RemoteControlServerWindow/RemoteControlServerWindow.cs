using System.Linq;
using UnityEditor;
using UnityEngine;

using Lilium.RemoteControl.Server;

namespace Lilium.RemoteControl
{
    public class RemoteControlServerWindow : EditorWindow
    {
        private RemoteControlServerSettings _settings;
        private Vector2 _scrollPosition;

        [MenuItem("Window/Remote Control/Remote Control Server")]
        public static void ShowWindow()
        {
            var window = GetWindow<RemoteControlServerWindow>("Remote Control Server");
            window.Show();
        }

        [InitializeOnLoadMethod]
        private static void AutoStartServer()
        {
            // エディタ起動時に自動起動設定をチェック
            var settings = RemoteControlServerSettings.GetOrCreate();

            foreach (var config in settings.serverConfigs)
            {
                if (!config.runningInEditor) continue;
                if (RemoteControlServerManager.HasServer(config.port)) continue;

                var server = config.CreateServer();
                if (server != null)
                {
                    RemoteControlServerManager.StartServer(config.port);
                    Debug.Log($"[Studio] Auto-started server on port {config.port}");
                }
            }
        }

        private void OnEnable()
        {
            _settings = RemoteControlServerSettings.GetOrCreate();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space();

            DrawAddServerButton();

            EditorGUILayout.Space();

            DrawServerList();
        }

        private void DrawAddServerButton()
        {
            GUI.color = Color.cyan;
            if (GUILayout.Button("+ Add New Server", GUILayout.Height(30)))
            {
                // 使用可能な次のポート番号を見つける
                int newPort = 3002;
                while (_settings.serverConfigs.Any(c => c != null && c.port == newPort))
                {
                    newPort++;
                }

                // ScriptableObjectアセットとして作成
                var newConfig = ScriptableObject.CreateInstance<RemoteControlServerConfig>();
                newConfig.port = newPort;
                newConfig.enableCors = true;
                newConfig.runningInEditor = false;

                // アセットフォルダを確保
                const string kConfigFolder = "Assets/Settings/RemoteControl";
                if (!AssetDatabase.IsValidFolder("Assets/Settings"))
                {
                    AssetDatabase.CreateFolder("Assets", "Settings");
                }
                if (!AssetDatabase.IsValidFolder(kConfigFolder))
                {
                    AssetDatabase.CreateFolder("Assets/Settings", "RemoteControl");
                }

                // アセット保存
                string assetPath = $"{kConfigFolder}/RemoteControl_Port{newPort}.asset";
                AssetDatabase.CreateAsset(newConfig, assetPath);
                AssetDatabase.SaveAssets();

                // リストに追加
                _settings.serverConfigs.Add(newConfig);
                EditorUtility.SetDirty(_settings);
                AssetDatabase.SaveAssets();
            }
            GUI.color = Color.white;
        }

        private void DrawServerList()
        {
            GUILayout.Label("Server List", EditorStyles.boldLabel);

            if (_settings.serverConfigs.Count == 0)
            {
                EditorGUILayout.HelpBox("No servers configured. Click '+ Add New Server' to create one.", MessageType.Info);
                return;
            }

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            for (int i = 0; i < _settings.serverConfigs.Count; i++)
            {
                DrawServerInstance(_settings.serverConfigs[i], i);
                EditorGUILayout.Space();
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawServerInstance(RemoteControlServerConfig config, int index)
        {
            EditorGUILayout.BeginVertical("box");

            // Header
            var hasServer = RemoteControlServerManager.HasServer(config.port);
            var isRunning = RemoteControlServerManager.IsServerRunning(config.port);

            var statusText = hasServer ? (isRunning ? "● Running" : "○ Stopped") : "○ Not Created";
            var statusColor = isRunning ? Color.green : (hasServer ? Color.yellow : Color.gray);

            EditorGUILayout.BeginHorizontal();
            GUI.enabled = false;
            EditorGUILayout.ObjectField(config, typeof(RemoteControlServerConfig), false);
            GUI.enabled = true;
            GUI.color = statusColor;
            GUILayout.Label(statusText, GUILayout.Width(100));
            GUI.color = Color.white;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            // Settings
            GUI.enabled = !hasServer || !isRunning;

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Port:", GUILayout.Width(100));
            EditorGUI.BeginChangeCheck();
            config.port = EditorGUILayout.IntField(config.port);
            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(config);
                AssetDatabase.SaveAssets();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Enable CORS:", GUILayout.Width(100));
            EditorGUI.BeginChangeCheck();
            config.enableCors = EditorGUILayout.Toggle(config.enableCors);
            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(config);
                AssetDatabase.SaveAssets();
            }
            EditorGUILayout.EndHorizontal();

            GUI.enabled = true;

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Running in Editor:", GUILayout.Width(120));
            EditorGUI.BeginChangeCheck();
            config.runningInEditor = EditorGUILayout.Toggle(config.runningInEditor);
            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(config);
                AssetDatabase.SaveAssets();
            }
            EditorGUILayout.EndHorizontal();

            if (hasServer && isRunning)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("URL:", GUILayout.Width(100));
                GUILayout.Label($"http://localhost:{config.port}/");
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space();

            // Control Buttons
            EditorGUILayout.BeginHorizontal();

            if (!hasServer)
            {
                GUI.color = Color.green;
                if (GUILayout.Button("Start Server", GUILayout.Height(25)))
                {
                    var server = config.CreateServer();
                    if (server != null)
                    {
                        RemoteControlServerManager.StartServer(config.port);
                    }
                }
                GUI.color = Color.white;
            }
            else if (isRunning)
            {
                GUI.color = Color.yellow;
                if (GUILayout.Button("Stop Server", GUILayout.Height(25)))
                {
                    RemoteControlServerManager.StopServer(config.port);
                }
                GUI.color = Color.white;
            }
            else
            {
                GUI.color = Color.green;
                if (GUILayout.Button("Start", GUILayout.Height(25)))
                {
                    RemoteControlServerManager.StartServer(config.port);
                }
                GUI.color = Color.white;
            }

            if (hasServer)
            {
                GUI.color = Color.red;
                if (GUILayout.Button("Remove Server", GUILayout.Height(25)))
                {
                    RemoteControlServerManager.RemoveServer(config.port);
                }
                GUI.color = Color.white;
            }

            GUI.color = Color.red;
            if (GUILayout.Button("Delete Config", GUILayout.Height(25), GUILayout.Width(100)))
            {
                if (EditorUtility.DisplayDialog("Confirm", $"Delete server configuration for port {config.port}?", "Yes", "No"))
                {
                    if (hasServer)
                    {
                        RemoteControlServerManager.RemoveServer(config.port);
                    }

                    // アセットファイルを削除
                    string assetPath = AssetDatabase.GetAssetPath(config);
                    if (!string.IsNullOrEmpty(assetPath))
                    {
                        AssetDatabase.DeleteAsset(assetPath);
                    }

                    // リストから削除
                    _settings.serverConfigs.RemoveAt(index);
                    EditorUtility.SetDirty(_settings);
                    AssetDatabase.SaveAssets();
                }
            }
            GUI.color = Color.white;

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        private void OnInspectorUpdate()
        {
            Repaint();
        }
    }
}
