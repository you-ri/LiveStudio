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

        public static RemoteControlServerSettings GetOrCreate()
        {
            // Search for existing settings asset in the entire project
            var guids = AssetDatabase.FindAssets("t:RemoteControlServerSettings");

            if (guids.Length > 0)
            {
                // Return the first found asset (singleton pattern)
                var path = AssetDatabase.GUIDToAssetPath(guids[0]);
                var settings = AssetDatabase.LoadAssetAtPath<RemoteControlServerSettings>(path);

                // Warn if multiple settings assets exist
                if (guids.Length > 1)
                {
                    Debug.LogWarning($"[Studio] Multiple RemoteControlServerSettings found. Using: {path}");
                }

                return settings;
            }

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
