// Copyright (c) You-Ri, 2026
using System;
using NUnit.Framework;
using Lilium.RemoteControl;
using Lilium.RemoteControl.Frames;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// What a frame number means as a moment in time.
    ///
    /// The number is the only position a frame carries, so whatever it is derived from decides what
    /// a recording can later be lined up against.
    /// </summary>
    [TestFixture]
    public class FrameClockTests
    {
        [TearDown]
        public void Finish()
        {
            FrameGate.RestoreDefaultClock();
        }

        [Test]
        public void TheLiveClock_ReadsTheTimeOfDay()
        {
            // Read as a timecode it has to say what the wall clock says, or a recording cannot be
            // matched to footage, to a log, or to the capture side -- which stamps its own timecode
            // the same way.
            var clock = new RealtimeFrameClock(FrameRate.FPS60);
            var wall = DateTime.Now.TimeOfDay.TotalSeconds;

            var seconds = clock.frameRate.AsSecounds(clock.Advance());

            Assert.AreEqual(wall, seconds, 1.0);
        }

        [Test]
        public void TwoReadsInsideOneInterval_AreOnePosition()
        {
            // The rate is the resolution of the axis. Two moments closer together than one frame
            // are the same position, and the gate leans on that to skip the second pump rather
            // than commit a second frame over the first.
            var clock = new RealtimeFrameClock(new FrameRate(1, 1));

            Assert.AreEqual(clock.Advance(), clock.Advance());
        }

        [Test]
        public void TheDefaultClock_IsTheRealtimeOne()
        {
            Assert.IsInstanceOf<RealtimeFrameClock>(FrameGate.clock);
        }

        [Test]
        public void AClockInstalledForOnePurpose_DoesNotOutliveIt()
        {
            // The clock is process-wide. A counter clock left behind by whoever borrowed it counts
            // pumps instead of time, so the editor's timecode then runs at whatever rate the editor
            // happens to tick at -- which is exactly what it did.
            FrameGate.SetClock(new FrameCounterClock(FrameRate.FPS60));
            Assert.IsInstanceOf<FrameCounterClock>(FrameGate.clock);

            FrameGate.RestoreDefaultClock();

            Assert.IsInstanceOf<RealtimeFrameClock>(FrameGate.clock);
        }
    }
}
