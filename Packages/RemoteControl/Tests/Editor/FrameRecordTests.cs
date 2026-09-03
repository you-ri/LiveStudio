// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using NUnit.Framework;
using Unity.Collections;
using Lilium.RemoteControl.Frames;
using Lilium.RemoteControl.Frames.Recording;

namespace Lilium.RemoteControl.Tests
{
    public class FrameRecordTests
    {
        private struct Pose
        {
            public float x;
            public float y;
        }

        private static FrameRecordHeader Header() => new FrameRecordHeader
        {
            frameRate = FrameRate.FPS60,
            startTicks = 638000000000000000L,
            engineId = "unity",
            buildId = "test-build",
        };

        /// <summary>Writes a short recording and hands back the bytes.</summary>
        private static byte[] Write(Action<FrameRecordWriter, FrameSymbolTable> body, bool close = true)
        {
            var symbols = new FrameSymbolTable();
            using (var stream = new MemoryStream())
            {
                using (var writer = new FrameRecordWriter(stream, Header(), leaveOpen: true))
                {
                    body(writer, symbols);
                    if (close) writer.Close(symbols);
                }

                return stream.ToArray();
            }
        }

        private static List<FrameEntryKind> KindsOf(byte[] bytes, out FrameRecordReader reader)
        {
            reader = new FrameRecordReader(new MemoryStream(bytes));
            var kinds = new List<FrameEntryKind>();
            while (reader.TryReadEntry(out var entry)) kinds.Add(entry.kind);
            return kinds;
        }

        [Test]
        public void Header_SurvivesTheRoundTrip()
        {
            var bytes = Write((writer, symbols) => { });

            using (var reader = new FrameRecordReader(new MemoryStream(bytes)))
            {
                Assert.AreEqual(FrameRate.FPS60, reader.header.frameRate);
                Assert.AreEqual(638000000000000000L, reader.header.startTicks);
                Assert.AreEqual("unity", reader.header.engineId);
                Assert.AreEqual("test-build", reader.header.buildId);
            }
        }

        [Test]
        public void NotARecording_IsRefusedRatherThanMisread()
        {
            var bytes = Encoding.UTF8.GetBytes("this is not a recording, not even close");

            Assert.Throws<InvalidDataException>(() => new FrameRecordReader(new MemoryStream(bytes)));
        }

        [Test]
        public void Frames_AreWrittenAsOneTimeOrderedStream()
        {
            using var events = new EventFrame();
            var bytes = Write((writer, symbols) =>
            {
                for (long f = 0; f < 3; f++)
                {
                    events.Reset(f, FrameRate.FPS60);
                    events.Add(new EventRecord(f, EventKind.PropertyWrite, symbols.Intern("rest"),
                        symbols.Intern("/live/a"), EventFlags.None));

                    var frame = new Frame { frameNumber = f, frameRate = FrameRate.FPS60, events = events };
                    writer.BeginFrame(in frame, symbols);
                    writer.WriteEvents(events, symbols);
                    writer.EndFrame();
                }
            });

            using (var reader = new FrameRecordReader(new MemoryStream(bytes)))
            {
                var seen = new List<(FrameEntryKind kind, long frame)>();
                while (reader.TryReadEntry(out var entry)) seen.Add((entry.kind, entry.frameNumber));

                // Symbols first (nothing can refer to an id that has not been named), then the
                // boundary, then what happened in the frame.
                Assert.AreEqual(FrameEntryKind.Symbol, seen[0].kind);
                Assert.AreEqual(FrameEntryKind.Symbol, seen[1].kind);
                Assert.AreEqual(FrameEntryKind.FrameBoundary, seen[2].kind);
                Assert.AreEqual(FrameEntryKind.Event, seen[3].kind);

                // Frame numbers only ever move forward.
                long previous = -1;
                foreach (var (_, frame) in seen)
                {
                    Assert.GreaterOrEqual(frame, previous);
                    previous = frame;
                }
            }
        }

