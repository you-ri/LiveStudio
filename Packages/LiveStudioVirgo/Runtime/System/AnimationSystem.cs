// Copyright (c) You-Ri, 2026
//
// ⚠ 同期注意: このファイルは jp.lilium.virgo.capture と jp.lilium.livestudio.virgo に
//   複製されています。片方を変更したときは必ずもう片方も同じ内容に更新してください。
//   ペア: Packages/jp.lilium.virgo.capture/Runtime/System/AnimationSystem.cs
//   namespace のみ Lilium.Virgo.Capture / Lilium.LiveStudio.Virgo で異なります。

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;

namespace Lilium.LiveStudio.Virgo
{

    /// <summary>
    /// AnimationFrameDataからアニメーションを合成するためのシステム
    /// </summary>
    public static class AnimationSystem
    {
        public static void MakeAnimationFrameData(Animator animator, out AnimationFrameData dst)
        {
            Debug.Assert(animator != null);

            dst = new AnimationFrameData();
            dst.valid = 1;
            dst.position = animator.transform.localPosition;
            dst.rotation = animator.transform.localRotation;
            dst.scale = animator.transform.localScale;
            dst.hipPosition = animator.GetBoneTransform(HumanBodyBones.Hips).localPosition;

            for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
            {
                var bone = animator.GetBoneTransform((HumanBodyBones)i);
                if (bone == null) continue;
                dst.AsRotation(i) = bone.localRotation;
            }
        }

        public static void MakeInvalidAnimationFrameData(out AnimationFrameData dst)
        {
            dst = new AnimationFrameData();
            dst.valid = 0;
        }


        public static void UpdateBodyAnimation(Animator animator, in AnimationFrameData src)
        {
            Debug.Assert(animator != null);

            animator.transform.position = src.position;
            animator.transform.rotation = src.rotation;
            animator.transform.localScale = src.scale;
            animator.GetBoneTransform(HumanBodyBones.Hips).localPosition = src.hipPosition;

            for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
            {
                var bone = animator.GetBoneTransform((HumanBodyBones)i);
                if (bone == null) continue;
                bone.localRotation = src.AsRotation(i);
            }
        }

        public static void Transform(in AnimationFrameData src, in Matrix4x4 matrix, out AnimationFrameData dst)
        {
            dst = src;
            dst.position = matrix.MultiplyPoint(src.position);
            dst.rotation = matrix.rotation * src.rotation;
            dst.scale = Vector3.Scale(matrix.lossyScale, src.scale);
            //dst.hipPosition = matrix.MultiplyPoint(src.hipPosition);

            dst.camera.position = matrix.MultiplyPoint(src.camera.position);
            dst.camera.rotation = matrix.rotation * src.camera.rotation;
        }
    }

}
