// Copyright (c) You-Ri, 2026
using System;
using UnityEngine;

namespace Lilium.RemoteControl.Frames
{
    // The tick and the engine's clock: how much time a frame covers, and -- while a replay drives
    // the engine -- the barrier that holds the engine to one frame per step of the take.
    public static partial class FrameGate
    {
        private static IFrameClock _clock = _NewDefaultClock();

        // Frame number the tick was last worked out for, and the width it came to. long.MinValue
        // rather than -1: a frame number is a position on a time axis and negative ones are legal.
        private static long _tickFrameNumber = long.MinValue;
        private static double _deltaTime;

        // Where the tick was last measured from: whether that frame was supplied, by whom, and at
        // what rate. A number is only a position against the axis it was counted on, so a change in
        // any of these makes the difference between two numbers meaningless rather than long.
        private static bool _tickSupplied;
        private static IFrameSource _tickSource;
        private static FrameRate _tickFrameRate;

        /// <summary>
        /// Widest tick handed out, in seconds. Anything wider is a jump -- a forward scrub, a machine
        /// that stalled -- and integrating over it would be a leap nothing lived through. Also the
        /// furthest a replay's barrier waits ahead, which keeps it well inside the pacer's own
        /// one-second stall rule.
        /// </summary>
        private const double kMaxTickSeconds = 0.25;

        /// <summary>Extra time a barrier waits beyond when its frame is due before giving up.</summary>
        private const int kBarrierSlackMs = 50;

        private static bool _driveEngineTime;
        private static bool _engineTimeDriven;
        private static bool _engineTimeRefused;

        /// <summary>Supplies the frame number stamped on each committed frame.</summary>
        public static IFrameClock clock => _clock;

        /// <summary>
        /// Seconds the frame just committed covers: the distance from the previous one, worked out
        /// from the frame numbers and the rate.
        ///
        /// **Anything that advances with time reads this rather than <c>Time.deltaTime</c>.** The
        /// engine's tick is whatever this machine managed to render, so two machines fed the same
        /// events integrate different amounts and drift apart -- which is the single largest source
        /// of that drift. This is the recorded width of the step, identical on every machine
        /// replaying the same take.
        ///
        /// A skipped frame number widens it rather than being hidden: the pump missed an interval,
        /// and time really did pass. A step backwards in a take (a scrub) is one interval, because a
        /// seek is not a duration anything should integrate over; so is the first frame of a run, of
        /// a replay, or back live after one, since those numbers are counted on different axes. A
        /// step wider than a quarter of a second is cut to that.
        ///
        /// A supplied frame carries the take's own number, so during a replay this is the take's
        /// step -- several records spent at one head make one wide step -- and a head that held the
        /// record it was on (a pause, nothing due yet) covers no time: zero.
        /// </summary>
        public static float deltaTime => (float)_deltaTime;

        /// <summary>
        /// Whether a supplied frame also drives the engine's clock (<c>Time.captureDeltaTime</c>).
        ///
        /// Off by default, and turned on by whoever is actually replaying. With it on, code that
        /// still reads <c>Time.deltaTime</c> follows the recording without being touched -- which is
        /// what makes a replay of existing code reproducible, and what a faster-than-real or a
        /// slower-than-real redraw runs on. A viewer merely watching frames go by leaves it off:
        /// stepping the engine's clock would stop the application it is being watched in from
        /// running at its own speed.
        ///
        /// <para>
        /// The same arrangement as a render cluster, with the take as the primary. The take is the
        /// authority on how much time each frame covers, and the engine applies that rather than
        /// measuring its own; and because a step handed to the engine is spent once per rendered
        /// frame, the gate also holds the engine to one frame per step -- it waits at the head until
        /// the source's next frame is due (<see cref="IFrameSchedule"/>) on a clock that can be
        /// waited on (<see cref="IFrameClockSync"/>), which is where an external sync source takes
        /// over the pacing. A head where the take did not move (a pause, a load being waited for)
        /// gives the engine back to real time, since there is no step to hand it.
        /// </para>
        ///
        /// <para>
        /// ⚠ The step lands one frame late: the engine settles a frame's time before the head runs,
        /// so a value set here is the one the next frame uses. The barrier keeps it at one frame per
        /// head, so the total is exact and only the phase is shifted -- the same shift as any input
        /// read at the head. Moving the head earlier to close it was rejected: it would make the
        /// head run before the engine's time for the frame exists, for replays only.
        /// </para>
        /// </summary>
        public static bool driveEngineTimeOnSuppliedFrames
        {
            get => _driveEngineTime;
            set
            {
                _driveEngineTime = value;
                _engineTimeRefused = false;
                if (!value) _ReleaseEngineTime();
            }
        }

