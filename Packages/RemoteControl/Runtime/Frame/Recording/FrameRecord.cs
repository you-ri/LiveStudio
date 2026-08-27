// Copyright (c) You-Ri, 2026
using System;

namespace Lilium.RemoteControl.Frames.Recording
{
    /// <summary>
    /// What an entry in a recording carries.
    ///
    /// The two lanes are mixed into one time-ordered stream rather than written as separate
    /// sections. Sections would lay the state out neatly but could not be appended to in the
    /// middle, so nothing could be written while recording; chunking would fix that at the price of
    /// holding a chunk in memory and losing it on a crash. Mixed, every entry is written the moment
    /// it happens and the order in the file is the order in time.
    /// </summary>
    public enum FrameEntryKind : byte
    {
        /// <summary>Start of a frame. Carries the tick, so replay can reconstruct the cadence.</summary>
        FrameBoundary = 0,

        /// <summary>
        /// A string joining the mapping table. Ids are assigned in order, so an entry says only the
        /// string; the id is its position. Appended as the table grows rather than written once, so
        /// a file that was cut short still resolves everything up to the cut.
        /// </summary>
        Symbol = 1,

        /// <summary>One input: what was written or called, by whom, with what payload.</summary>
        Input = 2,

        /// <summary>Every element of one state block, verbatim.</summary>
        State = 3,

        /// <summary>
        /// The inventory, written whenever its epoch moves and again at the keyframe interval.
        ///
        /// A frame carrying one is a keyframe. There is no separate keyframe entry because there is
        /// nothing else for it to hold: the state lane is dense and written in full every frame, so
        /// any frame already restores every value. The only thing a seek cannot get from the frame
        /// it lands on is the shape of the world, and that is this.
        /// </summary>
        Structure = 4,
    }

    /// <summary>
    /// Fixed marks in a recording. Kept together so the writer and the reader cannot disagree about
    /// them by drifting apart.
    /// </summary>
    public static class FrameRecordFormat
    {
        /// <summary>Start of file. "LiVe DaTa".</summary>
        public static readonly byte[] kMagic = { (byte)'L', (byte)'V', (byte)'D', (byte)'T' };

        /// <summary>
        /// Start of the footer, so a reader can tell a finished file from a cut one. Same stem as
        /// <see cref="kMagic"/> with an E for end, so the two cannot be mistaken for each other.
        /// </summary>
        public static readonly byte[] kFooterMagic = { (byte)'L', (byte)'V', (byte)'D', (byte)'E' };

        /// <summary>
        /// 2 added the keyframe list to the tail; 3 added the method to an input entry. Refused
        /// rather than guessed at: an older layout read at the newer offsets produces values that
        /// look plausible, which is worse than a file that will not open.
        ///
        /// The marks above changed with the rename to live data, which is a clean break: a file
        /// written before it is refused at the first four bytes rather than at the version.
        /// </summary>
        public const int kVersion = 3;

        /// <summary>
        /// Bytes the footer occupies: the three tail offsets and the marker. Read from the end.
        /// </summary>
        public const int kFooterSize = 8 + 8 + 8 + 4;
    }

    /// <summary>
    /// What a recording says about itself before any entry: enough to refuse a file that cannot be
    /// replayed here, and enough to read the frame numbers back as time.
    /// </summary>
    public struct FrameRecordHeader
    {
        /// <summary>Rate the frame numbers were counted at.</summary>
        public FrameRate frameRate;

        /// <summary>Wall clock the run started at, for lining a recording up with other material.</summary>
        public long startTicks;

        /// <summary>
        /// Which build produced this. A recording does not replay against a different build -- the
        /// mapping from ids to properties moves with the code -- so this is checked rather than
        /// hoped for.
        /// </summary>
        public string buildId;

        /// <summary>Engine that produced it. Unity recordings do not replay in Unreal.</summary>
        public string engineId;
    }

    /// <summary>
    /// One entry as it comes back from a reader.
    ///
    /// The payload is a window into the reader's buffer, valid until the next entry is read. A
    /// caller that needs to keep it copies it.
    /// </summary>
    public readonly ref struct FrameEntry
    {
        public readonly FrameEntryKind kind;

        /// <summary>Frame this entry belongs to. Carried per entry so a scan can place an entry
        /// even when it started in the middle of a file.</summary>
        public readonly long frameNumber;

        public readonly ReadOnlySpan<byte> payload;

        public FrameEntry(FrameEntryKind kind, long frameNumber, ReadOnlySpan<byte> payload)
        {
            this.kind = kind;
            this.frameNumber = frameNumber;
            this.payload = payload;
        }
    }
}
