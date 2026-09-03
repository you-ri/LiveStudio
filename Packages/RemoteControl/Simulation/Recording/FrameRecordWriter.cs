// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Collections.LowLevel.Unsafe;

namespace Lilium.RemoteControl.Frames.Recording
{
    /// <summary>
    /// Writes frames out as one time-ordered stream of self-describing entries.
    ///
    /// Every entry says its kind and its length, which is what makes a file readable after the
    /// process that was writing it died: without a length nothing can step over an entry it does
    /// not understand, and the tail index is exactly what a crash takes away.
    ///
    /// Nothing is buffered across frames. <see cref="EndFrame"/> leaves the file consistent, so the
    /// worst a crash costs is the frame in progress.
    /// </summary>
    public sealed class FrameRecordWriter : IDisposable
    {
        /// <summary>Where entries go. The file itself, or the chunk being built up.</summary>
        private readonly BinaryWriter _writer;

        /// <summary>The file. Chunk headers and the tail go here whatever <see cref="_writer"/> is.</summary>
        private readonly BinaryWriter _sink;

        // Held rather than reached for through the writer: BinaryWriter drops its reference to the
        // stream when it is disposed, so BaseStream is null by the time this would need it.
        private readonly Stream _stream;
        private readonly bool _ownsStream;

        /// <summary>
        /// Where a frame's first entry sits. Uncompressed that is an offset in the file; compressed
        /// it is an offset inside a chunk, which is why the chunk has to be named alongside it.
        /// </summary>
        private struct FrameMark
        {
            public long frameNumber;
            public int chunk;
            public long offset;
        }

        private readonly List<FrameMark> _frames = new List<FrameMark>();

        // Compression. When it is on, entries are built up in memory and go out a chunk at a time
        // instead of straight to the stream; everything that writes an entry is unaware of this,
        // because only where _writer points changes.
        private readonly bool _chunked;
        private readonly FrameChunkCodec _codec;
        private readonly MemoryStream _pending;
        private readonly List<long> _chunkOffsets = new List<long>();
        private int _frameStart;

        // Frames that carried the inventory. These are the frames a seek can land on and know the
        // shape of the world.
        private readonly List<long> _keyframes = new List<long>();

        // Symbols already written out. The table only ever grows, so catching up is a matter of
        // writing everything past this mark.
        private int _symbolsWritten;

        // Epoch of the inventory as last written, so an unchanged structure is not written again.
        private long _structureEpoch = -1;

        // Prefixes an event's target must not start with to be written, two per excluded id
        // (its properties and its functions). Rebuilt only when the caller's list changes.
        private string[] _excludePrefixes;
        private string[] _excludeIds;

        private long _firstFrameNumber = -1;
        private long _currentFrameNumber = -1;
        private bool _closed;

        /// <summary>Frames written so far.</summary>
        public int frameCount => _frames.Count;

        /// <summary>Frames that carried the inventory, in order.</summary>
        public IReadOnlyList<long> keyframes => _keyframes;

        /// <summary>Chunks closed so far, or zero when this recording is not compressed.</summary>
        public int chunkCount => _chunkOffsets.Count;

        /// <summary>Bytes written so far, counting whatever is still waiting to be compressed.</summary>
        public long length => _stream.Position + (_pending?.Length ?? 0);

        /// <summary>
        /// Opens a recording.
        ///
        /// <paramref name="chunked"/> compresses the entries, which costs about a second of the take
        /// if the process dies -- a chunk is only on disk once it is closed -- and buys roughly five
        /// times the file size back. It is off by default because a recording being written is also
        /// something another process can follow along, and an open chunk cannot be followed.
        /// </summary>
        public FrameRecordWriter(Stream stream, in FrameRecordHeader header, bool leaveOpen = false,
                                 bool chunked = false)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanWrite) throw new ArgumentException("Stream is not writable.", nameof(stream));

            _stream = stream;
            _ownsStream = !leaveOpen;
            _chunked = chunked;

            _sink = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

            _sink.Write(FrameRecordFormat.kMagic);
            _sink.Write(FrameRecordFormat.kVersion);
            _sink.Write(header.frameRate.numerator);
            _sink.Write(header.frameRate.denominator);
            _sink.Write(header.startTicks);
            _sink.Write(header.engineId ?? string.Empty);
            _sink.Write(header.buildId ?? string.Empty);
            _sink.Write(chunked ? (byte)1 : (byte)0);

