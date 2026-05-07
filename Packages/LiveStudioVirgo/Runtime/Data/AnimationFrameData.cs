// Copyright (c) You-Ri, 2026
//
// ⚠ 同期注意: このファイルは jp.lilium.virgo.capture と jp.lilium.livestudio.virgo に
//   複製されています。片方を変更したときは必ずもう片方も同じ内容に更新してください。
//   ペア: Packages/jp.lilium.virgo.capture/Runtime/Data/AnimationFrameData.cs
//   namespace のみ Lilium.Virgo.Capture / Lilium.LiveStudio.Virgo で異なります。

using UnityEngine;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Lilium.RemoteControl;

namespace Lilium.LiveStudio.Virgo
{
    public unsafe struct AnimationFrameData
    {
        static AnimationFrameData() => CompilerUtility.CheckBlittable<AnimationFrameData>();

        // Must match Lilium.LiveStudio.ARKitBlendShapeLocation.Max
        public const int kBlendShapeCount = 52;

        public bool isValid => valid!= 0 && Mathf.Approximately(camera.fieldOfView, 0f) == false;

        public byte valid;

        public CameraData camera;

        public Vector3 position;

        public Quaternion rotation;

        public Vector3 scale;

        public Vector3 hipPosition;

        public long frames;

        public fixed byte boneRotations[(int)HumanBodyBones.LastBone * CompilerUtility.QuaternionSize];

        public fixed float blendShapes[kBlendShapeCount];

        public Vector2 eyeDirection;

        public ref Quaternion AsRotation(int index)
        {
            Debug.Assert(index >= 0 && index < (int)HumanBodyBones.LastBone);
            return ref UnsafeUtility.AsRef<Quaternion>(UnsafeUtility.AddressOf(ref boneRotations[index * CompilerUtility.QuaternionSize]));
        }

        public ref float AsBlendShape(int index)
        {
            Debug.Assert(index >= 0 && index < kBlendShapeCount);
            return ref UnsafeUtility.AsRef<float>(UnsafeUtility.AddressOf(ref blendShapes[index * sizeof(float)]));
        }
    }
}
