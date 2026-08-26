// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Lilium.RemoteControl.Frames.Recording
{
    /// <summary>
    /// Reads a recording back, entry by entry.
    ///
    /// Two ways in, and the file supports both on purpose. Walking from the top needs nothing but
    /// the entries themselves, which is how a file that was cut short -- or one still being written
    /// -- is read. Seeking needs the tail index, which is an optimisation for a finished file rather
    /// than part of how the format works.
    ///
    /// Entry payloads are windows into a buffer this reader owns and reuses. A caller that keeps one
    /// past the next read is reading the entry after it.
    /// </summary>
    public sealed class FrameRecordReader : IDisposable
    {
        private readonly BinaryReader _reader;

        // Held rather than reached for through the reader: BinaryReader drops its reference to the
        // stream when it is disposed, so BaseStream is null by the time this would need it.
        private readonly Stream _stream;
        private readonly bool _ownsStream;
        private readonly long _entriesOffset;

        private byte[] _payload = Array.Empty<byte>();
        private int _payloadLength;

        private long[] _frameOffsets;
        private long[] _keyframes = Array.Empty<long>();
        private long _firstFrameNumber;

        /// <summary>What the file says about itself.</summary>
        public FrameRecordHeader header { get; }

        /// <summary>
        /// True when the file was closed properly and carries its tail. False for one that was cut
        /// short, which is still readable from the top.
        /// </summary>
        public bool hasIndex => _frameOffsets != null;

        /// <summary>Frames the tail index knows about, or zero when there is no index.</summary>
        public int indexedFrameCount => _frameOffsets?.Length ?? 0;

        /// <summary>
        /// Frames that carry the inventory, in order. These are the frames a seek can land on and
        /// know the shape of the world; everything between them restores its values but inherits
        /// its shape from whatever came before.
        /// </summary>
        public IReadOnlyList<long> keyframes => _keyframes;

        /// <summary>
        /// The complete mapping table from the tail, or null when there is none. Ids are positions
        /// in this list. Without an index the table is rebuilt by collecting
        /// <see cref="FrameEntryKind.Symbol"/> entries while reading.
        /// </summary>
        public IReadOnlyList<string> symbols { get; }

        public FrameRecordReader(Stream stream, bool leaveOpen = false)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanRead) throw new ArgumentException("Stream is not readable.", nameof(stream));
            if (!stream.CanSeek) throw new ArgumentException("Stream is not seekable.", nameof(stream));

            _reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            _stream = stream;
            _ownsStream = !leaveOpen;

            stream.Position = 0;
            var magic = _reader.ReadBytes(FrameRecordFormat.kMagic.Length);
            for (int i = 0; i < magic.Length; i++)
            {
                if (magic[i] == FrameRecordFormat.kMagic[i]) continue;

                throw new InvalidDataException("[RemoteControl] Not a frame recording.");
            }

            var version = _reader.ReadInt32();
            if (version != FrameRecordFormat.kVersion)
            {
                throw new InvalidDataException(
                    $"[RemoteControl] Frame recording version {version} cannot be read by version {FrameRecordFormat.kVersion}.");
            }

            var numerator = _reader.ReadUInt32();
            var denominator = _reader.ReadUInt32();

            header = new FrameRecordHeader
            {
                frameRate = new FrameRate(numerator, denominator),
                startTicks = _reader.ReadInt64(),
                engineId = _reader.ReadString(),
                buildId = _reader.ReadString(),
            };

            _entriesOffset = stream.Position;

            symbols = _TryReadTail();
            Rewind();
        }

        /// <summary>Goes back to the first entry.</summary>
        public void Rewind() => _stream.Position = _entriesOffset;

        /// <summary>
        /// Jumps to the start of a frame. Needs the tail index; without one, walk from
        /// <see cref="Rewind"/> instead.
        /// </summary>
        public bool TrySeekFrame(long frameNumber)
        {
            if (_frameOffsets == null) return false;

            var index = frameNumber - _firstFrameNumber;
            if (index < 0 || index >= _frameOffsets.Length) return false;

            _stream.Position = _frameOffsets[index];
            return true;
        }

        /// <summary>
        /// The last keyframe at or before a frame, or -1 when there is none before it. Where a seek
        /// starts from when the shape of the world matters and not just its values.
        /// </summary>
        public long KeyframeAtOrBefore(long frameNumber)
        {
            var found = -1L;
            for (int i = 0; i < _keyframes.Length; i++)
            {
                if (_keyframes[i] > frameNumber) break;

                found = _keyframes[i];
            }

            return found;
        }

        /// <summary>
        /// Reads the next entry. False at the end of the entries, which is either the footer or the
        /// end of the file.
        /// </summary>
        public bool TryReadEntry(out FrameEntry entry)
        {
            entry = default;

            var stream = _stream;
            if (stream.Position >= stream.Length) return false;

            // The tail is not an entry. Stopping here rather than trying to parse it is what keeps a
            // straight walk from running off the end of a finished file.
            if (_frameOffsets != null && stream.Position >= _tailOffset) return false;

            var kind = (FrameEntryKind)_reader.ReadByte();
            var length = _reader.ReadInt32();
            var frameNumber = _reader.ReadInt64();

            if (length < 0 || stream.Position + length > stream.Length)
            {
                // A file cut off mid-entry. The frames before it are intact, so this is the end of
                // what can be read rather than a failure.
                return false;
            }

            if (_payload.Length < length) _payload = new byte[Math.Max(length, 256)];

            var read = _reader.Read(_payload, 0, length);
            if (read != length) return false;

            _payloadLength = length;
            entry = new FrameEntry(kind, frameNumber, new ReadOnlySpan<byte>(_payload, 0, _payloadLength));
            return true;
        }

        public void Dispose()
        {
            _reader.Dispose();
            if (_ownsStream) _stream.Dispose();
        }

        // Where the tail starts, so a straight walk knows to stop. Long.MaxValue while there is none.
        private long _tailOffset = long.MaxValue;

        private IReadOnlyList<string> _TryReadTail()
        {
            var stream = _stream;
            if (stream.Length < _entriesOffset + FrameRecordFormat.kFooterSize) return null;

            stream.Position = stream.Length - FrameRecordFormat.kFooterMagic.Length;
            var magic = _reader.ReadBytes(FrameRecordFormat.kFooterMagic.Length);
            for (int i = 0; i < magic.Length; i++)
            {
                // No footer: the writer never got to close. Everything before this point is still
                // good, so this is not an error.
                if (magic[i] != FrameRecordFormat.kFooterMagic[i]) return null;
            }

            stream.Position = stream.Length - FrameRecordFormat.kFooterSize;
            var indexOffset = _reader.ReadInt64();
            var keyframeOffset = _reader.ReadInt64();
            var mappingOffset = _reader.ReadInt64();

            if (indexOffset < _entriesOffset || indexOffset >= stream.Length) return null;
            if (keyframeOffset < indexOffset || keyframeOffset >= stream.Length) return null;
            if (mappingOffset < keyframeOffset || mappingOffset >= stream.Length) return null;

            stream.Position = indexOffset;
            _firstFrameNumber = _reader.ReadInt64();
            var frameCount = _reader.ReadInt32();
            if (frameCount < 0) return null;

            var offsets = new long[frameCount];
            for (int i = 0; i < frameCount; i++) offsets[i] = _reader.ReadInt64();
            _frameOffsets = offsets;
            _tailOffset = indexOffset;

            stream.Position = keyframeOffset;
            var keyframeCount = _reader.ReadInt32();
            if (keyframeCount < 0) return null;

            var keyframes = new long[keyframeCount];
            for (int i = 0; i < keyframeCount; i++) keyframes[i] = _reader.ReadInt64();
            _keyframes = keyframes;

            stream.Position = mappingOffset;
            var symbolCount = _reader.ReadInt32();
            var table = new string[Math.Max(symbolCount, 0)];
            for (int i = 0; i < table.Length; i++) table[i] = _reader.ReadString();

            return table;
        }
    }
}
