// Copyright (c) You-Ri, 2026

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Read access to an avatar's current animator parameter values, by name hash.
    ///
    /// An avatar accessory (a child object with its own Animator) bridges the avatar's
    /// parameters onto its own controller every frame so that e.g. <c>GestureRight</c> or
    /// <c>FT/v2/*</c> drive the accessory the same way they drive the avatar. This interface
    /// is needed because some avatars (e.g. <see cref="VRCFTAvatar"/>) wrap their
    /// AnimatorController inside a PlayableGraph, so <c>Animator.GetFloat</c> does not return
    /// the live values — the read must go through the wrapping playable instead.
    /// </summary>
    public interface IAvatarParameterSource
    {
        /// <summary>True if the avatar declares a parameter with this name hash.</summary>
        bool HasParameter(int nameHash);

        float GetFloat(int nameHash);
        int GetInteger(int nameHash);
        bool GetBool(int nameHash);
    }
}
