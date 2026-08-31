// Copyright (c) You-Ri, 2026
using System.IO;
using System.Runtime.InteropServices;
using NUnit.Framework;
using UnityEngine;
using Lilium.RemoteControl.Frames;
using Lilium.RemoteControl.Frames.Recording;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// What a recording costs per frame.
    ///
    /// The numbers are asserted rather than just printed, because the thing they guard against is
    /// the format growing a few bytes at a time until an hour of capture no longer fits anywhere.
    /// A change that moves them should move them on purpose.
    /// </summary>
    public class FrameRecordSizeTests
    {
        /// <summary>
        /// Stands in for a capture pose, at exactly the size the real one is (measured: 1464 bytes
        /// for AvatarAnimationData). Sized rather than copied so this assembly does not have to see
        /// the avatar types, which live above it.
        /// </summary>
        [StructLayout(LayoutKind.Explicit, Size = 1464)]
        private struct PoseSized
        {
        }

        private const int kFrames = 120;

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

        private static double BytesPerFrame(int writesPerFrame, bool withPose)
        {
            void Producer(ref Frame frame)
            {
                if (!withPose) return;

                frame.state.GetOrCreate<PoseSized>().GetOrCreate(1).time = frame.frameNumber;
            }

            var stream = new MemoryStream();
            var recorder = new FrameRecorder();

            FrameGate.AddFrameHeadHandler(Producer);
            recorder.Start(stream, leaveOpen: true);
            FrameGate.sink = recorder;

            try
            {
                for (int f = 0; f < kFrames; f++)
                {
                    for (int w = 0; w < writesPerFrame; w++)
                    {
                        FrameGate._Enqueue(EventKind.PropertyWrite, "test",
                            "/live/object/cam" + w + "/fieldOfView", "35.5", () => true);
                    }

                    FrameGate.Pump();
                }
            }
            finally
            {
                FrameGate.sink = null;
                recorder.Stop();
                FrameGate.RemoveFrameHeadHandler(Producer);
            }

            var bytes = stream.Length;
            stream.Dispose();
            return bytes / (double)kFrames;
        }

        private static void Report(string label, double bytesPerFrame)
        {
            var perSecond = bytesPerFrame * 60.0;
            Debug.Log($"[Debug] frame recording {label}: {bytesPerFrame:F0} B/frame, " +
                      $"{perSecond / 1024.0:F1} KiB/s, {perSecond * 3600.0 / (1024 * 1024):F0} MB/hour");
        }

        [Test]
        public void AnEmptyFrame_CostsOnlyItsBoundary()
        {
            var bytesPerFrame = BytesPerFrame(0, withPose: false);
            Report("empty", bytesPerFrame);

            // 21 for the boundary entry (13 of entry header plus the rate) and 8 for the frame's
            // slot in the tail index, plus the file header and mapping table spread over the run.
            // The index is the deliberate part: 8 bytes a frame buys seeking to any frame at all.
            Assert.Less(bytesPerFrame, 40, "an idle frame should cost almost nothing");
            Assert.Greater(bytesPerFrame, 28, "the tail index is 8 bytes a frame and should be there");
        }

        [Test]
        public void OnePose_IsTheDominantCost()
        {
            var bytesPerFrame = BytesPerFrame(0, withPose: true);
            Report("1 pose", bytesPerFrame);

            // 1464 of pose, 16 of meta, 12 of block header, 13 of entry header, 21 of boundary.
            Assert.Greater(bytesPerFrame, 1500);
            Assert.Less(bytesPerFrame, 1560, "the pose should be carried nearly raw");
        }

        [Test]
        public void EachPropertyWrite_CostsAboutFortyBytes()
        {
            var withoutWrites = BytesPerFrame(0, withPose: true);
            var withEight = BytesPerFrame(8, withPose: true);
            var perWrite = (withEight - withoutWrites) / 8.0;

            Report("1 pose + 8 writes", withEight);
            Debug.Log($"[Debug] frame recording: {perWrite:F1} B per property write");

            // The record is 536 bytes in memory and its actual text on disk. Paths are named once in
            // the mapping table, so what repeats is the value and the record's own fields.
            Assert.Less(perWrite, 60, "a write should not carry its path every frame");
        }

        [Test]
        public void AnHourOfOnePose_StaysWithinTheDesignBudget()
        {
            var bytesPerFrame = BytesPerFrame(1, withPose: true);
            var megabytesPerHour = bytesPerFrame * 60.0 * 3600.0 / (1024 * 1024);

            Report("1 pose + 1 write", bytesPerFrame);

            // The design was written expecting roughly 316 MB for an hour of one avatar at 60fps.
            // Well past that means the format grew something it should not have.
            Assert.Less(megabytesPerHour, 400, $"{megabytesPerHour:F0} MB/hour is past what was planned for");
        }
    }
}
