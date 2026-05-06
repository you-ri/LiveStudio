// Copyright (c) You-Ri, 2026

using UnityEngine;
using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Aggregated per-frame animation data used to drive a Humanoid avatar.
    /// Composed of role-specific parts so callers can take only what they need.
    /// </summary>
    public unsafe struct AvatarAnimationData
    {
        static AvatarAnimationData() => CompilerUtility.CheckBlittable<AvatarAnimationData>();

        public AvatarRootData root;

        public HumanoidPoseData pose;

        public ARKitWeightData expression;

        public CameraData camera;

        public long frames;

        public bool isValid => root.valid != 0
                            && Mathf.Approximately(camera.fieldOfView, 0f) == false;
    }
}
