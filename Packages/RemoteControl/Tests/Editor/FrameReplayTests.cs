// Copyright (c) You-Ri, 2026
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Lilium.RemoteControl.Frames;
using Lilium.RemoteControl.Frames.Recording;

namespace Lilium.RemoteControl.Tests
{
    public class FrameReplayTests
    {
        [SetUp]
        public void ClearGate()
        {
            FrameGate.sink = null;
            FrameGate.ResetState("[test] cleared");
            FrameGate.SetClock(new FrameCounterClock(FrameRate.FPS60));
        }

        /// <summary>
        /// Puts the live clock back. The gate is process-wide, so a counter clock left behind
        /// here counts pumps for whoever runs next -- and for the editor session after the run,
        /// where it makes the timecode advance at whatever rate the editor happens to tick at.
        /// </summary>
        [TearDown]
        public void ReleaseClearGate()
        {
            FrameGate.sink = null;
            FrameGate.ResetState("[test] cleared");
            FrameGate.RestoreDefaultClock();
        }

        /// <summary>What one call handed the applier, kept past the call.</summary>
        private readonly struct AppliedEvent
        {
            public readonly EventKind kind;
            public readonly string verb;
            public readonly string target;
            public readonly string source;
            public readonly string payloadTypeName;
            public readonly byte[] payload;
            public readonly string text;

            public AppliedEvent(in ReplayEvent evt)
            {
                kind = evt.kind;
                verb = evt.verb;
                target = evt.target;
                source = evt.source;
                payloadTypeName = evt.payloadTypeName;
                payload = evt.payload.ToArray();
                text = evt.text;
            }
        }

        /// <summary>
        /// Collects what a replay hands it instead of applying anything.
        ///
        /// Copies rather than keeping the ReplayEvent: its payload is a window over the replayer's
        /// one buffer, so a list of them would all read as whatever the last event happened to be.
        /// </summary>
        private sealed class RecordingApplier : IEventApplier
        {
            public readonly List<AppliedEvent> applied = new List<AppliedEvent>();

            /// <summary>Targets to refuse, standing in for something that no longer exists.</summary>
            public readonly HashSet<string> refuse = new HashSet<string>();

            public bool Apply(in ReplayEvent evt, out string error)
            {
                if (refuse.Contains(evt.target))
                {
                    error = "404 Not found";
                    return false;
                }

                error = null;
                applied.Add(new AppliedEvent(in evt));
                return true;
            }
        }

