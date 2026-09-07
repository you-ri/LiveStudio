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
        /// <summary>
        /// 受信フレームを Studio 側のフレームへ変換する。姿勢だけは形が違う (ワイヤはボーンの
        /// ローカル回転、Studio は muscle 空間) ので <paramref name="normalizer"/> を通す。
        /// normalizer がまだ骨格を持っていない (アバター未ロード) 場合は false を返し、
        /// 呼び出し側はそのフレームの姿勢が無いものとして扱う。
        /// </summary>
        public static unsafe bool ToLiveStudio(in AnimationFrameData src, WirePoseNormalizer normalizer,
            out Lilium.LiveStudio.AvatarAnimationData dst)
        {
            dst = new Lilium.LiveStudio.AvatarAnimationData();

            dst.root.valid = src.valid;
            dst.root.position = src.position;
            dst.root.rotation = src.rotation;
            dst.root.scale = src.scale;

            if (normalizer == null || !normalizer.TryNormalize(in src, out dst.pose)) return false;

            fixed (float* dstWeights = dst.expression.weights)
            fixed (float* srcWeights = src.blendShapes)
            {
                UnsafeUtility.MemCpy(dstWeights, srcWeights,
                    sizeof(float) * (int)Lilium.LiveStudio.ARKitBlendShapeLocation.Max);
            }

            // CameraData (Lilium.LiveStudio.Virgo) and Lilium.LiveStudio.CameraData share an
            // identical sequential layout, so the whole camera array is copied verbatim, the
            // same way the blendshape buffer above is bridged across the type boundary.
            fixed (byte* dstCameras = dst.cameras)
            fixed (byte* srcCameras = src.cameras)
            {
                UnsafeUtility.MemCpy(dstCameras, srcCameras,
                    AnimationFrameData.kCameraChannelCount * CameraData.Size);
            }

            dst.frames = src.frames;
            return true;
        }
    }
}
