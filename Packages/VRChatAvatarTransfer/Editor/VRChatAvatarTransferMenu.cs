// Copyright (c) You-Ri, 2026
using UnityEditor;
using UnityEngine;

namespace Lilium.VRChatAvatarTransfer.Editor
{
    internal static class VRChatAvatarTransferMenu
    {
        private const string MenuRoot = "Tools/Virgo Motion/VRChat Avatar Transfer/";

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
    }
}
