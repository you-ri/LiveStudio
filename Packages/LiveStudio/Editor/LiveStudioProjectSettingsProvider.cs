// Copyright (c) You-Ri, 2026

using System.Collections.Generic;
using System.IO;
using System.Linq;

using UnityEditor;
using UnityEngine;

namespace Lilium.LiveStudio.Editor
{
    static class LiveStudioProjectSettingsProvider
    {
        const string kSettingsPath = "Project/Live Studio";

        // Editable proxy used while the active source is the package default. Edits go to this
        // proxy first; on apply we persist its values as a per-project override and discard it.
        static LiveStudioProjectSettings _packageDefaultProxy;

        [SettingsProvider]
        public static SettingsProvider CreateProvider()
        {
            var provider = new SettingsProvider(kSettingsPath, SettingsScope.Project)
            {
                label = "Live Studio",
                guiHandler = _ => _DrawGUI(),
                keywords = new HashSet<string>(new[] { "Live Studio", "Avatar", "VRM" })
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
                    "Live Studio settings asset is missing from the package. The package may be corrupted.",
                    MessageType.Error);
                return;
            }

            bool usingPackageDefault = perProject == null;
            LiveStudioProjectSettings activeAsset = usingPackageDefault ? packageDefault : perProject;
            LiveStudioProjectSettings editTarget;

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
                    typeof(LiveStudioProjectSettings),
                    allowSceneObjects: false);
            }
            if (usingPackageDefault)
            {
                EditorGUILayout.HelpBox(
                    "Showing package default. Editing any value creates a per-project override at " +
                    LiveStudioProjectSettings.kAssetPath + ".",
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

        static LiveStudioProjectSettings _LoadPerProjectAsset()
        {
            if (EditorBuildSettings.TryGetConfigObject(LiveStudioProjectSettings.kConfigKey, out LiveStudioProjectSettings settings) && settings != null)
            {
                _EnsurePreloaded(settings);
                return settings;
            }
            var asset = AssetDatabase.LoadAssetAtPath<LiveStudioProjectSettings>(LiveStudioProjectSettings.kAssetPath);
            if (asset != null)
            {
                EditorBuildSettings.AddConfigObject(LiveStudioProjectSettings.kConfigKey, asset, true);
                _EnsurePreloaded(asset);
            }
            return asset;
        }

        static LiveStudioProjectSettings _LoadPackageDefault()
        {
            return AssetDatabase.LoadAssetAtPath<LiveStudioProjectSettings>(LiveStudioProjectSettings.kPackageDefaultPath);
        }

        static LiveStudioProjectSettings _PromoteProxyToOverride(LiveStudioProjectSettings proxy)
        {
            var dir = Path.GetDirectoryName(LiveStudioProjectSettings.kAssetPath);
            if (!AssetDatabase.IsValidFolder(dir))
            {
                Directory.CreateDirectory(dir);
                AssetDatabase.Refresh();
            }

            var copy = Object.Instantiate(proxy);
            copy.name = Path.GetFileNameWithoutExtension(LiveStudioProjectSettings.kAssetPath);
            AssetDatabase.CreateAsset(copy, LiveStudioProjectSettings.kAssetPath);
            AssetDatabase.SaveAssets();

            EditorBuildSettings.AddConfigObject(LiveStudioProjectSettings.kConfigKey, copy, true);
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