            if (chunked)
            {
                _codec = new FrameChunkCodec();
                _pending = new MemoryStream();
                _writer = new BinaryWriter(_pending, Encoding.UTF8, leaveOpen: true);
            }
            else
            {
                _writer = _sink;
            }
        }

        /// <summary>
        /// Closes the chunk that ends where the frame now opening began, and carries whatever has
        /// already been written for that frame over to the front of the next one.
        ///
        /// Called when a keyframe is written, so chunk starts and keyframes are the same places: a
        /// seek lands on a keyframe, and expanding the chunk that opens there is all it costs.
        /// </summary>
        private void _CutChunkBeforeCurrentFrame()
        {
            if (!_chunked || _frameStart <= 0) return;

            var buffer = _pending.GetBuffer();
            var total = (int)_pending.Length;

            _FlushChunk(new ReadOnlySpan<byte>(buffer, 0, _frameStart));

            var carried = total - _frameStart;
            Buffer.BlockCopy(buffer, _frameStart, buffer, 0, carried);
            _pending.SetLength(carried);
            _pending.Position = carried;

            var mark = _frames[_frames.Count - 1];
            mark.chunk = _chunkOffsets.Count;
            mark.offset = 0;
            _frames[_frames.Count - 1] = mark;

            _frameStart = 0;
        }

        /// <summary>
        /// Compresses a run of entries and writes it out.
        ///
        /// Length-prefixed the way an entry is, and for the same reason: a reader that does not have
        /// the tail can still step through the chunks, and a chunk a crash cut in half can be told
        /// from a whole one instead of being expanded into nonsense.
        /// </summary>
        private void _FlushChunk(ReadOnlySpan<byte> entries)
        {
            if (entries.Length == 0) return;

            var compressed = _codec.Encode(entries, out var body);

            _chunkOffsets.Add(_stream.Position);
            _sink.Write(compressed);
            _sink.Write(entries.Length);
            _sink.Write(body, 0, compressed);
        }

        /// <summary>
        /// Opens a frame and writes its boundary entry. Any mapping-table growth since the last
        /// frame goes out first, so every id an entry refers to has already been named.
        /// </summary>
        public void BeginFrame(in Frame frame, FrameSymbolTable symbols)
        {
            _RequireOpen();
            if (_currentFrameNumber >= 0) throw new InvalidOperationException("A frame is already open.");

            _currentFrameNumber = frame.frameNumber;
            if (_firstFrameNumber < 0) _firstFrameNumber = frame.frameNumber;

            // Noted before the symbols rather than after: a frame's mapping-table growth goes out
            // ahead of its boundary, and a seek that landed past it would meet ids it cannot resolve.
            _frameStart = _chunked ? (int)_pending.Length : 0;
            _frames.Add(new FrameMark
            {
                frameNumber = frame.frameNumber,
                chunk = _chunked ? _chunkOffsets.Count : -1,
                offset = _chunked ? _frameStart : _stream.Position,
            });

            _WriteSymbolsSince(symbols);

            _BeginEntry(FrameEntryKind.FrameBoundary, 4 + 4);
            _writer.Write(frame.frameRate.numerator);
            _writer.Write(frame.frameRate.denominator);
        }

        /// <summary>
        /// Writes the inventory. Skipped when it has not moved, unless <paramref name="force"/> --
        /// which is how a keyframe is made: the same inventory written again so a seek landing here
        /// does not have to walk back for it.
        /// </summary>
        public void WriteStructure(StructureBlock structure, FrameSymbolTable symbols, bool force = false)
        {
            _RequireFrame();
            _WriteSymbolsSince(symbols);

            if (structure == null) return;
            if (!force && structure.epoch == _structureEpoch) return;

            _structureEpoch = structure.epoch;
            _keyframes.Add(_currentFrameNumber);
            _CutChunkBeforeCurrentFrame();

            var count = structure.count;
            _BeginEntry(FrameEntryKind.Structure, 8 + 4 + count * (4 + 4 + 4 + 4));
            _writer.Write(structure.epoch);
            _writer.Write(count);

            for (int i = 0; i < count; i++)
            {
                var entry = structure[i];
                _writer.Write(entry.id);
                _writer.Write(entry.typeId);
                _writer.Write(entry.parentId);
                _writer.Write(entry.recipeId);
            }
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

            // Named first, written second. The writer is the only thing that interns type names, so
            // doing it inline would put an id into an entry one frame before the entry that names
            // it -- and a reader walking forward would meet a reference it cannot resolve.
            for (int i = 0; i < blocks.Count; i++)
            {
                if (blocks[i].count == 0) continue;

                symbols.Intern(blocks[i].elementType.FullName);
            }

            _WriteSymbolsSince(symbols);

            for (int i = 0; i < blocks.Count; i++)
            {
                var block = blocks[i];
                if (block.count == 0) continue;

                var typeId = symbols.Intern(block.elementType.FullName);
                var bytes = block.AsBytes();

                // The width and the layout, because the width alone was never enough: two builds
                // that disagree about the order of two float members produce elements of the same
                // size, and reading one as the other puts each value in the wrong member without
                // anything noticing. Zero when nothing declared a layout for this type.
                var layoutHash = StateLayoutRegistry.HashFor(block.elementType.FullName);

                _BeginEntry(FrameEntryKind.State, 4 + 4 + 4 + 8 + bytes.Length);
                _writer.Write(typeId);
                _writer.Write(block.elementSize);
                _writer.Write(block.count);
                _writer.Write(layoutHash);
                _writer.Write(bytes);
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
        public unsafe void WriteEvents(EventFrame events, FrameSymbolTable symbols,
            string[] excludeObjectIds = null)
        {
            _RequireFrame();
            _WriteSymbolsSince(symbols);

            if (events == null) return;

            _UpdateExclusion(excludeObjectIds);

            for (int i = 0; i < events.eventCount; i++)
            {
                var record = events[i];

                if (_IsExcluded(record.targetId, symbols)) continue;

                // The payload is a fixed-size buffer but only its used length is stored: a record
                // costs the whole buffer in memory and whatever it actually says on disk. A typed
                // value is usually a handful of bytes, so most records cost almost nothing here.
                var payloadLength = record.payloadLength;

                _BeginEntry(FrameEntryKind.Event, 8 + 4 + 4 + 4 + 4 + 4 + 1 + 4 + payloadLength);
                _writer.Write(record.sequence);
                _writer.Write((int)record.kind);
                _writer.Write(record.sourceId);
                _writer.Write(record.targetId);
                _writer.Write(record.verbId);
                _writer.Write(record.payloadTypeId);
                _writer.Write((byte)record.flags);
                _writer.Write(payloadLength);

                // Written as one block, raw. The record is a local copy, so its buffer sits on the
                // stack -- already pinned, and the pointer cannot be moved out from under this call.
                _writer.Write(new ReadOnlySpan<byte>(record.payload, payloadLength));
            }
        }

        /// <summary>Closes the frame. The file is consistent from here until the next one opens.</summary>
        public void EndFrame()
        {
            _RequireFrame();
            _currentFrameNumber = -1;
        }

        /// <summary>
        /// Writes the tail and closes: the frame index, the complete mapping table, and a footer
        /// pointing at both.
        ///
        /// All of it can be rebuilt by walking the entries, which is the point -- a file that never
        /// got here is still readable, just without the shortcuts.
        /// </summary>
        public void Close(FrameSymbolTable symbols)
        {
            if (_closed) return;
            if (_currentFrameNumber >= 0) EndFrame();

            if (_chunked)
            {
                _writer.Flush();
                _FlushChunk(new ReadOnlySpan<byte>(_pending.GetBuffer(), 0, (int)_pending.Length));
                _pending.SetLength(0);
            }

            var indexOffset = _stream.Position;
            _sink.Write(_firstFrameNumber < 0 ? 0 : _firstFrameNumber);
            _sink.Write(_frames.Count);

            // Uncompressed, a frame is found at a file offset and nothing else is needed. Compressed,
            // the offset is inside a chunk, so the chunk comes with it -- and the frame number is
            // written out rather than read back from the file, because reading one now means
            // expanding a chunk for every step of a search that used to be a dozen small reads.
            if (_chunked)
            {
                for (int i = 0; i < _frames.Count; i++)
                {
                    _sink.Write(_frames[i].frameNumber);
                    _sink.Write(_frames[i].chunk);
                    _sink.Write((int)_frames[i].offset);
                }

                _sink.Write(_chunkOffsets.Count);
                for (int i = 0; i < _chunkOffsets.Count; i++) _sink.Write(_chunkOffsets[i]);
            }
            else
            {
                for (int i = 0; i < _frames.Count; i++) _sink.Write(_frames[i].offset);
            }

            var keyframeOffset = _stream.Position;
            _sink.Write(_keyframes.Count);
            for (int i = 0; i < _keyframes.Count; i++) _sink.Write(_keyframes[i]);

            var mappingOffset = _stream.Position;
            var count = symbols?.count ?? 0;
            _sink.Write(count);
            for (int i = 0; i < count; i++) _sink.Write(symbols.Resolve(i));

            _sink.Write(indexOffset);
            _sink.Write(keyframeOffset);
            _sink.Write(mappingOffset);
            _sink.Write(FrameRecordFormat.kFooterMagic);

            _sink.Flush();
            _closed = true;
        }

        public void Dispose()
        {
            _writer.Flush();
            if (!ReferenceEquals(_writer, _sink)) _writer.Dispose();

            _sink.Flush();
            _sink.Dispose();

            _pending?.Dispose();

            if (_ownsStream) _stream.Dispose();
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

        private void _WriteSymbolsSince(FrameSymbolTable symbols)
        {
            if (symbols == null) return;

            var count = symbols.count;
            for (int id = _symbolsWritten; id < count; id++)
            {
                var value = symbols.Resolve(id);
                var bytes = Encoding.UTF8.GetByteCount(value);

                _BeginEntry(FrameEntryKind.Symbol, 4 + 4 + bytes);
                _writer.Write(id);
                _writer.Write(bytes);
                _writer.Write(Encoding.UTF8.GetBytes(value));
            }

            _symbolsWritten = count;
        }

        private void _BeginEntry(FrameEntryKind kind, int payloadLength)
        {
            _writer.Write((byte)kind);
            _writer.Write(payloadLength);
            _writer.Write(_currentFrameNumber);
        }

        private void _RequireOpen()
        {
            if (_closed) throw new InvalidOperationException("The recording is closed.");
        }

        private void _RequireFrame()
        {
            _RequireOpen();
            if (_currentFrameNumber < 0) throw new InvalidOperationException("No frame is open.");
        }
    }
}
