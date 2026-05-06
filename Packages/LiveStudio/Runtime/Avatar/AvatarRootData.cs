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

        public byte valid;

        public Vector3 position;

        public Quaternion rotation;

        public Vector3 scale;
    }
}
