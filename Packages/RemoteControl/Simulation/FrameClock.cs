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

        /// <summary>
        /// Frame number for the frame about to be committed.
        ///
        /// May return the number it returned last time, which says no new frame is due yet: the
        /// rate is the resolution of the time axis, so two calls inside one interval are two
        /// moments at the same position. The gate skips the frame rather than committing a second
        /// one under the same number.
        /// </summary>
        long Advance();

        /// <summary>Return to the start of the timeline.</summary>
        void Reset();
    }

    /// <summary>
    /// Counts frames at a fixed rate, without reading a wall clock. A replay driven at a different
    /// real-world speed therefore produces the same frame numbers.
    ///
    /// This is the clock a test installs: a test drives the pump itself, so a step is a step rather
    /// than an interval of wall time, and every pump has to produce a frame. Live runs use
    /// <see cref="RealtimeFrameClock"/>, where two pumps inside one interval are one frame.
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

    /// <summary>
    /// Quantises the time of day by the frame rate: the frame number is where now falls on a time
    /// axis whose resolution is the rate.
    ///
    /// Anchored to the wall clock rather than to process start, so a timecode read off a frame is
    /// the time that frame happened -- which is what lets a recording be lined up against footage,
    /// a log, or another machine. This is the same construction the capture side already uses for
    /// its timecode (Virgo's TimeManager), so the two agree on what a moment is called.
    ///
    /// The anchor is read once and advanced by a monotonic source from there. Reading the calendar
    /// every frame would import its steps: the wall clock jumps when the machine syncs its time or
    /// crosses a daylight-saving boundary, and a frame number that went backwards would address a
    /// slot that has already been written. The cost is that the anchor ages -- a session left open
    /// for days drifts from the clock on the wall -- and that a session running past midnight keeps
    /// counting up rather than wrapping, so its hours read past 24.
    ///
    /// Numbers are skipped rather than stretched. A producer that missed its interval leaves a gap,
    /// which is what actually happened -- the alternative, counting pumps, spends the same numbers
    /// over a longer wall time and quietly reports a run as slower or faster than it was.
    /// </summary>
    // LRC011 is suppressed for this type on purpose. The rule says the simulation must not read a
    // clock of its own -- but a clock has to enter somewhere, and this is that door. Real time is an
    // input here, in the same sense a gamepad is: it is read once at the boundary, stamped onto the
    // frame, and everything downstream reads it from the frame. Replacing this clock with a supplied
    // one is exactly how replay works.
#pragma warning disable LRC011
    public sealed class RealtimeFrameClock : IFrameClock
    {
        private readonly System.Diagnostics.Stopwatch _sinceAnchor = new System.Diagnostics.Stopwatch();

        /// <summary>Seconds past midnight when the anchor was taken.</summary>
        private double _anchor;

        public RealtimeFrameClock(FrameRate rate)
        {
            frameRate = rate;
            Reset();
        }

        public FrameRate frameRate { get; }

        public long Advance() => frameRate.AsFrameNumber(now);

        /// <summary>Seconds past midnight, as of this call.</summary>
        public double now => _anchor + _sinceAnchor.Elapsed.TotalSeconds;

        /// <summary>Re-reads the wall clock. The timeline restarts from the current time of day.</summary>
        public void Reset()
        {
            _anchor = System.DateTime.Now.TimeOfDay.TotalSeconds;
            _sinceAnchor.Restart();
        }
    }
#pragma warning restore LRC011
}
