// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Lilium.RemoteControl.Frames.Recording
{
    /// <summary>
    /// Writes frames out as one time-ordered stream of self-describing entries, compressed a chunk
    /// at a time.
    ///
    /// The frame thread only appends: every entry goes into a native buffer, with nothing allocated
    /// and nothing waited on. A chunk -- the entries from one keyframe to the next -- is handed off
    /// whole to a thread of its own, which transposes it, deflates it and puts it on disk.
    /// Compression and the disk are the two costs in a recording the frame has no control over, so
    /// neither of them is on the frame.
    ///
    /// Every entry says its kind and its length, which is what makes a file readable after the
    /// process writing it died: without a length nothing can step over an entry it does not
    /// understand, and the tail index is exactly what a crash takes away. What a crash costs is the
    /// chunk in progress -- up to a keyframe interval.
    /// </summary>
    public sealed unsafe class FrameRecordWriter : IDisposable
    {
        /// <summary>Bytes one inventory entry occupies on disk. See <see cref="ObjectEntry"/>.</summary>
        internal const int kEntrySize = 4 * 7;

        /// <summary>
        /// A chunk this long is closed at the next frame even without a keyframe, so a take that
        /// never has one does not hold the whole recording in memory.
        /// </summary>
        private const int kMaxChunkBytes = 4 * 1024 * 1024;

        /// <summary>Where a frame's boundary sits: which chunk, and how far into it.</summary>
        private struct FrameMark
        {
            public long frameNumber;
            public int chunk;
            public int offset;
        }

        private readonly Stream _stream;
        private readonly bool _ownsStream;
        private readonly ChunkWriter _chunks;

        // The chunk being built on the frame thread, which chunk it will be, and where the open
        // frame began in it.
        private UnsafeList<byte> _chunk;
        private int _chunkIndex;
        private int _frameStart;

        private UnsafeList<FrameMark> _frames;
        private UnsafeList<long> _keyframes;

        // Symbols already written out. The table only ever grows, so catching up is a matter of
        // writing everything past this mark.
        private int _symbolsWritten;

        // Epoch of the inventory as last written, so an unchanged structure is not written again.
        private long _structureEpoch = -1;

        // Ids a frame's state blocks were named with, kept between naming them and writing them.
        private int[] _blockTypeIds = new int[8];
        private int[] _blockSchemaIds = new int[8];

        // Prefixes an event's target must not start with to be written, two per excluded id
        // (its properties and its functions). Rebuilt only when the caller's list changes.
        private string[] _excludePrefixes;
        private string[] _excludeIds;

        private long _firstFrameNumber = -1;
        private long _currentFrameNumber = -1;
        private long _entryBytes;
        private bool _closed;
        private bool _disposed;

        /// <summary>Frames written so far.</summary>
        public int frameCount => _frames.Length;

        /// <summary>Frames that carried the inventory so far.</summary>
        public int keyframeCount => _keyframes.Length;

        /// <summary>Chunks on disk so far.</summary>
        public int chunkCount => _chunks.writtenCount;

        /// <summary>
        /// Bytes on disk so far, plus what is waiting to be compressed. An estimate while recording
        /// (the open chunk counts at its raw size) and exact once closed.
        /// </summary>
        public long length => _chunks.writtenBytes + _chunk.Length;

        /// <summary>
        /// Bytes of entries written, before compression: what the format costs, as opposed to what
        /// the content happens to compress to.
        /// </summary>
        public long entryBytes => _entryBytes;

        public FrameRecordWriter(Stream stream, in FrameRecordHeader header, bool leaveOpen = false)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanWrite) throw new ArgumentException("Stream is not writable.", nameof(stream));

            _stream = stream;
            _ownsStream = !leaveOpen;

            // On this thread and before the chunk thread exists: from here on only that thread
            // touches the stream until the tail goes on.
            _WriteHeader(stream, in header);

            _chunk = new UnsafeList<byte>(64 * 1024, Allocator.Persistent);
            _frames = new UnsafeList<FrameMark>(1024, Allocator.Persistent);
            _keyframes = new UnsafeList<long>(64, Allocator.Persistent);
            _chunks = new ChunkWriter(stream);
        }

        /// <summary>
        /// Opens a frame and writes its boundary entry, then any mapping-table growth since the last
        /// frame -- after the boundary, so a seek that lands on the boundary meets every id before
        /// anything uses it.
        /// </summary>
        public void BeginFrame(in Frame frame, FrameSymbolTable symbols)
        {
            _RequireOpen();
            _chunks.ThrowIfFailed();
            if (_currentFrameNumber >= 0) throw new InvalidOperationException("A frame is already open.");

            // Closed between frames rather than in the middle of one, so a chunk always starts on a
            // boundary.
            if (_chunk.Length >= kMaxChunkBytes) _CloseChunk(_chunk.Length);

            _currentFrameNumber = frame.frameNumber;
            if (_firstFrameNumber < 0) _firstFrameNumber = frame.frameNumber;

            _frameStart = _chunk.Length;
            _frames.Add(new FrameMark
            {
                frameNumber = frame.frameNumber,
                chunk = _chunkIndex,
                offset = _frameStart,
            });

            _BeginEntry(FrameEntryKind.FrameBoundary, FrameRecordFormat.kBoundarySize);
            _Write(frame.frameNumber);
            _Write((uint)frame.frameRate.numerator);
            _Write((uint)frame.frameRate.denominator);

            _WriteSymbolsSince(symbols);
        }

        /// <summary>
        /// Writes the inventory. Skipped when it has not moved, unless <paramref name="force"/> --
        /// which is how a keyframe is made: the same inventory written again so a seek landing here
        /// does not have to walk back for it.
        ///
        /// A keyframe also starts a chunk, so a seek to one costs expanding the one chunk that
        /// begins there.
        /// </summary>
        public void WriteStructure(StructureBlock structure, FrameSymbolTable symbols, bool force = false)
        {
            _RequireFrame();
            _WriteSymbolsSince(symbols);

            if (structure == null) return;
            if (!force && structure.epoch == _structureEpoch) return;

            _structureEpoch = structure.epoch;
            _keyframes.Add(_currentFrameNumber);
            _CutChunkAtCurrentFrame();

            var bytes = structure.AsBytes();
            _BeginEntry(FrameEntryKind.Structure, 8 + 4 + bytes.Length);
            _Write(structure.epoch);
            _Write(structure.count);

            // The inventory as it sits in memory: seven ints an entry, in the order ObjectEntry
            // declares them, which is the order a reader takes them back in.
            _Write(bytes);
        }

        /// <summary>
        /// Writes every state block that has anything in it. Blocks go out in the order the set
        /// lists them, which is the order they were first created, so a run lays its types out the
        /// same way each time.
        /// </summary>
        public void WriteState(StateBlockSet state, FrameSymbolTable symbols)
        {
            _RequireFrame();
            if (state == null) return;

            var blocks = state.blocks;
            if (_blockTypeIds.Length < blocks.Count)
            {
                Array.Resize(ref _blockTypeIds, Math.Max(blocks.Count, _blockTypeIds.Length * 2));
                Array.Resize(ref _blockSchemaIds, _blockTypeIds.Length);
            }

            // Named first, written second. The writer is the only thing that interns type names, so
            // doing it inline would put an id into an entry before the entry that names it.
            //
            // The description of the block goes into the same table as its name, and for the same
            // reasons the table exists: it only ever grows, it is written into the stream as it
            // grows so a take a crash cut short still resolves what it reached, and the tail carries
            // it in full so a seek can adopt it whole.
            for (int i = 0; i < blocks.Count; i++)
            {
                var block = blocks[i];
                if (block.count == 0) continue;

                _blockTypeIds[i] = symbols.Intern(block.typeName);

                var schema = StateTypes.FindSchema(block.typeName);
                _blockSchemaIds[i] = schema == null ? FrameSymbolTable.kNone : symbols.Intern(schema.ToText());
            }

            _WriteSymbolsSince(symbols);

            for (int i = 0; i < blocks.Count; i++)
            {
                var block = blocks[i];
                if (block.count == 0) continue;

                var bytes = block.AsBytes();

                // What the block holds, member by member. The width beside it is not redundant: it
                // is what lets a reader step over the entry, and what the chunk codec transposes by,
                // neither of which can wait for a description that may have been named in an
                // earlier chunk. kNone when nothing described this type -- a producer that
                // hand-registers a struct still travels on width alone.
                _BeginEntry(FrameEntryKind.State, 4 + 4 + 4 + 4 + bytes.Length);
                _Write(_blockTypeIds[i]);
                _Write(block.elementSize);
                _Write(block.count);
                _Write(_blockSchemaIds[i]);
                _Write(bytes);
            }
        }

        /// <summary>
        /// Writes the events applied at this frame's head, in the order they were applied.
        ///
        /// <paramref name="excludeObjectIds"/> names exposed objects whose events are left out.
        /// This is for whatever drives the recording: its buttons are not part of the world being
        /// recorded, and keeping them means a replay presses them again -- starting a recording, or
        /// tearing down the replay that is running. It takes a list because the controls and the
        /// machinery behind them are usually two separate exposed objects.
        /// </summary>
        public void WriteEvents(EventFrame events, FrameSymbolTable symbols,
            string[] excludeObjectIds = null)
        {
            _RequireFrame();
            _WriteSymbolsSince(symbols);

            if (events == null) return;

            _UpdateExclusion(excludeObjectIds);

            for (int i = 0; i < events.eventCount; i++)
            {
                ref readonly var record = ref events[i];

                if (_IsExcluded(record.targetId, symbols)) continue;

                var payload = events.PayloadOf(in record);

                _BeginEntry(FrameEntryKind.Event, 8 + 4 + 4 + 4 + 4 + 4 + 1 + 4 + payload.Length);
                _Write(record.sequence);
                _Write((int)record.kind);
                _Write(record.sourceId);
                _Write(record.targetId);
                _Write(record.verbId);
                _Write(record.payloadTypeId);
                *_Reserve(1) = (byte)record.flags;
                _Write(payload.Length);
                _Write(payload);
            }
        }

        /// <summary>Closes the frame.</summary>
        public void EndFrame()
        {
            _RequireFrame();
            _currentFrameNumber = -1;
        }

        /// <summary>
        /// Waits until every chunk handed off so far is on disk. The open chunk is not among them:
        /// this is the file as a crash right now would leave it. For tests.
        /// </summary>
        internal void WaitForWrittenChunks() => _chunks.WaitIdle();

        /// <summary>
        /// Writes the last chunk, waits for every chunk to reach the disk, then writes the tail and
        /// closes: the frame index, the chunk offsets, the keyframes, the complete mapping table,
        /// and a footer pointing at them.
        ///
        /// All of it can be rebuilt by walking the chunks, which is the point -- a file that never
        /// got here is still readable, just without the shortcuts.
        /// </summary>
        public void Close(FrameSymbolTable symbols)
        {
            if (_closed) return;
            if (_currentFrameNumber >= 0) EndFrame();

            _CloseChunk(_chunk.Length);
            _chunks.Finish();

            _WriteTail(symbols);
            _closed = true;
        }

        /// <summary>
        /// Releases the writer. Without <see cref="Close"/> the chunk in progress is still written,
        /// but no tail: what a writer that was abandoned rather than finished leaves behind is a file
        /// that reads from the top and cannot be sought.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                if (!_closed)
                {
                    _currentFrameNumber = -1;
                    _CloseChunk(_chunk.Length);
                    _chunks.Finish(throwOnFailure: false);
                }
            }
            finally
            {
                _chunks.Dispose();

                if (_chunk.IsCreated) _chunk.Dispose();
                if (_frames.IsCreated) _frames.Dispose();
                if (_keyframes.IsCreated) _keyframes.Dispose();

                _stream.Flush();
                if (_ownsStream) _stream.Dispose();
            }
        }

        // ---------------------------------------------------------------- chunks

        /// <summary>
        /// Starts a new chunk at the open frame, so the chunk and the keyframe begin in the same
        /// place. What the frame has written so far moves with it.
        /// </summary>
        private void _CutChunkAtCurrentFrame()
        {
            if (_frameStart <= 0) return;

            _CloseChunk(_frameStart);

            ref var mark = ref _frames.ElementAt(_frames.Length - 1);
            mark.chunk = _chunkIndex;
            mark.offset = 0;

            _frameStart = 0;
        }

        /// <summary>
        /// Hands the bytes before <paramref name="end"/> to the chunk thread and carries whatever
        /// follows into a fresh buffer. Nothing is copied but the carried part, which is the open
        /// frame's first few entries at most.
        /// </summary>
        private void _CloseChunk(int end)
        {
            if (end <= 0) return;

            var next = _chunks.Rent();
            var carried = _chunk.Length - end;

            if (carried > 0)
            {
                _Grow(ref next, carried);
                next.Resize(carried);
                UnsafeUtility.MemCpy(next.Ptr, _chunk.Ptr + end, carried);
            }

            _chunk.Resize(end);
            _chunks.Enqueue(_chunk);

            _chunk = next;
            _chunkIndex++;
        }

        // ---------------------------------------------------------------- entries

        private void _BeginEntry(FrameEntryKind kind, int payloadLength)
        {
            var header = _Reserve(FrameRecordFormat.kEntryHeaderSize);
            header[0] = (byte)kind;
            *(int*)(header + 1) = payloadLength;

            _entryBytes += FrameRecordFormat.kEntryHeaderSize + payloadLength;
        }

        private void _Write(int value) => *(int*)_Reserve(4) = value;

        private void _Write(uint value) => *(uint*)_Reserve(4) = value;

        private void _Write(long value) => *(long*)_Reserve(8) = value;

        private void _Write(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length == 0) return;

            bytes.CopyTo(new Span<byte>(_Reserve(bytes.Length), bytes.Length));
        }

        /// <summary>Makes room at the end of the open chunk and hands back where it starts.</summary>
        private byte* _Reserve(int bytes)
        {
            var at = _chunk.Length;
            _Grow(ref _chunk, at + bytes);
            _chunk.Resize(at + bytes);
            return _chunk.Ptr + at;
        }

        // Doubling, so a chunk buffer that has reached its size stops reallocating.
        private static void _Grow(ref UnsafeList<byte> list, int required)
        {
            if (required > list.Capacity) list.SetCapacity(Math.Max(required, list.Capacity * 2));
        }

        private void _WriteSymbolsSince(FrameSymbolTable symbols)
        {
            if (symbols == null) return;

            var count = symbols.count;
            for (int id = _symbolsWritten; id < count; id++)
            {
                var value = symbols.Resolve(id);
                var byteCount = Encoding.UTF8.GetByteCount(value);

                _BeginEntry(FrameEntryKind.Symbol, 4 + 4 + byteCount);
                _Write(id);
                _Write(byteCount);

                if (byteCount == 0) continue;

                var destination = _Reserve(byteCount);
                fixed (char* chars = value)
                {
                    Encoding.UTF8.GetBytes(chars, value.Length, destination, byteCount);
                }
            }

            _symbolsWritten = count;
        }

        private void _UpdateExclusion(string[] excludeObjectIds)
        {
            if (_SameIds(excludeObjectIds)) return;

            _excludeIds = excludeObjectIds;

            var count = 0;
            if (excludeObjectIds != null)
            {
                for (int i = 0; i < excludeObjectIds.Length; i++)
                {
                    if (!string.IsNullOrEmpty(excludeObjectIds[i])) count++;
                }
            }

            if (count == 0)
            {
                _excludePrefixes = null;
                return;
            }

            _excludePrefixes = new string[count * 2];
            var next = 0;
            for (int i = 0; i < excludeObjectIds.Length; i++)
            {
                var id = excludeObjectIds[i];
                if (string.IsNullOrEmpty(id)) continue;

                _excludePrefixes[next++] = "/live/object/" + id + "/";
                _excludePrefixes[next++] = "/live/function/" + id + "/";
            }
        }

        // The caller hands the same array every frame, so identity is the common case. Compared by
        // content as well, because nothing stops a caller from rebuilding it.
        private bool _SameIds(string[] ids)
        {
            if (ReferenceEquals(_excludeIds, ids)) return true;
            if (_excludeIds == null || ids == null) return false;
            if (_excludeIds.Length != ids.Length) return false;

            for (int i = 0; i < ids.Length; i++)
            {
                if (!string.Equals(_excludeIds[i], ids[i], StringComparison.Ordinal)) return false;
            }
            return true;
        }

        private bool _IsExcluded(int targetId, FrameSymbolTable symbols)
        {
            if (_excludePrefixes == null) return false;
            if (!symbols.TryResolve(targetId, out var target)) return false;

            for (int i = 0; i < _excludePrefixes.Length; i++)
            {
                if (target.StartsWith(_excludePrefixes[i], StringComparison.Ordinal)) return true;
            }
            return false;
        }

        // ---------------------------------------------------------------- header and tail

        private static void _WriteHeader(Stream stream, in FrameRecordHeader header)
        {
            using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

            writer.Write(FrameRecordFormat.kMagic);
            writer.Write(FrameRecordFormat.kVersion);
            writer.Write((uint)header.frameRate.numerator);
            writer.Write((uint)header.frameRate.denominator);
            writer.Write(header.startTicks);
            WriteString(writer, header.engineId);
            WriteString(writer, header.buildId);
            writer.Flush();
        }

        private void _WriteTail(FrameSymbolTable symbols)
        {
            using var writer = new BinaryWriter(_stream, Encoding.UTF8, leaveOpen: true);

            var indexOffset = _stream.Position;
            writer.Write(_firstFrameNumber < 0 ? 0 : _firstFrameNumber);
            writer.Write(_frames.Length);

            // The frame number is written out rather than read back from the file, because reading
            // one would mean expanding a chunk for every step of a search.
            for (int i = 0; i < _frames.Length; i++)
            {
                var mark = _frames[i];
                writer.Write(mark.frameNumber);
                writer.Write(mark.chunk);
                writer.Write(mark.offset);
            }

            var offsets = _chunks.offsets;
            writer.Write(offsets.Count);
            for (int i = 0; i < offsets.Count; i++) writer.Write(offsets[i]);

            var keyframeOffset = _stream.Position;
            writer.Write(_keyframes.Length);
            for (int i = 0; i < _keyframes.Length; i++) writer.Write(_keyframes[i]);

            var mappingOffset = _stream.Position;
            var count = symbols?.count ?? 0;
            writer.Write(count);
            for (int i = 0; i < count; i++) WriteString(writer, symbols.Resolve(i));

            writer.Write(indexOffset);
            writer.Write(keyframeOffset);
            writer.Write(mappingOffset);
            writer.Write(FrameRecordFormat.kFooterMagic);
            writer.Flush();
        }

        /// <summary>
        /// A string as every string in the file is written: its UTF-8 byte count, then the bytes.
        /// Null is written as empty.
        /// </summary>
        internal static void WriteString(BinaryWriter writer, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                writer.Write(0);
                return;
            }

            var bytes = Encoding.UTF8.GetBytes(value);
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }

        private void _RequireOpen()
        {
            if (_closed || _disposed) throw new InvalidOperationException("The recording is closed.");
        }

        private void _RequireFrame()
        {
            _RequireOpen();
            if (_currentFrameNumber < 0) throw new InvalidOperationException("No frame is open.");
        }

        /// <summary>
        /// The thread chunks are compressed and written on.
        ///
        /// Handed whole chunks in order and writes them in that order, so the chunk the frame
        /// thread numbered n is the nth on disk. Buffers come back to a free list once written, so
        /// a recording that has settled into its rhythm reuses the same two or three.
        /// </summary>
        private sealed class ChunkWriter : IDisposable
        {
            private readonly object _gate = new object();
            private readonly Queue<UnsafeList<byte>> _queued = new Queue<UnsafeList<byte>>();
            private readonly Stack<UnsafeList<byte>> _free = new Stack<UnsafeList<byte>>();
            private readonly List<long> _offsets = new List<long>();
            private readonly FrameChunkCodec _codec = new FrameChunkCodec();
            private readonly byte[] _header = new byte[FrameRecordFormat.kChunkHeaderSize];
            private readonly Stream _stream;
            private readonly Thread _thread;

            private bool _finishing;
            private bool _finished;
            private bool _writing;
            private Exception _failure;
            private long _writtenBytes;
            private int _writtenCount;

            public ChunkWriter(Stream stream)
            {
                _stream = stream;
                _writtenBytes = stream.Position;

                _thread = new Thread(_Run) { IsBackground = true, Name = "LiveData chunk writer" };
                _thread.Start();
            }

            public long writtenBytes => Interlocked.Read(ref _writtenBytes);

            public int writtenCount => Volatile.Read(ref _writtenCount);

            /// <summary>Where each chunk starts in the file, in chunk order. Read only after <see cref="Finish"/>.</summary>
            public List<long> offsets => _offsets;

            public UnsafeList<byte> Rent()
            {
                lock (_gate)
                {
                    if (_free.Count > 0) return _free.Pop();
                }

                return new UnsafeList<byte>(64 * 1024, Allocator.Persistent);
            }

            public void Enqueue(UnsafeList<byte> chunk)
            {
                lock (_gate)
                {
                    if (_finished || _finishing)
                    {
                        throw new InvalidOperationException("[RemoteControl] The recording has stopped taking chunks.");
                    }

                    _queued.Enqueue(chunk);
                    Monitor.PulseAll(_gate);
                }
            }

            /// <summary>Waits until nothing is queued and nothing is being written.</summary>
            public void WaitIdle()
            {
                lock (_gate)
                {
                    while ((_queued.Count > 0 || _writing) && !_finished) Monitor.Wait(_gate);
                }
            }

            /// <summary>
            /// Rethrows on the frame thread what went wrong on this one, so a recorder that lost its
            /// disk is detached at the next frame rather than recording into nothing.
            /// </summary>
            public void ThrowIfFailed()
            {
                var failure = Volatile.Read(ref _failure);
                if (failure != null) throw new IOException("[RemoteControl] Writing the recording failed.", failure);
            }

            /// <summary>Waits for every queued chunk to be written, then stops the thread.</summary>
            public void Finish(bool throwOnFailure = true)
            {
                lock (_gate)
                {
                    if (!_finished)
                    {
                        _finishing = true;
                        Monitor.PulseAll(_gate);
                    }
                }

                if (!_finished)
                {
                    _thread.Join();
                    _finished = true;
                }

                if (throwOnFailure) ThrowIfFailed();
            }

            public void Dispose()
            {
                Finish(throwOnFailure: false);

                while (_free.Count > 0) _free.Pop().Dispose();
                while (_queued.Count > 0) _queued.Dequeue().Dispose();
            }

            private void _Run()
            {
                while (true)
                {
                    UnsafeList<byte> chunk;

                    lock (_gate)
                    {
                        while (_queued.Count == 0 && !_finishing) Monitor.Wait(_gate);
                        if (_queued.Count == 0) return;

                        chunk = _queued.Dequeue();
                        _writing = true;
                    }

                    // After a failure the rest is not written: a file with a hole in the middle is
                    // worse than one that ends where the disk gave out.
                    if (Volatile.Read(ref _failure) == null)
                    {
                        try
                        {
                            _Write(chunk);
                        }
                        catch (Exception e)
                        {
                            Volatile.Write(ref _failure, e);
                        }
                    }

                    chunk.Clear();

                    lock (_gate)
                    {
                        _free.Push(chunk);
                        _writing = false;
                        Monitor.PulseAll(_gate);
                    }
                }
            }

            private void _Write(UnsafeList<byte> chunk)
            {
                var entries = new ReadOnlySpan<byte>(chunk.Ptr, chunk.Length);
                var compressed = _codec.Encode(entries, out var body);

                _offsets.Add(_stream.Position);

                _WriteInt32(_header, 0, compressed);
                _WriteInt32(_header, 4, chunk.Length);
                _stream.Write(_header, 0, _header.Length);
                _stream.Write(body, 0, compressed);

                Interlocked.Add(ref _writtenBytes, _header.Length + compressed);
                Interlocked.Increment(ref _writtenCount);
            }

            private static void _WriteInt32(byte[] target, int offset, int value)
            {
                target[offset] = (byte)value;
                target[offset + 1] = (byte)(value >> 8);
                target[offset + 2] = (byte)(value >> 16);
                target[offset + 3] = (byte)(value >> 24);
            }
        }
    }
}
