// Copyright (c) You-Ri, 2026

using UnityEngine;
using Unity.Collections.LowLevel.Unsafe;
using Lilium.RemoteControl;
using Lilium.RemoteControl.Frames;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// A humanoid pose in muscle space: what the body is doing, said in a way that does not name a
    /// particular model's skeleton.
    ///
    /// This is the form the pose is recorded in, and that is why it is muscle space rather than bone
    /// rotations. A bone rotation only means something against the rest pose it was authored on, so a
    /// take recorded that way can only ever be replayed on the model it was captured with -- swap the
    /// avatar and the arms go somewhere else. Muscle values are normalized against each joint's own
    /// range, and <see cref="bodyPosition"/> is divided by the avatar's scale, so the same numbers
    /// describe the same pose on a different body.
    ///
    /// Presence sits beside each muscle rather than being folded into it, because a muscle whose
    /// presence is zero is not "relaxed" -- it is unknown, and the avatar's own animation should show
    /// through there instead.
    /// </summary>
    public unsafe struct HumanoidPoseData
    {
        /// <summary>
        /// <see cref="HumanTrait.MuscleCount"/>, as a constant so it can size a fixed buffer. Checked
        /// against the real one on first use: a Unity release that changes it would otherwise be
        /// found as a wrong-looking pose rather than as an error.
        /// </summary>
        public const int kMuscleCount = 95;

        static HumanoidPoseData()
        {
            CompilerUtility.CheckBlittable<HumanoidPoseData>();

            if (HumanTrait.MuscleCount != kMuscleCount)
            {
                Debug.LogError($"[Core] HumanTrait.MuscleCount is {HumanTrait.MuscleCount}, not {kMuscleCount}. " +
                               "The pose layout no longer matches Unity's muscle table.");
            }
        }

        /// <summary>
        /// Body position relative to the avatar root, normalized by the avatar's scale
        /// (<see cref="HumanPose.bodyPosition"/>). Measured: two rigs of different size holding the
        /// same pose report the same value here, which is what makes a recording model-independent.
        /// </summary>
        public Vector3 bodyPosition;

        /// <summary>Body rotation relative to the avatar root (<see cref="HumanPose.bodyRotation"/>).</summary>
        public Quaternion bodyRotation;

        /// <summary>
        /// Tracking presence (0..1) of <see cref="bodyPosition"/> and <see cref="bodyRotation"/>.
        /// The hips carry no muscle of their own, so their presence is carried here.
        /// </summary>
        public float bodyPresence;

        // Shown with its presence beside it, because a muscle whose presence is zero is last frame's
        // value rather than data.
        [LiveArray(typeof(float), labels = typeof(HumanMuscle), pairedWith = nameof(musclePresences))]
        public fixed float muscles[kMuscleCount];

        // Per-muscle tracking presence (0..1). 1 = fully tracked (use the mocap value), 0 = not
        // tracked (let the avatar's own animation flow through). Indexed by <see cref="HumanMuscle"/>,
        // matching <see cref="muscles"/>.
        [LiveArray(typeof(float), labels = typeof(HumanMuscle))]
        public fixed float musclePresences[kMuscleCount];

        public ref float AsMuscle(int index)
        {
            Debug.Assert(index >= 0 && index < kMuscleCount);
            return ref UnsafeUtility.AsRef<float>(UnsafeUtility.AddressOf(ref muscles[index]));
        }

        public ref float AsMusclePresence(int index)
        {
            Debug.Assert(index >= 0 && index < kMuscleCount);
            return ref UnsafeUtility.AsRef<float>(UnsafeUtility.AddressOf(ref musclePresences[index]));
        }
    }
}
