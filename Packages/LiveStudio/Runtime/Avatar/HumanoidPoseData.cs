// Copyright (c) You-Ri, 2026

using UnityEngine;
using Unity.Collections.LowLevel.Unsafe;
using Lilium.RemoteControl;

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

        public fixed byte boneRotations[(int)HumanBodyBones.LastBone * CompilerUtility.QuaternionSize];

        public ref Quaternion AsRotation(int index)
        {
            Debug.Assert(index >= 0 && index < (int)HumanBodyBones.LastBone);
            return ref UnsafeUtility.AsRef<Quaternion>(UnsafeUtility.AddressOf(ref boneRotations[index * CompilerUtility.QuaternionSize]));
        }
    }
}
