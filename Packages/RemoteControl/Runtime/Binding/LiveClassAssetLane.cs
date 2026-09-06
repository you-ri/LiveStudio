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
        /// <summary>
        /// Nothing said: the lane follows from the persistence (<see cref="FrameLaneRules"/>). A
        /// member the live scene saves is carried, on the state lane where its value can be moved
        /// as bytes and on the event lane where it cannot.
        /// </summary>
        Auto = 0,

        /// <summary>Recorded when it changes, one entry at a time.</summary>
        Event = 1,

        /// <summary>Copied every frame at a fixed size.</summary>
        State = 2,

        /// <summary>
        /// Not carried by the frame at all. For a function. On a value member the lane follows from
        /// whether the live scene saves it (<see cref="FrameLaneRules"/>): unticking
        /// <c>persistable</c> takes it off the frame, and asking for None while it is saved is
        /// refused (<c>LaneRefusal.NoneRefused</c>).
        /// </summary>
        None = 3,
    }
}
