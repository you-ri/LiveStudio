// Copyright (c) You-Ri, 2026

using UnityEngine;
using Unity.Collections.LowLevel.Unsafe;
using Lilium.RemoteControl;
using Lilium.RemoteControl.Frames;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Humanoid bone rotations and the local hip position for a single frame.
    /// Storage layout matches <see cref="HumanBodyBones"/> indexing up to
    /// <see cref="HumanBodyBones.LastBone"/>.
    /// </summary>
    public unsafe struct HumanoidPoseData
    {
        static HumanoidPoseData() => CompilerUtility.CheckBlittable<HumanoidPoseData>();

        public Vector3 hipPosition;

        // Quaternions, not bytes -- the buffer is bytes only because a fixed buffer of a struct is
        // not a thing C# has. Shown with its presence beside it, because a rotation whose presence is
        // zero is last frame's value rather than data.
        [LiveArray(typeof(Quaternion), labels = typeof(HumanBodyBones),
            pairedWith = nameof(bonePresences))]
        public fixed byte boneRotations[(int)HumanBodyBones.LastBone * CompilerUtility.QuaternionSize];

        // Per-bone tracking presence (0..1). 1 = fully tracked (use the mocap rotation),
        // 0 = not tracked (let the avatar's animation flow through). Indexed by
        // <see cref="HumanBodyBones"/>, matching <see cref="boneRotations"/>.
        public fixed float bonePresences[(int)HumanBodyBones.LastBone];

        public ref Quaternion AsRotation(int index)
        {
            Debug.Assert(index >= 0 && index < (int)HumanBodyBones.LastBone);
            return ref UnsafeUtility.AsRef<Quaternion>(UnsafeUtility.AddressOf(ref boneRotations[index * CompilerUtility.QuaternionSize]));
        }

        public ref float AsPresence(int index)
        {
            Debug.Assert(index >= 0 && index < (int)HumanBodyBones.LastBone);
            return ref UnsafeUtility.AsRef<float>(UnsafeUtility.AddressOf(ref bonePresences[index]));
        }
    }
}
