// Copyright (c) You-Ri, 2026

using System.Collections.Generic;
using System.IO;
using System.Linq;

using UnityEditor;
using UnityEngine;

namespace Lilium.RemoteControl.Editor
{
    /// <summary>
    /// Project Settings page for <see cref="RemoteControlProjectSettings"/>. The package default is
    /// shown read-through: editing any value promotes a copy to a per-project override under
    /// Assets, registers it as the config object and preloads it so a player build carries it
    /// (and, through it, the live class assets it names).
    /// </summary>
    static class RemoteControlProjectSettingsProvider
    {
        const string kSettingsPath = "Project/Remote Control";

        // Editable proxy used while the active source is the package default. Edits go to this
        // proxy first; on apply we persist its values as a per-project override and discard it.
        static RemoteControlProjectSettings _packageDefaultProxy;

        [SettingsProvider]
        public static SettingsProvider CreateProvider()
        {
            return new SettingsProvider(kSettingsPath, SettingsScope.Project)
            {
                label = "Remote Control",
                guiHandler = _ => _DrawGUI(),
                keywords = new HashSet<string>(new[] { "Remote Control", "Live Class", "Binding" })
            };
        }

        static void _DrawGUI()
        {
            var perProject = _LoadPerProjectAsset();
            var packageDefault = perProject == null ? _LoadPackageDefault() : null;

            if (perProject == null && packageDefault == null)
            {
                EditorGUILayout.HelpBox(
                    "Remote Control settings asset is missing from the package. The package may be corrupted.",
                    MessageType.Error);
                return;
            }

            bool usingPackageDefault = perProject == null;
            RemoteControlProjectSettings activeAsset = usingPackageDefault ? packageDefault : perProject;
            RemoteControlProjectSettings editTarget;

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
                    typeof(RemoteControlProjectSettings),
                    allowSceneObjects: false);
            }
            if (usingPackageDefault)
            {
                EditorGUILayout.HelpBox(
                    "Showing package default. Editing any value creates a per-project override at " +
                    RemoteControlProjectSettings.kAssetPath + ".",
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

                // The declarations are applied at load, so an edit here would otherwise only take
                // effect after the next domain reload.
                RemoteControlProjectSettings.ApplyLiveClassAssets();
            }
        }

        static RemoteControlProjectSettings _LoadPerProjectAsset()
        {
            if (EditorBuildSettings.TryGetConfigObject(RemoteControlProjectSettings.kConfigKey, out RemoteControlProjectSettings settings) && settings != null)
            {
                _EnsurePreloaded(settings);
                return settings;
            }
            var asset = AssetDatabase.LoadAssetAtPath<RemoteControlProjectSettings>(RemoteControlProjectSettings.kAssetPath);
            if (asset != null)
            {
                EditorBuildSettings.AddConfigObject(RemoteControlProjectSettings.kConfigKey, asset, true);
                _EnsurePreloaded(asset);
            }
            return asset;
        }

        static RemoteControlProjectSettings _LoadPackageDefault()
        {
            return Resources.Load<RemoteControlProjectSettings>(RemoteControlProjectSettings.kResourcesPath);
        }

        static RemoteControlProjectSettings _PromoteProxyToOverride(RemoteControlProjectSettings proxy)
        {
            var dir = Path.GetDirectoryName(RemoteControlProjectSettings.kAssetPath);
            if (!AssetDatabase.IsValidFolder(dir))
            {
                Directory.CreateDirectory(dir);
                AssetDatabase.Refresh();
            }

            var copy = Object.Instantiate(proxy);
            copy.name = Path.GetFileNameWithoutExtension(RemoteControlProjectSettings.kAssetPath);
            AssetDatabase.CreateAsset(copy, RemoteControlProjectSettings.kAssetPath);
            AssetDatabase.SaveAssets();

            EditorBuildSettings.AddConfigObject(RemoteControlProjectSettings.kConfigKey, copy, true);
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
