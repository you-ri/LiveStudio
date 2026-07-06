// Copyright (c) You-Ri, 2026

using UnityEngine;
using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Root transform data for an avatar (position / rotation / scale)
    /// plus a validity flag.
    /// </summary>
    public struct AvatarRootData
    {
        static AvatarRootData() => CompilerUtility.CheckUnmanaged<AvatarRootData>();

        /// <summary>Body (pose) tracking bit of <see cref="valid"/>.</summary>
        public const byte kBodyValidFlag = 1 << 0;

        /// <summary>Face (expression) tracking bit of <see cref="valid"/>.</summary>
        public const byte kFaceValidFlag = 1 << 1;

        /// <summary>
        /// Tracking bitmask. bit0 (<see cref="kBodyValidFlag"/>) = body/pose tracked,
        /// bit1 (<see cref="kFaceValidFlag"/>) = face/expression tracked.
        /// Nonzero means at least one is tracked (kept for backward compatibility with
        /// the former 0/1 flag).
        /// </summary>
        public byte valid;

        /// <summary>True while body (pose) tracking is valid.</summary>
        public bool bodyValid => (valid & kBodyValidFlag) != 0;

        /// <summary>True while face (expression) tracking is valid.</summary>
        public bool faceValid => (valid & kFaceValidFlag) != 0;

        public Vector3 position;

        public Quaternion rotation;

        public Vector3 scale;
    }
}
