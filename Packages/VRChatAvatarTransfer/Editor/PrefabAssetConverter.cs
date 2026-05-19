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

                // VRChat 由来アバターは VRCAvatar を IAvatar として動かす。
                // (FX controller の Viseme/Voice パラメータを ARKit から駆動)
                if (root.GetComponent<Lilium.LiveStudio.VRCAvatar>() == null)
                {
                    Undo.AddComponent<Lilium.LiveStudio.VRCAvatar>(root);
                }

                // VRCAvatarDescriptor の VisemeBlendShape lipsync 設定を VRCAvatar へ移植する。
                // descriptor が strip される前に実行する必要がある。
                var vrcAvatar = root.GetComponent<Lilium.LiveStudio.VRCAvatar>();
                VRCLipSyncConverter.Convert(root, vrcAvatar);

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

            // NDMF / Modular Avatar のベイク出力では、複数レイヤー (Base/Action/Gesture/FX...)
            // のコントローラーが 1 つの SubAssetContainer 内のサブアセットとしてまとまっている。
            // VRCAvatarDescriptor の FX playable layer 参照 (source) こそが目的のコントローラー
            // なので、その local file id でコピー内の対応サブアセットを一意に特定する。
            // (LoadAssetAtPath<AnimatorController> はコンテナ内で最初に見つかった
            // コントローラー = 別レイヤーを返してしまうため誤アサインになっていた)
            long srcLocalId = 0;
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source, out _, out srcLocalId);
            var srcName = source.name;

            var dstPath = $"{Vrm10ObjectBuilder.OutputFolder}/{safeName}.FX.controller";
            if (AssetDatabase.LoadAssetAtPath<Object>(dstPath) != null)
            {
                AssetDatabase.DeleteAsset(dstPath);
            }
            if (!AssetDatabase.CopyAsset(srcPath, dstPath))
            {
                VRChatAvatarTransferLog.Warn(
                    $"Failed to copy FX controller '{srcPath}' to '{dstPath}'; parameter drivers not converted.");
                return null;
            }

            var copy = FindCopiedFxController(dstPath, srcLocalId, srcName);
            if (copy == null)
            {
                VRChatAvatarTransferLog.Warn(
                    $"Copied FX asset '{dstPath}' contains no AnimatorController matching the source FX layer '{srcName}'; parameter drivers not converted.");
                return null;
            }

            AvatarParameterDriverConverter.Convert(copy);
            AvatarAnimatorTrackingControlConverter.Convert(copy);
            return copy;
        }

        /// <summary>
        /// Resolves the copied FX <see cref="AnimatorController"/> at <paramref name="assetPath"/>
        /// that corresponds to the descriptor's FX playable-layer controller. Matching is by the
        /// source's local file identifier (AssetDatabase.CopyAsset preserves sub-asset local ids),
        /// which is unambiguous for multi-controller containers (NDMF / Modular Avatar bake).
        /// Falls back to name match, then to the sole controller for a standalone .controller asset.
        /// </summary>
        private static AnimatorController FindCopiedFxController(string assetPath, long sourceLocalId, string controllerName)
        {
            var all = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            AnimatorController firstController = null;
            AnimatorController nameMatch = null;
            int controllerCount = 0;
            foreach (var obj in all)
            {
                if (!(obj is AnimatorController ac)) continue;
                controllerCount++;
                if (firstController == null) firstController = ac;

                if (sourceLocalId != 0
                    && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(ac, out _, out long localId)
                    && localId == sourceLocalId)
                {
                    return ac; // descriptor の FX 参照と同一実体
                }
                if (nameMatch == null && ac.name == controllerName) nameMatch = ac;
            }
            if (nameMatch != null) return nameMatch;
            // 単独 .controller (サブアセット 1 個) は一致条件を満たさなくてもそれを使う。
            return controllerCount == 1 ? firstController : null;
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
