// Copyright (c) You-Ri, 2026
namespace Lilium.RemoteControl.Frames
{
    /// <summary>
    /// Receives every frame once it is complete: after its inputs have been applied and after the
    /// state producers have written their blocks, but before it is published.
    ///
    /// Recording and mirroring are the same thing seen from two sides -- one writes the frame to a
    /// file, the other sends it to another machine -- so they take the same frames through the same
    /// point rather than each tapping the gate their own way.
    ///
    /// Called on the main thread at the frame head. Anything expensive belongs on the other side of
    /// a buffer: this runs inside the point every input is waiting on.
    /// </summary>
    public interface IFrameSink
    {
        /// <summary>
        /// Hands over a completed frame. The symbol table is passed alongside because the ids in the
        /// frame mean nothing without it, and a consumer has to write or send the two together.
        ///
        /// The frame and everything reachable from it are valid for the duration of this call only.
        /// </summary>
        void OnFrameCompleted(in Frame frame, InputSymbolTable symbols);
    }
}
