// Copyright (c) You-Ri, 2026

namespace Lilium.RemoteControl.Frames
{
    /// <summary>
    /// Supplies the frame number stamped on each committed frame.
    ///
    /// Frame number is the index the whole product already uses (<see cref="FrameBuffer{T}"/>,
    /// FrameFileStream, the capture receivers). <see cref="Timecode"/> is the readable form of the
    /// same position and is produced from the number plus <see cref="frameRate"/>, so nothing here
    /// stores a timecode of its own.
    ///
    /// Replaceable so an external sync source (LTC, a house clock, an upstream node) can take over
    /// later without anything else changing: only the supply is swapped.
    /// </summary>
    public interface IFrameClock
    {
        /// <summary>Rate the frame numbers are counted at. Needed to read them back as time.</summary>
        FrameRate frameRate { get; }

        /// <summary>Frame number for the frame about to be committed. Called once per frame.</summary>
        long Advance();

        /// <summary>Return to the start of the timeline.</summary>
        void Reset();
    }

    /// <summary>
    /// Default clock: counts frames at a fixed rate, without reading a wall clock. A replay driven
    /// at a different real-world speed therefore produces the same frame numbers.
    ///
    /// It counts pumps, not elapsed time, so its declared rate describes how the numbers should be
    /// read back rather than how fast they are produced -- the editor ticks at its own pace, and a
    /// player that misses frames still advances by one. That makes it exact for ordering and replay
    /// but not for lining up with anything outside the process. Anything that has to agree with an
    /// external clock (recorded footage, gear, another node) needs a clock driven by that source
    /// instead.
    /// </summary>
    public sealed class FrameCounterClock : IFrameClock
    {
        private long _frames;

        public FrameCounterClock(FrameRate rate)
        {
            frameRate = rate;
        }

        public FrameRate frameRate { get; }

        public long Advance() => _frames++;

        public void Reset() => _frames = 0;
    }
}
