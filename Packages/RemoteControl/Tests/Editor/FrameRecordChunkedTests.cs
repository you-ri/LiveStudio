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
    /// Compression is only allowed to change the size of a recording. Everything read back out of
    /// one -- the entries, their order, seeking, the index -- has to be what the same take says
    /// uncompressed, so most of these tests write the take twice and compare the two.
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

        /// <summary>One entry as read back, kept so two recordings can be compared entry for entry.</summary>
        private struct Read
        {
            public FrameEntryKind kind;
            public long frameNumber;
            public byte[] payload;
        }

        /// <summary>
        /// Writes a take of <paramref name="frames"/> frames, with a keyframe every 20 and a pose
        /// where most of the values hold still -- close enough to a real recording that the chunking
        /// is exercised the way it will be in use.
        /// </summary>
        private static byte[] WriteTake(int frames, bool chunked, bool close = true)
        {
            var symbols = new FrameSymbolTable();
            var structure = new StructureBlock();
            var state = new StateBlockSet();

            using (var stream = new MemoryStream())
            {
                using (var writer = new FrameRecordWriter(stream, Header(), leaveOpen: true, chunked: chunked))
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

                    if (close) writer.Close(symbols);
                }

                state.Dispose();
                structure.Dispose();

                return stream.ToArray();
            }
        }

        private static List<Read> ReadAll(byte[] bytes)
        {
            var entries = new List<Read>();
            using (var reader = new FrameRecordReader(new MemoryStream(bytes)))
            {
                while (reader.TryReadEntry(out var entry))
                {
                    entries.Add(new Read
                    {
                        kind = entry.kind,
                        frameNumber = entry.frameNumber,
                        payload = entry.payload.ToArray(),
                    });
                }
            }

            return entries;
        }

        private static void AssertSameEntries(List<Read> expected, List<Read> actual)
        {
            Assert.AreEqual(expected.Count, actual.Count, "a different number of entries came back");

            for (int i = 0; i < expected.Count; i++)
            {
                Assert.AreEqual(expected[i].kind, actual[i].kind, $"entry {i} is a different kind");
                Assert.AreEqual(expected[i].frameNumber, actual[i].frameNumber, $"entry {i} is on a different frame");
                CollectionAssert.AreEqual(expected[i].payload, actual[i].payload, $"entry {i} carries different bytes");
            }
        }

        [Test]
        public void ACompressedTake_ReadsBackAsTheSameEntries()
        {
            AssertSameEntries(ReadAll(WriteTake(90, chunked: false)), ReadAll(WriteTake(90, chunked: true)));
        }

        [Test]
        public void ACompressedTake_IsSmaller()
        {
            var plain = WriteTake(120, chunked: false);
            var chunked = WriteTake(120, chunked: true);

            Assert.Less(chunked.Length, plain.Length,
                        $"compressed should be smaller ({chunked.Length} vs {plain.Length})");
        }

        [Test]
        public void ACompressedTake_SaysItIsCompressed()
        {
            using (var reader = new FrameRecordReader(new MemoryStream(WriteTake(30, chunked: true))))
            {
                Assert.IsTrue(reader.isChunked);
            }

            using (var reader = new FrameRecordReader(new MemoryStream(WriteTake(30, chunked: false))))
            {
                Assert.IsFalse(reader.isChunked);
            }
        }

        [Test]
        public void TheIndexOfACompressedTake_MatchesThePlainOne()
        {
            using (var plain = new FrameRecordReader(new MemoryStream(WriteTake(90, chunked: false))))
            using (var chunked = new FrameRecordReader(new MemoryStream(WriteTake(90, chunked: true))))
            {
                Assert.IsTrue(chunked.hasIndex);
                Assert.AreEqual(plain.indexedFrameCount, chunked.indexedFrameCount);
                Assert.AreEqual(plain.firstFrameNumber, chunked.firstFrameNumber);
                CollectionAssert.AreEqual(plain.keyframes, chunked.keyframes);

                for (int i = 0; i < plain.indexedFrameCount; i++)
                {
                    Assert.AreEqual(plain.FrameNumberAt(i), chunked.FrameNumberAt(i), $"frame {i}");
                }

                for (long f = 0; f < 90; f++)
                {
                    Assert.AreEqual(plain.IndexOfFrame(f), chunked.IndexOfFrame(f), $"looking up frame {f}");
                }
            }
        }

        [Test]
        public void SeekingToEveryFrame_LandsOnThatFrame()
        {
            // Every frame, not a sample of them: the frames near a chunk boundary are the ones a
            // mistake in the index would land wrong, and which those are is not obvious from here.
            using (var reader = new FrameRecordReader(new MemoryStream(WriteTake(90, chunked: true))))
            {
                for (long f = 0; f < 90; f++)
                {
                    Assert.IsTrue(reader.TrySeekFrame(f), $"could not seek to frame {f}");
                    Assert.IsTrue(reader.TryReadEntry(out var entry), $"nothing to read at frame {f}");
                    Assert.AreEqual(f, entry.frameNumber, $"seeking to frame {f} landed elsewhere");
                }
            }
        }

        [Test]
        public void ABookmarkTakenWhileWalking_GoesBackToTheSameEntry()
        {
            // This is what the viewer does: one pass noting where things are, then jumps back.
            using (var reader = new FrameRecordReader(new MemoryStream(WriteTake(60, chunked: true))))
            {
                var marks = new List<long>();
                var kinds = new List<FrameEntryKind>();
                var numbers = new List<long>();

                while (true)
                {
                    var mark = reader.position;
                    if (!reader.TryReadEntry(out var entry)) break;

                    marks.Add(mark);
                    kinds.Add(entry.kind);
                    numbers.Add(entry.frameNumber);
                }

                Assert.Greater(marks.Count, 0);

                for (int i = marks.Count - 1; i >= 0; i--)
                {
                    Assert.IsTrue(reader.TrySeekTo(marks[i]), $"could not go back to entry {i}");
                    Assert.IsTrue(reader.TryReadEntry(out var entry), $"nothing at entry {i}");
                    Assert.AreEqual(kinds[i], entry.kind, $"entry {i} came back as a different kind");
                    Assert.AreEqual(numbers[i], entry.frameNumber, $"entry {i} came back on a different frame");
                }
            }
        }

        [Test]
        public void ACompressedTakeThatWasNeverClosed_ReadsUpToTheLastWholeChunk()
        {
            // What a crash leaves behind. The chunk in progress is gone -- that is the cost of
            // compressing -- but everything before it has to still be there.
            var bytes = WriteTake(90, chunked: true, close: false);

            using (var reader = new FrameRecordReader(new MemoryStream(bytes)))
            {
                Assert.IsFalse(reader.hasIndex, "an unclosed file has no tail");

                var frames = new List<long>();
                while (reader.TryReadEntry(out var entry))
                {
                    if (entry.kind == FrameEntryKind.FrameBoundary) frames.Add(entry.frameNumber);
                }

                Assert.Greater(frames.Count, 0, "the whole chunks before the cut should still read");
                CollectionAssert.IsOrdered(frames);
                Assert.AreEqual(0, frames[0], "the take should still start where it started");
            }
        }

        [Test]
        public void ChunksStartAtKeyframes()
        {
            // The reason the two are tied together: a seek lands on a keyframe, and that costs one
            // chunk to expand only if the chunk starts there.
            var bytes = WriteTake(90, chunked: true);

            using (var reader = new FrameRecordReader(new MemoryStream(bytes)))
            {
                foreach (var keyframe in reader.keyframes)
                {
                    Assert.IsTrue(reader.TrySeekFrame(keyframe), $"could not seek to keyframe {keyframe}");

                    // Offset zero within its chunk is what "the chunk starts here" looks like from
                    // outside: the low half of the bookmark is the position inside the chunk.
                    Assert.AreEqual(0, (int)(reader.position & 0xFFFFFFFF),
                                    $"keyframe {keyframe} is not at the start of a chunk");
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
                using (var writer = new FrameRecordWriter(stream, Header(), leaveOpen: true, chunked: true))
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
                var frames = 0;
                while (reader.TryReadEntry(out var entry))
                {
                    if (entry.kind == FrameEntryKind.FrameBoundary) frames++;
                }

                Assert.AreEqual(40, frames);
            }
        }
    }
}
