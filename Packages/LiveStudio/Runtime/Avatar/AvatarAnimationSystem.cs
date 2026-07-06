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

        // Time-based interpolation between two received frames so the avatar pose
        // advances smoothly every render frame even when the render rate exceeds the
        // capture rate (60fps). Without this, the pose freezes between received frames
        // and secondary physics (spring bones) jitters on the stalled pose.
        //
        // a/b are passed by value: AsRotation/AsCamera are not readonly, so accessing
        // them on an `in` parameter would force a defensive copy per element. Copying
        // once up front and reading from the locals avoids that.
        public static void Lerp(AvatarAnimationData a, AvatarAnimationData b, float t, out AvatarAnimationData dst)
        {
            dst = b;

            // Combine tracking bitmasks per-bit: a body/face bit stays set only while both
            // interpolation endpoints have it (the former AND semantics, now per-flag).
            dst.root.valid = (byte)(a.root.valid & b.root.valid);
            dst.root.position = Vector3.Lerp(a.root.position, b.root.position, t);
            dst.root.rotation = Quaternion.Slerp(a.root.rotation, b.root.rotation, t);
            dst.root.scale = Vector3.Lerp(a.root.scale, b.root.scale, t);

            dst.pose.hipPosition = Vector3.Lerp(a.pose.hipPosition, b.pose.hipPosition, t);
            for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
            {
                dst.pose.AsRotation(i) = Quaternion.Slerp(a.pose.AsRotation(i), b.pose.AsRotation(i), t);
                dst.pose.AsPresence(i) = Mathf.Lerp(a.pose.AsPresence(i), b.pose.AsPresence(i), t);
            }

            for (int i = 0; i < (int)ARKitBlendShapeLocation.Max; i++)
            {
                var location = (ARKitBlendShapeLocation)i;
                dst.expression.AtWeight(location) = Mathf.Lerp(a.expression.AtWeight(location), b.expression.AtWeight(location), t);
            }

            for (int i = 0; i < AvatarAnimationData.kCameraChannelCount; i++)
            {
                ref CameraData ca = ref a.AsCamera(i);
                ref CameraData cb = ref b.AsCamera(i);
                ref CameraData cd = ref dst.AsCamera(i);
                cd.position = Vector3.Lerp(ca.position, cb.position, t);
                cd.rotation = Quaternion.Slerp(ca.rotation, cb.rotation, t);
                cd.fieldOfView = Mathf.Lerp(ca.fieldOfView, cb.fieldOfView, t);
                cd.nearClipPlane = Mathf.Lerp(ca.nearClipPlane, cb.nearClipPlane, t);
                cd.farClipPlane = Mathf.Lerp(ca.farClipPlane, cb.farClipPlane, t);
                cd.aspect = Mathf.Lerp(ca.aspect, cb.aspect, t);
            }

            dst.frames = b.frames;
        }

        public static void Transform(in AvatarAnimationData src, in Matrix4x4 matrix, out AvatarAnimationData dst)
        {
            dst = src;
            dst.root.position = matrix.MultiplyPoint(src.root.position);
            dst.root.rotation = matrix.rotation * src.root.rotation;
            dst.root.scale = Vector3.Scale(matrix.lossyScale, src.root.scale);

            // dst already holds a copy of src, so read camera values from dst to avoid
            // defensive copies when invoking AsCamera on the readonly `in` parameter.
            for (int i = 0; i < AvatarAnimationData.kCameraChannelCount; i++)
            {
                ref CameraData cam = ref dst.AsCamera(i);
                Vector3 position = cam.position;
                Quaternion rotation = cam.rotation;
                cam.position = matrix.MultiplyPoint(position);
                cam.rotation = matrix.rotation * rotation;
            }
        }

        public static void MakeRoot(Transform transform, out AvatarRootData dst)
        {
            Debug.Assert(transform != null);
            dst = new AvatarRootData
            {
                // Locally-built frame is fully tracked (body + face).
                valid = AvatarRootData.kBodyValidFlag | AvatarRootData.kFaceValidFlag,
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
