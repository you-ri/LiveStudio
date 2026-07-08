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
        ///
        /// <paramref name="bundleToken"/> overrides the salt used to make the bundle's internal id
        /// (CAB-...) unique. When null (the default) the salt is the primary asset's GUID, which suits
        /// single-content bundles (one prop / avatar). Multi-content bundles (e.g. an animation pack) pass
        /// an explicit token derived from ALL member GUIDs, so the internal id is stable for a given clip
        /// set and does not shift when the first member changes.
        /// </summary>
        public static bool Build(string bundleName, string[] assetNames, string destPath, string bundleToken = null)
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
            // the same files is already loaded". Salt the internal name with the primary source asset's
            // GUID so each source gets a distinct CAB. The GUID is the asset's stable identity: it survives
            // rename/move (unlike the path) and is unique per asset, so the same prefab always maps to the
            // same CAB (reproducible) while two different props never collide. The on-disk file name
            // (destPath) is unaffected; loaders open by path and never reference the internal name.
            // Fall back to a hash of the path only if the GUID cannot be resolved.
            // An explicit token (multi-content bundles) wins; otherwise salt with the primary asset's GUID,
            // falling back to a hash of its path when the GUID cannot be resolved.
            if (string.IsNullOrEmpty(bundleToken))
            {
                var sourceGuid = AssetDatabase.AssetPathToGUID(assetNames[0]);
                bundleToken = string.IsNullOrEmpty(sourceGuid) ? StableToken(assetNames[0]) : sourceGuid;
            }
            var uniqueBundleName = bundleName + "_" + bundleToken;

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

        /// <summary>
        /// A short, stable, filesystem-safe token (hex MD5) derived from a string. Stable across
        /// runs/machines so re-exporting the same input yields the same bundle name — and therefore the
        /// same internal CAB — keeping the build reproducible. Exposed so multi-content exporters can
        /// derive a token from all member GUIDs.
        /// </summary>
        public static string StableToken(string value)
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
