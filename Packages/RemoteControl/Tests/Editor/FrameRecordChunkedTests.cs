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
    /// A recording is written a chunk at a time: the entries from one keyframe to the next,
    /// compressed together on the writer's own thread. The chunking is only allowed to change the
    /// size -- every frame, its order, the index and seeking have to come back as they were written.
    /// </summary>
    public class FrameRecordChunkedTests
    {
        private struct Pose
        {
            public float x;
            public float y;
            public float z;
        }

        private static FrameRecordHeader Header() => new FrameRecordHeader
        {
            frameRate = FrameRate.FPS60,
            startTicks = 638000000000000000L,
            engineId = "unity",
            buildId = "test-build",
        };

        /// <summary>
        /// Writes a take of <paramref name="frames"/> frames, with a keyframe every 20 and a pose
        /// where most of the values hold still -- close enough to a real recording that the chunking
        /// is exercised the way it will be in use.
        /// </summary>
        private static byte[] WriteTake(int frames, out long entryBytes, bool close = true)
        {
            var symbols = new FrameSymbolTable();
            var structure = new StructureBlock();
            var state = new StateBlockSet();

            using (var stream = new MemoryStream())
            {
                using (var writer = new FrameRecordWriter(stream, Header(), leaveOpen: true))
                {
                    structure.AddOrUpdate(symbols.Intern("cam"), symbols.Intern("Camera"), FrameSymbolTable.kNone);

                    for (long f = 0; f < frames; f++)
                    {
                        var frame = new Frame { frameNumber = f, frameRate = FrameRate.FPS60, structure = structure };

                        writer.BeginFrame(in frame, symbols);
                        writer.WriteStructure(structure, symbols, force: f % 20 == 0);

                        ref var element = ref state.GetOrCreate<Pose>().GetOrCreate(1);
                        element.time = f;
                        element.value.x = f * 0.01f;

                        writer.WriteState(state, symbols);
                        writer.EndFrame();
                    }

                    entryBytes = writer.entryBytes;
                    if (close) writer.Close(symbols);
                }

                state.Dispose();
                structure.Dispose();

                return stream.ToArray();
            }
        }

        private static byte[] WriteTake(int frames, bool close = true) => WriteTake(frames, out _, close);

        private static List<long> BoundariesOf(FrameRecordReader reader)
        {
            var frames = new List<long>();
            while (reader.TryReadEntry(out var entry))
            {
                if (entry.kind == FrameEntryKind.FrameBoundary) frames.Add(entry.frameNumber);
            }

            return frames;
        }

        [Test]
        public void ATake_ReadsBackEveryFrameInOrder()
        {
            using (var reader = new FrameRecordReader(new MemoryStream(WriteTake(90))))
            {
                var frames = BoundariesOf(reader);

                Assert.AreEqual(90, frames.Count);
                for (int i = 0; i < frames.Count; i++) Assert.AreEqual(i, frames[i], $"frame {i}");
            }
        }

        [Test]
        public void ATake_IsSmallerThanItsEntries()
        {
            var bytes = WriteTake(120, out var entryBytes);

            Assert.Less(bytes.Length, entryBytes,
                        $"compressed should be smaller than what was written ({bytes.Length} vs {entryBytes})");
        }

        [Test]
        public void TheIndex_CoversEveryFrame()
        {
            using (var reader = new FrameRecordReader(new MemoryStream(WriteTake(90))))
            {
                Assert.IsTrue(reader.hasIndex);
                Assert.AreEqual(90, reader.indexedFrameCount);
                Assert.AreEqual(0, reader.firstFrameNumber);
                CollectionAssert.AreEqual(new long[] { 0, 20, 40, 60, 80 }, reader.keyframes);

                for (int i = 0; i < 90; i++)
                {
                    Assert.AreEqual(i, reader.FrameNumberAt(i), $"frame {i}");
                    Assert.AreEqual(i, reader.IndexOfFrame(i), $"looking up frame {i}");
                }
            }
        }

        [Test]
        public void SeekingToEveryFrame_LandsOnThatFrame()
        {
            // Every frame, not a sample of them: the frames near a chunk boundary are the ones a
            // mistake in the index would land wrong, and which those are is not obvious from here.
            using (var reader = new FrameRecordReader(new MemoryStream(WriteTake(90))))
            {
                for (long f = 0; f < 90; f++)
                {
                    Assert.IsTrue(reader.TrySeekFrame(f), $"could not seek to frame {f}");
                    Assert.IsTrue(reader.TryReadEntry(out var entry), $"nothing to read at frame {f}");
                    Assert.AreEqual(FrameEntryKind.FrameBoundary, entry.kind, $"seeking to frame {f} did not land on its boundary");
                    Assert.AreEqual(f, entry.frameNumber, $"seeking to frame {f} landed elsewhere");
                }
            }
        }

        [Test]
        public void SeekingBackwards_StillReadsTheFramesInOrderAfterward()
        {
            using (var reader = new FrameRecordReader(new MemoryStream(WriteTake(90))))
            {
                Assert.IsTrue(reader.TrySeekFrame(70));
                Assert.IsTrue(reader.TrySeekFrame(10));

                var frames = BoundariesOf(reader);
                Assert.AreEqual(80, frames.Count, "frames 10 to 89");
                Assert.AreEqual(10, frames[0]);
                CollectionAssert.IsOrdered(frames);
            }
        }

        [Test]
        public void ATakeThatWasNeverClosed_StillReadsFromTheTop()
        {
            // An abandoned writer still puts its open chunk on disk; what it does not write is the
            // tail. A crash would lose that last chunk too -- but everything before it is there.
            var bytes = WriteTake(90, close: false);

            using (var reader = new FrameRecordReader(new MemoryStream(bytes)))
            {
                Assert.IsFalse(reader.hasIndex, "an unclosed file has no tail");

                var frames = BoundariesOf(reader);
                Assert.AreEqual(90, frames.Count);
                CollectionAssert.IsOrdered(frames);
                Assert.AreEqual(0, frames[0], "the take should still start where it started");
            }
        }

        [Test]
        public void ChunksStartAtKeyframes()
        {
            // The reason the two are tied together: a seek lands on a keyframe, and that costs one
            // chunk to expand only if the chunk starts there.
            using (var reader = new FrameRecordReader(new MemoryStream(WriteTake(90))))
            {
                foreach (var keyframe in reader.keyframes)
                {
                    var index = reader.IndexOfFrame(keyframe);
                    Assert.AreEqual(0, reader.CursorOfFrameAt(index), $"keyframe {keyframe} is not at the start of a chunk");
                }
            }
        }

        [Test]
        public void ATakeWithNoKeyframesAfterTheFirst_StillRoundTrips()
        {
            // Nothing forces a cut, so the whole take is one chunk. Worth its own test because it is
            // the path where the carry-over in the writer never runs.
            var symbols = new FrameSymbolTable();
            var state = new StateBlockSet();
            byte[] bytes;

            using (var stream = new MemoryStream())
            {
                using (var writer = new FrameRecordWriter(stream, Header(), leaveOpen: true))
                {
                    for (long f = 0; f < 40; f++)
                    {
                        var frame = new Frame { frameNumber = f, frameRate = FrameRate.FPS60 };

                        writer.BeginFrame(in frame, symbols);
                        ref var element = ref state.GetOrCreate<Pose>().GetOrCreate(1);
                        element.value.y = f;
                        writer.WriteState(state, symbols);
                        writer.EndFrame();
                    }

                    writer.Close(symbols);
                }

                bytes = stream.ToArray();
            }

            state.Dispose();

            using (var reader = new FrameRecordReader(new MemoryStream(bytes)))
            {
                Assert.AreEqual(40, BoundariesOf(reader).Count);
            }
        }
    }
}
