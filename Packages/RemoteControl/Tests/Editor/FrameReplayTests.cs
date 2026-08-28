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
        private readonly struct AppliedInput
        {
            public readonly InputKind kind;
            public readonly string verb;
            public readonly string target;
            public readonly string source;
            public readonly string payloadTypeName;
            public readonly byte[] payload;
            public readonly string text;

            public AppliedInput(in ReplayInput input)
            {
                kind = input.kind;
                verb = input.verb;
                target = input.target;
                source = input.source;
                payloadTypeName = input.payloadTypeName;
                payload = input.payload.ToArray();
                text = input.text;
            }
        }

        /// <summary>
        /// Collects what a replay hands it instead of applying anything.
        ///
        /// Copies rather than keeping the ReplayInput: its payload is a window over the replayer's
        /// one buffer, so a list of them would all read as whatever the last input happened to be.
        /// </summary>
        private sealed class RecordingApplier : IInputApplier
        {
            public readonly List<AppliedInput> applied = new List<AppliedInput>();

            /// <summary>Targets to refuse, standing in for something that no longer exists.</summary>
            public readonly HashSet<string> refuse = new HashSet<string>();

            public bool Apply(in ReplayInput input, out string error)
            {
                if (refuse.Contains(input.target))
                {
                    error = "404 Not found";
                    return false;
                }

                error = null;
                applied.Add(new AppliedInput(in input));
                return true;
            }
        }

        private static byte[] Record(int frames, System.Action beforePump)
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

        [Test]
        public void EveryRecordedInput_ReachesTheApplier()
        {
            var next = 0;
            var bytes = Record(4, () =>
            {
                var value = (next++).ToString();
                FrameGate._Enqueue(InputKind.PropertyWrite, "test", "/live/object/cam/fov", value,
                    () => true);
            });

            var applier = new RecordingApplier();
            using (var replayer = new FrameReplayer(new MemoryStream(bytes), applier))
            {
                while (replayer.Advance()) { }

                Assert.AreEqual(4, replayer.appliedInputCount);
                Assert.AreEqual(0, replayer.failedInputCount);
            }

            // In order, with the values they were applied with.
            Assert.AreEqual(4, applier.applied.Count);
            for (int i = 0; i < 4; i++)
            {
                Assert.AreEqual(i.ToString(), applier.applied[i].text);
                Assert.AreEqual("/live/object/cam/fov", applier.applied[i].target);
                Assert.AreEqual(InputKind.PropertyWrite, applier.applied[i].kind);
            }
        }

        [Test]
        public void TheVerb_SurvivesSoAReplayDoesNotHaveToGuessIt()
        {
            // The same path answers to more than one verb, so replaying a write as a reset would be
            // a plausible-looking wrong answer rather than a failure.
            var bytes = Record(1, () =>
                FrameGate._Enqueue(InputKind.PropertyWrite, "test", "/live/object/cam/fov", "35.0",
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
                FrameGate._Enqueue(InputKind.FunctionCall, "unit-test", "/live/function/reset", "{}",
                    () => true));

            var applier = new RecordingApplier();
            using (var replayer = new FrameReplayer(new MemoryStream(bytes), applier))
            {
                while (replayer.Advance()) { }
            }

            Assert.AreEqual("unit-test", applier.applied[0].source);
            Assert.AreEqual(InputKind.FunctionCall, applier.applied[0].kind);
        }

        [Test]
        public void AnInputThatCannotBeApplied_IsCountedRatherThanStoppingTheReplay()
        {
            var toggle = false;
            var bytes = Record(4, () =>
            {
                toggle = !toggle;
                var target = toggle ? "/live/object/gone/fov" : "/live/object/cam/fov";
                FrameGate._Enqueue(InputKind.PropertyWrite, "test", target, "1", () => true);
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

                Assert.AreEqual(2, replayer.appliedInputCount);
                Assert.AreEqual(2, replayer.failedInputCount);
            }
        }

        [Test]
        public void ATruncatedPayload_IsSkippedRatherThanAppliedWrong()
        {
            // What was kept of it is not what was applied live, so putting it back would quietly
            // change the value instead of reproducing it.
            var bytes = Record(1, () =>
                FrameGate._Enqueue(InputKind.PropertyWrite, "test", "/live/object/cam/curve",
                    new string('x', 4000), () => true));

            var applier = new RecordingApplier();
            using (var replayer = new FrameReplayer(new MemoryStream(bytes), applier))
            {
                while (replayer.Advance()) { }

                Assert.AreEqual(1, replayer.skippedTruncatedCount);
                Assert.AreEqual(0, replayer.appliedInputCount);
            }

            Assert.IsEmpty(applier.applied);
        }

        [Test]
        public void Seeking_AppliesOnlyThatFramesInputs()
        {
            // The frames walked through on the way are already accounted for in the state that was
            // restored; applying their inputs again would be a second helping of the same change.
            var next = 0;
            var bytes = Record(6, () =>
            {
                var value = (next++).ToString();
                FrameGate._Enqueue(InputKind.PropertyWrite, "test", "/live/object/cam/fov", value,
                    () => true);
            });

            var applier = new RecordingApplier();
            using (var replayer = new FrameReplayer(new MemoryStream(bytes), applier))
            {
                Assert.IsTrue(replayer.TrySeek(4));

                Assert.AreEqual(4, replayer.frameNumber);
                Assert.AreEqual(1, replayer.appliedInputCount);
                Assert.AreEqual("4", applier.applied[0].text);
            }
        }
    }
}
