// Copyright (c) You-Ri, 2026
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
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

                var safeName = Vrm10ObjectBuilder.MakeFileSafe(Path.GetFileNameWithoutExtension(assetPath));

                PhysBoneToSpringBoneConverter.TryConvert(root, out _);
                VRCConstraintToUnityConstraintConverter.Convert(root);
                ApplyFxAnimatorController(root, safeName);

                // VRCFT v2 互換 (FX controller に "FT/v2/" 系パラメータが存在) の場合は
                // VRCFTAvatar を IAvatar として採用し、ARKit を直接 FT/v2/ パラメータへ書き込む。
                // この経路では VRCAvatar 向けの viseme/blink/expressions 移植は適用しない
                // (FT テンプレートが Animator 内で表情を駆動するため、データソースが噛み合わない)。
                if (IsVRCFTCompatible(root))
                {
                    if (root.GetComponent<Lilium.LiveStudio.VRCFTAvatar>() == null)
                    {
                        Undo.AddComponent<Lilium.LiveStudio.VRCFTAvatar>(root);
                    }
                    VRChatAvatarTransferLog.Info(
                        $"'{root.name}': VRCFT-compatible avatar detected; VRCFTAvatar added instead of VRCAvatar.");
                }
                else
                {
                    // VRChat 由来アバターは VRCAvatar を IAvatar として動かす。
                    // (FX controller の Viseme/Voice パラメータを ARKit から駆動)
                    if (root.GetComponent<Lilium.LiveStudio.VRCAvatar>() == null)
                    {
                        Undo.AddComponent<Lilium.LiveStudio.VRCAvatar>(root);
                    }

                    // VRCAvatarDescriptor の VisemeBlendShape lipsync / eyelid Blink / ExpressionsMenu を
                    // VRCAvatar へ移植する。descriptor が strip される前に実行する必要がある。
                    var vrcAvatar = root.GetComponent<Lilium.LiveStudio.VRCAvatar>();
                    VRCLipSyncConverter.Convert(root, vrcAvatar);
                    VRCEyeBlinkConverter.Convert(root, vrcAvatar);
                    VRCExpressionsConverter.Convert(root, vrcAvatar);
                }

                StripVRChatComponents(root);

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

        /// <summary>
        /// Duplicates the avatar's FX AnimatorController into the output folder, converts any
        /// VRChat VRCAvatarParameterDriver behaviours on the copy into VRCSDK-independent
        /// AvatarParameterDriver, and assigns the copy to the root Animator. The original
        /// FX controller asset is never modified.
        /// </summary>
        private static void ApplyFxAnimatorController(GameObject root, string safeName)
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

            var applied = DuplicateAndConvertFxController(fxController, safeName) ?? fxController;
            animator.runtimeAnimatorController = applied;
            VRChatAvatarTransferLog.Info($"'{root.name}': applied FX animator controller '{applied.name}'.");
        }

        /// <summary>
        /// VRCFT v2 互換アバターかを Animator パラメータ名で判定する。
        /// ApplyFxAnimatorController 実行後 (Animator に複製済み FX controller が割当済み) を前提とする。
        /// </summary>
        private static bool IsVRCFTCompatible(GameObject root)
        {
            var animator = root.GetComponent<Animator>();
            if (animator == null) return false;
            return HasVRCFTV2Parameters(animator.runtimeAnimatorController as AnimatorController);
        }

        /// <summary>
        /// AnimatorController に VRCFT v2 テンプレートの "FT/v2/" 系パラメータが
        /// 1 つでも存在するかを判定する。VRCFT v2 テンプレート (Adjerry91) は 99 個を
        /// 一括で生成するため 1 個閾値でも誤検出のリスクは事実上ない。
        /// 変換前判定 (Window) と変換後判定 (PrefabAssetConverter) で共有する。
        /// </summary>
        internal static bool HasVRCFTV2Parameters(AnimatorController controller)
        {
            const string FT_V2_PREFIX = "FT/v2/";
            if (controller == null) return false;
            foreach (var p in controller.parameters)
            {
                if (p != null && !string.IsNullOrEmpty(p.name) && p.name.StartsWith(FT_V2_PREFIX))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Copies the source FX controller asset into the output folder and converts its
        /// parameter-driver behaviours. Returns the copy, or null when duplication is not
        /// possible (caller falls back to the original controller).
        /// </summary>
        private static AnimatorController DuplicateAndConvertFxController(RuntimeAnimatorController source, string safeName)
        {
            var srcPath = AssetDatabase.GetAssetPath(source);
            if (string.IsNullOrEmpty(srcPath))
            {
                VRChatAvatarTransferLog.Warn(
                    $"FX controller '{source.name}' has no asset path; parameter drivers not converted.");
                return null;
            }

            // ソース非破壊のため必ずコピーして変換する (behaviour 置換は破壊的なため)。
            // 通常の単独 .controller は AssetDatabase.CopyAsset で非破壊コピー、
            // 別アセットのサブアセット (複数 AnimatorController を抱えるコンテナの一部) は
            // CopyAsset で transition が一部欠落する事例があるため、AnimatorControllerCloner で
            // 標準 AnimatorController API のみで deep-clone する。
            var dstPath = $"{Vrm10ObjectBuilder.OutputFolder}/{safeName}.FX.controller";
            if (AssetDatabase.LoadAssetAtPath<Object>(dstPath) != null)
            {
                AssetDatabase.DeleteAsset(dstPath);
            }

            var srcMain = AssetDatabase.LoadMainAssetAtPath(srcPath);
            bool sourceIsSubAsset = !ReferenceEquals(srcMain, source);
            AnimatorController copy;
            if (sourceIsSubAsset)
            {
                if (!(source is AnimatorController srcCtrl))
                {
                    VRChatAvatarTransferLog.Warn(
                        $"FX source '{source.name}' is a sub-asset but not an AnimatorController; parameter drivers not converted.");
                    return null;
                }
                copy = AnimatorControllerCloner.CloneToPath(srcCtrl, dstPath);
            }
            else
            {
                if (!AssetDatabase.CopyAsset(srcPath, dstPath))
                {
                    VRChatAvatarTransferLog.Warn(
                        $"Failed to copy FX controller '{srcPath}' to '{dstPath}'; parameter drivers not converted.");
                    return null;
                }
                copy = AssetDatabase.LoadAssetAtPath<AnimatorController>(dstPath);
            }

            if (copy == null)
            {
                VRChatAvatarTransferLog.Warn(
                    $"Failed to produce a usable AnimatorController copy at '{dstPath}'; parameter drivers not converted.");
                return null;
            }

            AvatarParameterDriverConverter.Convert(copy);
            AvatarAnimatorTrackingControlConverter.Convert(copy);
            return copy;
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

            // 元 VRChat アバターには、この非 VRChat プロジェクトに存在しない
            // サードパーティ製コンポーネントが Missing Script として残ることがある。
            // SaveAsPrefabAsset は Missing Script を含む prefab の保存を拒否するため除去する。
            int missingRemoved = 0;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == null) continue;
                missingRemoved += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
            }
            if (missingRemoved > 0)
            {
                VRChatAvatarTransferLog.Info($"'{root.name}': removed {missingRemoved} missing script(s).");
            }
        }
    }
}
