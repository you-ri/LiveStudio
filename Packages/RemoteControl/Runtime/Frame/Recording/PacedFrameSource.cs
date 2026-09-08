// Copyright (c) You-Ri, 2026
using System;

namespace Lilium.RemoteControl.Frames.Recording
{
    /// <summary>
    /// Plays a take back at the speed it was recorded at, whatever speed the machine playing it
    /// manages to run.
    ///
    /// A replayer walks records: one record per frame head, which is the right thing for redrawing
    /// a take frame by frame and the wrong thing for watching one. A take recorded while the
    /// machine was managing twenty frames a second holds a record every third frame number, so a
    /// machine doing sixty spends sixty records a second on twenty seconds' worth of them and the
    /// take runs three times fast. Nothing was wrong with the recording: the frame numbers say
    /// exactly how far apart the records are, and walking them ignores that.
    ///
    /// So this wraps a replayer and decides, at each head, how many records are due -- none, one, or
    /// several. What it measures the time with is the frame it is filling: the gate stamps a frame
    /// with the live clock's number and rate before it asks a source to fill it, so the distance
    /// from the last head is already on the frame and this reads no clock of its own (LRC011).
    ///
    /// <para>
    /// Not wrapping it is the other mode, and a real one: a source that walks records regardless of
    /// wall time is what an offline redraw wants, where every recorded frame has to be drawn and the
    /// engine's clock is driven by the take rather than by the room. That is
    /// <see cref="FrameReplayer"/> used directly.
    /// </para>
    ///
    /// <para>
    /// ⚠ The two modes differ in what <see cref="FrameGate.deltaTime"/> means, and the difference is
    /// the point of having both. Paced, the step is this machine's -- the take's own values are
    /// written back at every head either way, but anything integrating outside the take (a spring, a
    /// particle, a camera rig) advances by real time and therefore differently on a machine that
    /// renders at another rate. Unpaced, the step is the take's, so those integrate identically
    /// everywhere and the price is that the take no longer runs at the speed it happened. Watching
    /// wants the first; comparing two machines and redrawing a take want the second.
    /// </para>
    /// </summary>
    public sealed class PacedFrameSource : IFrameSource, IDisposable
    {
        /// <summary>
        /// Records one frame head may put back before it gives up and supplies what it has.
        ///
        /// A cap rather than a budget: applying a record can be expensive (a setter that loads an
        /// asset), so a head that has fallen far behind must not try to make all of it up at once --
        /// that is how being late becomes being later.
        /// </summary>
        public const int kDefaultMaxRecordsPerFrame = 8;

        private readonly FrameReplayer _replayer;

        private double _speed = 1.0;

        // Take frames owed but not yet spent, carried across heads with its fraction: a take at a
        // different rate than the machine playing it owes a fractional frame most heads, and
        // dropping that is a drift of up to one frame a second.
        private double _owed;

        // Live frame number the last head was at, to measure how much real time this one covers.
        // Not a clock read -- the number comes off the frame the gate handed over.
        private long _lastLiveFrameNumber = long.MinValue;

        // What the gate had counted as held-for-loading when this last looked. A change means the
        // gate did not ask for frames for a while, and that stretch is not time the take should
        // sprint through: it is time the take was deliberately stopped.
        private long _lastHeldFrameCount;

        // Where the take was when this last supplied a frame, so a move it did not make itself --
        // a scrub, a jump from a tool -- can be told from one it did.
        private long _lastPlayerFrameNumber = long.MinValue;

        public PacedFrameSource(FrameReplayer replayer)
        {
            _replayer = replayer ?? throw new ArgumentNullException(nameof(replayer));
        }

        /// <summary>The take being paced. Transport (pause, seek, loop) is still asked of it.</summary>
        public FrameReplayer replayer => _replayer;

        /// <summary>
        /// How fast the take runs against real time. One is the speed it was recorded at; zero
        /// holds it, which is what a pause is made of.
        /// </summary>
        public double speed
        {
            get => _speed;
            set => _speed = value > 0 ? value : 0;
        }

        /// <summary>Records one head may put back. See <see cref="kDefaultMaxRecordsPerFrame"/>.</summary>
        public int maxRecordsPerFrame { get; set; } = kDefaultMaxRecordsPerFrame;

        /// <summary>
        /// Heads that ran out of their record budget with time still owed, and dropped it.
        ///
        /// Each one is a moment the take skipped rather than played. Not an error -- the machine
        /// really could not keep up -- but a replay that collects these is not showing the take's
        /// timing any more.
        /// </summary>
        public long lateFrameCount { get; private set; }

