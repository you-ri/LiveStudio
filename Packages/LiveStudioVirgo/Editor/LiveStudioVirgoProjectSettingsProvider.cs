// Copyright (c) You-Ri, 2026

using System.Collections.Generic;
using System.IO;
using System.Linq;

using UnityEditor;
using UnityEngine;

namespace Lilium.LiveStudio.Virgo.Editor
{
    static class LiveStudioVirgoProjectSettingsProvider
    {
        const string kSettingsPath = "Project/Live Studio/Virgo";

        // Editable proxy used while the active source is the package default. Edits go to this
        // proxy first; on apply we persist its values as a per-project override and discard it.
        static LiveStudioVirgoProjectSettings _packageDefaultProxy;

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
            var perProject = _LoadPerProjectAsset();
            var packageDefault = perProject == null ? _LoadPackageDefault() : null;

            if (perProject == null && packageDefault == null)
            {
                EditorGUILayout.HelpBox(
                    "Live Studio Virgo settings asset is missing from the package. The package may be corrupted.",
                    MessageType.Error);
                return;
            }

            bool usingPackageDefault = perProject == null;
            LiveStudioVirgoProjectSettings activeAsset = usingPackageDefault ? packageDefault : perProject;
            LiveStudioVirgoProjectSettings editTarget;

            if (usingPackageDefault)
            {
                if (_packageDefaultProxy == null)
                {
                    _packageDefaultProxy = Object.Instantiate(packageDefault);
                    _packageDefaultProxy.name = packageDefault.name;
                    _packageDefaultProxy.hideFlags = HideFlags.DontSave;
                }
                editTarget = _packageDefaultProxy;
            }
            else
            {
                if (_packageDefaultProxy != null)
                {
                    Object.DestroyImmediate(_packageDefaultProxy);
                    _packageDefaultProxy = null;
                }
                editTarget = perProject;
            }

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField(
                    "Active Asset",
                    activeAsset,
                    typeof(LiveStudioVirgoProjectSettings),
                    allowSceneObjects: false);
            }
            if (usingPackageDefault)
            {
                EditorGUILayout.HelpBox(
                    "Showing package default. Editing any value creates a per-project override at " +
                    LiveStudioVirgoProjectSettings.kAssetPath + ".",
                    MessageType.Info);
            }
            EditorGUILayout.Space();

            using var so = new SerializedObject(editTarget);
            so.Update();

            var iter = so.GetIterator();
            iter.NextVisible(true);
            while (iter.NextVisible(false))
            {
                EditorGUILayout.PropertyField(iter, true);
            }

            if (so.ApplyModifiedProperties())
            {
                if (usingPackageDefault)
                {
                    perProject = _PromoteProxyToOverride(editTarget);
                    Object.DestroyImmediate(_packageDefaultProxy);
                    _packageDefaultProxy = null;
                }
                EditorUtility.SetDirty(perProject);
            }
        }

        static LiveStudioVirgoProjectSettings _LoadPerProjectAsset()
        {
            if (EditorBuildSettings.TryGetConfigObject(LiveStudioVirgoProjectSettings.kConfigKey, out LiveStudioVirgoProjectSettings settings) && settings != null)
            {
                _EnsurePreloaded(settings);
                return settings;
            }
            var asset = AssetDatabase.LoadAssetAtPath<LiveStudioVirgoProjectSettings>(LiveStudioVirgoProjectSettings.kAssetPath);
            if (asset != null)
            {
                EditorBuildSettings.AddConfigObject(LiveStudioVirgoProjectSettings.kConfigKey, asset, true);
                _EnsurePreloaded(asset);
            }
            return asset;
        }

        static LiveStudioVirgoProjectSettings _LoadPackageDefault()
        {
            return AssetDatabase.LoadAssetAtPath<LiveStudioVirgoProjectSettings>(LiveStudioVirgoProjectSettings.kPackageDefaultPath);
        }

        static LiveStudioVirgoProjectSettings _PromoteProxyToOverride(LiveStudioVirgoProjectSettings proxy)
        {
            var dir = Path.GetDirectoryName(LiveStudioVirgoProjectSettings.kAssetPath);
            if (!AssetDatabase.IsValidFolder(dir))
            {
                Directory.CreateDirectory(dir);
                AssetDatabase.Refresh();
            }

            var copy = Object.Instantiate(proxy);
            copy.name = Path.GetFileNameWithoutExtension(LiveStudioVirgoProjectSettings.kAssetPath);
            AssetDatabase.CreateAsset(copy, LiveStudioVirgoProjectSettings.kAssetPath);
            AssetDatabase.SaveAssets();

            EditorBuildSettings.AddConfigObject(LiveStudioVirgoProjectSettings.kConfigKey, copy, true);
            _EnsurePreloaded(copy);
            return copy;
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