        private static byte[] Record(int frames, System.Action beforePump)
        {
            var stream = new MemoryStream();
            var recorder = new FrameRecorder();
            // These tests are about the events they submit, so the recorder is asked not to add the
            // values it restates into each keyframe (see LiveEventRestateSystem): the editor session
            // this runs in has live objects of its own, and their values would show up as events
            // nothing in the test wrote.
            recorder.restateValues = false;

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

        [Test]
        public void EveryRecordedInput_ReachesTheApplier()
        {
            var next = 0;
            var bytes = Record(4, () =>
            {
                var value = (next++).ToString();
                FrameGate._Enqueue(EventKind.PropertyWrite, "test", "/live/object/cam/fov", value,
                    () => true);
            });

            var applier = new RecordingApplier();
            using (var replayer = new FrameReplayer(new MemoryStream(bytes), applier))
            {
                while (replayer.Advance()) { }

                Assert.AreEqual(4, replayer.appliedEventCount);
                Assert.AreEqual(0, replayer.failedEventCount);
            }

            // In order, with the values they were applied with.
            Assert.AreEqual(4, applier.applied.Count);
            for (int i = 0; i < 4; i++)
            {
                Assert.AreEqual(i.ToString(), applier.applied[i].text);
                Assert.AreEqual("/live/object/cam/fov", applier.applied[i].target);
                Assert.AreEqual(EventKind.PropertyWrite, applier.applied[i].kind);
            }
        }

        [Test]
        public void TheVerb_SurvivesSoAReplayDoesNotHaveToGuessIt()
        {
            // The same path answers to more than one verb, so replaying a write as a reset would be
            // a plausible-looking wrong answer rather than a failure.
            var bytes = Record(1, () =>
                FrameGate._Enqueue(EventKind.PropertyWrite, "test", "/live/object/cam/fov", "35.0",
                    () => true, verb: "PUT"));

            var applier = new RecordingApplier();
            using (var replayer = new FrameReplayer(new MemoryStream(bytes), applier))
            {
                while (replayer.Advance()) { }
            }

            Assert.AreEqual("PUT", applier.applied[0].verb);
        }

        [Test]
        public void TheSource_IsCarriedThroughSoATrackCanBeLeftOut()
        {
            var bytes = Record(1, () =>
                FrameGate._Enqueue(EventKind.FunctionCall, "unit-test", "/live/function/reset", "{}",
                    () => true));

            var applier = new RecordingApplier();
            using (var replayer = new FrameReplayer(new MemoryStream(bytes), applier))
            {
                while (replayer.Advance()) { }
            }

            Assert.AreEqual("unit-test", applier.applied[0].source);
            Assert.AreEqual(EventKind.FunctionCall, applier.applied[0].kind);
        }

        [Test]
        public void AnInputThatCannotBeApplied_IsCountedRatherThanStoppingTheReplay()
        {
            var toggle = false;
            var bytes = Record(4, () =>
            {
                toggle = !toggle;
                var target = toggle ? "/live/object/gone/fov" : "/live/object/cam/fov";
                FrameGate._Enqueue(EventKind.PropertyWrite, "test", target, "1", () => true);
            });

            var applier = new RecordingApplier();
            applier.refuse.Add("/live/object/gone/fov");

            using (var replayer = new FrameReplayer(new MemoryStream(bytes), applier))
            {
                // A long take should say how much of it landed rather than stop at the first thing
                // that no longer exists.
                UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
                while (replayer.Advance()) { }
                UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;

                Assert.AreEqual(2, replayer.appliedEventCount);
                Assert.AreEqual(2, replayer.failedEventCount);
            }
        }

        [Test]
        public void ATruncatedPayload_IsSkippedRatherThanAppliedWrong()
        {
            // What was kept of it is not what was applied live, so putting it back would quietly
            // change the value instead of reproducing it.
            var bytes = Record(1, () =>
                FrameGate._Enqueue(EventKind.PropertyWrite, "test", "/live/object/cam/curve",
                    new string('x', 4000), () => true));

            var applier = new RecordingApplier();
            using (var replayer = new FrameReplayer(new MemoryStream(bytes), applier))
            {
                while (replayer.Advance()) { }

                Assert.AreEqual(1, replayer.skippedTruncatedCount);
                Assert.AreEqual(0, replayer.appliedEventCount);
            }

            Assert.IsEmpty(applier.applied);
        }

        [Test]
        public void Seeking_AppliesTheLastWriteToATarget_NotEveryOneWalkedOver()
        {
            // The value as of the destination is what is wanted, not the history of how it got
            // there: putting every intermediate write back would run the same setter once per frame
            // walked, and a setter that loads an asset would load it once per frame.
            var next = 0;
            var bytes = Record(6, () =>
            {
                var value = (next++).ToString();
                FrameGate._Enqueue(EventKind.PropertyWrite, "test", "/live/object/cam/fov", value,
                    () => true);
            });

            var applier = new RecordingApplier();
            using (var replayer = new FrameReplayer(new MemoryStream(bytes), applier))
            {
                Assert.IsTrue(replayer.TrySeek(4));

                Assert.AreEqual(4, replayer.frameNumber);
                Assert.AreEqual(1, replayer.appliedEventCount);
                Assert.AreEqual("4", applier.applied[0].text);
            }
        }

        [Test]
        public void Seeking_PutsBackAValueWrittenBeforeThatFrame()
        {
            // On the event lane the value at a frame is the last write at or before it, so a seek
            // has to walk back to the keyframe and collect. Applying only the destination frame's
            // own records -- which is what this used to do -- left every member nobody happened to
            // touch on that one frame at whatever the machine was already holding.
            var next = 0;
            var bytes = Record(6, () =>
            {
                var value = (next++).ToString();
                FrameGate._Enqueue(EventKind.PropertyWrite, "test", "/live/object/cam/fov", value,
                    () => true);

                // Written once, early, and never again.
                if (value == "1")
                {
                    FrameGate._Enqueue(EventKind.PropertyWrite, "test", "/live/object/cam/near",
                        "0.3", () => true);
                }
            });

            var applier = new RecordingApplier();
            using (var replayer = new FrameReplayer(new MemoryStream(bytes), applier))
            {
                Assert.IsTrue(replayer.TrySeek(4));

                Assert.AreEqual(2, replayer.appliedEventCount, "one write per target was expected");

                var near = applier.applied.Find(e => e.target == "/live/object/cam/near");
                Assert.AreEqual("0.3", near.text, "a value set before the seek point was lost");

                var fov = applier.applied.Find(e => e.target == "/live/object/cam/fov");
                Assert.AreEqual("4", fov.text);
            }
        }

        [Test]
        public void TheRecording_SaysHowManyFramesItHoldsAndWhereTheyStart()
        {
            // What a scrubber moves through. Without it a position can only be turned into a frame
            // number by walking the recording, which is the thing seeking exists to avoid.
            var bytes = Record(6, null);

            using (var player = new FrameRecordPlayer(new MemoryStream(bytes)))
            {
                Assert.AreEqual(6, player.frameCount);
                Assert.IsTrue(player.TrySeek(player.firstFrameNumber));
                Assert.AreEqual(player.firstFrameNumber, player.frameNumber);
            }
        }

        [Test]
        public void Holding_ReSuppliesTheSameFrameInsteadOfWalkingOn()
        {
            var next = 0;
            var bytes = Record(6, () =>
            {
                var value = (next++).ToString();
                FrameGate._Enqueue(EventKind.PropertyWrite, "test", "/live/object/cam/fov", value,
                    () => true);
            });

            var applier = new RecordingApplier();
            var frame = new Frame();

            using (var replayer = new FrameReplayer(new MemoryStream(bytes), applier))
            {
                Assert.IsTrue(replayer.FillFrame(ref frame));
                var held = replayer.frameNumber;

                replayer.isPaused = true;

                // Still supplying, so the gate keeps taking its frame from the recording rather than
                // handing the world straight back to the live producers.
                Assert.IsTrue(replayer.FillFrame(ref frame));
                Assert.IsTrue(replayer.FillFrame(ref frame));

                Assert.AreEqual(held, replayer.frameNumber);

                // And the frame's own events are not applied again while it is held.
                Assert.AreEqual(1, replayer.appliedEventCount);
            }

            Assert.AreEqual(1, applier.applied.Count);
        }

        [Test]
        public void HoldingBeforeTheFirstFrame_StillPlaysOne()
        {
            // Paused from the start, the player's lanes are empty -- and supplying those would blank
            // the world rather than hold it.
            var bytes = Record(3, null);

            var applier = new RecordingApplier();
            var frame = new Frame();

            using (var replayer = new FrameReplayer(new MemoryStream(bytes), applier))
            {
                replayer.isPaused = true;

                Assert.IsTrue(replayer.FillFrame(ref frame));
                Assert.GreaterOrEqual(replayer.frameNumber, 0);
                Assert.IsNotNull(frame.state);
            }
        }

        [Test]
        public void SeekingWhileHeld_MovesTheFrameThatIsSupplied()
        {
            // What a scrub is: a hold, then a jump. The held frame has to follow the jump, or the
            // slider moves and the world does not.
            var bytes = Record(6, null);

            var applier = new RecordingApplier();
            var frame = new Frame();

            using (var replayer = new FrameReplayer(new MemoryStream(bytes), applier))
            {
                Assert.IsTrue(replayer.FillFrame(ref frame));

                replayer.isPaused = true;
                Assert.IsTrue(replayer.TrySeek(4));
                Assert.AreEqual(4, replayer.frameNumber);

                Assert.IsTrue(replayer.FillFrame(ref frame));
                Assert.AreEqual(4, replayer.frameNumber);
            }
        }
    }
}