        [Test]
        public void Input_ComesBackWithEveryFieldIntact()
        {
            using var events = new EventFrame();
            var bytes = Write((writer, symbols) =>
            {
                events.Reset(0, FrameRate.FPS60);

                var record = new EventRecord(7, EventKind.FunctionCall, symbols.Intern("rest"),
                    symbols.Intern("/live/camera/reset"), EventFlags.PayloadTruncated);
                Span<byte> text = stackalloc byte[EventRecord.kPayloadCapacity];
                EventPayload.TryWriteString("35.0", text, out var textLength);
                record.SetPayload(text.Slice(0, textLength),
                    symbols.Intern(EventPayload.kRequestTypeName));

                events.Add(record);

                var frame = new Frame { frameNumber = 0, frameRate = FrameRate.FPS60, events = events };
                writer.BeginFrame(in frame, symbols);
                writer.WriteEvents(events, symbols);
                writer.EndFrame();
            });

            using (var reader = new FrameRecordReader(new MemoryStream(bytes)))
            {
                while (reader.TryReadEntry(out var entry))
                {
                    if (entry.kind != FrameEntryKind.Event) continue;

                    Assert.AreEqual(7L, BitConverter.ToInt64(entry.payload.Slice(0, 8).ToArray(), 0));
                    Assert.AreEqual((int)EventKind.FunctionCall, BitConverter.ToInt32(entry.payload.Slice(8, 4).ToArray(), 0));
                    // 8 sequence, 4 kind, 4 source, 4 target, 4 verb, 4 payload type, 1 flags,
                    // 4 length, payload.
                    Assert.AreEqual((byte)EventFlags.PayloadTruncated, entry.payload[28]);

                    var payloadLength = BitConverter.ToInt32(entry.payload.Slice(29, 4).ToArray(), 0);
                    Assert.AreEqual("35.0", EventPayload.ReadString(entry.payload.Slice(33, payloadLength)));

                    var payloadTypeId = BitConverter.ToInt32(entry.payload.Slice(24, 4).ToArray(), 0);
                    Assert.AreEqual(EventPayload.kRequestTypeName, reader.symbols[payloadTypeId]);

                    // The target resolves through the table the file carries.
                    var targetId = BitConverter.ToInt32(entry.payload.Slice(16, 4).ToArray(), 0);
                    Assert.AreEqual("/live/camera/reset", reader.symbols[targetId]);
                    return;
                }

                Assert.Fail("no evt entry was written");
            }
        }

        [Test]
        public void State_ComesBackByteForByte()
        {
            var state = new StateBlockSet();
            var block = state.GetOrCreate<Pose>();

            var bytes = Write((writer, symbols) =>
            {
                ref var element = ref block.GetOrCreate(symbols.Intern("cam"));
                element.time = 4321;
                element.value = new Pose { x = 1.5f, y = -2.5f };

                var frame = new Frame { frameNumber = 0, frameRate = FrameRate.FPS60, state = state };
                writer.BeginFrame(in frame, symbols);
                writer.WriteState(state, symbols);
                writer.EndFrame();
            });

            using (var reader = new FrameRecordReader(new MemoryStream(bytes)))
            {
                while (reader.TryReadEntry(out var entry))
                {
                    if (entry.kind != FrameEntryKind.State) continue;

                    var typeId = BitConverter.ToInt32(entry.payload.Slice(0, 4).ToArray(), 0);
                    var elementSize = BitConverter.ToInt32(entry.payload.Slice(4, 4).ToArray(), 0);
                    var count = BitConverter.ToInt32(entry.payload.Slice(8, 4).ToArray(), 0);

                    Assert.AreEqual(typeof(Pose).FullName, reader.symbols[typeId]);
                    Assert.AreEqual(block.elementSize, elementSize);
                    Assert.AreEqual(1, count);

                    var elements = MemoryMarshal.Cast<byte, StateElement<Pose>>(entry.payload.Slice(4 + 4 + 4 + 8));
                    Assert.AreEqual(4321, elements[0].time);
                    Assert.AreEqual(1.5f, elements[0].value.x);
                    Assert.AreEqual(-2.5f, elements[0].value.y);
                    return;
                }

                Assert.Fail("no state entry was written");
            }
        }

        [Test]
        public void Structure_IsWrittenOnlyWhenItMoves()
        {
            var structure = new StructureBlock();

            var bytes = Write((writer, symbols) =>
            {
                structure.AddOrUpdate(symbols.Intern("cam"), symbols.Intern("Camera"), FrameSymbolTable.kNone);

                for (long f = 0; f < 3; f++)
                {
                    // Only the second frame changes anything.
                    if (f == 1) structure.AddOrUpdate(symbols.Intern("light"), symbols.Intern("Light"), FrameSymbolTable.kNone);

                    var frame = new Frame { frameNumber = f, frameRate = FrameRate.FPS60, structure = structure };
                    writer.BeginFrame(in frame, symbols);
                    writer.WriteStructure(structure, symbols);
                    writer.EndFrame();
                }
            });

            var kinds = KindsOf(bytes, out var reader);
            using (reader)
            {
                var structures = kinds.FindAll(k => k == FrameEntryKind.Structure).Count;
                Assert.AreEqual(2, structures, "an unchanged inventory should not be written again");
            }
        }

        [Test]
        public void FinishedFile_CanSeekToAFrame()
        {
            var bytes = Write((writer, symbols) =>
            {
                for (long f = 0; f < 5; f++)
                {
                    var frame = new Frame { frameNumber = f, frameRate = FrameRate.FPS60 };
                    writer.BeginFrame(in frame, symbols);
                    writer.EndFrame();
                }
            });

            using (var reader = new FrameRecordReader(new MemoryStream(bytes)))
            {
                Assert.IsTrue(reader.hasIndex);
                Assert.AreEqual(5, reader.indexedFrameCount);

                Assert.IsTrue(reader.TrySeekFrame(3));
                Assert.IsTrue(reader.TryReadEntry(out var entry));
                Assert.AreEqual(FrameEntryKind.FrameBoundary, entry.kind);
                Assert.AreEqual(3, entry.frameNumber);

                Assert.IsFalse(reader.TrySeekFrame(99));
            }
        }