        public bool FillFrame(ref Frame frame)
        {
            var player = _replayer.player;

            // Somebody else moved the take: a scrub from the transport, a jump from a tool. What was
            // owed was owed from where it used to be, so it is dropped rather than spent sprinting
            // away from where the operator just put it.
            if (player.frameNumber != _lastPlayerFrameNumber) _owed = 0;

            _Accumulate(in frame);

            var consumed = 0;
            var ranOutOfBudget = false;

            while (true)
            {
                if (consumed >= maxRecordsPerFrame)
                {
                    ranOutOfBudget = true;
                    break;
                }

                if (!player.TryPeekNextFrameNumber(out var next))
                {
                    // The take ended. When something played at this head it is that record's head,
                    // so the end is reached at the next one -- the last frame of a take gets shown
                    // like every other.
                    if (consumed > 0) break;

                    // Looping starts the take again rather than letting the gate detach the source:
                    // the world going back live mid-take is the one thing a loop must not do.
                    if (!_replayer.loop || !_replayer.RewindToStart()) return false;

                    // A pass starts where it starts. Time owed against the end of the take means
                    // nothing at the beginning of it.
                    _owed = 0;
                    consumed++;
                    break;
                }

                // What the step costs, in the take's own frames: how far it is from here to there.
                var first = player.frameNumber < 0;
                var gap = first ? 0 : next - player.frameNumber;
                if (!first && _owed < gap) break;

                var epoch = player.structure.epoch;

                if (!_replayer.Advance()) break;

                // The first record plays whatever is owed, and the accounting starts from it: a
                // frame head that supplied nothing would blank the world rather than hold it, so the
                // opening frame is not something the pacer is allowed to make wait.
                if (first) _owed = 0;
                else _owed -= gap;

                consumed++;

                // A record this take put back has taken the replay away from us: it stopped the
                // replay, or started another. The replayer may already be disposed, so nothing may
                // read it again -- including the supply below.
                if (!ReferenceEquals(FrameGate.source, this)) return false;

                // The shape of the world changed. The inventory is only acted on at the frame head,
                // which has not run yet, so anything the next record addresses to something this one
                // made would be written into a world that does not hold it. The rest of the
                // catch-up waits for that head; what is owed stays owed.
                if (player.structure.epoch != epoch) break;
            }

            // Further behind than one head is allowed to make up. The backlog is dropped rather than
            // carried: making it up would be a sprint the operator sees as the take jumping, and the
            // time really was lost. Real-time pacing resumes from here.
            if (ranOutOfBudget && _owed > 0)
            {
                _owed = 0;
                lateFrameCount++;
            }

            // Held, whether nothing was due or several records were: the take is at one position at
            // the end of a head, and that position is what the frame is a view of.
            var supplied = _replayer.SupplyCurrent(ref frame);
            _lastPlayerFrameNumber = player.frameNumber;
            return supplied;
        }

        public void Dispose() => _replayer.Dispose();

        /// <summary>
        /// Adds the real time this head covers to what the take owes, in the take's own frames.
        /// </summary>
        private void _Accumulate(in Frame frame)
        {
            var live = frame.frameNumber;
            var previous = _lastLiveFrameNumber;
            _lastLiveFrameNumber = live;

            var held = FrameGate.heldFrameCount;
            var waited = held != _lastHeldFrameCount;
            _lastHeldFrameCount = held;

            // Bookkeeping first, then stand down: a pause that did not note where the live clock had
            // got to would resume owing the whole length of the pause.
            if (_replayer.isPaused) return;

            var perLive = _TakeFramesPerLiveFrame(in frame);
            long advanced;

            if (previous == long.MinValue || waited)
            {
                // The first head, or the first after the gate stopped asking while something loaded.
                // Neither is a stretch of time the take was playing through.
                advanced = 1;
            }
            else
            {
                advanced = live - previous;

                // Backwards (a clock that was replaced or reset under us) or a stall long enough
                // that playing through it would be a jump rather than a catch-up. One interval,
                // which is what the gate itself does with a frame number it cannot make sense of.
                var ceiling = _MaxLiveAdvance(in frame);
                if (advanced <= 0 || advanced > ceiling) advanced = 1;
            }

            _owed += advanced * perLive * _speed;
        }

        /// <summary>
        /// How many of the take's frames one of this machine's frames is worth. One when either rate
        /// is unusable, which degrades to a record a head -- the behaviour of a replay with no pacer
        /// at all, rather than a stalled or sprinting one.
        /// </summary>
        private double _TakeFramesPerLiveFrame(in Frame frame)
        {
            var live = frame.frameRate.AsDecimal();
            var take = _replayer.player.header.frameRate.AsDecimal();

            // Written as a failed test rather than a passed one so a NaN, which fails every
            // comparison, lands here rather than being multiplied into what is owed.
            if (!(live > 0) || !(take > 0)) return 1.0;

            return take / live;
        }

        /// <summary>
        /// The longest gap between heads that still counts as time passing: one second. Beyond that
        /// the machine hitched, and a take is better held than fast-forwarded through it.
        /// </summary>
        private static long _MaxLiveAdvance(in Frame frame)
        {
            var perSecond = frame.frameRate.AsDecimal();
            if (!(perSecond > 0)) return 1;

            return (long)perSecond;
        }
    }
}