        /// <summary>
        /// Replaces the clock, for an external sync source or for replay. Main thread only, and not
        /// while a frame is being filled -- <see cref="Pump"/> reads the clock and the buffer as a
        /// pair.
        /// </summary>
        public static void SetClock(IFrameClock value)
        {
            _clock = value ?? throw new ArgumentNullException(nameof(value));
            _clock.Reset();
            _lastPumpedFrameNumber = -1;
            _buffer.Reset();
        }

        /// <summary>
        /// Puts the clock back to the one a live run uses.
        ///
        /// The clock is process-wide, so anything that installs one for its own purposes -- a test
        /// driving the pump by hand, a tool stepping through a recording -- hands the rest of the
        /// editor session whatever it left behind. A counter clock left in place makes the timecode
        /// read as fast as the editor happens to tick, which is how it once ran ten times fast.
        ///
        /// Deliberately not done by <see cref="ResetState"/>: a restart must not throw away an
        /// external sync source someone installed on purpose.
        /// </summary>
        public static void RestoreDefaultClock() => SetClock(_NewDefaultClock());

        /// <summary>The clock a live run is driven by, unless something replaced it.</summary>
        private static IFrameClock _NewDefaultClock() => new RealtimeFrameClock(FrameRate.FPS60);

        /// <summary>
        /// What the player loop runs every engine frame: while a replay drives the engine's clock,
        /// wait for the frame to be due; then pump. Split from the hook so a test can drive it.
        ///
        /// The wait is outside <see cref="Pump"/> because tests and hosts call that directly to step
        /// a frame, and a step that blocked on wall time would be neither.
        /// </summary>
        internal static void _PumpAtFrameHead()
        {
            if (_driveEngineTime && _source != null) _WaitUntilDue();

            var before = _lastPumpedFrameNumber;
            Pump();

            // No frame this time -- the wait gave up, or the clock did not move. The step already
            // handed to the engine would be spent a second time on this frame, which is exactly the
            // fast-forward the barrier exists to prevent, so the engine goes back to real time.
            if (_lastPumpedFrameNumber == before) _ReleaseEngineTime();
        }

        /// <summary>
        /// Holds the engine at the head until the source's next frame is due: the frame barrier.
        ///
        /// At least one clock frame, so a source that cannot say when it is due still renders one
        /// frame per step. At most <see cref="kMaxTickSeconds"/> ahead, so a take that stood still
        /// for a long time is walked through in steps the tick would hand out anyway instead of
        /// freezing the application for the length of the gap.
        /// </summary>
        private static void _WaitUntilDue()
        {
            if (!(_clock is IFrameClockSync sync)) return;

            var last = _lastPumpedFrameNumber;
            if (last < 0) return;

            // Waiting for a load: nothing is supplied, the engine runs on real time, and there is
            // nothing to be on time for.
            if (supplyHoldCount > 0) return;

            var rate = _clock.frameRate;
            var ceiling = last + _MaxTickFrames(rate);
            var due = last + 1;

            if (_source is IFrameSchedule schedule && schedule.TryGetNextDue(last, rate, out var scheduled))
            {
                due = Math.Max(due, Math.Min(scheduled, ceiling));
            }

            var timeoutMs = (int)Math.Ceiling(rate.AsSecounds(due - last) * 1000.0) + kBarrierSlackMs;
            sync.WaitUntil(due, timeoutMs);
        }

