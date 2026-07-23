// Copyright (c) You-Ri, 2026

using System;
using System.Collections.Generic;
using System.IO;

using UnityEngine.SceneManagement;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Discovers the app's built-in sets from the build's scene list — the runtime counterpart of
    /// <see cref="BuiltinAssetRegistry"/> for scenes. Unlike a built-in prop (baked into a catalog because
    /// a Resources asset cannot be enumerated at runtime), the build's scene list is directly readable at
    /// runtime (<see cref="SceneManager.sceneCountInBuildSettings"/> /
    /// <see cref="SceneUtility.GetScenePathByBuildIndex"/>), so no bake step or catalog is needed: any
    /// scene added to the build is a built-in set.
    ///
    /// Build index 0 is skipped: it is the bootstrap (base) scene the app starts in, already surfaced as
    /// <see cref="StageManager"/>'s persistent set entry. Every other build scene becomes a loadable
    /// <see cref="BuiltinSetAsset"/>, so all build scenes are represented as sets. (Excluding specific
    /// scenes that should not be sets is a future opt-out, deliberately not modeled here.)
    /// </summary>
    public static class BuiltinSetSource
    {
        /// <summary>
        /// Builds a <see cref="BuiltinSetAsset"/> for each build scene except the bootstrap (index 0),
        /// with its id / name / scene path populated so <see cref="ExternalAssetManager"/> can list and
        /// load it. Empty when the build has only the bootstrap scene.
        /// </summary>
        public static IReadOnlyList<AssetBase> GetSets()
        {
            int count = SceneManager.sceneCountInBuildSettings;
            if (count <= 1) return Array.Empty<AssetBase>();

            var result = new List<AssetBase>(count - 1);
            for (int i = 1; i < count; i++)
            {
                var path = SceneUtility.GetScenePathByBuildIndex(i);
                if (string.IsNullOrEmpty(path)) continue;

                result.Add(new BuiltinSetAsset
                {
                    id = path,
                    name = Path.GetFileNameWithoutExtension(path),
                    scenePath = path,
                    filePath = string.Empty,
                    path = string.Empty,
                    enabled = false,
                    isLoaded = false,
                });
            }
            return result;
        }
    }
}
