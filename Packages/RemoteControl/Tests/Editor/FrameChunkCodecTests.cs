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
    /// The codec has exactly one contract: what comes out is what went in. Everything it does
    /// internally -- splitting the state lane out, grouping bytes across frames -- is only allowed
    /// to exist because it is invisible from here.
    /// </summary>
    public class FrameChunkCodecTests
    {
        private const int kEntryHeader = 1 + 4 + 8;

        private static void Append(List<byte> to, FrameEntryKind kind, long frame, byte[] payload)
        {
            to.Add((byte)kind);
            to.AddRange(BitConverter.GetBytes(payload.Length));
            to.AddRange(BitConverter.GetBytes(frame));
            to.AddRange(payload);
        }

        /// <summary>A state payload of <paramref name="count"/> elements, filled by the caller.</summary>
        private static byte[] State(int typeId, int elementSize, int count, Func<int, int, byte> value)
        {
            // type, element width, element count, layout hash -- the shape a state entry names
            // before its elements. The hash is left zero: what is under test is the transposition,
            // and the codec copies the header through without reading past the shape.
            const int header = 4 + 4 + 4 + 8;

            var payload = new byte[header + elementSize * count];
            Buffer.BlockCopy(BitConverter.GetBytes(typeId), 0, payload, 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(elementSize), 0, payload, 4, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(count), 0, payload, 8, 4);

            for (int element = 0; element < count; element++)
            {
                for (int b = 0; b < elementSize; b++)
                {
                    payload[header + element * elementSize + b] = value(element, b);
                }
            }

            return payload;
        }

        /// <summary>Encodes and decodes, and insists the bytes came back unchanged.</summary>
        private static int RoundTrip(byte[] entries)
        {
            var codec = new FrameChunkCodec();
            var compressed = codec.Encode(entries, out var chunk);
            var packed = new byte[compressed];
            Buffer.BlockCopy(chunk, 0, packed, 0, compressed);

            var decoded = codec.Decode(packed, entries.Length, out var buffer);
            Assert.AreEqual(entries.Length, decoded, "the chunk expanded to a different length");

            var back = new byte[decoded];
            Buffer.BlockCopy(buffer, 0, back, 0, decoded);
            CollectionAssert.AreEqual(entries, back, "the chunk did not round-trip");

            return compressed;
        }

        [Test]
        public void EmptyRange_RoundTrips()
        {
            RoundTrip(Array.Empty<byte>());
        }

        [Test]
        public void EntriesWithoutState_RoundTripUntouched()
        {
            var entries = new List<byte>();
            Append(entries, FrameEntryKind.FrameBoundary, 10, new byte[] { 1, 0, 0, 0, 60, 0, 0, 0 });
            Append(entries, FrameEntryKind.Symbol, 10, new byte[] { 5, 104, 101, 108, 108, 111 });
            Append(entries, FrameEntryKind.Event, 11, new byte[] { 7, 7, 7 });

            RoundTrip(entries.ToArray());
        }

        [Test]
        public void StateAcrossFrames_RoundTrips()
        {
            var entries = new List<byte>();
            for (int frame = 0; frame < 30; frame++)
            {
                var f = frame;
                Append(entries, FrameEntryKind.FrameBoundary, frame, new byte[] { 1, 0, 0, 0, 60, 0, 0, 0 });
                Append(entries, FrameEntryKind.State, frame,
                       State(3, 16, 2, (element, b) => (byte)(b == 0 ? f : element * 31 + b)));
            }

            RoundTrip(entries.ToArray());
        }

        [Test]
        public void ShapeChangingMidChunk_StillRoundTrips()
        {
            // The recorder can add a state element without touching the structure epoch, so a chunk
            // is not guaranteed to be rectangular. Each shape becomes its own group instead.
            var entries = new List<byte>();
            for (int frame = 0; frame < 12; frame++)
            {
                var f = frame;
                var count = frame < 5 ? 1 : 3;

                Append(entries, FrameEntryKind.FrameBoundary, frame, new byte[] { 1, 0, 0, 0, 60, 0, 0, 0 });
                Append(entries, FrameEntryKind.State, frame, State(3, 8, count, (element, b) => (byte)(f + element + b)));
            }

            RoundTrip(entries.ToArray());
        }

        [Test]
        public void BlockAppearingPartWayThrough_StillRoundTrips()
        {
            var entries = new List<byte>();
            for (int frame = 0; frame < 10; frame++)
            {
                var f = frame;
                Append(entries, FrameEntryKind.FrameBoundary, frame, new byte[] { 1, 0, 0, 0, 60, 0, 0, 0 });
                Append(entries, FrameEntryKind.State, frame, State(3, 12, 1, (element, b) => (byte)(f * b)));

                if (frame >= 6)
                {
                    Append(entries, FrameEntryKind.State, frame, State(9, 4, 2, (element, b) => (byte)(f + b)));
                }
            }

            RoundTrip(entries.ToArray());
        }

        [Test]
        public void StatePayloadThatDoesNotMatchItsShape_IsCarriedVerbatim()
        {
            // Both sides apply the same test, so an entry the codec cannot make sense of still
            // survives rather than being refused or silently mangled.
            var entries = new List<byte>();
            var broken = new byte[20];
            Buffer.BlockCopy(BitConverter.GetBytes(3), 0, broken, 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(16), 0, broken, 4, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(9), 0, broken, 8, 4);

            Append(entries, FrameEntryKind.State, 1, broken);

            RoundTrip(entries.ToArray());
        }

        /// <summary>
        /// A chunk shaped like a measured take: 370 floats an element, two thirds of them never
        /// moving, the rest drifting with capture noise in the low mantissa bits.
        ///
        /// The proportions matter. Made too regular -- one byte counting cleanly up -- the whole
        /// block repeats at a fixed distance and plain compression already crushes it, which says
        /// nothing about recorded data.
        /// </summary>
        private static byte[] TakeShapedChunk(int frames)
        {
            const int kFloats = 370;
            var entries = new List<byte>();
            var baseline = new float[kFloats];

            var seed = 12345u;
            uint Next()
            {
                seed = seed * 1664525u + 1013904223u;
                return seed;
            }

            for (int k = 0; k < kFloats; k++) baseline[k] = (Next() % 2000) * 0.001f - 1f;

            var value = new float[kFloats];
            Array.Copy(baseline, value, kFloats);

            for (int frame = 0; frame < frames; frame++)
            {
                for (int k = 0; k < kFloats; k++)
                {
                    if (k % 3 != 0) continue;

                    value[k] = baseline[k]
                               + UnityEngine.Mathf.Sin(frame * 0.05f + k) * 0.2f
                               + (Next() % 64) * 0.0000001f;
                }

                const int header = 4 + 4 + 4 + 8;

                var payload = new byte[header + kFloats * 4];
                Buffer.BlockCopy(BitConverter.GetBytes(3), 0, payload, 0, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(kFloats * 4), 0, payload, 4, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(1), 0, payload, 8, 4);
                Buffer.BlockCopy(value, 0, payload, header, kFloats * 4);

                Append(entries, FrameEntryKind.FrameBoundary, frame, new byte[] { 1, 0, 0, 0, 60, 0, 0, 0 });
                Append(entries, FrameEntryKind.State, frame, payload);
            }

            return entries.ToArray();
        }

        [Test]
        public void StateThatBarelyMoves_CompressesFarBetterThanTheRangeAsItStands()
        {
            // This is the reason the codec exists, so it is asserted rather than assumed: the same
            // bytes grouped across frames beat the range as it stands by about a third on real
            // takes (28% -> 19% of raw).
            var raw = TakeShapedChunk(60);
            var chunked = RoundTrip(raw);

            var plain = new MemoryStream();
            using (var deflate = new System.IO.Compression.DeflateStream(
                       plain, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
            {
                deflate.Write(raw, 0, raw.Length);
            }

            Assert.Less(chunked, plain.Length * 0.9,
                        $"grouping bytes across frames should beat compressing the range as it stands " +
                        $"({chunked} vs {plain.Length} of {raw.Length})");
        }

        [Test]
        public void ARealRecording_RoundTrips()
        {
            var symbols = new FrameSymbolTable();
            byte[] bytes;

            using (var stream = new MemoryStream())
            {
                var header = new FrameRecordHeader
                {
                    frameRate = FrameRate.FPS60,
                    startTicks = 638000000000000000L,
                    engineId = "unity",
                    buildId = "test-build",
                };

                using (var writer = new FrameRecordWriter(stream, header, leaveOpen: true))
                {
                    var state = new StateBlockSet();
                    for (int frame = 0; frame < 40; frame++)
                    {
                        var live = new Frame { frameNumber = frame, frameRate = FrameRate.FPS60 };
                        ref var element = ref state.GetOrCreate<Pose>().GetOrCreate(1);
                        element.value.x = frame * 0.01f;

                        writer.BeginFrame(in live, symbols);
                        writer.WriteState(state, symbols);
                        writer.EndFrame();
                    }

                    state.Dispose();
                }

                bytes = stream.ToArray();
            }

            long entriesOffset;
            using (var reader = new FrameRecordReader(new MemoryStream(bytes), leaveOpen: false))
            {
                reader.Rewind();
                entriesOffset = reader.position;
            }

            var entries = new byte[bytes.Length - entriesOffset];
            Buffer.BlockCopy(bytes, (int)entriesOffset, entries, 0, entries.Length);

            RoundTrip(entries);
        }

        [Test]
        public void TheCodecCanBeUsedAgain()
        {
            // The buffers are reused, so a second call has to not be reading the first one's leavings.
            var codec = new FrameChunkCodec();

            for (int pass = 0; pass < 3; pass++)
            {
                var entries = new List<byte>();
                for (int frame = 0; frame < 5 + pass * 4; frame++)
                {
                    var f = frame;
                    Append(entries, FrameEntryKind.State, frame,
                           State(3 + pass, 8, 1 + pass, (element, b) => (byte)(f + element * b)));
                }

                var raw = entries.ToArray();
                var compressed = codec.Encode(raw, out var chunk);

                var packed = new byte[compressed];
                Buffer.BlockCopy(chunk, 0, packed, 0, compressed);

                var decoded = codec.Decode(packed, raw.Length, out var buffer);
                var back = new byte[decoded];
                Buffer.BlockCopy(buffer, 0, back, 0, decoded);

                CollectionAssert.AreEqual(raw, back, $"pass {pass} did not round-trip");
            }
        }

        /// <summary>Stands in for a captured value: a few floats, most of which hold still.</summary>
        private struct Pose
        {
            public float x;
            public float y;
            public float z;
        }

        [Test]
        public void ATruncatedEntry_IsRefusedRatherThanGuessedAt()
        {
            var entries = new List<byte>();
            Append(entries, FrameEntryKind.State, 1, State(3, 8, 1, (element, b) => (byte)b));

            var raw = entries.ToArray();
            var cut = new byte[raw.Length - 4];
            Buffer.BlockCopy(raw, 0, cut, 0, cut.Length);

            var codec = new FrameChunkCodec();
            Assert.Throws<InvalidDataException>(() => codec.Encode(cut, out _));
        }
    }
}
