// Copyright (c) You-Ri, 2026
using System.IO;
using UnityEditor;
using UnityEngine;
using VRC.Core;
using VRC.SDK3.Avatars.Components;

namespace Lilium.VRChatAvatarTransfer.Editor
{
    /// <summary>
    /// Converts a VRChat avatar prefab asset into a non-VRChat prefab written under
    /// <see cref="Vrm10ObjectBuilder.OutputFolder"/>. Loads the source prefab into an isolated scene,
    /// runs PhysBone / Constraint converters, applies the FX AnimatorController from VRCAvatarDescriptor,
    /// strips VRChat-only components, and saves the result as a new prefab. The source asset is not modified.
    /// </summary>
    internal static class PrefabAssetConverter
    {
        public static bool Convert(string assetPath)
        {
            GameObject root = null;
            try
            {
                root = PrefabUtility.LoadPrefabContents(assetPath);
                if (root == null)
                {
                    VRChatAvatarTransferLog.Error($"Failed to load prefab contents: '{assetPath}'.");
                    return false;
                }

                PhysBoneToSpringBoneConverter.TryConvert(root, out _);
                VRCConstraintToUnityConstraintConverter.Convert(root);
                ApplyFxAnimatorController(root);
                StripVRChatComponents(root);

                var safeName = Vrm10ObjectBuilder.MakeFileSafe(Path.GetFileNameWithoutExtension(assetPath));
                var outPath = $"{Vrm10ObjectBuilder.OutputFolder}/{safeName}.prefab";
                PrefabUtility.SaveAsPrefabAsset(root, outPath, out var saved);
                if (saved)
                {
                    VRChatAvatarTransferLog.Info($"Saved converted prefab to '{outPath}'.");
                }
                else
                {
                    VRChatAvatarTransferLog.Error($"Failed to save converted prefab to '{outPath}'.");
                }
                return saved;
            }
            catch (System.Exception ex)
            {
                VRChatAvatarTransferLog.Error($"Prefab asset conversion failed for '{assetPath}': {ex}");
                return false;
            }
            finally
            {
                if (root != null) PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void ApplyFxAnimatorController(GameObject root)
        {
            var desc = root.GetComponent<VRCAvatarDescriptor>();
            if (desc == null || desc.baseAnimationLayers == null) return;

            RuntimeAnimatorController fxController = null;
            foreach (var layer in desc.baseAnimationLayers)
            {
                if (layer.type == VRCAvatarDescriptor.AnimLayerType.FX)
                {
                    fxController = layer.animatorController;
                    break;
                }
            }
            if (fxController == null)
            {
                VRChatAvatarTransferLog.Info($"'{root.name}': no custom FX animator controller in VRCAvatarDescriptor.");
                return;
            }

            var animator = root.GetComponent<Animator>();
            if (animator == null)
            {
                VRChatAvatarTransferLog.Warn($"'{root.name}': no Animator component on root; FX controller not applied.");
                return;
            }

            animator.runtimeAnimatorController = fxController;
            VRChatAvatarTransferLog.Info($"'{root.name}': applied FX animator controller '{fxController.name}'.");
        }

        private static void StripVRChatComponents(GameObject root)
        {
            int removed = 0;
            foreach (var desc in root.GetComponentsInChildren<VRCAvatarDescriptor>(true))
            {
                if (desc == null) continue;
                Object.DestroyImmediate(desc);
                removed++;
            }
            foreach (var pm in root.GetComponentsInChildren<PipelineManager>(true))
            {
                if (pm == null) continue;
                Object.DestroyImmediate(pm);
                removed++;
            }
            if (removed > 0)
            {
                VRChatAvatarTransferLog.Info($"'{root.name}': stripped {removed} VRChat-only component(s) (VRCAvatarDescriptor / PipelineManager).");
            }
        }
    }
}
