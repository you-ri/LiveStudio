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

        // Compression. When it is on, entries are read out of an expanded chunk rather than off the
        // stream, and a frame is found by naming a chunk and an offset inside it.
        private readonly bool _chunked;
        private readonly FrameChunkCodec _codec;
        private readonly List<long> _chunkStarts = new List<long>();
        private byte[] _chunkRaw = Array.Empty<byte>();
        private byte[] _chunkData = Array.Empty<byte>();
        private int _chunkLength;
        private int _chunkCursor;
        private int _chunkIndex = -1;

        private long[] _frameNumbers;
        private int[] _frameChunks;
        private int[] _frameCursors;

        /// <summary>What the file says about itself.</summary>
        public FrameRecordHeader header { get; }

        /// <summary>
        /// True when the file was closed properly and carries its tail. False for one that was cut
        /// short, which is still readable from the top.
        /// </summary>
        public bool hasIndex => _chunked ? _frameNumbers != null : _frameOffsets != null;

        /// <summary>Frames the tail index knows about, or zero when there is no index.</summary>
        public int indexedFrameCount => (_chunked ? _frameNumbers?.Length : _frameOffsets?.Length) ?? 0;

        /// <summary>True when the entries are compressed.</summary>
        public bool isChunked => _chunked;

        /// <summary>
        /// Frame number the index starts at, or zero when there is no index.
        ///
        /// Frame numbers are the gate's, so a recording does not start at zero -- and the index is
        /// contiguous from here, which is what lets a position within the recording be turned into
        /// a frame number without walking it.
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

            _chunked = _reader.ReadByte() != 0;
            if (_chunked) _codec = new FrameChunkCodec();

            _entriesOffset = stream.Position;

            symbols = _TryReadTail();
            Rewind();
        }

        /// <summary>Goes back to the first entry.</summary>
        public void Rewind()
        {
            _stream.Position = _entriesOffset;

            if (!_chunked) return;

            _chunkIndex = -1;
            _chunkLength = 0;
            _chunkCursor = 0;
            _LoadNextChunk();
        }

        /// <summary>
        /// Where the next entry starts.
        ///
        /// For building an index over a file that has none. A recording cut short has no tail, and a
        /// reader that can only walk forward makes browsing it quadratic: noting each frame's mark on
        /// one pass turns every later jump into a seek.
        ///
        /// A bookmark rather than a file offset. Uncompressed the two are the same thing, but
        /// compressed a position is a chunk and an offset inside it, so this is only meaningful to
        /// the <see cref="TrySeekTo"/> of the reader that handed it out.
        /// </summary>
        public long position => _chunked ? ((long)_chunkIndex << 32) | (uint)_chunkCursor : _stream.Position;

        /// <summary>
        /// Goes back to a bookmark a previous <see cref="position"/> reported.
        ///
        /// Refuses anything outside the entries rather than trusting the caller: a bookmark landing
        /// in the header or the tail would be read as an entry, and the length it found there would
        /// send the next read somewhere arbitrary.
        /// </summary>
        public bool TrySeekTo(long offset)
        {
            if (_chunked)
            {
                var chunk = (int)(offset >> 32);
                var cursor = (int)(offset & 0xFFFFFFFF);

                if (chunk != _chunkIndex && !_LoadChunk(chunk)) return false;
                if (cursor < 0 || cursor > _chunkLength) return false;

                _chunkCursor = cursor;
                return true;
            }

            if (offset < _entriesOffset || offset >= _stream.Length) return false;
            if (_frameOffsets != null && offset >= _tailOffset) return false;

            _stream.Position = offset;
            return true;
        }

        /// <summary>
        /// Jumps to the start of a frame. Needs the tail index; without one, walk from
        /// <see cref="Rewind"/> instead.
        /// </summary>
        public bool TrySeekFrame(long frameNumber)
        {
            var index = IndexOfFrame(frameNumber);
            if (index < 0) return false;

            if (_chunked)
            {
                if (_frameChunks[index] != _chunkIndex && !_LoadChunk(_frameChunks[index])) return false;

                _chunkCursor = _frameCursors[index];
                return true;
            }

            _stream.Position = _frameOffsets[index];
            return true;
        }

        /// <summary>
        /// Frame number the index's <paramref name="index"/>th frame carries, or -1 when there is no
        /// index or the position is outside it.
        ///
        /// Read out of the file rather than counted on from the first. A frame number comes from the
        /// clock, and a run that drops below rate skips numbers -- a 2628 frame take recorded at
        /// sixty hertz was measured spanning 2805 numbers. So position n is not frame
        /// <c>first + n</c>, and treating it as one lands a seek tens of frames from where it was
        /// asked for.
        /// </summary>
        public long FrameNumberAt(int index)
        {
            // Compressed, the index carries the numbers: reading one back out of the file would mean
            // expanding a chunk, which turns a search into a dozen of them.
            if (_chunked)
            {
                if (_frameNumbers == null || index < 0 || index >= _frameNumbers.Length) return -1;

                return _frameNumbers[index];
            }

            if (_frameOffsets == null || index < 0 || index >= _frameOffsets.Length) return -1;

            var restore = _stream.Position;
            try
            {
                return _ReadFrameNumberAt(_frameOffsets[index]);
            }
            finally
            {
                _stream.Position = restore;
            }
        }

        /// <summary>
        /// Where a frame sits in the index, or -1 when the recording does not hold that frame.
        ///
        /// A binary search rather than a table built at open: frame numbers only ever increase down
        /// the file, so finding one costs a dozen small reads instead of a walk over every frame --
        /// which is the walk the index exists to avoid.
        /// </summary>
        public int IndexOfFrame(long frameNumber)
        {
            if (_chunked)
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

            if (_frameOffsets == null) return -1;

            var restore = _stream.Position;
            try
            {
                var low = 0;
                var high = _frameOffsets.Length - 1;

                while (low <= high)
                {
                    var mid = low + ((high - low) >> 1);
                    var value = _ReadFrameNumberAt(_frameOffsets[mid]);

                    // A boundary that cannot be read means the file has been cut into rather than
                    // that it is missing a frame, and guessing a direction from it would send the
                    // search off into the part that is still intact.
                    if (value < 0) return -1;

                    if (value == frameNumber) return mid;

                    if (value < frameNumber) low = mid + 1;
                    else high = mid - 1;
                }
            }
            finally
            {
                _stream.Position = restore;
            }

            return -1;
        }

        /// <summary>
        /// The frame number of the frame that starts at an offset.
        ///
        /// Scans forward for the boundary rather than reading straight off the offset: an index entry
        /// points at where the frame's writing began, and the mapping-table growth of that frame goes
        /// out ahead of its boundary. Usually there is none and the boundary is the first entry.
        /// </summary>
        private long _ReadFrameNumberAt(long offset)
        {
            var limit = _frameOffsets != null ? _tailOffset : _stream.Length;
            if (offset < _entriesOffset || offset >= limit) return -1;

            _stream.Position = offset;

            while (_stream.Position < limit)
            {
                var kind = (FrameEntryKind)_reader.ReadByte();
                var length = _reader.ReadInt32();
                var frameNumber = _reader.ReadInt64();

                if (kind == FrameEntryKind.FrameBoundary) return frameNumber;
                if (length < 0 || _stream.Position + length > limit) return -1;

                _stream.Position += length;
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
        /// Reads the next entry. False at the end of the entries, which is either the footer or the
        /// end of the file.
        /// </summary>
        public bool TryReadEntry(out FrameEntry entry)
        {
            entry = default;

            if (_chunked) return _TryReadChunkedEntry(out entry);

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

        /// <summary>
        /// Reads the next entry out of the expanded chunk, opening the following one when this chunk
        /// runs out. A chunk that cannot be expanded ends the walk the way a cut entry does.
        /// </summary>
        private bool _TryReadChunkedEntry(out FrameEntry entry)
        {
            entry = default;

            while (_chunkCursor >= _chunkLength)
            {
                if (!_LoadNextChunk()) return false;
            }

            const int kEntryHeader = 1 + 4 + 8;
            if (_chunkCursor + kEntryHeader > _chunkLength) return false;

            var data = _chunkData;
            var at = _chunkCursor;

            var kind = (FrameEntryKind)data[at];
            var length = data[at + 1] | (data[at + 2] << 8) | (data[at + 3] << 16) | (data[at + 4] << 24);
            var frameNumber = BitConverter.ToInt64(data, at + 5);

            at += kEntryHeader;
            if (length < 0 || at + length > _chunkLength) return false;

            _chunkCursor = at + length;
            entry = new FrameEntry(kind, frameNumber, new ReadOnlySpan<byte>(data, at, length));
            return true;
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
            if (compressed <= 0 || expanded <= 0) return false;
            if (_stream.Position + compressed > limit) return false;

            if (_chunkRaw.Length < compressed) _chunkRaw = new byte[Math.Max(compressed, 1024)];
            if (_reader.Read(_chunkRaw, 0, compressed) != compressed) return false;

            _chunkLength = _codec.Decode(new ReadOnlySpan<byte>(_chunkRaw, 0, compressed), expanded, out _chunkData);
            _chunkCursor = 0;
            _chunkIndex = index;
            return true;
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

            if (_chunked)
            {
                _frameNumbers = new long[frameCount];
                _frameChunks = new int[frameCount];
                _frameCursors = new int[frameCount];

                for (int i = 0; i < frameCount; i++)
                {
                    _frameNumbers[i] = _reader.ReadInt64();
                    _frameChunks[i] = _reader.ReadInt32();
                    _frameCursors[i] = _reader.ReadInt32();
                }

                var chunkCount = _reader.ReadInt32();
                if (chunkCount < 0) return null;

                _chunkStarts.Clear();
                for (int i = 0; i < chunkCount; i++) _chunkStarts.Add(_reader.ReadInt64());
            }
            else
            {
                var offsets = new long[frameCount];
                for (int i = 0; i < frameCount; i++) offsets[i] = _reader.ReadInt64();
                _frameOffsets = offsets;
            }

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
