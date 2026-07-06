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

        // 送信するカメラの本数。capture の CaptureDataHolder.maxChannels と一致させること
        // (channel0 / channel1 = clients[0] / clients[1])。
        public const int kCameraChannelCount = 2;

        // valid はトラッキング状態のビットマスク。bit0 = body (MediaPipe) 追跡中、
        // bit1 = face (ARKit) 追跡中。0 以外なら「いずれか追跡中」(従来の 0/1 フラグ互換)。
        public const byte kBodyValidFlag = 1 << 0;
        public const byte kFaceValidFlag = 1 << 1;

        public bool isValid => valid!= 0 && Mathf.Approximately(AsCamera(0).fieldOfView, 0f) == false;

        public byte valid;

        /// <summary>body (MediaPipe) が追跡中か。</summary>
        public bool bodyValid => (valid & kBodyValidFlag) != 0;

        /// <summary>face (ARKit) が追跡中か。</summary>
        public bool faceValid => (valid & kFaceValidFlag) != 0;

        public fixed byte cameras[kCameraChannelCount * CameraData.Size];

        public Vector3 position;

        public Quaternion rotation;

        public Vector3 scale;

        public Vector3 hipPosition;

        public long frames;

        public fixed byte boneRotations[(int)HumanBodyBones.LastBone * CompilerUtility.QuaternionSize];

        public fixed float blendShapes[kBlendShapeCount];

        public Vector2 eyeDirection;

        // ボーンごとのトラッキング状態 (0..1)。1 = 完全にトラッキング (モーキャプ姿勢を採用)、
        // 0 = 未トラッキング (受信側でアニメーションを流す)。HumanBodyBones インデックスに対応。
        public fixed float bonePresences[(int)HumanBodyBones.LastBone];

        public ref CameraData AsCamera(int index)
        {
            Debug.Assert(index >= 0 && index < kCameraChannelCount);
            return ref UnsafeUtility.AsRef<CameraData>(UnsafeUtility.AddressOf(ref cameras[index * CameraData.Size]));
        }

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

        public ref float AsPresence(int index)
        {
            Debug.Assert(index >= 0 && index < (int)HumanBodyBones.LastBone);
            return ref UnsafeUtility.AsRef<float>(UnsafeUtility.AddressOf(ref bonePresences[index]));
        }
    }
}
