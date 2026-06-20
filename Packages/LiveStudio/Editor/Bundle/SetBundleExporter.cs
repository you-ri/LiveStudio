// Copyright (c) You-Ri, 2026

using System;
using System.IO;

using UnityEditor;
using UnityEngine;

namespace Lilium.LiveStudio.Editor
{
    /// <summary>
    /// Exports a Unity scene as a set bundle (<c>*.set.lsb</c>) that the Studio app can load
    /// additively at runtime via <see cref="StageManager"/>.
    /// </summary>
    internal static class SetBundleExporter
    {
        private const string kBundleName = "set";
        private const string kExtension = ".set.lsb";
        private const string kMenuPath = "Assets/Lilium Live Studio/Export Set Bundle (.set.lsb)";

        /// <summary>Builds a set bundle from the given <c>.unity</c> scene asset and copies it to destPath.</summary>
        public static bool Export(string sceneAssetPath, string destPath)
        {
            if (string.IsNullOrEmpty(sceneAssetPath) ||
                !sceneAssetPath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogError($"[LiveStudio] Not a scene asset: '{sceneAssetPath}'.");
                return false;
            }

            return BundleBuildUtility.Build(kBundleName, new[] { sceneAssetPath }, destPath);
        }

        [MenuItem(kMenuPath)]
        private static void _ExportSelectedSet()
        {
            var sceneAsset = Selection.activeObject as SceneAsset;
            var assetPath = AssetDatabase.GetAssetPath(sceneAsset);
            if (string.IsNullOrEmpty(assetPath))
            {
                Debug.LogError("[LiveStudio] Select a Scene asset in the Project window first.");
                return;
            }

            var defaultName = $"{Path.GetFileNameWithoutExtension(assetPath)}{kExtension}";
            var savePath = EditorUtility.SaveFilePanel("Export Set Bundle", "", defaultName, "lsb");
            if (string.IsNullOrEmpty(savePath)) return;

            // SaveFilePanel only handles a single extension; guarantee the compound suffix.
            if (!savePath.EndsWith(kExtension, StringComparison.OrdinalIgnoreCase))
            {
                savePath = Path.ChangeExtension(savePath, null);
                if (savePath.EndsWith(".set", StringComparison.OrdinalIgnoreCase))
                {
                    savePath = savePath.Substring(0, savePath.Length - ".set".Length);
                }
                savePath += kExtension;
            }

            Export(assetPath, savePath);
        }

        [MenuItem(kMenuPath, validate = true)]
        private static bool _ValidateExportSelectedSet()
        {
            return Selection.activeObject is SceneAsset;
        }
    }
}
