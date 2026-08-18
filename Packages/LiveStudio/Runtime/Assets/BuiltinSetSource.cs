// Copyright (c) You-Ri, 2026

using System;
using System.Collections.Generic;
using System.IO;

using UnityEngine;
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
    /// The bootstrap (base) scene is skipped: it is already surfaced as <see cref="StageManager"/>'s
    /// persistent set entry, and offering it a second time would let it be loaded additively on top of
    /// itself — a duplicate of the whole studio scene, including a second AvatarController. Every other
    /// build scene becomes a loadable <see cref="BuiltinSetAsset"/>. (Excluding further scenes that should
    /// not be sets is a future opt-out, deliberately not modeled here.)
    /// </summary>
    public static class BuiltinSetSource
    {
        /// <summary>
        /// Builds a <see cref="BuiltinSetAsset"/> for each build scene except the bootstrap one,
        /// with its id / name / scene path populated so <see cref="ExternalAssetManager"/> can list and
        /// load it. Empty when the build holds nothing but the bootstrap scene.
        /// </summary>
        public static IReadOnlyList<AssetBase> GetSets()
        {
            int count = SceneManager.sceneCountInBuildSettings;
            if (count <= 1) return Array.Empty<AssetBase>();

            var basePath = _ResolveBaseScenePath();

            var result = new List<AssetBase>(count - 1);
            for (int i = 0; i < count; i++)
            {
                var path = SceneUtility.GetScenePathByBuildIndex(i);
                if (string.IsNullOrEmpty(path)) continue;
                if (path == basePath) continue;

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

        // The bootstrap scene is build index 0 only in a player launched normally: in the Editor any
        // scene can be played, and a base-scene switch re-points it at runtime. StageManager captures
        // the scene it started in, so ask it first and fall back to index 0 only before it exists.
        private static string _ResolveBaseScenePath()
        {
            var stage = StageManager.current;
            if (stage != null && !string.IsNullOrEmpty(stage.persistentScenePath)) return stage.persistentScenePath;

            if (Application.isPlaying)
            {
                var active = SceneManager.GetActiveScene();
                if (active.IsValid() && !string.IsNullOrEmpty(active.path)) return active.path;
            }

            return SceneUtility.GetScenePathByBuildIndex(0);
        }
    }
}
