// Copyright (c) You-Ri, 2026

using UnityEngine;
using Unity.Collections.LowLevel.Unsafe;
using Lilium.RemoteControl;

namespace Lilium.LiveStudio.Virgo
{
    /// <summary>
    /// VirgoMotion の通信用フレーム (<see cref="Lilium.Virgo.AnimationFrameData"/>) と
    /// LiveStudio のアバター制御用フレーム (<see cref="Lilium.LiveStudio.AvatarAnimationData"/>) を
    /// 仲介するブリッジ。
    /// </summary>
    public static class AnimationFrameBridge
    {
        public static unsafe void ToLiveStudio(in AnimationFrameData src, out Lilium.LiveStudio.AvatarAnimationData dst)
        {
            dst = new Lilium.LiveStudio.AvatarAnimationData();

            dst.root.valid = src.valid;
            dst.root.position = src.position;
            dst.root.rotation = src.rotation;
            dst.root.scale = src.scale;

            dst.pose.hipPosition = src.hipPosition;

            fixed (byte* dstBones = dst.pose.boneRotations)
            fixed (byte* srcBones = src.boneRotations)
            {
                UnsafeUtility.MemCpy(dstBones, srcBones,
                    (int)HumanBodyBones.LastBone * CompilerUtility.QuaternionSize);
            }

            fixed (float* dstPresences = dst.pose.bonePresences)
            fixed (float* srcPresences = src.bonePresences)
            {
                UnsafeUtility.MemCpy(dstPresences, srcPresences,
                    sizeof(float) * (int)HumanBodyBones.LastBone);
            }

            fixed (float* dstWeights = dst.expression.weights)
            fixed (float* srcWeights = src.blendShapes)
            {
                UnsafeUtility.MemCpy(dstWeights, srcWeights,
                    sizeof(float) * (int)Lilium.LiveStudio.ARKitBlendShapeLocation.Max);
            }

            // CameraData (Lilium.LiveStudio.Virgo) and Lilium.LiveStudio.CameraData share an
            // identical sequential layout, so the whole camera array is copied verbatim, the
            // same way the bone/blendshape buffers above are bridged across the type boundary.
            fixed (byte* dstCameras = dst.cameras)
            fixed (byte* srcCameras = src.cameras)
            {
                UnsafeUtility.MemCpy(dstCameras, srcCameras,
                    AnimationFrameData.kCameraChannelCount * CameraData.Size);
            }

            dst.frames = src.frames;
        }
    }
}
