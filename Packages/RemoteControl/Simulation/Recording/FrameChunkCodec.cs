// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace Lilium.RemoteControl.Frames.Recording
{
    /// <summary>
    /// Packs a range of entries into a compressed chunk, and unpacks it back to the byte-identical
    /// range it came from.
    ///
    /// That identity is the whole design. Everything above -- the entry walk, seeking, the viewer --
    /// keeps reading exactly the bytes it always did, so the reorganisation done here stays
    /// invisible to all of it and a round-trip test is enough to hold the codec to its contract.
    ///
    /// What gets reorganised is the state lane. State payloads are dense arrays of the same shape
    /// every frame, and only a little of each value moves between frames: measured over real takes,
    /// two thirds of the floats are bit-identical to the previous frame, half never change at all,
    /// and the sign and exponent byte holds still in 98% of frames. Laid out frame after frame those
    /// repeats sit ~1.4 KB apart, far enough that a match token costs nearly what it saves. Grouping
    /// the same byte of the same element across the chunk's frames puts them side by side instead,
    /// which measured 1.5x better than compressing the range as it stands (28% -> 19% of raw).
    ///
    /// Nothing here assumes the shape holds across the chunk. A block that appears, disappears, or
    /// changes its element count simply forms its own group, so a chunk spanning a structural change
    /// still round-trips -- it just compresses a little worse.
    /// </summary>
    public sealed class FrameChunkCodec
    {
        /// <summary>
        /// A run of state entries sharing a shape, and where its bytes sit in the blob.
        ///
        /// Keyed by the shape rather than the type alone: an element count that changes mid-chunk
        /// has to split, because the byte at a given offset stops meaning the same thing.
        /// </summary>
        private struct Group
        {
            public int typeId;
            public int elementSize;
            public int count;
            public int frames;
            public int blobOffset;
        }

        /// <summary>Bytes an entry spends on its header: kind, payload length, frame number.</summary>
        private const int kEntryHeader = 1 + 4 + 8;

        /// <summary>
        /// Bytes a state payload spends naming its shape before the elements: type, element width,
        /// element count, layout hash. Copied through verbatim -- only the elements are transposed,
        /// and the header is the same bytes in every frame of a group, which deflate crushes.
        /// </summary>
        private const int kStateHeader = 4 + 4 + 4 + 8;

        // Reused across calls. A chunk is built about once a second, but it is built on the frame
        // thread, so these are buffers to refill rather than allocations to make.
        private readonly List<Group> _groups = new List<Group>();
        private int[] _cursors = Array.Empty<int>();
        private byte[] _body = Array.Empty<byte>();
        private byte[] _out = Array.Empty<byte>();
        private readonly MemoryStream _staging = new MemoryStream();

        /// <summary>
        /// Compresses a range of whole entries into a chunk. The buffer handed back is valid until
        /// the next call on this codec, which is the rule the reader's payloads already follow.
        /// </summary>
        public int Encode(ReadOnlySpan<byte> entries, out byte[] buffer)
        {
            var bodyLength = _BuildBody(entries);

            _staging.SetLength(0);
            using (var deflate = new DeflateStream(_staging, CompressionLevel.Optimal, leaveOpen: true))
            {
                deflate.Write(_body, 0, bodyLength);
            }

            var length = (int)_staging.Length;
            _Ensure(ref _out, length);
            Buffer.BlockCopy(_staging.GetBuffer(), 0, _out, 0, length);

            buffer = _out;
            return length;
        }

        /// <summary>
        /// Restores the entry range a chunk was made from. <paramref name="expandedLength"/> is what
        /// the chunk header recorded, so the result is sized once rather than grown into -- and it
        /// doubles as the check that the round trip landed where it started.
        /// </summary>
        public int Decode(ReadOnlySpan<byte> chunk, int expandedLength, out byte[] buffer)
        {
            var bodyLength = _Inflate(chunk);
            var length = _Expand(bodyLength, expandedLength);

            buffer = _out;
            return length;
        }

        /// <summary>
        /// Lays the range out as skeleton, then group directory, then blobs.
        ///
        /// The skeleton keeps every entry header and every non-state payload where it was, so
        /// decoding is a walk that puts state payloads back rather than a reassembly that has to
        /// remember what the order was.
        /// </summary>
        private int _BuildBody(ReadOnlySpan<byte> entries)
        {
            _groups.Clear();

            // Pass one measures everything, so the body can be sized before a byte of it is written.
            var skeletonLength = 0;
            var position = 0;
            while (position < entries.Length)
            {
                var length = _ReadInt32(entries, position + 1);
                if (length < 0 || position + kEntryHeader + length > entries.Length)
                {
                    throw new InvalidDataException("[RemoteControl] Chunk was given a partial entry.");
                }

                var payload = entries.Slice(position + kEntryHeader, length);
                skeletonLength += kEntryHeader;

                if (_TryReadShape(entries[position], payload, length, out var typeId, out var elementSize, out var count))
                {
                    skeletonLength += kStateHeader;

                    var index = _IndexOfGroup(typeId, elementSize, count);
                    if (index < 0)
                    {
                        _groups.Add(new Group { typeId = typeId, elementSize = elementSize, count = count });
                        index = _groups.Count - 1;
                    }

                    var group = _groups[index];
                    group.frames++;
                    _groups[index] = group;
                }
                else
                {
                    skeletonLength += length;
                }

                position += kEntryHeader + length;
            }

            var blobLength = 0;
            for (int i = 0; i < _groups.Count; i++)
            {
                var group = _groups[i];
                group.blobOffset = blobLength;
                _groups[i] = group;

                blobLength += group.frames * group.count * group.elementSize;
            }

            var directoryLength = 4 + _groups.Count * 16;
            var bodyLength = 4 + skeletonLength + directoryLength + blobLength;
            _Ensure(ref _body, bodyLength);

            var body = _body;
            _WriteInt32(body, 0, skeletonLength);

            const int skeleton = 4;
            var directory = skeleton + skeletonLength;
            var blobs = directory + directoryLength;

            _WriteInt32(body, directory, _groups.Count);
            for (int i = 0; i < _groups.Count; i++)
            {
                var at = directory + 4 + i * 16;
                _WriteInt32(body, at, _groups[i].typeId);
                _WriteInt32(body, at + 4, _groups[i].elementSize);
                _WriteInt32(body, at + 8, _groups[i].count);
                _WriteInt32(body, at + 12, _groups[i].frames);
            }

            // Pass two fills the skeleton and scatters each state byte to where the same byte of the
            // same element sits for every other frame in the chunk.
            _ResetCursors(_groups.Count);
            position = 0;
            var write = skeleton;
            while (position < entries.Length)
            {
                var length = _ReadInt32(entries, position + 1);
                var payload = entries.Slice(position + kEntryHeader, length);

                entries.Slice(position, kEntryHeader).CopyTo(new Span<byte>(body, write, kEntryHeader));
                write += kEntryHeader;

                if (_TryReadShape(entries[position], payload, length, out var typeId, out var elementSize, out var count))
                {
                    payload.Slice(0, kStateHeader).CopyTo(new Span<byte>(body, write, kStateHeader));
                    write += kStateHeader;

                    var index = _IndexOfGroup(typeId, elementSize, count);
                    var group = _groups[index];
                    var frame = _cursors[index]++;
                    var stride = group.frames;
                    var plane = blobs + group.blobOffset + frame;

                    for (int element = 0; element < count; element++)
                    {
                        var source = kStateHeader + element * elementSize;
                        var target = plane + element * elementSize * stride;

                        for (int b = 0; b < elementSize; b++)
                        {
                            body[target + b * stride] = payload[source + b];
                        }
                    }
                }
                else
                {
                    payload.CopyTo(new Span<byte>(body, write, length));
                    write += length;
                }

                position += kEntryHeader + length;
            }

            return bodyLength;
        }

        /// <summary>Walks the skeleton and puts the state payloads back, restoring the original bytes.</summary>
        private int _Expand(int bodyLength, int expandedLength)
        {
            var body = _body;

            if (bodyLength < 4) throw new InvalidDataException("[RemoteControl] Chunk is too short to hold a skeleton.");

            var skeletonLength = _ReadInt32(body, 0);
            const int skeleton = 4;
            var directory = skeleton + skeletonLength;

            if (skeletonLength < 0 || directory + 4 > bodyLength)
            {
                throw new InvalidDataException("[RemoteControl] Chunk skeleton runs past the chunk.");
            }

            var groupCount = _ReadInt32(body, directory);
            if (groupCount < 0 || directory + 4 + groupCount * 16 > bodyLength)
            {
                throw new InvalidDataException("[RemoteControl] Chunk directory runs past the chunk.");
            }

            var blobs = directory + 4 + groupCount * 16;

            _groups.Clear();
            var blobLength = 0;
            for (int i = 0; i < groupCount; i++)
            {
                var at = directory + 4 + i * 16;
                var group = new Group
                {
                    typeId = _ReadInt32(body, at),
                    elementSize = _ReadInt32(body, at + 4),
                    count = _ReadInt32(body, at + 8),
                    frames = _ReadInt32(body, at + 12),
                    blobOffset = blobLength,
                };

                if (group.elementSize <= 0 || group.count <= 0 || group.frames < 0)
                {
                    throw new InvalidDataException("[RemoteControl] Chunk declares a shape that cannot exist.");
                }

                _groups.Add(group);
                blobLength += group.frames * group.count * group.elementSize;
            }

            if (blobs + blobLength > bodyLength)
            {
                throw new InvalidDataException("[RemoteControl] Chunk blobs run past the chunk.");
            }

            _Ensure(ref _out, expandedLength);
            var output = _out;

            _ResetCursors(groupCount);
            var read = skeleton;
            var write = 0;
            var end = skeleton + skeletonLength;
            while (read < end)
            {
                var kind = body[read];
                var length = _ReadInt32(body, read + 1);

                Buffer.BlockCopy(body, read, output, write, kEntryHeader);
                read += kEntryHeader;
                write += kEntryHeader;

                var head = new ReadOnlySpan<byte>(body, read, Math.Min(kStateHeader, end - read));
                if (_TryReadShape(kind, head, length, out var typeId, out var elementSize, out var count))
                {
                    Buffer.BlockCopy(body, read, output, write, kStateHeader);
                    read += kStateHeader;
                    write += kStateHeader;

                    var index = _IndexOfGroup(typeId, elementSize, count);
                    if (index < 0) throw new InvalidDataException("[RemoteControl] Chunk names a shape it does not carry.");

                    var group = _groups[index];
                    var frame = _cursors[index]++;
                    if (frame >= group.frames)
                    {
                        throw new InvalidDataException("[RemoteControl] Chunk holds more frames of a shape than it declared.");
                    }

                    var stride = group.frames;
                    var plane = blobs + group.blobOffset + frame;

                    for (int element = 0; element < count; element++)
                    {
                        var source = plane + element * elementSize * stride;
                        var target = write + element * elementSize;

                        for (int b = 0; b < elementSize; b++)
                        {
                            output[target + b] = body[source + b * stride];
                        }
                    }

                    write += count * elementSize;
                }
                else
                {
                    Buffer.BlockCopy(body, read, output, write, length);
                    read += length;
                    write += length;
                }
            }

            if (write != expandedLength)
            {
                throw new InvalidDataException(
                    $"[RemoteControl] Chunk expanded to {write} bytes where {expandedLength} were recorded.");
            }

            return write;
        }

        private int _Inflate(ReadOnlySpan<byte> chunk)
        {
            // Grown before it is filled, not after: SetLength zeroes whatever it exposes, so copying
            // first and sizing second wipes the chunk and the inflate reports corrupted data.
            _staging.SetLength(0);
            _staging.SetLength(chunk.Length);

            chunk.CopyTo(new Span<byte>(_staging.GetBuffer(), 0, chunk.Length));
            _staging.Position = 0;

            _Ensure(ref _body, 1024);

            var total = 0;
            using (var inflate = new DeflateStream(_staging, CompressionMode.Decompress, leaveOpen: true))
            {
                while (true)
                {
                    if (total == _body.Length) _Ensure(ref _body, _body.Length * 2);

                    var read = inflate.Read(_body, total, _body.Length - total);
                    if (read == 0) break;

                    total += read;
                }
            }

            return total;
        }

        /// <summary>
        /// True when an entry is a state entry whose payload matches the shape it declares.
        ///
        /// A payload that does not match is carried verbatim rather than refused. The check runs
        /// identically on both sides, so whatever the encoder passes over the decoder passes over
        /// too, and an entry this codec does not recognise still survives the round trip.
        /// </summary>
        private static bool _TryReadShape(byte kind, ReadOnlySpan<byte> head, int payloadLength,
                                          out int typeId, out int elementSize, out int count)
        {
            typeId = 0;
            elementSize = 0;
            count = 0;

            if (kind != (byte)FrameEntryKind.State) return false;
            if (payloadLength < kStateHeader || head.Length < kStateHeader) return false;

            typeId = _ReadInt32(head, 0);
            elementSize = _ReadInt32(head, 4);
            count = _ReadInt32(head, 8);

            if (elementSize <= 0 || count <= 0) return false;

            return payloadLength - kStateHeader == (long)elementSize * count;
        }

        private int _IndexOfGroup(int typeId, int elementSize, int count)
        {
            for (int i = 0; i < _groups.Count; i++)
            {
                var group = _groups[i];
                if (group.typeId == typeId && group.elementSize == elementSize && group.count == count) return i;
            }

            return -1;
        }

        private void _ResetCursors(int count)
        {
            if (_cursors.Length < count) _cursors = new int[Math.Max(count, 8)];

            Array.Clear(_cursors, 0, count);
        }

        private static void _Ensure(ref byte[] buffer, int length)
        {
            if (buffer.Length >= length) return;

            Array.Resize(ref buffer, Math.Max(length, 1024));
        }

        private static int _ReadInt32(ReadOnlySpan<byte> source, int offset)
            => source[offset] | (source[offset + 1] << 8) | (source[offset + 2] << 16) | (source[offset + 3] << 24);

        private static int _ReadInt32(byte[] source, int offset)
            => source[offset] | (source[offset + 1] << 8) | (source[offset + 2] << 16) | (source[offset + 3] << 24);

        private static void _WriteInt32(byte[] target, int offset, int value)
        {
            target[offset] = (byte)value;
            target[offset + 1] = (byte)(value >> 8);
            target[offset + 2] = (byte)(value >> 16);
            target[offset + 3] = (byte)(value >> 24);
        }
    }
}
