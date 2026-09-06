// Copyright (c) You-Ri, 2026
namespace Lilium.RemoteControl
{
    /// <summary>
    /// Which lane of the live data carries an exposed member.
    ///
    /// The two lanes are of equal standing. For a member whose writes all pass through the frame
    /// gate the choice is about cost, not correctness: a property write is recorded with the value
    /// itself, so replaying it needs no re-computation either way. What differs is sparse versus
    /// dense, which makes frequency the only criterion.
    ///
    /// The equivalence has one condition. A member that is also written from inside the application,
    /// bypassing the gate, leaves no trace in the event lane -- those writes are not events and are
    /// never recorded. Such a member has to be <see cref="State"/>, or the write has to be driven by
    /// an event that is itself recorded. Declaring it <see cref="Event"/> and writing to it
    /// internally records nothing and produces a recording that cannot be replayed.
    ///
    /// Which lane a member is on follows from how it is persisted unless the member says otherwise
    /// -- see <see cref="FrameLaneRules"/>. A member the live scene saves is carried; a member saved
    /// elsewhere, or not at all, is not. The values below are what a member can ask for out loud.
    /// </summary>
    public enum FrameLane
    {
        /// <summary>
        /// Recorded when it changes, one entry at a time. What a member the live scene saves gets
        /// when it says nothing about its lane; declared explicitly, it puts a member the scene does
        /// not save onto the frame anyway (the exception <see cref="FrameLaneRules"/> describes).
        /// </summary>
        Event = 0,

        /// <summary>
        /// Copied every frame at a fixed size. Opt-in, because it is paid for even when the value
        /// does not change. Worth it above roughly ten changes a second -- an input record is 536
        /// bytes whatever it carries, so a value dragged sixty times a second costs far more as
        /// input than as state.
        /// </summary>
        State = 1,

        /// <summary>
        /// Not carried by the frame at all. For values a spare machine is expected to differ on --
        /// window placement, screen resolution, which port to listen on.
        ///
        /// Not something a member declares: it follows from the member being saved to the project
        /// settings, to an owner's own file, or nowhere (<see cref="FrameLaneRules"/>). Written on a
        /// member it is an error (<c>LRC013</c>), because a saved member declared off the frame is a
        /// scene file the recording disagrees with. Where it is still said out loud is on a whole
        /// type (<c>[LiveClass(lane = FrameLane.None)]</c>, absorbing) and on a function, which has
        /// no persistence to derive it from.
        /// </summary>
        None = 2,
    }
}
