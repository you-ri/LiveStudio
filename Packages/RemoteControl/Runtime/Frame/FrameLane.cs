// Copyright (c) You-Ri, 2026
namespace Lilium.RemoteControl
{
    /// <summary>
    /// Which lane of the deterministic frame carries an exposed member.
    ///
    /// The two lanes are of equal standing. For a member whose writes all pass through the frame
    /// gate the choice is about cost, not correctness: a property write is recorded with the value
    /// itself, so replaying it needs no re-computation either way. What differs is sparse versus
    /// dense, which makes frequency the only criterion.
    ///
    /// The equivalence has one condition. A member that is also written from inside the application,
    /// bypassing the gate, leaves no trace in the input lane -- those writes are not inputs and are
    /// never recorded. Such a member has to be <see cref="State"/>, or the write has to be driven by
    /// an input that is itself recorded. Declaring it <see cref="Input"/> and writing to it
    /// internally records nothing and produces a recording that cannot be replayed.
    /// </summary>
    public enum FrameLane
    {
        /// <summary>
        /// Recorded when it changes, one entry at a time. The default, for two reasons: an
        /// undecorated member keeps behaving the way it does today, and a default of
        /// <see cref="State"/> would mean reading every exposed member every frame by reflection,
        /// which does not hold up.
        /// </summary>
        Input = 0,

        /// <summary>
        /// Copied every frame at a fixed size. Opt-in, because it is paid for even when the value
        /// does not change. Worth it above roughly ten changes a second -- an input record is 536
        /// bytes whatever it carries, so a value dragged sixty times a second costs far more as
        /// input than as state.
        /// </summary>
        State = 1,

        /// <summary>
        /// Not carried by the frame at all. For values a spare machine is expected to differ on --
        /// window placement, screen resolution. Independent of whether the member is persisted.
        /// </summary>
        None = 2,
    }
}
