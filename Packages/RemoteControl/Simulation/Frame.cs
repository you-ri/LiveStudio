// Copyright (c) You-Ri, 2026
namespace Lilium.RemoteControl.Frames
{
    /// <summary>
    /// The frame being built at a frame head: where it sits in time, and the two lanes that carry
    /// it. Handed to producers by reference so nothing is copied and there is no question about
    /// which frame is being written.
    ///
    /// <code>
    /// Frame
    /// ├─ position    frame number / rate / structure epoch
    /// ├─ structure   inventory (id / type / parent)
    /// ├─ state       dense per-type arrays, one element per object
    /// └─ events      the records applied at this head
    /// </code>
    ///
    /// The structure and state blocks are carried from frame to frame rather than rebuilt: together
    /// they are the current state of the world, not a per-frame delta. The event lane is the
    /// opposite -- it holds only what arrived at this head.
    ///
    /// A struct rather than a class: it is passed by reference on a hot path, and the lanes it will
    /// grow are themselves blocks meant to be moved wholesale rather than chased through pointers.
    /// </summary>
    public struct Frame
    {
        /// <summary>Monotonic frame number since the start of the run. The order of record.</summary>
        public long frameNumber;

        /// <summary>Rate the frame number was counted at. Needed to read the number back as time.</summary>
        public FrameRate frameRate;

        /// <summary>Shape: what exists and how many. Shared across frames.</summary>
        public StructureBlock structure;

        /// <summary>Values: dense per-type arrays. Shared across frames.</summary>
        public StateBlockSet state;

        /// <summary>
        /// The events applied at this head, in the order they were applied. Owned by
        /// <see cref="EventFrameBuffer"/> and reused, so it is a reference into the live slot --
        /// valid for the duration of the frame head, not afterwards.
        /// </summary>
        public EventFrame events;

        /// <summary>
        /// True when this frame's lanes were filled by an <see cref="IFrameSource"/> -- played from a
        /// recording, or followed from another machine -- rather than produced by what is running.
        ///
        /// Producers of the state lane read the frame instead of writing it when this is set. That is
        /// the whole of the difference between live and replay from a producer's side: not "am I
        /// being replayed" but "has this frame already been filled".
        /// </summary>
        public bool isSupplied;

        /// <summary>
        /// The table the ids in this frame's lanes index.
        ///
        /// Carried on the frame because a supplied frame's ids belong to the recording that filled
        /// it, not to this run: the same address is a different number in each, and reading one
        /// against the other names whatever happens to sit in that slot now. It was left to each
        /// consumer to notice, and three of them did not -- so a take replayed into nothing while
        /// the viewer, which did resolve through the recording, showed it as correct.
        ///
        /// A reader takes the table from the frame it was handed and asks no further questions.
        /// </summary>
        public FrameSymbolTable symbols;

        /// <summary>Readable position, derived from the number and the rate rather than stored.</summary>
        public Timecode timecode => new Timecode(frameNumber, frameRate);

        /// <summary>
        /// Sequence of the most recent structural change, read from the structure rather than kept
        /// alongside it so the two cannot disagree. State written against one epoch must not be
        /// applied against another.
        /// </summary>
        public long structureEpoch => structure?.epoch ?? 0;

        public override string ToString()
            => $"frame {frameNumber} @ {timecode} (epoch {structureEpoch})";
    }

    /// <summary>
    /// Runs at the head of a frame, after that frame's events have been applied.
    ///
    /// The frame is passed by reference so a producer writes into it directly. Taking it by value
    /// would hand out a copy that goes nowhere once the state block exists.
    /// </summary>
    public delegate void FrameHeadDelegate(ref Frame frame);
}
