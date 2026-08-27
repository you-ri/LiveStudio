// Copyright (c) You-Ri, 2026
namespace Lilium.RemoteControl.Frames
{
    /// <summary>
    /// Watches frames go by without taking them.
    ///
    /// Separate from <see cref="IFrameSink"/> because the two are different jobs. A sink takes the
    /// frame somewhere -- a file, another machine -- and there can only be one, because who owns the
    /// frame has to be a settled question. Watching is not owning: a viewer, a meter and a drift
    /// check can all look at the same frame, and none of them changes what happens to it.
    ///
    /// Kept apart for a plain reason: if watching went through the sink, opening a viewer would stop
    /// a recording. Chaining sinks instead leaves nobody owning the chain.
    ///
    /// Called at the same point as the sink -- the frame complete, its inputs still attached. Not at
    /// the frame head: there the state lane has not been written yet, and an observer would see this
    /// frame's inputs beside last frame's values, which is worse than useless when the thing being
    /// diagnosed is which of the two went wrong.
    ///
    /// ⚠ This runs inside the point every input is waiting on. Copy what you need and leave; do the
    /// work on your own clock. Nothing reachable from the frame outlives the call, and a supplied
    /// frame's blocks are overwritten as soon as the recording advances.
    ///
    /// ⚠ Read only. Writing to the frame here lands after the producers have run and after the sink
    /// has taken its copy, so the recording and the running application would disagree. Even asking
    /// a state block set for a block it does not have changes what a recording looks like, because
    /// that is also how a type announces itself and how the block order is fixed.
    /// </summary>
    public interface IFrameObserver
    {
        /// <summary>
        /// Hands over a completed frame, with the symbol table needed to read the ids in it.
        ///
        /// Throwing detaches the observer rather than repeating the failure every frame. Whoever
        /// attached it can see that happened through <see cref="FrameGate.detachedObserverCount"/>.
        /// </summary>
        void OnFrameCompleted(in Frame frame, InputSymbolTable symbols);
    }
}
