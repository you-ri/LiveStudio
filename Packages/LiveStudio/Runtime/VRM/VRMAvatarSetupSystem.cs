#if VRMC_VRM10
using System.Collections;
using System.Collections.Generic;
using System.Linq;

using UnityEngine;

using VRM;
using UniVRM10;

using GameObjectUtility = Lilium.RemoteControl.GameObjectUtility;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Lilium.LiveStudio
{


    public static class VRMAvatarSetupSystem
    {



        private static bool HasPerfectSyncSupport(Vrm10Instance vrm10Instance)
        {
            if (vrm10Instance == null || vrm10Instance.Vrm == null || vrm10Instance.Vrm.Expression == null)
            {
                return false;
            }

            foreach (var key in ARKitBlendShapeUtility.ARKitToString)
            {
                if (vrm10Instance.Vrm.Expression.Clips.Any(t => string.Equals(t.Clip.name, key.Value, System.StringComparison.OrdinalIgnoreCase)))
                {
                    return true; // PerfectSync 対応の表情が見つかった
                }
            }
            return false;
        }

        private static bool HasPerfectSyncSupport(VRMBlendShapeProxy blendShapeProxy)
        {
            if (blendShapeProxy == null || blendShapeProxy.BlendShapeAvatar == null)
            {
                return false;
            }

            var clips = blendShapeProxy.BlendShapeAvatar.Clips;
            if (clips == null) return false;

            foreach (var key in ARKitBlendShapeUtility.ARKitToString)
            {
                if (clips.Any(t => t != null && string.Equals(t.BlendShapeName, key.Value, System.StringComparison.OrdinalIgnoreCase)))
                {
                    return true; // PerfectSync 対応の表情が見つかった
                }
            }
            return false;
        }


        private static void SetupArmsTwistRelaxer(
            GameObject postRigPrefab, Animator animator, string armName,
            HumanBodyBones leftUpperArm, HumanBodyBones leftLowerArm, HumanBodyBones leftHand,
            HumanBodyBones rightUpperArm, HumanBodyBones rightLowerArm, HumanBodyBones rightHand)
        {
            Debug.Assert(postRigPrefab != null, "PostRig Arm Prefab is null.");
            var postRigArm = GameObjectUtility.GetOrCreateInstanceFromPrefab(armName, postRigPrefab, animator.transform);

            var twistRelaxer = postRigArm.GetComponent<BoneTwistController>();
            Debug.Assert(twistRelaxer != null, "BoneTwistController component not found on PostRig Arm prefab.");

            ref var leftUpperArmTwistSolver = ref twistRelaxer.twistSolvers[0];
            leftUpperArmTwistSolver.targetBone = animator.GetBoneTransform(leftUpperArm);
            leftUpperArmTwistSolver.childBone = animator.GetBoneTransform(leftLowerArm);

            ref var leftLowerArmTwistSolver = ref twistRelaxer.twistSolvers[1];
            leftLowerArmTwistSolver.targetBone = animator.GetBoneTransform(leftLowerArm);
            leftLowerArmTwistSolver.childBone = animator.GetBoneTransform(leftHand);

            ref var rightUpperArmTwistSolver = ref twistRelaxer.twistSolvers[2];
            rightUpperArmTwistSolver.targetBone = animator.GetBoneTransform(rightUpperArm);
            rightUpperArmTwistSolver.childBone = animator.GetBoneTransform(rightLowerArm);

            ref var rightLowerArmTwistSolver = ref twistRelaxer.twistSolvers[3];
            rightLowerArmTwistSolver.targetBone = animator.GetBoneTransform(rightLowerArm);
            rightLowerArmTwistSolver.childBone = animator.GetBoneTransform(rightHand);

            GameObjectUtility.SetDirty(twistRelaxer);
        }

        public static void SetupVRMTargetAvatar(GameObject avatar, VRMAvatarSetupSettings settings)
        {
            Debug.Assert(avatar != null, "Avatar GameObject is null.");
            Debug.Assert(settings != null, "VRMAvatarSetupSettings is null.");

            bool hasVrm1Model = avatar.GetComponent<Vrm10Instance>() != null;

            if (hasVrm1Model)
            {
                var vrm10Instance = avatar.GetComponent<Vrm10Instance>();
                var facialController = GameObjectUtility.GetOrAddComponent<VRM1Avatar>(avatar);

                // PerfectSync 対応モデルかをチェック
                facialController.expressionMode = HasPerfectSyncSupport(vrm10Instance) ? ExpressionMode.PerfectSync : ExpressionMode.Preset;
                SetUpdateWhenOffscreen(avatar, true);
            }
            else
            {
                // VRM 0.x モデルの場合
                var blendShapeProxy = avatar.GetComponent<VRMBlendShapeProxy>();
                if (blendShapeProxy != null)
                {
                    var facialController = GameObjectUtility.GetOrAddComponent<VRM0Avatar>(avatar);

                    // PerfectSync 対応モデルかをチェック
                    facialController.expressionMode = HasPerfectSyncSupport(blendShapeProxy) ? ExpressionMode.PerfectSync : ExpressionMode.Preset;
                    SetUpdateWhenOffscreen(avatar, true);
                }
            }


            // 腕のコンストレイントが設定済みか？
            if (avatar.GetComponentsInChildren<Vrm10RollConstraint>(true).Length == 0)
            {
                // 両腕のTwistRelaxerを設定
                SetupArmsTwistRelaxer(
                    settings.armsPostRigPrefab, avatar.GetComponent<Animator>(), "Arms Post Rig",
                    HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
                    HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand);
            }


        }

        public static void SetUpdateWhenOffscreen(GameObject avatar, bool value)
        {
            // SkinnedMeshRendererのupdateWhenOffscreenを有効にする
            var skinnedMeshRenderers = avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int i = 0; i < skinnedMeshRenderers.Length; i++)
            {
                if (skinnedMeshRenderers[i].sharedMesh != null)
                {
                    skinnedMeshRenderers[i].updateWhenOffscreen = value;
                }
            }
        }

        // skeletonBoneに対応するHumanBodyBoneを取得
        private static HumanBodyBones? GetHumanBodyBoneFromSkeletonBone(UnityEngine.SkeletonBone skeletonBone, HumanDescription humanDescription)
        {
            // HumanBone配列からboneNameが一致するものを検索
            var humanBone = System.Array.Find(humanDescription.human, hb => hb.boneName == skeletonBone.name);

            if (!string.IsNullOrEmpty(humanBone.humanName))
            {
                // humanNameからHumanBodyBonesへ変換
                if (System.Enum.TryParse<HumanBodyBones>(humanBone.humanName, out var bodyBone))
                {
                    return bodyBone;
                }
            }

            return null; // 対応するHumanBodyBoneが見つからない
        }


    }
}
#endif