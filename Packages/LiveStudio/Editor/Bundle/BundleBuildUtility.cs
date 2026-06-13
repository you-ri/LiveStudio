// Copyright (c) You-Ri, 2026

using System.IO;

using UnityEditor;
using UnityEngine;

namespace Lilium.LiveStudio.Editor
{
    /// <summary>
    /// Builds a single LiveStudio bundle (<c>*.scene.lsb</c> / <c>*.avatar.lsb</c>) from one or more
    /// assets and copies the result to a destination path.
    ///
    /// Shared by the scene and avatar exporters so the bundle build settings (compression, target,
    /// shader-variant stripping override) live in one place.
    /// </summary>
    public static class BundleBuildUtility
    {
        private const string kTempBuildDir = "LiveStudioBundleBuild";

        /// <summary>
        /// Builds an AssetBundle named <paramref name="bundleName"/> containing
        /// <paramref name="assetNames"/> and copies it to <paramref name="destPath"/>.
        /// Returns true on success. A scene <c>.unity</c> asset produces a streamed scene bundle.
        /// </summary>
        public static bool Build(string bundleName, string[] assetNames, string destPath)
        {
            if (string.IsNullOrEmpty(destPath))
            {
                Debug.LogError("[LiveStudio] Destination bundle path is empty.");
                return false;
            }

            if (assetNames == null || assetNames.Length == 0)
            {
                Debug.LogError("[LiveStudio] No assets to bundle.");
                return false;
            }

            foreach (var assetName in assetNames)
            {
                if (string.IsNullOrEmpty(assetName) || AssetImporter.GetAtPath(assetName) == null)
                {
                    Debug.LogError($"[LiveStudio] Invalid asset path: '{assetName}'.");
                    return false;
                }
            }

            // Output to the project's Temp folder (outside Assets/) so it is not re-imported.
            var tempOutDir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Temp", kTempBuildDir);
            Directory.CreateDirectory(tempOutDir);

            // Explicit map overload so the importer's assetBundleName is left untouched.
            var build = new AssetBundleBuild
            {
                assetBundleName = bundleName,
                assetNames = assetNames,
            };

            // Disable URP unused-variant stripping during the build so lilToon etc. keep their
            // shader_feature_local variants (otherwise bundled materials load magenta).
            AssetBundleManifest manifest;
            using (UrpShaderStrippingOverride.DisableUnusedVariantStripping())
            {
                manifest = BuildPipeline.BuildAssetBundles(
                    tempOutDir,
                    new[] { build },
                    BuildAssetBundleOptions.ChunkBasedCompression,
                    BuildTarget.StandaloneWindows64);
            }

            try
            {
                if (manifest == null)
                {
                    Debug.LogError("[LiveStudio] BuildPipeline.BuildAssetBundles failed (null manifest).");
                    return false;
                }

                var producedPath = Path.Combine(tempOutDir, bundleName);
                if (!File.Exists(producedPath))
                {
                    Debug.LogError($"[LiveStudio] Built bundle not found at '{producedPath}'.");
                    return false;
                }

                File.Copy(producedPath, destPath, overwrite: true);
                Debug.Log($"[LiveStudio] Exported bundle to '{destPath}'.");
                return true;
            }
            finally
            {
                if (Directory.Exists(tempOutDir))
                {
                    Directory.Delete(tempOutDir, recursive: true);
                }
            }
        }
    }
}
