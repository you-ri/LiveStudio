// Copyright (c) You-Ri, 2026

namespace Lilium.RemoteControl
{
    /// <summary>
    /// Which lane of the live data an asset-declared member is carried by.
    ///
    /// A separate enum from <see cref="Frames.FrameLane"/> only so that "not said" is a value an
    /// inspector can show. The frame layer has no use for such a value -- by the time a member
    /// reaches it, the question has been answered.
    /// </summary>
    public enum LiveClassAssetLane
    {
        /// <summary>Field to the state lane, property to the event lane. See ResolveLane.</summary>
        Auto = 0,

        /// <summary>Recorded when it changes, one entry at a time.</summary>
        Event = 1,

        /// <summary>Copied every frame at a fixed size.</summary>
        State = 2,

        /// <summary>Not carried by the frame at all.</summary>
        None = 3,
    }
}
