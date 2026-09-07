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
        public static void MakeInvalidAvatarAnimationData(out AvatarAnimationData dst)
        {
            dst = new AvatarAnimationData();
            dst.root.valid = 0;
        }

        /// <summary>
        /// Writes a frame onto an avatar directly (no PlayableGraph): the root transform, then the
        /// pose through <paramref name="poseHandler"/>.
        ///
        /// The handler is the caller's because it is bound to one avatar and owns native memory --
        /// building one per frame would allocate, and building one here would leave nobody to
        /// dispose it. Callers that drive an avatar every frame keep one for the life of that avatar.
        /// </summary>
        public static void UpdateBodyAnimation(Animator animator, HumanPoseHandler poseHandler, in AvatarAnimationData src)
        {
            Debug.Assert(animator != null);

            UpdateRoot(animator.transform, in src.root);
            UpdatePose(poseHandler, in src.pose);
        }

        // Time-based interpolation between two received frames so the avatar pose
        // advances smoothly every render frame even when the render rate exceeds the
        // capture rate (60fps). Without this, the pose freezes between received frames
        // and secondary physics (spring bones) jitters on the stalled pose.
        //
        // a/b are passed by value: AsMuscle/AsCamera are not readonly, so accessing
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

            dst.pose.bodyPosition = Vector3.Lerp(a.pose.bodyPosition, b.pose.bodyPosition, t);
            dst.pose.bodyRotation = Quaternion.Slerp(a.pose.bodyRotation, b.pose.bodyRotation, t);
            dst.pose.bodyPresence = Mathf.Lerp(a.pose.bodyPresence, b.pose.bodyPresence, t);
            for (int i = 0; i < HumanoidPoseData.kMuscleCount; i++)
            {
                // Muscles are scalars, so this is a plain lerp -- and a better-behaved one than the
                // slerp per bone it replaces: interpolating two joint angles cannot swing a limb
                // through a path neither endpoint took.
                dst.pose.AsMuscle(i) = Mathf.Lerp(a.pose.AsMuscle(i), b.pose.AsMuscle(i), t);
                dst.pose.AsMusclePresence(i) = Mathf.Lerp(a.pose.AsMusclePresence(i), b.pose.AsMusclePresence(i), t);
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

        /// <summary>
        /// Reads an avatar's current pose in muscle space.
        /// </summary>
        public static void MakePose(HumanPoseHandler poseHandler, out HumanoidPoseData dst)
        {
            Debug.Assert(poseHandler != null);

            dst = new HumanoidPoseData();
            _EnsureScratch();
            poseHandler.GetHumanPose(ref _scratch);

            dst.bodyPosition = _scratch.bodyPosition;
            dst.bodyRotation = _scratch.bodyRotation;
            dst.bodyPresence = 1f;
            for (int i = 0; i < HumanoidPoseData.kMuscleCount; i++)
            {
                dst.AsMuscle(i) = _scratch.muscles[i];
                dst.AsMusclePresence(i) = 1f;
            }
        }

        /// <summary>
        /// Writes a muscle-space pose onto an avatar. Presence is not consulted: this path has no
        /// upstream animation to blend an untracked part against, so the whole pose is applied.
        /// </summary>
        public static void UpdatePose(HumanPoseHandler poseHandler, in HumanoidPoseData src)
        {
            Debug.Assert(poseHandler != null);

            _EnsureScratch();
            _scratch.bodyPosition = src.bodyPosition;
            _scratch.bodyRotation = src.bodyRotation;
            for (int i = 0; i < HumanoidPoseData.kMuscleCount; i++)
            {
                // The `in` parameter would be copied defensively on every AsMuscle call (it is not a
                // readonly member), so the read goes through a local copy of the index instead.
                _scratch.muscles[i] = _MuscleOf(in src, i);
            }

            poseHandler.SetHumanPose(ref _scratch);
        }

        private static float _MuscleOf(in HumanoidPoseData src, int index)
        {
            unsafe
            {
                fixed (float* muscles = src.muscles) return muscles[index];
            }
        }

        // Reused across calls so applying a pose does not allocate a 95-float array every frame.
        // Lazily filled rather than initialized from a hook, so it is there whether or not the
        // domain was reloaded.
        private static HumanPose _scratch;

        private static void _EnsureScratch()
        {
            if (_scratch.muscles == null || _scratch.muscles.Length != HumanoidPoseData.kMuscleCount)
                _scratch.muscles = new float[HumanoidPoseData.kMuscleCount];
        }
    }
}
