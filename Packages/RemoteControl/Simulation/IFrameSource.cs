// Copyright (c) You-Ri, 2026
namespace Lilium.RemoteControl.Frames
{
    /// <summary>
    /// Supplies a frame's lanes from somewhere other than the running application: a recording being
    /// played, or another machine being followed.
    ///
    /// The mirror image of <see cref="IFrameSink"/>. A sink takes finished frames out; a source puts
    /// them in. Between the two, replay and mirroring stop being special cases of the gate and
    /// become the same two questions asked in opposite directions -- which is why they share the
    /// shape rather than each reaching into the pump their own way.
    ///
    /// Called at the frame head before the queued live events are applied, so what the recording
    /// asked for lands first and an operator acting right now lands on top of it.
    ///
    /// While a source is set, the frame is marked <see cref="Frame.isSupplied"/> and the producers
    /// that would otherwise write the state lane read it instead. That is the whole of the
    /// difference between live and replay: who fills the frame.
    /// </summary>
    public interface IFrameSource
    {
        /// <summary>
        /// Fills this frame's structure, state and events. False when there is nothing left to
        /// supply, which retires the source and lets the frame fall back to the live lanes.
        ///
        /// The frame is valid for the duration of this call. A source may point the frame's lanes at
        /// blocks it owns rather than copying into the gate's, in which case those blocks must
        /// outlive the frame head.
        /// </summary>
        bool FillFrame(ref Frame frame);
    }

    /// <summary>
    /// A source that knows when its next frame is due, so the gate can wait for it instead of
    /// running heads that have nothing to play.
    ///
    /// The counterpart of a cluster's frame barrier. A replay drives the engine's clock with the
    /// take's own step, and that is only right if the engine renders one frame per step: every frame
    /// in between would advance the clock by a step that was already spent. So while a replay is
    /// driving time, the gate asks here where on its clock the next step falls and waits for it
    /// (<see cref="IFrameClockSync"/>).
    ///
    /// Optional. A source that does not implement it is waited for one clock frame at a time.
    /// </summary>
    public interface IFrameSchedule
    {
        /// <summary>
        /// The clock frame at which the next supplied frame will move, given the one the gate last
        /// pumped. False when the source is not moving at all (held, or running at speed zero); the
        /// gate then waits a single frame, so a held replay neither spins nor stalls.
        ///
        /// Must not change anything: it is asked before the head it predicts, and the head itself
        /// has to find the source exactly as it was.
        /// </summary>
        bool TryGetNextDue(long lastClockFrame, FrameRate clockRate, out long dueClockFrame);
    }
}
