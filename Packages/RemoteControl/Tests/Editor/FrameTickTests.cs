// Copyright (c) You-Ri, 2026
using System.Collections.Generic;
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

        [Test]
        public void AStepWiderThanAQuarterOfASecond_IsCutToThat()
        {
            // Six seconds in which nothing was pumped. Integrating over all of it at once would be a
            // leap nothing lived through.
            FrameGate.SetClock(new ScriptedClock(10, 370));

            FrameGate.Pump();
            FrameGate.Pump();

            Assert.AreEqual(15f / 60f, FrameGate.deltaTime, 1e-6f);
        }

        [Test]
        public void TheFirstSuppliedFrameAfterLiveOnes_IsOneInterval()
        {
            // The live clock counts the time of day and the take counts its own frames. The
            // difference between one of each is not a duration.
            FrameGate.SetClock(new ScriptedClock(0, 1));
            FrameGate.Pump();

            FrameGate.source = new ScriptedSource { frameNumber = 3_672_000 };
            FrameGate.Pump();

            Assert.AreEqual(1f / 60f, FrameGate.deltaTime, 1e-6f);
        }

        [Test]
        public void ASuppliedFrameThatStoodStill_CoversNoTime_AndGivesTheEngineBack()
        {
            FrameGate.SetClock(new ScriptedClock(0, 1, 2));
            FrameGate.driveEngineTimeOnSuppliedFrames = true;

            var source = new ScriptedSource { frameNumber = 500 };
            FrameGate.source = source;

            FrameGate.Pump();
            source.frameNumber = 501;
            FrameGate.Pump();
            Assert.AreEqual(1f / 60f, Time.captureDeltaTime, 1e-6f);

            // Held: the take is on the record it was already on. There is no step to hand the
            // engine, and keeping the last one would spend it again on every frame the hold lasts.
            FrameGate.Pump();

            Assert.AreEqual(0f, FrameGate.deltaTime);
            Assert.AreEqual(0f, Time.captureDeltaTime);
        }

        /// <summary>
        /// A clock that can be waited on, where waiting is what moves it: a wait for a frame arrives
        /// at that frame, the way real time would, and is written down so a test can see what the
        /// gate waited for.
        /// </summary>
        private sealed class WaitableClock : IFrameClock, IFrameClockSync
        {
            public long now;

            /// <summary>Stands in for an external sync source that lost its signal.</summary>
            public bool stuck;

            public readonly List<long> waits = new List<long>();

            public FrameRate frameRate => FrameRate.FPS60;

            public long Advance() => now;

            public void Reset() => now = 0;

            public bool WaitUntil(long frameNumber, int timeoutMs)
            {
                waits.Add(frameNumber);
                if (stuck) return false;

                if (now < frameNumber) now = frameNumber;
                return true;
            }
        }

        /// <summary>A source that moves one frame a head and says it is due a fixed distance ahead.</summary>
        private sealed class ScheduledSource : IFrameSource, IFrameSchedule
        {
            public long frameNumber;
            public long dueIn = 1;
            public bool moving = true;

            public bool FillFrame(ref Frame frame)
            {
                frame.frameNumber = frameNumber++;
                frame.frameRate = FrameRate.FPS60;
                return true;
            }

            public bool TryGetNextDue(long lastClockFrame, FrameRate clockRate, out long dueClockFrame)
            {
                dueClockFrame = lastClockFrame + dueIn;
                return moving;
            }
        }

        [Test]
        public void ADrivenReplay_WaitsForEachClockFrame()
        {
            var clock = new WaitableClock();
            FrameGate.SetClock(clock);
            FrameGate.driveEngineTimeOnSuppliedFrames = true;
            FrameGate.source = new ScriptedSource { frameNumber = 500 };

            FrameGate._PumpAtFrameHead();
            FrameGate._PumpAtFrameHead();
            FrameGate._PumpAtFrameHead();

            // The first head has nothing before it to wait from. Every one after it waits for the
            // next frame, so the engine renders one frame per step rather than as many as it can --
            // which is what spent the same step several times over and ran the take fast.
            CollectionAssert.AreEqual(new long[] { 1, 2 }, clock.waits);
        }

        [Test]
        public void AScheduledSource_IsWaitedForUntilItIsDue()
        {
            var clock = new WaitableClock();
            FrameGate.SetClock(clock);
            FrameGate.driveEngineTimeOnSuppliedFrames = true;
            FrameGate.source = new ScheduledSource { frameNumber = 500, dueIn = 3 };

            FrameGate._PumpAtFrameHead();
            FrameGate._PumpAtFrameHead();

            CollectionAssert.AreEqual(new long[] { 3 }, clock.waits);
        }

        [Test]
        public void AScheduleFarAhead_IsWaitedForAQuarterOfASecondAtATime()
        {
            // A take that stood still for a long stretch. Waiting all of it out at once would freeze
            // the application for the length of the gap.
            var clock = new WaitableClock();
            FrameGate.SetClock(clock);
            FrameGate.driveEngineTimeOnSuppliedFrames = true;
            FrameGate.source = new ScheduledSource { frameNumber = 500, dueIn = 1000 };

            FrameGate._PumpAtFrameHead();
            FrameGate._PumpAtFrameHead();

            CollectionAssert.AreEqual(new long[] { 15 }, clock.waits);
        }

        [Test]
        public void AHeldSchedule_StillWaitsOneFrame()
        {
            // A paused take is not due at all, but a head that did not wait would run the editor as
            // fast as it can go for the length of the pause.
            var clock = new WaitableClock();
            FrameGate.SetClock(clock);
            FrameGate.driveEngineTimeOnSuppliedFrames = true;
            FrameGate.source = new ScheduledSource { frameNumber = 500, dueIn = 1000, moving = false };

            FrameGate._PumpAtFrameHead();
            FrameGate._PumpAtFrameHead();

            CollectionAssert.AreEqual(new long[] { 1 }, clock.waits);
        }

        [Test]
        public void ALiveRun_IsNeverWaitedFor()
        {
            var clock = new WaitableClock();
            FrameGate.SetClock(clock);
            FrameGate.driveEngineTimeOnSuppliedFrames = true;

            FrameGate._PumpAtFrameHead();
            clock.now = 1;
            FrameGate._PumpAtFrameHead();

            CollectionAssert.IsEmpty(clock.waits);
        }

        [Test]
        public void AReplayHeldForLoading_IsNotWaitedFor()
        {
            var clock = new WaitableClock();
            FrameGate.SetClock(clock);
            FrameGate.driveEngineTimeOnSuppliedFrames = true;
            FrameGate.source = new ScriptedSource { frameNumber = 500 };

            FrameGate._PumpAtFrameHead();

            FrameGate.HoldSupply("[test] loading");
            clock.now = 1;
            FrameGate._PumpAtFrameHead();
            FrameGate.ReleaseSupply("[test] loading");

            CollectionAssert.IsEmpty(clock.waits);
        }

        [Test]
        public void ABarrierThatGaveUp_GivesTheEngineBack()
        {
            var clock = new WaitableClock();
            FrameGate.SetClock(clock);
            FrameGate.driveEngineTimeOnSuppliedFrames = true;
            FrameGate.source = new ScheduledSource { frameNumber = 500 };

            FrameGate._PumpAtFrameHead();
            Assert.AreNotEqual(0f, Time.captureDeltaTime);

            // The clock did not move while the gate waited. No frame is committed, and the step the
            // engine already holds must not be spent a second time on this one.
            clock.stuck = true;
            FrameGate._PumpAtFrameHead();

            Assert.AreEqual(0f, Time.captureDeltaTime);
        }
    }
}