        [Test]
        public void SeekingAFrame_FindsItEvenWhenTheClockSkippedNumbers()
        {
            // What a real take looks like: the frame number comes from the clock, so a run that
            // drops below rate leaves gaps. Positions and frame numbers then part company, and a
            // seek that counted on from the first landed tens of frames away -- silently, because
            // the frame it landed on is a perfectly good frame.
            var numbers = new long[] { 100, 101, 105, 106, 140, 141, 142, 200 };

            var bytes = Write((writer, symbols) =>
            {
                foreach (var f in numbers)
                {
                    var frame = new Frame { frameNumber = f, frameRate = FrameRate.FPS60 };
                    writer.BeginFrame(in frame, symbols);
                    writer.EndFrame();
                }
            });

            using (var reader = new FrameRecordReader(new MemoryStream(bytes)))
            {
                Assert.AreEqual(numbers.Length, reader.indexedFrameCount);

                for (int i = 0; i < numbers.Length; i++)
                {
                    Assert.AreEqual(numbers[i], reader.FrameNumberAt(i), $"position {i}");
                    Assert.AreEqual(i, reader.IndexOfFrame(numbers[i]), $"frame {numbers[i]}");

                    Assert.IsTrue(reader.TrySeekFrame(numbers[i]));
                    Assert.IsTrue(reader.TryReadEntry(out var entry));
                    Assert.AreEqual(FrameEntryKind.FrameBoundary, entry.kind);
                    Assert.AreEqual(numbers[i], entry.frameNumber);
                }

                // A number inside a gap was never recorded, so there is nothing to land on.
                Assert.AreEqual(-1, reader.IndexOfFrame(120));
                Assert.IsFalse(reader.TrySeekFrame(120));

                Assert.AreEqual(-1, reader.FrameNumberAt(-1));
                Assert.AreEqual(-1, reader.FrameNumberAt(numbers.Length));
            }
        }

        [Test]
        public void CutShortFile_IsStillReadableFromTheTop()
        {
            // What a crash leaves behind: entries, no tail. Being able to read this is the reason
            // entries carry their own length.
            var complete = Write((writer, symbols) =>
            {
                for (long f = 0; f < 4; f++)
                {
                    var frame = new Frame { frameNumber = f, frameRate = FrameRate.FPS60 };
                    writer.BeginFrame(in frame, symbols);
                    writer.EndFrame();
                }
            }, close: false);

            using (var reader = new FrameRecordReader(new MemoryStream(complete)))
            {
                Assert.IsFalse(reader.hasIndex, "a file that was never closed has no tail");
                Assert.IsNull(reader.symbols);
                Assert.IsFalse(reader.TrySeekFrame(0), "seeking needs the index");

                var frames = 0;
                while (reader.TryReadEntry(out var entry))
                {
                    if (entry.kind == FrameEntryKind.FrameBoundary) frames++;
                }

                Assert.AreEqual(4, frames, "every frame that was written is still there");
            }
        }

        [Test]
        public void FileCutMidEntry_ReadsUpToTheCutRatherThanThrowing()
        {
            var complete = Write((writer, symbols) =>
            {
                for (long f = 0; f < 4; f++)
                {
                    var frame = new Frame { frameNumber = f, frameRate = FrameRate.FPS60 };
                    writer.BeginFrame(in frame, symbols);
                    writer.EndFrame();
                }
            }, close: false);

            // Lop off a few bytes, landing in the middle of the last entry.
            var truncated = new byte[complete.Length - 5];
            Array.Copy(complete, truncated, truncated.Length);

            using (var reader = new FrameRecordReader(new MemoryStream(truncated)))
            {
                var frames = 0;
                while (reader.TryReadEntry(out var entry))
                {
                    if (entry.kind == FrameEntryKind.FrameBoundary) frames++;
                }

                Assert.AreEqual(3, frames, "the frames before the cut survive");
            }
        }

        [Test]
        public void Symbols_AreWrittenOnceHoweverManyFramesUseThem()
        {
            using var events = new EventFrame();

            var bytes = Write((writer, symbols) =>
            {
                for (long f = 0; f < 10; f++)
                {
                    events.Reset(f, FrameRate.FPS60);
                    events.Add(new EventRecord(f, EventKind.PropertyWrite, symbols.Intern("rest"),
                        symbols.Intern("/live/object/cam/fov"), EventFlags.None));

                    var frame = new Frame { frameNumber = f, frameRate = FrameRate.FPS60, events = events };
                    writer.BeginFrame(in frame, symbols);
                    writer.WriteEvents(events, symbols);
                    writer.EndFrame();
                }
            });

            var kinds = KindsOf(bytes, out var reader);
            using (reader)
            {
                // This is the whole point of the mapping table: a path arriving sixty times a second
                // costs its characters once.
                Assert.AreEqual(2, kinds.FindAll(k => k == FrameEntryKind.Symbol).Count);
                Assert.AreEqual(10, kinds.FindAll(k => k == FrameEntryKind.Event).Count);
            }
        }
    }
}
