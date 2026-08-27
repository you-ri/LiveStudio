// Copyright (c) You-Ri, 2026

using UnityEngine;
using Unity.Collections.LowLevel.Unsafe;
using Lilium.RemoteControl;
using Lilium.RemoteControl.Frames;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Aggregated per-frame animation data used to drive a Humanoid avatar.
    /// Composed of role-specific parts so callers can take only what they need.
    /// </summary>
    public unsafe struct AvatarAnimationData
    {
        static AvatarAnimationData() => CompilerUtility.CheckBlittable<AvatarAnimationData>();

        // Number of capture-camera channels carried per frame.
        // Must match the wire frame (Lilium.LiveStudio.Virgo.AnimationFrameData.kCameraChannelCount).
        public const int kCameraChannelCount = 2;

        public AvatarRootData root;

        public HumanoidPoseData pose;

        public ARKitWeightData expression;

        [LiveArray(typeof(CameraData))]
        public fixed byte cameras[kCameraChannelCount * CameraData.Size];

        public long frames;

        public ref CameraData AsCamera(int index)
        {
            Debug.Assert(index >= 0 && index < kCameraChannelCount);
            return ref UnsafeUtility.AsRef<CameraData>(UnsafeUtility.AddressOf(ref cameras[index * CameraData.Size]));
        }

        /// <summary>True while the capture camera carries a meaningful pose (non-zero FOV).</summary>
        public bool cameraValid => Mathf.Approximately(AsCamera(0).fieldOfView, 0f) == false;

        public bool isValid => root.valid != 0 && cameraValid;

        /// <summary>True while body (pose) tracking is valid within a meaningful frame.</summary>
        public bool bodyTracked => root.bodyValid && cameraValid;

        /// <summary>True while face (expression) tracking is valid within a meaningful frame.</summary>
        public bool faceTracked => root.faceValid && cameraValid;
    }
}
