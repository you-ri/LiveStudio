// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Lilium.RemoteControl.Frames;
using Lilium.RemoteControl.Frames.Recording;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// Playing a take at the speed it was recorded at.
    ///
    /// The case these are all built around: a machine that was managing twenty frames a second wrote
    /// a record every third frame number, and a machine doing sixty must not spend those records
    /// three times as fast as they were made.
    /// </summary>
    public class PacedFrameSourceTests
    {
        /// <summary>A clock that counts by a fixed step, standing in for a machine at that rate.</summary>
        private sealed class SteppingClock : IFrameClock
        {
            private readonly long _step;
            private long _next;

            public SteppingClock(long step, FrameRate rate)
            {
                _step = step;
                frameRate = rate;
                Reset();
            }

            public FrameRate frameRate { get; }

            public long Advance()
            {
                _next += _step;
                return _next;
            }

            // One step short of zero, so the first frame of a take is frame zero whatever the step
            // is. A take that starts at two reads the same as one that starts at zero everywhere
            // except in an assertion about where it ends.
            public void Reset() => _next = -_step;
        }

        /// <summary>
        /// A clock that hands out exactly the numbers a test names.
        ///
        /// What a run below rate really looks like: not a slower rate, but the same one with frames
        /// missing where the machine was busy, scattered unevenly.
        /// </summary>
        private sealed class ScriptedClock : IFrameClock
        {
            private readonly long[] _numbers;
            private int _next;

            public ScriptedClock(params long[] numbers)
            {
                _numbers = numbers;
            }

            public FrameRate frameRate => FrameRate.FPS60;

            public long Advance() => _numbers[Math.Min(_next++, _numbers.Length - 1)];

            public void Reset() => _next = 0;
        }

        /// <summary>Collects what a replay hands it instead of applying anything.</summary>
        private sealed class NullApplier : IEventApplier
        {
            public readonly List<string> applied = new List<string>();

            /// <summary>Runs with the event, for a take that reaches back into the gate.</summary>
            public Action<ReplayEvent> onApply;

            public bool Apply(in ReplayEvent evt, out string error)
            {
                error = null;
                applied.Add(evt.text);
                onApply?.Invoke(evt);
                return true;
            }
        }

        [SetUp]
        public void ClearGate()
        {
            FrameGate.sink = null;
            FrameGate.source = null;
            FrameGate.ResetState("[test] cleared");
        }

        [TearDown]
        public void ReleaseClearGate()
        {
            FrameGate.sink = null;
            FrameGate.source = null;
            FrameGate.ResetState("[test] cleared");
            FrameGate.RestoreDefaultClock();
        }

        /// <summary>
        /// Records a take on a machine that manages one frame every <paramref name="step"/> frame
        /// numbers. A step of three is the twenty-frames-a-second machine these tests are about.
        /// </summary>
        private static byte[] Record(int frames, long step, Action beforePump = null)
            => Record(frames, step, FrameRate.FPS60, beforePump);

        private static byte[] Record(int frames, long step, FrameRate rate, Action beforePump = null)
        {
            FrameGate.SetClock(new SteppingClock(step, rate));

            return RecordFrames(frames, beforePump);
        }

        /// <summary>
        /// Records a take whose frames land on exactly these numbers -- a run at rate that lost the
        /// ones in between.
        /// </summary>
        private static byte[] RecordAt(params long[] frameNumbers)
        {
            FrameGate.SetClock(new ScriptedClock(frameNumbers));

            return RecordFrames(frameNumbers.Length, null);
        }

        private static byte[] RecordFrames(int frames, Action beforePump)
        {
            var stream = new MemoryStream();
            var recorder = new FrameRecorder();

            recorder.Start(stream, leaveOpen: true);
            FrameGate.sink = recorder;

            try
            {
                for (int i = 0; i < frames; i++)
                {
                    beforePump?.Invoke();
                    FrameGate.Pump();
                }
            }
            finally
            {
                FrameGate.sink = null;
                recorder.Stop();
            }

            return stream.ToArray();
        }

        private static PacedFrameSource Open(byte[] bytes, IEventApplier applier, bool loop = false)
        {
            var replayer = new FrameReplayer(new MemoryStream(bytes), applier) { loop = loop };
            return new PacedFrameSource(replayer);
        }

        /// <summary>One frame head at <paramref name="liveFrameNumber"/>, driven without the gate.</summary>
        private static bool Pump(PacedFrameSource source, long liveFrameNumber, EventFrame lane = null)
        {
            var frame = new Frame
            {
                frameNumber = liveFrameNumber,
                frameRate = FrameRate.FPS60,
                events = lane,
            };

            return source.FillFrame(ref frame);
        }

        [Test]
        public void ATakeRecordedBelowRate_PlaysBackInTheSameWallTime()
        {
            // Twelve records, one every third frame number: what a machine managing twenty frames a
            // second writes over thirty-six of them.
            var bytes = Record(12, step: 3);

            var applier = new NullApplier();
            using (var source = Open(bytes, applier))
            {
                FrameGate.source = source;

                var positions = new List<long>();
                for (long live = 0; live < 100; live++)
                {
                    if (!Pump(source, live)) break;
                    positions.Add(source.replayer.frameNumber);
                }

                // Twelve records spread over thirty-four heads rather than spent in twelve: the take
                // lasts as long as the performance did instead of a third as long. Thirty-four and
                // not thirty-six because the last record has no successor to say how long it stood,
                // so the take ends on it.
                Assert.AreEqual(34, positions.Count,
                    "the take did not play at the speed it was recorded at");
                Assert.AreEqual(33, positions[positions.Count - 1], "the take did not reach its end");

                // And it moved once every three heads rather than at every one.
                Assert.AreEqual(0, positions[0]);
                Assert.AreEqual(0, positions[1]);
                Assert.AreEqual(0, positions[2]);
                Assert.AreEqual(3, positions[3]);
                Assert.AreEqual(3, positions[5]);
                Assert.AreEqual(6, positions[6]);
            }
        }

        [Test]
        public void ATakeWhoseFramesWereDroppedUnevenly_HoldsEachOneForAsLongAsItLasted()
        {
            // What a run below rate actually produces. Nothing here is periodic: the machine lost
            // three frames in one place, six in another and fourteen at the end, and the average
            // rate that comes out of it describes none of those moments. A pacer that worked from
            // an average would show the take smoothly and wrongly.
            var numbers = new long[] { 0, 3, 7, 8, 14, 15, 16, 30 };
            var bytes = RecordAt(numbers);

            var applier = new NullApplier();
            using (var source = Open(bytes, applier))
            {
                FrameGate.source = source;

                for (long live = 0; live <= 30; live++)
                {
                    Assert.IsTrue(Pump(source, live), $"the take ended early, at head {live}");

                    // The take was recorded on this machine's axis and starts at zero, so at every
                    // head it stands on the last frame recorded at or before now: one that was held
                    // for fourteen numbers is shown for fourteen heads, and one held for a single
                    // number for a single head.
                    Assert.AreEqual(LastAtOrBefore(numbers, live), source.replayer.frameNumber,
                        $"at head {live}");
                }

                Assert.IsFalse(Pump(source, 31), "the take did not end");
            }
        }

        private static long LastAtOrBefore(long[] numbers, long frame)
        {
            var found = -1L;
            for (int i = 0; i < numbers.Length; i++)
            {
                if (numbers[i] > frame) break;

                found = numbers[i];
            }

            return found;
        }

        [Test]
        public void OnAMachineSlowerThanTheTake_SeveralRecordsAreSpentAtOneHead()
        {
            var next = 0;
            var bytes = Record(9, step: 1, () =>
            {
                var value = (next++).ToString();
                FrameGate._Enqueue(EventKind.Set, "test", "/live/object/cam/fov", value, () => true);
            });

            var applier = new NullApplier();
            using (var source = Open(bytes, applier))
            {
                FrameGate.source = source;

                // The machine playing it manages one head every three frame numbers.
                Pump(source, 0);
                Assert.AreEqual(0, source.replayer.frameNumber);

                using (var lane = new EventFrame())
                {
                    lane.Reset(1, FrameRate.FPS60);
                    Pump(source, 3, lane);

                    // Three of the take's frames passed, so three of its records did -- in order,
                    // none skipped, and all carried at the head they became visible at.
                    Assert.AreEqual(3, source.replayer.frameNumber);
                    CollectionAssert.AreEqual(new[] { "0", "1", "2", "3" }, applier.applied);
                    Assert.AreEqual(3, lane.eventCount,
                        "the records put back were not carried in the lane");
                }
            }
        }

        [Test]
        public void ARateTheTakeWasNotRecordedAt_IsConvertedRatherThanCounted()
        {
            // A take written against a thirty-frame clock, played on a machine at sixty. Its records
            // are one frame number apart, but one of its frames is two of this machine's.
            var bytes = Record(8, step: 1, new FrameRate(1, 30));

            var applier = new NullApplier();
            using (var source = Open(bytes, applier))
            {
                FrameGate.source = source;

                for (long live = 0; live < 5; live++) Pump(source, live);

                Assert.AreEqual(2, source.replayer.frameNumber,
                    "the take was counted in this machine's frames rather than in its own");
            }
        }

        [Test]
        public void APauseHoldsThePositionAndDoesNotBankTheTimeItLasted()
        {
            var bytes = Record(10, step: 1);

            var applier = new NullApplier();
            using (var source = Open(bytes, applier))
            {
                FrameGate.source = source;

                Pump(source, 0);
                Pump(source, 1);
                Assert.AreEqual(1, source.replayer.frameNumber);

                source.replayer.isPaused = true;
                for (long live = 2; live < 20; live++) Pump(source, live);

                Assert.AreEqual(1, source.replayer.frameNumber, "a pause did not hold the take");

                // Resumed, it carries on from where it was rather than making up the pause.
                source.replayer.isPaused = false;
                Pump(source, 20);
                Assert.AreEqual(2, source.replayer.frameNumber, "the pause was made up for");
            }
        }

        [Test]
        public void AFrameSupplyThatWasHeldForLoading_IsNotMadeUpFor()
        {
            var bytes = Record(10, step: 1);

            var applier = new NullApplier();
            using (var source = Open(bytes, applier))
            {
                FrameGate.source = source;

                Pump(source, 0);
                Assert.AreEqual(0, source.replayer.frameNumber);

                // The gate stopped asking for frames while something loaded, and counts the heads it
                // stood still for. That is how a wait is told from a machine falling behind.
                FrameGate.HoldSupply("[test] loading");
                for (int i = 0; i < 30; i++) FrameGate.Pump();
                FrameGate.ReleaseSupply("[test] loading");

                Pump(source, 40);

                Assert.AreEqual(1, source.replayer.frameNumber,
                    "the take sprinted through the frames it was held for");
            }
        }

        [Test]
        public void AHeadThatIsFarBehind_SpendsABoundedNumberOfRecords()
        {
            var bytes = Record(200, step: 1);

            var applier = new NullApplier();
            using (var source = Open(bytes, applier))
            {
                FrameGate.source = source;
                source.maxRecordsPerFrame = 4;

                Pump(source, 0);

                // Half a second of frame numbers at once: near enough to count as time passing, and
                // still capped at what one head is allowed to put back.
                Pump(source, 30);

                Assert.AreEqual(4, source.replayer.frameNumber, "one head put back more than its cap");
                Assert.AreEqual(1, source.lateFrameCount);

                // The backlog was dropped rather than carried, so the next head is an ordinary one.
                Pump(source, 31);
                Assert.AreEqual(5, source.replayer.frameNumber, "the dropped backlog was spent later");
            }
        }

        [Test]
        public void AStallLongerThanASecond_IsHeldRatherThanPlayedThrough()
        {
            var bytes = Record(200, step: 1);

            var applier = new NullApplier();
            using (var source = Open(bytes, applier))
            {
                FrameGate.source = source;

                Pump(source, 0);
                Pump(source, 600);

                // Ten seconds of wall time in which the machine drew nothing. Playing ten seconds of
                // take through it would be a jump; one frame is what the gate itself does with a
                // distance it cannot make sense of.
                Assert.AreEqual(1, source.replayer.frameNumber);
            }
        }

        [Test]
        public void ASeekWhileRunning_DoesNotSprintAfterwards()
        {
            var bytes = Record(60, step: 3);

            var applier = new NullApplier();
            using (var source = Open(bytes, applier))
            {
                FrameGate.source = source;

                Pump(source, 0);
                Pump(source, 1);

                // The transport jumps. What was owed was owed from where the take used to be.
                Assert.IsTrue(source.replayer.TrySeek(source.replayer.player.FrameNumberAt(20)));
                var landed = source.replayer.frameNumber;

                Pump(source, 2);
                Assert.AreEqual(landed, source.replayer.frameNumber, "the take moved off the seek");

                Pump(source, 3);
                Pump(source, 4);
                Pump(source, 5);
                Assert.AreEqual(landed + 3, source.replayer.frameNumber,
                    "pacing did not resume from where the seek landed");
            }
        }

        [Test]
        public void ALoopStartsThePassAgainRatherThanEndingTheTake()
        {
            var bytes = Record(4, step: 1);

            var applier = new NullApplier();
            using (var source = Open(bytes, applier, loop: true))
            {
                FrameGate.source = source;

                for (long live = 0; live < 4; live++) Assert.IsTrue(Pump(source, live));
                Assert.AreEqual(3, source.replayer.frameNumber);

                // Past the end: the take starts again instead of retiring the source.
                Assert.IsTrue(Pump(source, 4), "a looping take ended");
                Assert.AreEqual(0, source.replayer.frameNumber, "the loop did not rewind");
            }
        }

        [Test]
        public void ATakeThatEnds_RetiresTheSource()
        {
            var bytes = Record(3, step: 1);

            var applier = new NullApplier();
            using (var source = Open(bytes, applier))
            {
                FrameGate.source = source;

                for (long live = 0; live < 3; live++) Assert.IsTrue(Pump(source, live));

                Assert.IsFalse(Pump(source, 3), "the take did not end");
            }
        }

        [Test]
        public void ARecordThatStopsTheReplay_DoesNotReadTheTakeAgain()
        {
            var next = 0;
            var bytes = Record(9, step: 1, () =>
            {
                var value = (next++).ToString();
                FrameGate._Enqueue(EventKind.Set, "test", "/live/object/cam/fov", value, () => true);
            });

            var applier = new NullApplier();
            var source = Open(bytes, applier);

            // A take carries the requests that were made during it, and one of them can be the one
            // that stops the replay playing it back. Anything read after that is read out of a
            // recording that has been closed.
            applier.onApply = evt =>
            {
                if (evt.text == "0") return;

                FrameGate.source = null;
                source.Dispose();
            };

            FrameGate.source = source;

            Assert.IsTrue(Pump(source, 0));
            Assert.AreEqual(1, applier.applied.Count);

            // Five more records are due at this head. The first of them takes the replay away, and
            // nothing after it may touch the take.
            Assert.IsFalse(Pump(source, 5), "a detached source went on supplying frames");
            Assert.AreEqual(2, applier.applied.Count, "the take was read after it was disposed");
        }

        [Test]
        public void TheGate_DoesNotDetachASourceARecordJustPutInItsPlace()
        {
            var bytes = Record(4, step: 1);
            var applier = new NullApplier();

            using (var replaced = Open(bytes, applier))
            {
                // A source that is already finished, standing in for the take that ends by starting
                // another one.
                var ended = new EndedSource(() => FrameGate.source = replaced);
                FrameGate.source = ended;
                FrameGate.Pump();

                Assert.AreSame(replaced, FrameGate.source,
                    "the gate detached the source the ending one had just installed");

                FrameGate.source = null;
            }
        }

        /// <summary>Retires immediately, having put something else in its place on the way out.</summary>
        private sealed class EndedSource : IFrameSource
        {
            private readonly Action _onFill;

            public EndedSource(Action onFill)
            {
                _onFill = onFill;
            }

            public bool FillFrame(ref Frame frame)
            {
                _onFill();
                return false;
            }
        }
    }
}
