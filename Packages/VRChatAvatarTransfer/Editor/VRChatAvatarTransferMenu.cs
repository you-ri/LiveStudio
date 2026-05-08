// Copyright (c) You-Ri, 2026
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Lilium.VRChatAvatarTransfer.Editor
{
    internal static class VRChatAvatarTransferMenu
    {
        private const string MenuRoot = "Tools/VRChat Avatar Transfer/";

        [MenuItem(MenuRoot + "Convert PhysBone to VRM SpringBone (Selected)")]
        private static void ConvertPhysBoneToSpringBone()
        {
            ForEachSelectedAvatar(avatar =>
            {
                PhysBoneToSpringBoneConverter.TryConvert(avatar, out _);
            });
        }

        [MenuItem(MenuRoot + "Convert VRC Constraint to Unity Constraint (Selected)")]
        private static void ConvertConstraints()
        {
            ForEachSelectedAvatar(avatar =>
            {
                VRCConstraintToUnityConstraintConverter.Convert(avatar);
            });
        }

        [MenuItem(MenuRoot + "Convert All (VRM SpringBone) (Selected)")]
        private static void ConvertAllVrm()
        {
            ForEachSelectedAvatar(avatar =>
            {
                PhysBoneToSpringBoneConverter.TryConvert(avatar, out _);
                VRCConstraintToUnityConstraintConverter.Convert(avatar);
            });
        }

        [MenuItem(MenuRoot + "Convert All (VRM SpringBone) (Prefab Asset)", false)]
        private static void ConvertAllVrmPrefabAsset()
        {
            var prefabPaths = CollectSelectedPrefabAssetPaths();
            if (prefabPaths.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    "VRChat Avatar Transfer",
                    "No prefab asset selected. Select one or more prefab assets in the Project window.",
                    "OK");
                return;
            }

            Vrm10ObjectBuilder.EnsureFolder(Vrm10ObjectBuilder.OutputFolder);

            int success = 0;
            foreach (var path in prefabPaths)
            {
                if (PrefabAssetConverter.Convert(path)) success++;
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            VRChatAvatarTransferLog.Info(
                $"Prefab asset conversion finished: {success}/{prefabPaths.Count} prefab(s) written to '{Vrm10ObjectBuilder.OutputFolder}'.");
        }

        [MenuItem(MenuRoot + "Convert All (VRM SpringBone) (Prefab Asset)", true)]
        private static bool ValidateConvertAllVrmPrefabAsset()
            => CollectSelectedPrefabAssetPaths().Count > 0;

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
