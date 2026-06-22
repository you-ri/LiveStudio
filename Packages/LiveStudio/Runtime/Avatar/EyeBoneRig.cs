// Copyright (c) You-Ri, 2026

using UnityEngine;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Shared eye-bone control for avatar components that rotate the eyes by bone
    /// (VRCAvatar / VRCFTAvatar). Caches the Left/Right eye bones together with each
    /// bone's neutral local rotation and its head-relative offset, then applies a
    /// yaw/pitch look direction back onto the bones. Pure transform math, no allocation;
    /// the only difference between callers is the source of the gaze values, which are
    /// passed into <see cref="Apply"/>.
    /// </summary>
    public struct EyeBoneRig
    {
        Transform _leftEye;
        Transform _rightEye;
        Quaternion _leftNeutral;
        Quaternion _rightNeutral;
        Quaternion _leftOffset;
        Quaternion _rightOffset;

        /// <summary>True once at least one eye bone has been resolved.</summary>
        public bool hasEyeBones => _leftEye != null || _rightEye != null;

        /// <summary>
        /// Resolves the eye bones from the animator and caches each bone's neutral
        /// rotation and head-relative offset. Call once after the avatar rig exists.
        /// </summary>
        public void Setup(Animator animator)
        {
            _leftEye = animator.GetBoneTransform(HumanBodyBones.LeftEye);
            _rightEye = animator.GetBoneTransform(HumanBodyBones.RightEye);
            var headBone = animator.GetBoneTransform(HumanBodyBones.Head);

            if (_leftEye != null && headBone != null)
            {
                _leftNeutral = _leftEye.localRotation;
                _leftOffset = Quaternion.Inverse(headBone.rotation) * _leftEye.rotation;
            }
            if (_rightEye != null && headBone != null)
            {
                _rightNeutral = _rightEye.localRotation;
                _rightOffset = Quaternion.Inverse(headBone.rotation) * _rightEye.rotation;
            }
        }

        /// <summary>
        /// Rotates each eye bone toward the given gaze. Values are normalized gaze
        /// components (eyeLeftX positive = LookOut for the left eye); <paramref name="max"/>
        /// is the per-axis maximum angle in degrees. No-op for bones that were not found.
        /// </summary>
        public void Apply(float eyeLeftX, float eyeLeftY, float eyeRightX, float eyeRightY, Vector2 max)
        {
            if (_leftEye != null)
            {
                // EyeLeftX: 正=左向き(LookOut) なので反転して正=右向きに統一
                float yaw = eyeLeftX * max.x;
                float pitch = eyeLeftY * max.y;
                Vector3 input = new Vector3(-pitch, -yaw, 0f);
                _leftEye.localRotation = Quaternion.Inverse(_leftOffset) * Quaternion.Euler(input) * _leftOffset * _leftNeutral;
            }

            if (_rightEye != null)
            {
                float yaw = -eyeRightX * max.x;
                float pitch = eyeRightY * max.y;
                Vector3 input = new Vector3(-pitch, -yaw, 0f);
                _rightEye.localRotation = Quaternion.Inverse(_rightOffset) * Quaternion.Euler(input) * _rightOffset * _rightNeutral;
            }
        }
    }
}
