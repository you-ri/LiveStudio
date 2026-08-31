// Copyright (c) You-Ri, 2026
using NUnit.Framework;
using UnityEngine;
using Lilium.RemoteControl.Frames;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// The tick anything that advances with time is supposed to read.
    ///
    /// The engine's own tick is whatever this machine managed to render, so two machines fed the
    /// same events integrate different amounts and drift apart. This one is the width of the step
    /// the frame covers, worked out from the frame numbers -- the same on every machine replaying
    /// the same take.
    /// </summary>
    public class FrameTickTests
    {
        /// <summary>A clock that hands out whatever numbers a test wants, in order.</summary>
        private sealed class ScriptedClock : IFrameClock
        {
            private readonly long[] _numbers;
            private int _next;

            public ScriptedClock(params long[] numbers)
            {
                _numbers = numbers;
            }

            public FrameRate frameRate => FrameRate.FPS60;

            public long Advance() => _numbers[Mathf.Min(_next++, _numbers.Length - 1)];

            public void Reset() => _next = 0;
        }

        /// <summary>Stands in for a replay: hands the gate a frame that says when it is from.</summary>
        private sealed class ScriptedSource : IFrameSource
        {
            public long frameNumber;
            public int remaining = int.MaxValue;

            /// <summary>Frames actually handed over, so a test can tell a stall from a slow take.</summary>
            public int frameCount;

            public bool FillFrame(ref Frame frame)
            {
                if (remaining-- <= 0) return false;

                frameCount++;
                frame.frameNumber = frameNumber;
                frame.frameRate = FrameRate.FPS60;
                return true;
            }
        }

        [SetUp]
        public void ClearGate()
        {
            FrameGate.sink = null;
            FrameGate.ResetState("[test] cleared");
        }

        [TearDown]
        public void ReleaseClearGate()
        {
            Assert.AreEqual(0, FrameGate.supplyHoldCount, "a test left the replay waiting on something");

            FrameGate.driveEngineTimeOnSuppliedFrames = false;
            FrameGate.sink = null;
            FrameGate.source = null;
            FrameGate.ResetState("[test] cleared");
            FrameGate.RestoreDefaultClock();

            Assert.AreEqual(0f, Time.captureDeltaTime, "the engine's clock was left driven by a test");
        }

        [Test]
        public void OneFrameToTheNext_IsOneInterval()
        {
            FrameGate.SetClock(new ScriptedClock(10, 11));

            FrameGate.Pump();
            FrameGate.Pump();

            Assert.AreEqual(1f / 60f, FrameGate.deltaTime, 1e-6f);
        }

        [Test]
        public void ASkippedFrameNumber_WidensTheTick()
        {
            // The pump missed two intervals: time really did pass, and hiding it would make
            // everything that integrates over the tick run slow whenever the machine stutters.
            FrameGate.SetClock(new ScriptedClock(10, 13));

            FrameGate.Pump();
            FrameGate.Pump();

            Assert.AreEqual(3f / 60f, FrameGate.deltaTime, 1e-6f);
        }

        [Test]
        public void AStepBackwards_IsOneInterval()
        {
            // A scrub is a seek, not a duration: nothing moved through the frames in between, and a
            // negative tick would run every integrator backwards.
            FrameGate.SetClock(new ScriptedClock(100, 40));

            FrameGate.Pump();
            FrameGate.Pump();

            Assert.AreEqual(1f / 60f, FrameGate.deltaTime, 1e-6f);
        }

        [Test]
        public void ASuppliedFrame_HandsOutTheRecordedTick_NotThisMachines()
        {
            // The clock this machine runs on says one thing; the recording says another. What a
            // replay hands out has to be the recording's.
            FrameGate.SetClock(new ScriptedClock(0, 1));
            var source = new ScriptedSource { frameNumber = 500 };
            FrameGate.source = source;

            FrameGate.Pump();
            source.frameNumber = 504;
            FrameGate.Pump();

            Assert.AreEqual(4f / 60f, FrameGate.deltaTime, 1e-6f);
        }

        [Test]
        public void AReplayDrivingEngineTime_StepsTheEnginesClockByTheRecordedTick()
        {
            FrameGate.SetClock(new ScriptedClock(0, 1));
            FrameGate.driveEngineTimeOnSuppliedFrames = true;

            var source = new ScriptedSource { frameNumber = 500 };
            FrameGate.source = source;

            FrameGate.Pump();
            source.frameNumber = 502;
            FrameGate.Pump();

            Assert.AreEqual(2f / 60f, Time.captureDeltaTime, 1e-6f,
                "code still reading Time.deltaTime would not be following the take");
        }

        [Test]
        public void WhenTheReplayEnds_TheEnginesClockGoesBackToRealTime()
        {
            FrameGate.SetClock(new ScriptedClock(0, 1, 2));
            FrameGate.driveEngineTimeOnSuppliedFrames = true;

            FrameGate.source = new ScriptedSource { frameNumber = 500, remaining = 1 };

            FrameGate.Pump();
            Assert.AreNotEqual(0f, Time.captureDeltaTime);

            // The source ran out: the frames after it are live ones, and a live run is not stepped.
            FrameGate.Pump();

            Assert.AreEqual(0f, Time.captureDeltaTime);
        }

        [Test]
        public void AHeldSupply_KeepsTheReplayWhereItIs()
        {
            // The take asked for something that is still loading. Playing on would put frames into a
            // world that has not finished being built, and the take's fidelity would come to depend
            // on how fast this machine reads a disk.
            FrameGate.SetClock(new ScriptedClock(0, 1, 2));
            var source = new ScriptedSource { frameNumber = 500 };
            FrameGate.source = source;

            FrameGate.Pump();
            Assert.AreEqual(1, source.frameCount, "the first frame did not come from the recording");

            FrameGate.HoldSupply("test");
            FrameGate.Pump();

            Assert.AreEqual(1, source.frameCount, "the replay ran on while something was still loading");
            Assert.AreEqual(1, FrameGate.heldFrameCount);

            FrameGate.ReleaseSupply("test");
            FrameGate.Pump();

            Assert.AreEqual(2, source.frameCount, "the replay did not resume once the wait was over");
        }

        [Test]
        public void TwoThingsToWaitOn_AreBothWaitedFor()
        {
            FrameGate.SetClock(new ScriptedClock(0, 1, 2, 3));
            var source = new ScriptedSource { frameNumber = 500 };
            FrameGate.source = source;

            FrameGate.HoldSupply("a");
            FrameGate.HoldSupply("b");
            FrameGate.Pump();

            FrameGate.ReleaseSupply("a");
            FrameGate.Pump();
            Assert.AreEqual(0, source.frameCount, "the replay went on before the second one finished");

            FrameGate.ReleaseSupply("b");
            FrameGate.Pump();
            Assert.AreEqual(1, source.frameCount);
        }

        [Test]
        public void ALiveRun_LeavesTheEnginesClockAlone()
        {
            // Nothing is being reproduced, so nothing should be stepping the application's time --
            // including a viewer that merely has frames going past it.
            FrameGate.SetClock(new ScriptedClock(0, 1));
            FrameGate.driveEngineTimeOnSuppliedFrames = true;

            FrameGate.Pump();
            FrameGate.Pump();

            Assert.AreEqual(0f, Time.captureDeltaTime);
        }
    }
}
