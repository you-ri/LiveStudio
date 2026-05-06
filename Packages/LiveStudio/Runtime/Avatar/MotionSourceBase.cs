// Copyright (c) You-Ri, 2026

using UnityEngine;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Abstract source of motion frame data driving an avatar.
    /// Derived classes update <see cref="frameData"/> each frame; consumers
    /// pull the latest value.
    /// </summary>
    public abstract class MotionSourceBase : MonoBehaviour
    {
        public AvatarAnimationData frameData;

        /// <summary>
        /// Reset camera offset relative to the current avatar pose. Default no-op;
        /// concrete sources that track a real camera (e.g. mocap) should override.
        /// </summary>
        public virtual void ResetCamera() { }
    }
}
