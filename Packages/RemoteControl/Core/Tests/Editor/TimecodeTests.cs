// Copyright (c) You-Ri, 2026
// Consolidated from Lilium.Virgo.EditorTests.TimecodeTests into Lilium.RemoteControl.Tests.

using NUnit.Framework;

namespace Lilium.RemoteControl.Tests
{
    public class TimecodeTests
    {
        private FrameRate frameRate30;
        private FrameRate frameRate60;

        [SetUp]
        public void Setup()
        {
            frameRate30 = new FrameRate(1, 30);
            frameRate60 = new FrameRate(1, 60);
        }

        [Test]
        public void Constructor_WithSpecificTimeValues_CalculatesCorrectTimecode()
        {
            double time1 = 81159.9788219507; // 22:32:39:58.720
            double time2 = 81159.99659844;   // 22:32:39:59.256

            var timecode1 = new Timecode(time1, frameRate60);
            var timecode2 = new Timecode(time2, frameRate60);

            Assert.AreEqual(22, timecode1.hours);
            Assert.AreEqual(32, timecode1.minutes);
            Assert.AreEqual(39, timecode1.seconds);
            Assert.AreEqual(58, timecode1.frames);
            Assert.AreEqual(720, timecode1.decimalFrames);

            Assert.AreEqual(22, timecode2.hours);
            Assert.AreEqual(32, timecode2.minutes);
            Assert.AreEqual(39, timecode2.seconds);
            Assert.AreEqual(59, timecode2.frames);
        }

        [Test]
        public void Constructor_WithSpecificTimeValues_CalculatesCorrectTimecode2()
        {
            double time1 = 81888.9791298469; // 22:44:48:58.368
            double time2 = 81888.9966927131; // 22:44:48:59.000

            var timecode1 = new Timecode(time1, frameRate60);
            var timecode2 = new Timecode(time2, frameRate60);

            Assert.AreEqual(22, timecode1.hours);
            Assert.AreEqual(44, timecode1.minutes);
            Assert.AreEqual(48, timecode1.seconds);
            Assert.AreEqual(58, timecode1.frames);

            Assert.AreEqual(22, timecode2.hours);
            Assert.AreEqual(44, timecode2.minutes);
            Assert.AreEqual(48, timecode2.seconds);
            Assert.AreEqual(59, timecode2.frames);
        }

        [Test]
        public void Constructor_WithParameters_SetsCorrectValues()
        {
            var timecode = new Timecode(1, 30, 45, 15, 500, true);

            Assert.AreEqual(1, timecode.hours);
            Assert.AreEqual(30, timecode.minutes);
            Assert.AreEqual(45, timecode.seconds);
            Assert.AreEqual(15, timecode.frames);
            Assert.AreEqual(500, timecode.decimalFrames);
            Assert.AreEqual(true, timecode.dropFrameFormat);
        }

        [Test]
        public void Constructor_WithTime_CalculatesCorrectTimecode()
        {
            // 1 hour 1 minute 5.5 seconds.
            double time = 3665.5;

            var timecode = new Timecode(time, frameRate30);

            Assert.AreEqual(1, timecode.hours);
            Assert.AreEqual(1, timecode.minutes);
            Assert.AreEqual(5, timecode.seconds);
        }

        [Test]
        public void FrameCount_CountsEveryFrame_NotEveryOtherOneAtTheSecondBoundary()
        {
            // Worked out through seconds, 61 frames at 60fps came back as frame 0 of second 1 --
            // the same timecode as frame 60 -- because the fraction left after subtracting the
            // whole second multiplies back out to 0.9999999999999964.
            Assert.AreEqual("00:00:01:00", new Timecode(60L, frameRate60).ToSmpteString());
            Assert.AreEqual("00:00:01:01", new Timecode(61L, frameRate60).ToSmpteString());
            Assert.AreEqual("00:00:01:02", new Timecode(62L, frameRate60).ToSmpteString());
        }

        [Test]
        public void FrameCount_IsExactAcrossEveryFrameOfASecond()
        {
            // Every frame of a second maps to its own timecode and back, with nothing landing twice.
            for (long frame = 0; frame < 600; frame++)
            {
                var timecode = new Timecode(frame, frameRate60);

                Assert.AreEqual(frame % 60, timecode.frames, $"frame {frame}");
                Assert.AreEqual(frame / 60, timecode.seconds, $"frame {frame}");
            }
        }

        [Test]
        public void FrameCount_RollsOverIntoMinutesAndHours()
        {
            Assert.AreEqual("00:01:00:00", new Timecode(3600L, frameRate60).ToSmpteString());
            Assert.AreEqual("01:00:00:00", new Timecode(216000L, frameRate60).ToSmpteString());
            Assert.AreEqual("01:23:08:09", new Timecode(299289L, frameRate60).ToSmpteString());
        }

        [Test]
        public void TheStandardForm_LeavesOutTheSubFrame()
        {
            // Derived from a whole frame number the sub-frame is always zero, and printing ".000"
            // every time says nothing while looking like a format nobody uses.
            var timecode = new Timecode(93969L, frameRate60);

            Assert.AreEqual("00:26:06:09", timecode.ToSmpteString());
            Assert.AreEqual("00:26:06:09.000", timecode.ToString());
        }

