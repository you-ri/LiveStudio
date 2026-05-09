// Copyright (c) You-Ri, 2026

using System.Collections.Generic;
using System.Linq;

using UnityEditor;
using UnityEngine;

namespace Lilium.LiveStudio.Virgo.Editor
{
    static class LiveStudioVirgoProjectSettingsProvider
    {
        const string kSettingsPath = "Project/Live Studio/Virgo";
        const string kAssetPath = "Packages/jp.lilium.livestudio.virgo/Contents/Settings/LiveStudioVirgoProjectSettings.asset";

        [SettingsProvider]
        public static SettingsProvider CreateProvider()
        {
            var provider = new SettingsProvider(kSettingsPath, SettingsScope.Project)
            {
                label = "Virgo",
                guiHandler = _ => _DrawGUI(),
                keywords = new HashSet<string>(new[] { "Live Studio", "Virgo", "Fusion" })
            };
            return provider;
        }

        static void _DrawGUI()
        {
            var settings = _GetOrCreateSettings();
            if (settings == null) return;

            using var so = new SerializedObject(settings);
            so.Update();

            var iter = so.GetIterator();
            iter.NextVisible(true);
            while (iter.NextVisible(false))
            {
                EditorGUILayout.PropertyField(iter, true);
            }

            if (so.ApplyModifiedProperties())
            {
                EditorUtility.SetDirty(settings);
            }
        }

        static LiveStudioVirgoProjectSettings _GetOrCreateSettings()
        {
            if (EditorBuildSettings.TryGetConfigObject(LiveStudioVirgoProjectSettings.kConfigKey, out LiveStudioVirgoProjectSettings settings) && settings != null)
            {
                _EnsurePreloaded(settings);
                return settings;
            }

            settings = AssetDatabase.LoadAssetAtPath<LiveStudioVirgoProjectSettings>(kAssetPath);
            if (settings == null)
            {
                var dir = System.IO.Path.GetDirectoryName(kAssetPath);
                if (!AssetDatabase.IsValidFolder(dir))
                {
                    System.IO.Directory.CreateDirectory(dir);
                    AssetDatabase.Refresh();
                }
                settings = ScriptableObject.CreateInstance<LiveStudioVirgoProjectSettings>();
                AssetDatabase.CreateAsset(settings, kAssetPath);
                AssetDatabase.SaveAssets();
            }

            EditorBuildSettings.AddConfigObject(LiveStudioVirgoProjectSettings.kConfigKey, settings, true);
            _EnsurePreloaded(settings);
            return settings;
        }

        static void _EnsurePreloaded(Object asset)
        {
            var preloaded = PlayerSettings.GetPreloadedAssets();
            if (preloaded != null && preloaded.Contains(asset)) return;

            var list = preloaded != null ? preloaded.ToList() : new List<Object>();
            list.RemoveAll(a => a == null);
            list.Add(asset);
            PlayerSettings.SetPreloadedAssets(list.ToArray());
        }
    }
}
