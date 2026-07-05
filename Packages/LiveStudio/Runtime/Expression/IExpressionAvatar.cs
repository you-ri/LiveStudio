// Copyright (c) You-Ri, 2026

using System;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Avatar that exposes a runtime expression weight surface.
    /// Implementations such as VRM avatars provide blendshape access via FacialKey.
    /// </summary>
    public interface IExpressionAvatar
    {
        bool SetWeight(FacialKey key, float weight);

        float GetWeight(FacialKey key);

        ReadOnlySpan<FacialKey> GetExpressions();
    }
}