        [Test]
        public void DropFrame_IsWrittenWithASemicolon()
        {
            // The convention that tells the two apart on sight.
            var timecode = new Timecode(1, 2, 3, 4, 0, dropFrameFormat: true);

            Assert.AreEqual("01:02:03;04", timecode.ToSmpteString());
        }

        [Test]
        public void Constructor_WithFrameCount_CalculatesCorrectTimecode()
        {
            // 30fps * 60 seconds = 1 minute.
            long frameCount = 1800;

            var timecode = new Timecode(frameCount, frameRate30);

            Assert.AreEqual(0, timecode.hours);
            Assert.AreEqual(1, timecode.minutes);
            Assert.AreEqual(0, timecode.seconds);
            Assert.AreEqual(0, timecode.frames);
        }

        [Test]
        public void ToFrameNumber_ReturnsCorrectFrameCount()
        {
            // 1 minute 15 frames at 30fps.
            var timecode = new Timecode(0, 1, 0, 15);

            long frameCount = timecode.ToFrameNumber(frameRate30);

            // 60 seconds * 30fps + 15 frames.
            Assert.AreEqual(1815, frameCount);
        }

        [Test]
        public void ToSecounds_ReturnsCorrectTime()
        {
            // 1 hour 30 seconds.
            var timecode = new Timecode(1, 0, 30, 0);

            double seconds = timecode.ToSecounds(frameRate30);

            Assert.AreEqual(3630.0, seconds, 0.01);
        }

        [Test]
        public void ToString_ReturnsCorrectFormat()
        {
            var timecode = new Timecode(1, 2, 3, 4, 567);

            string result = timecode.ToString();

            Assert.AreEqual("01:02:03:04.567", result);
        }

        [Test]
        public void ToString_WithZeroValues_ReturnsCorrectFormat()
        {
            var timecode = new Timecode(0, 0, 0, 0, 0);

            string result = timecode.ToString();

            Assert.AreEqual("00:00:00:00.000", result);
        }

        [Test]
        public void Equals_WithSameValues_ReturnsTrue()
        {
            var timecode1 = new Timecode(1, 2, 3, 4, 567, true);
            var timecode2 = new Timecode(1, 2, 3, 4, 567, true);

            Assert.IsTrue(timecode1.Equals(timecode2));
            Assert.IsTrue(timecode1 == timecode2);
        }

        [Test]
        public void Equals_WithDifferentValues_ReturnsFalse()
        {
            var timecode1 = new Timecode(1, 2, 3, 4, 567, true);
            var timecode2 = new Timecode(1, 2, 3, 5, 567, true);

            Assert.IsFalse(timecode1.Equals(timecode2));
            Assert.IsTrue(timecode1 != timecode2);
        }

        [Test]
        public void Equals_WithDifferentDropFrameFormat_ReturnsFalse()
        {
            var timecode1 = new Timecode(1, 2, 3, 4, 567, true);
            var timecode2 = new Timecode(1, 2, 3, 4, 567, false);

            Assert.IsFalse(timecode1.Equals(timecode2));
        }

        [Test]
        public void GetHashCode_WithSameValues_ReturnsSameHash()
        {
            var timecode1 = new Timecode(1, 2, 3, 4, 567, true);
            var timecode2 = new Timecode(1, 2, 3, 4, 567, true);

            Assert.AreEqual(timecode1.GetHashCode(), timecode2.GetHashCode());
        }

        [Test]
        public void Equals_WithNull_ReturnsFalse()
        {
            var timecode = new Timecode(1, 2, 3, 4);

            Assert.IsFalse(timecode.Equals(null));
        }

        [Test]
        public void Equals_WithDifferentType_ReturnsFalse()
        {
            var timecode = new Timecode(1, 2, 3, 4);
            var otherObject = "not a timecode";

            Assert.IsFalse(timecode.Equals(otherObject));
        }

        [Test]
        public void Constructor_WithLargeTime_HandlesCorrectly()
        {
            // 24 hours.
            double largeTime = 86400;

            var timecode = new Timecode(largeTime, frameRate30);

            Assert.AreEqual(24, timecode.hours);
            Assert.AreEqual(0, timecode.minutes);
            Assert.AreEqual(0, timecode.seconds);
        }

        // Regression: frame numbers must stay exact and strictly increasing well past float's
        // 2^24 integer limit (~3.2 days at 60fps). The old (float) cast collapsed neighbouring
        // frame numbers onto the same value, which stuttered the long-running playback pipeline.
        [Test]
        public void AsFrameNumber_BeyondFloatPrecision_StaysExact()
        {
            // 300000s @ 60fps = 18,000,000 frames, above 2^24 = 16,777,216.
            double time = 300000.0;
            long frame = frameRate60.AsFrameNumber(time);

            Assert.AreEqual(18_000_000L, frame);
        }

        [Test]
        public void AsFrameNumber_BeyondFloatPrecision_OneFrameStepAdvancesByOne()
        {
            // One frame apart (1/60s) at a large elapsed time must differ by exactly one frame.
            double time = 300000.0;
            long frame0 = frameRate60.AsFrameNumber(time);
            long frame1 = frameRate60.AsFrameNumber(time + 1.0 / 60.0);

            Assert.AreEqual(frame0 + 1, frame1);
        }
    }
}
