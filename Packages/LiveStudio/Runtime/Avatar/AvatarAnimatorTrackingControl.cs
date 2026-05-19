// Copyright (c) You-Ri, 2026

using UnityEngine;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Whether a body region is driven by external tracking or by the animator.
    /// Values match VRChat's VRC_AnimatorTrackingControl.TrackingType so converted
    /// data maps one-to-one (NoChange = leave as-is).
    /// </summary>
    public enum AvatarTrackingType
    {
        NoChange = 0,
        Tracking = 1,
        Animation = 2,
    }

    /// <summary>
    /// Non-VRChat replacement for VRChat's VRCAnimatorTrackingControl. Attached to
    /// AnimatorController states; on state enter it pushes the per-region tracking
    /// modes onto the <see cref="VRCAvatar"/> on the animated GameObject, which uses
    /// them to gate mouth (Viseme/Voice) and eye reproduction.
    /// Clean-room implementation: references only the public, functional API shape
    /// and contains no VRChat SDK code.
    /// </summary>
    public class AvatarAnimatorTrackingControl : StateMachineBehaviour
    {
        public AvatarTrackingType trackingHead = AvatarTrackingType.NoChange;
        public AvatarTrackingType trackingLeftHand = AvatarTrackingType.NoChange;
        public AvatarTrackingType trackingRightHand = AvatarTrackingType.NoChange;
        public AvatarTrackingType trackingHip = AvatarTrackingType.NoChange;
        public AvatarTrackingType trackingLeftFoot = AvatarTrackingType.NoChange;
        public AvatarTrackingType trackingRightFoot = AvatarTrackingType.NoChange;
        public AvatarTrackingType trackingLeftFingers = AvatarTrackingType.NoChange;
        public AvatarTrackingType trackingRightFingers = AvatarTrackingType.NoChange;
        public AvatarTrackingType trackingEyes = AvatarTrackingType.NoChange;
        public AvatarTrackingType trackingMouth = AvatarTrackingType.NoChange;

        public string debugString;

        public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            // VRCAvatar is required to live on the animated GameObject (it carries
            // [RequireComponent(typeof(Animator))]). Only it consumes tracking state.
            var avatar = animator.GetComponent<VRCAvatar>();
            if (avatar != null)
            {
                avatar.ApplyTrackingControl(this);
            }
        }
    }
}
