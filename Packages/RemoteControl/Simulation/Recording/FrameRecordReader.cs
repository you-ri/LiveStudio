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
    /// the chunks themselves, which is how a file that was cut short -- or one still being written
    /// -- is read. Seeking needs the tail index, which is an optimisation for a finished file rather
    /// than part of how the format works.
    ///
    /// Entries are read out of one expanded chunk at a time. Payloads are windows into that buffer,
    /// which this reader owns and reuses: a caller that keeps one past the next read is reading the
    /// entry after it.
    /// </summary>
    public sealed class FrameRecordReader : IDisposable
    {
        private const int kEntryHeader = FrameRecordFormat.kEntryHeaderSize;

        private readonly BinaryReader _reader;

        // Held rather than reached for through the reader: BinaryReader drops its reference to the
        // stream when it is disposed, so BaseStream is null by the time this would need it.
        private readonly Stream _stream;
        private readonly bool _ownsStream;
        private readonly long _entriesOffset;

        private readonly FrameChunkCodec _codec = new FrameChunkCodec();
        private readonly List<long> _chunkStarts = new List<long>();
        private byte[] _chunkRaw = Array.Empty<byte>();
        private byte[] _chunkData = Array.Empty<byte>();
        private int _chunkLength;
        private int _chunkCursor;
        private int _chunkIndex = -1;

        // The frame the entries being read belong to: the number the last boundary carried.
        private long _frameNumber = -1;

        private long[] _frameNumbers;
        private int[] _frameChunks;
        private int[] _frameCursors;
        private long[] _keyframes = Array.Empty<long>();
        private long _firstFrameNumber;

        // Where the tail starts, so a straight walk knows to stop. Long.MaxValue while there is none.
        private long _tailOffset = long.MaxValue;

        /// <summary>What the file says about itself.</summary>
        public FrameRecordHeader header { get; }

        /// <summary>
        /// True when the file was closed properly and carries its tail. False for one that was cut
        /// short, which is still readable from the top.
        /// </summary>
        public bool hasIndex => _frameNumbers != null;

        /// <summary>Frames the tail index knows about, or zero when there is no index.</summary>
        public int indexedFrameCount => _frameNumbers?.Length ?? 0;

        /// <summary>
        /// Frame number the index starts at, or zero when there is no index.
        ///
        /// Frame numbers are the gate's, so a recording does not start at zero.
        /// </summary>
        public long firstFrameNumber => _firstFrameNumber;

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
            if (magic.Length != FrameRecordFormat.kMagic.Length) throw new InvalidDataException("[RemoteControl] Not a frame recording.");

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
                engineId = _ReadString(),
                buildId = _ReadString(),
            };

            _entriesOffset = stream.Position;

            symbols = _TryReadTail();
            Rewind();
        }

        /// <summary>Goes back to the first entry.</summary>
        public void Rewind()
        {
            _stream.Position = _entriesOffset;
            _chunkIndex = -1;
            _chunkLength = 0;
            _chunkCursor = 0;
            _frameNumber = -1;
        }

        /// <summary>
        /// Jumps to the start of a frame -- its boundary. Needs the tail index; without one, walk
        /// from <see cref="Rewind"/> instead.
        /// </summary>
        public bool TrySeekFrame(long frameNumber)
        {
            var index = IndexOfFrame(frameNumber);
            if (index < 0) return false;

            if (_frameChunks[index] != _chunkIndex && !_LoadChunk(_frameChunks[index])) return false;
            if (_frameCursors[index] < 0 || _frameCursors[index] > _chunkLength) return false;

            _chunkCursor = _frameCursors[index];
            _frameNumber = frameNumber;
            return true;
        }

        /// <summary>
        /// Frame number the index's <paramref name="index"/>th frame carries, or -1 when there is no
        /// index or the position is outside it.
        ///
        /// Read out of the index rather than counted on from the first. A frame number comes from the
        /// clock, and a run that drops below rate skips numbers -- a 2628 frame take recorded at
        /// sixty hertz was measured spanning 2805 numbers. So position n is not frame
        /// <c>first + n</c>, and treating it as one lands a seek tens of frames from where it was
        /// asked for.
        /// </summary>
        public long FrameNumberAt(int index)
        {
            if (_frameNumbers == null || index < 0 || index >= _frameNumbers.Length) return -1;

            return _frameNumbers[index];
        }

        /// <summary>
        /// How far into its chunk the index's <paramref name="index"/>th frame starts, or -1. Zero
        /// means the frame opens its chunk -- which every keyframe should.
        /// </summary>
        internal int CursorOfFrameAt(int index)
        {
            if (_frameCursors == null || index < 0 || index >= _frameCursors.Length) return -1;

            return _frameCursors[index];
        }

        /// <summary>
        /// Where a frame sits in the index, or -1 when the recording does not hold that frame. A
        /// binary search: frame numbers only ever increase down the file.
        /// </summary>
        public int IndexOfFrame(long frameNumber)
        {
            if (_frameNumbers == null) return -1;

            var lo = 0;
            var hi = _frameNumbers.Length - 1;
            while (lo <= hi)
            {
                var middle = lo + ((hi - lo) >> 1);
                var value = _frameNumbers[middle];

                if (value == frameNumber) return middle;

                if (value < frameNumber) lo = middle + 1;
                else hi = middle - 1;
            }

            return -1;
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
        /// Reads the next entry. False at the end of the entries, which is either the tail or the
        /// end of what could be read -- a chunk cut short ends the walk rather than failing it.
        /// </summary>
        public bool TryReadEntry(out FrameEntry entry)
        {
            entry = default;

            while (_chunkCursor >= _chunkLength)
            {
                if (!_LoadNextChunk()) return false;
            }

            if (_chunkCursor + kEntryHeader > _chunkLength) return false;

            var data = _chunkData;
            var at = _chunkCursor;

            var kind = (FrameEntryKind)data[at];
            var length = _ReadInt32(data, at + 1);

            at += kEntryHeader;
            if (length < 0 || at + length > _chunkLength) return false;

            _chunkCursor = at + length;

            if (kind == FrameEntryKind.FrameBoundary && length >= 8)
            {
                _frameNumber = BitConverter.ToInt64(data, at);
            }

            entry = new FrameEntry(kind, _frameNumber, new ReadOnlySpan<byte>(data, at, length));
            return true;
        }

        public void Dispose()
        {
            _reader.Dispose();
            if (_ownsStream) _stream.Dispose();
        }

        /// <summary>Opens the chunk after the one loaded, discovering it when the tail did not name it.</summary>
        private bool _LoadNextChunk()
        {
            var next = _chunkIndex + 1;
            if (next < _chunkStarts.Count) return _LoadChunk(next);

            // No tail, so the chunks are being found as the walk goes. Where the stream sits is where
            // the next one starts, because the last read left it just past the previous body.
            var limit = _ChunkLimit();
            if (_stream.Position + FrameRecordFormat.kChunkHeaderSize > limit) return false;

            var start = _stream.Position;
            if (!_ReadChunkHere(next, limit)) return false;

            _chunkStarts.Add(start);
            return true;
        }

        /// <summary>Opens a chunk the tail named, or one already met while walking.</summary>
        private bool _LoadChunk(int index)
        {
            if (index < 0 || index >= _chunkStarts.Count) return false;

            _stream.Position = _chunkStarts[index];
            return _ReadChunkHere(index, _ChunkLimit());
        }

        private long _ChunkLimit() => _tailOffset == long.MaxValue ? _stream.Length : _tailOffset;

        private bool _ReadChunkHere(int index, long limit)
        {
            if (_stream.Position + FrameRecordFormat.kChunkHeaderSize > limit) return false;

            var compressed = _reader.ReadInt32();
            var expanded = _reader.ReadInt32();

            // A chunk the writer never finished. Everything before it is intact, so this ends the
            // walk rather than failing it -- the same rule an entry cut in half follows.
            if (compressed <= 0 || expanded < 0) return false;
            if (_stream.Position + compressed > limit) return false;

            if (_chunkRaw.Length < compressed) _chunkRaw = new byte[Math.Max(compressed, Math.Max(1024, _chunkRaw.Length * 2))];
            if (_reader.Read(_chunkRaw, 0, compressed) != compressed) return false;

            try
            {
                _chunkLength = _codec.Decode(new ReadOnlySpan<byte>(_chunkRaw, 0, compressed), expanded, out _chunkData);
            }
            catch (InvalidDataException)
            {
                // Damaged rather than cut: the same answer, since nothing after it can be trusted.
                return false;
            }

            _chunkCursor = 0;
            _chunkIndex = index;
            return true;
        }

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

            var numbers = new long[frameCount];
            var chunks = new int[frameCount];
            var cursors = new int[frameCount];

            for (int i = 0; i < frameCount; i++)
            {
                numbers[i] = _reader.ReadInt64();
                chunks[i] = _reader.ReadInt32();
                cursors[i] = _reader.ReadInt32();
            }

            var chunkCount = _reader.ReadInt32();
            if (chunkCount < 0) return null;

            _chunkStarts.Clear();
            for (int i = 0; i < chunkCount; i++) _chunkStarts.Add(_reader.ReadInt64());

            _frameNumbers = numbers;
            _frameChunks = chunks;
            _frameCursors = cursors;
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
            for (int i = 0; i < table.Length; i++) table[i] = _ReadString();

            return table;
        }

        /// <summary>A string as the writer lays one down: its UTF-8 byte count, then the bytes.</summary>
        private string _ReadString()
        {
            var length = _reader.ReadInt32();
            if (length < 0 || _stream.Position + length > _stream.Length)
            {
                throw new InvalidDataException("[RemoteControl] A string in the recording runs past its end.");
            }

            return length == 0 ? string.Empty : Encoding.UTF8.GetString(_reader.ReadBytes(length));
        }

        private static int _ReadInt32(byte[] source, int offset)
            => source[offset] | (source[offset + 1] << 8) | (source[offset + 2] << 16) | (source[offset + 3] << 24);
    }
}
