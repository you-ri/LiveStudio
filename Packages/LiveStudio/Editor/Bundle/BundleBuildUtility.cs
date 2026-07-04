// Copyright (c) You-Ri, 2026

using System.IO;
using System.Security.Cryptography;
using System.Text;

using UnityEditor;
using UnityEngine;

namespace Lilium.LiveStudio.Editor
{
    /// <summary>
    /// Builds a single LiveStudio bundle (<c>*.set.lsb</c> / <c>*.avatar.lsb</c>) from one or more
    /// assets and copies the result to a destination path.
    ///
    /// Shared by the set and avatar exporters so the bundle build settings (compression, target,
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

            // Unity derives the bundle's internal serialized-file id ("CAB-...") from the bundle NAME in a
            // deterministic build. Every prop is exported under the same constant name ("prop"), so all
            // prop bundles used to share one CAB — loading two at once fails with "another AssetBundle with
            // the same files is already loaded". Salt the internal name with a stable hash of the primary
            // source asset so each source gets a distinct CAB (same asset re-exported → same CAB, so the
            // output stays reproducible). The on-disk file name (destPath) is unaffected; loaders open by
            // path and never reference the internal name.
            var uniqueBundleName = bundleName + "_" + _StableToken(assetNames[0]);

            // Explicit map overload so the importer's assetBundleName is left untouched.
            var build = new AssetBundleBuild
            {
                assetBundleName = uniqueBundleName,
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

                var producedPath = Path.Combine(tempOutDir, uniqueBundleName);
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

        // A short, stable, filesystem-safe token derived from a string (the source asset path). Stable
        // across runs/machines so re-exporting the same asset yields the same bundle name — and therefore
        // the same internal CAB — keeping the build reproducible.
        private static string _StableToken(string value)
        {
            using (var md5 = MD5.Create())
            {
                var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty));
                var sb = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++) sb.Append(hash[i].ToString("x2"));
                return sb.ToString();
            }
        }
    }
}
