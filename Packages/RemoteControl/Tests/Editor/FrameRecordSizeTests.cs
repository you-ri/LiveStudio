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
    /// Two numbers, kept apart on purpose. The entries are what the format costs -- asserted to the
    /// byte, because the thing they guard against is the format growing a few bytes at a time. The
    /// file is what the content compresses to, which depends on what was recorded and so is only
    /// held to the budget the design was written against.
    /// </summary>
    public class FrameRecordSizeTests
    {
        /// <summary>
        /// Stands in for a capture pose, at exactly the size the real one is (measured: 1144 bytes
        /// for AvatarAnimationData). Sized rather than copied so this assembly does not have to see
        /// the avatar types, which live above it.
        /// </summary>
        [StructLayout(LayoutKind.Explicit, Size = 1144)]
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

        private static (double entries, double file) BytesPerFrame(int writesPerFrame, bool withPose)
        {
            void Producer(ref Frame frame)
            {
                if (!withPose) return;

                frame.state.GetOrCreate<PoseSized>().GetOrCreate(1).time = frame.frameNumber;
            }

            var stream = new MemoryStream();
            var recorder = new FrameRecorder();
            long entryBytes;

            FrameGate.AddFrameHeadHandler(Producer);
            recorder.Start(stream, leaveOpen: true);
            FrameGate.sink = recorder;

            try
            {
                for (int f = 0; f < kFrames; f++)
                {
                    for (int w = 0; w < writesPerFrame; w++)
                    {
                        FrameGate._Enqueue(EventKind.Set, "test",
                            "/live/object/cam" + w + "/fieldOfView", "35.5", () => true);
                    }

                    FrameGate.Pump();
                }
            }
            finally
            {
                FrameGate.sink = null;
                entryBytes = recorder.entryBytes;
                recorder.Stop();
                FrameGate.RemoveFrameHeadHandler(Producer);
            }

            var fileBytes = stream.Length;
            stream.Dispose();
            return (entryBytes / (double)kFrames, fileBytes / (double)kFrames);
        }

        private static void Report(string label, (double entries, double file) perFrame)
        {
            var perSecond = perFrame.file * 60.0;
            Debug.Log($"[Debug] frame recording {label}: {perFrame.entries:F0} B/frame of entries, " +
                      $"{perFrame.file:F0} B/frame on disk, {perSecond * 3600.0 / (1024 * 1024):F1} MB/hour");
        }

        [Test]
        public void AnEmptyFrame_CostsOnlyItsBoundary()
        {
            var perFrame = BytesPerFrame(0, withPose: false);
            Report("empty", perFrame);

            // 21 for the boundary entry: 5 of entry header, 8 of frame number, 8 of rate. The
            // mapping table's first frame is spread over the run on top.
            Assert.GreaterOrEqual(perFrame.entries, 21);
            Assert.Less(perFrame.entries, 26, "an idle frame should cost its boundary and nothing else");
        }

        [Test]
        public void OnePose_IsTheDominantCost()
        {
            var perFrame = BytesPerFrame(0, withPose: true);
            Report("1 pose", perFrame);

            // 1144 of pose, 16 of meta, 16 of block header, 5 of entry header, 21 of boundary.
            Assert.GreaterOrEqual(perFrame.entries, 1202);
            Assert.Less(perFrame.entries, 1210, "the pose should be carried nearly raw");
        }

        [Test]
        public void EachPropertyWrite_CostsAboutFortyBytes()
        {
            var withoutWrites = BytesPerFrame(0, withPose: true);
            var withEight = BytesPerFrame(8, withPose: true);
            var perWrite = (withEight.entries - withoutWrites.entries) / 8.0;

            Report("1 pose + 8 writes", withEight);
            Debug.Log($"[Debug] frame recording: {perWrite:F1} B of entries per property write");

            // 5 of entry header, 33 of record fields, and the value. Paths are named once in the
            // mapping table, so what repeats is the value and the record's own fields.
            Assert.Less(perWrite, 60, "a write should not carry its path every frame");
        }

        [Test]
        public void AnHourOfOnePose_StaysWithinTheDesignBudget()
        {
            var perFrame = BytesPerFrame(1, withPose: true);
            var megabytesPerHour = perFrame.file * 60.0 * 3600.0 / (1024 * 1024);

            Report("1 pose + 1 write", perFrame);

            // The design was written expecting roughly 316 MB for an hour of one avatar at 60fps
            // before compression. Well past that on disk means the format grew something it should
            // not have.
            Assert.Less(megabytesPerHour, 400, $"{megabytesPerHour:F0} MB/hour is past what was planned for");
        }
    }
}
