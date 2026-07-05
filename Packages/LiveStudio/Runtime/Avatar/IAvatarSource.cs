// Copyright (c) You-Ri, 2026

using System;
using UnityEngine;

namespace Lilium.LiveStudio
{
    public interface IAvatarSource
    {
        event Action<GameObject> onAvatarReady;
    }
}
