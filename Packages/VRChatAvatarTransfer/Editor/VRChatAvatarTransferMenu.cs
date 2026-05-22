// Copyright (c) You-Ri, 2026
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Lilium.VRChatAvatarTransfer.Editor
{
    internal static class VRChatAvatarTransferMenu
    {
        private const string MenuRoot = "Tools/VRChat Avatar Transfer/";

        [MenuItem(MenuRoot + "Transfer")]
        private static void OpenTransferWindow()
        {
            VRChatAvatarTransferWindow.Open();
        }

        [MenuItem(MenuRoot + "Make Baked Prefab (Selected)", false)]
        private static void SaveInMemoryAssetsAndMakePrefab()
        {
            var sceneObjects = CollectSelectedSceneObjects();
            if (sceneObjects.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    "VRChat Avatar Transfer",
                    "No scene GameObject selected. Select the baked avatar (e.g. 'Avatar(Clone)') in the Hierarchy.",
                    "OK");
                return;
            }

            int success = 0;
            foreach (var go in sceneObjects)
            {
                if (BakedAvatarPrefabBuilder.Build(go)) success++;
            }
            VRChatAvatarTransferLog.Info(
                $"Make prefab finished: {success}/{sceneObjects.Count} prefab(s) written to '{BakedAvatarPrefabBuilder.kOutputFolder}'.");
        }

        [MenuItem(MenuRoot + "Make Baked Prefab (Selected)", true)]
        private static bool ValidateSaveInMemoryAssetsAndMakePrefab()
            => CollectSelectedSceneObjects().Count > 0;

        private static List<GameObject> CollectSelectedSceneObjects()
        {
            var result = new List<GameObject>();
            var gameObjects = Selection.gameObjects;
            if (gameObjects == null) return result;

            foreach (var go in gameObjects)
            {
                if (go == null) continue;
                if (EditorUtility.IsPersistent(go)) continue; // Project アセットは対象外
                result.Add(go);
            }
            return result;
        }

        private static void ForEachSelectedAvatar(System.Action<GameObject> action)
        {
            var gameObjects = Selection.gameObjects;
            if (gameObjects == null || gameObjects.Length == 0)
            {
                EditorUtility.DisplayDialog(
                    "VRChat Avatar Transfer",
                    "No GameObject selected. Select one or more avatar root objects in the hierarchy.",
                    "OK");
                return;
            }

            foreach (var go in gameObjects)
            {
                if (go == null) continue;
                action(go);
            }
        }

        private static List<string> CollectSelectedPrefabAssetPaths()
        {
            var result = new List<string>();
            var seen = new HashSet<string>();
            var objects = Selection.objects;
            if (objects == null) return result;

            foreach (var obj in objects)
            {
                if (obj == null) continue;
                var path = AssetDatabase.GetAssetPath(obj);
                if (string.IsNullOrEmpty(path)) continue;

                var prefabType = PrefabUtility.GetPrefabAssetType(obj);
                if (prefabType == PrefabAssetType.NotAPrefab || prefabType == PrefabAssetType.MissingAsset) continue;

                if (seen.Add(path)) result.Add(path);
            }
            return result;
        }
    }
}
