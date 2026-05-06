// Copyright (c) You-Ri, 2026

using UnityEngine;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Conversion routines between an <see cref="Animator"/> and the
    /// role-specific avatar data structs (<see cref="AvatarRootData"/>,
    /// <see cref="HumanoidPoseData"/>, <see cref="AvatarAnimationData"/>).
    /// </summary>
    public static class AvatarAnimationSystem
    {
        public static void MakeAvatarAnimationData(Animator animator, out AvatarAnimationData dst)
        {
            Debug.Assert(animator != null);

            dst = new AvatarAnimationData();
            dst.root.valid = 1;
            MakeRoot(animator.transform, out dst.root);
            MakePose(animator, out dst.pose);
        }

        public static void MakeInvalidAvatarAnimationData(out AvatarAnimationData dst)
        {
            dst = new AvatarAnimationData();
            dst.root.valid = 0;
        }

        public static void UpdateBodyAnimation(Animator animator, in AvatarAnimationData src)
        {
            Debug.Assert(animator != null);

            UpdateRoot(animator.transform, in src.root);
            UpdatePose(animator, in src.pose);
        }

        public static void Transform(in AvatarAnimationData src, in Matrix4x4 matrix, out AvatarAnimationData dst)
        {
            dst = src;
            dst.root.position = matrix.MultiplyPoint(src.root.position);
            dst.root.rotation = matrix.rotation * src.root.rotation;
            dst.root.scale = Vector3.Scale(matrix.lossyScale, src.root.scale);

            dst.camera.position = matrix.MultiplyPoint(src.camera.position);
            dst.camera.rotation = matrix.rotation * src.camera.rotation;
        }

        public static void MakeRoot(Transform transform, out AvatarRootData dst)
        {
            Debug.Assert(transform != null);
            dst = new AvatarRootData
            {
                valid = 1,
                position = transform.localPosition,
                rotation = transform.localRotation,
                scale = transform.localScale,
            };
        }

        public static void UpdateRoot(Transform transform, in AvatarRootData src)
        {
            Debug.Assert(transform != null);
            transform.position = src.position;
            transform.rotation = src.rotation;
            transform.localScale = src.scale;
        }

        public static void MakePose(Animator animator, out HumanoidPoseData dst)
        {
            Debug.Assert(animator != null);

            dst = new HumanoidPoseData();
            dst.hipPosition = animator.GetBoneTransform(HumanBodyBones.Hips).localPosition;

            for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
            {
                var bone = animator.GetBoneTransform((HumanBodyBones)i);
                if (bone == null) continue;
                dst.AsRotation(i) = bone.localRotation;
            }
        }

        public static void UpdatePose(Animator animator, in HumanoidPoseData src)
        {
            Debug.Assert(animator != null);

            animator.GetBoneTransform(HumanBodyBones.Hips).localPosition = src.hipPosition;

            for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
            {
                var bone = animator.GetBoneTransform((HumanBodyBones)i);
                if (bone == null) continue;
                bone.localRotation = src.AsRotation(i);
            }
        }
    }
}