        /// <summary>
        /// Works out how much time the frame just filled covers, and lets a replay drive the engine
        /// with it.
        /// </summary>
        private static void _UpdateTick()
        {
            var supplied = _frame.isSupplied;
            var source = supplied ? _source : null;

            // The first frame of a run, of a replay, or back live after one: the two numbers were
            // counted on different axes, so their difference is not a duration. One interval.
            var sameAxis = _tickFrameNumber != long.MinValue
                && supplied == _tickSupplied
                && ReferenceEquals(source, _tickSource)
                && _frame.frameRate == _tickFrameRate;

            var advanced = sameAxis ? _frame.frameNumber - _tickFrameNumber : 1;

            // A supplied frame on the number it already stood on: the take held (paused, or nothing
            // was due yet at this head). No time passed in it.
            var stood = sameAxis && supplied && advanced == 0;

            // A scrub backwards: one interval. A seek covers no time -- nothing integrating over it
            // moved through those frames -- and a backwards one would otherwise hand out a negative
            // tick.
            if (advanced <= 0) advanced = 1;

            var ceiling = _MaxTickFrames(_frame.frameRate);
            if (advanced > ceiling) advanced = ceiling;

            _deltaTime = stood ? 0 : _frame.frameRate.AsSecounds(advanced);
            _tickFrameNumber = _frame.frameNumber;
            _tickSupplied = supplied;
            _tickSource = source;
            _tickFrameRate = _frame.frameRate;

            if (!stood && _CarriesTimeAuthority()) _DriveEngineTime();
            else _ReleaseEngineTime();
        }

        /// <summary>
        /// Whether this frame's step is the one the engine should run on.
        ///
        /// A frame filled by a source, with the switch on, is the only case today: its numbers are
        /// the take's. Asked in one place because it is the question an external sync source will
        /// change -- a live run clocked by a house timecode carries the authority too.
        /// </summary>
        private static bool _CarriesTimeAuthority() => _frame.isSupplied && _driveEngineTime;

        /// <summary>Hands the engine this frame's step.</summary>
        private static void _DriveEngineTime()
        {
            // Something else is already stepping the engine -- a screen recorder, a capture tool.
            // Both writing it would make each frame whichever wrote last, so this stands aside, says
            // so once, and only FrameGate.deltaTime follows the take.
            if (!_engineTimeDriven && Time.captureDeltaTime != 0f)
            {
                if (!_engineTimeRefused)
                {
                    _engineTimeRefused = true;
                    Debug.LogWarning(
                        "[RemoteControl] Time.captureDeltaTime is already in use by something else; " +
                        "the replay leaves the engine's clock alone.");
                }

                return;
            }

            _engineTimeDriven = true;
            Time.captureDeltaTime = (float)_deltaTime;
        }

        /// <summary>The widest tick, in frames of <paramref name="rate"/>. At least one.</summary>
        private static long _MaxTickFrames(FrameRate rate)
        {
            if (rate.numerator == 0 || rate.denominator == 0) return 1;

            var frames = rate.AsFrameNumber(kMaxTickSeconds);
            return frames < 1 ? 1 : frames;
        }

        /// <summary>
        /// Gives the engine's clock back to real time.
        ///
        /// Only when this is what took it: <c>Time.captureDeltaTime</c> is process-wide and a screen
        /// recorder is entitled to be holding it for its own reasons.
        /// </summary>
        private static void _ReleaseEngineTime()
        {
            if (!_engineTimeDriven) return;

            _engineTimeDriven = false;
            Time.captureDeltaTime = 0f;
        }
    }
}
