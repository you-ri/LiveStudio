using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Lilium.RemoteControl;

namespace Lilium.RemoteControl.Server
{
    /// <summary>
    /// 複数のサーバーインスタンスをサポート
    /// </summary>
    public class RemoteControlServerSettings : ScriptableObject
    {
        [SerializeField]
        private List<RemoteControlServerConfig> _serverConfigs = new List<RemoteControlServerConfig>();

        public List<RemoteControlServerConfig> serverConfigs => _serverConfigs;

        /// <summary>
        /// The project's settings asset, or null when it has none. For callers that only read the
        /// configured servers (e.g. the toolbar toggle) and must not author an asset as a side effect.
        /// </summary>
        public static RemoteControlServerSettings Find()
        {
            // Search for existing settings asset in the entire project
            var guids = AssetDatabase.FindAssets("t:RemoteControlServerSettings");

            if (guids.Length == 0) return null;

            // Return the first found asset (singleton pattern)
            var path = AssetDatabase.GUIDToAssetPath(guids[0]);

            // Warn if multiple settings assets exist
            if (guids.Length > 1)
            {
                Debug.LogWarning($"[Studio] Multiple RemoteControlServerSettings found. Using: {path}");
            }

            return AssetDatabase.LoadAssetAtPath<RemoteControlServerSettings>(path);
        }

        public static RemoteControlServerSettings GetOrCreate()
        {
            var existing = Find();
            if (existing != null) return existing;

            // No existing settings found, create new one
            const string kSettingsFolder = "Assets/Settings";
            const string kNewAssetPath = "Assets/Settings/RemoteControlServerSettings.asset";

            // Create Settings folder if it doesn't exist
            if (!AssetDatabase.IsValidFolder(kSettingsFolder))
            {
                AssetDatabase.CreateFolder("Assets", "Settings");
            }

            var newSettings = CreateInstance<RemoteControlServerSettings>();
            AssetDatabase.CreateAsset(newSettings, kNewAssetPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Studio] Created RemoteControlServerSettings at {kNewAssetPath}");

            return newSettings;
        }

        public static SerializedObject GetSerializedObject()
        {
            return new SerializedObject(GetOrCreate());
        }
    }
}
