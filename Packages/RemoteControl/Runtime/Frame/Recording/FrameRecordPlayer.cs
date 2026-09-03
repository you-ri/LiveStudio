// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Lilium.RemoteControl.Frames.Recording
{
    /// <summary>
    /// Plays a recording back into a structure and a set of state blocks.
    ///
    /// What this restores is the state lane: the inventory and the values. The event lane is read
    /// out too, but applying an event means writing to a property or calling a method, which is
    /// somebody else's job -- <see cref="events"/> hands the records over for whoever owns that
    /// pipeline.
    ///
    /// Two ways to move: <see cref="Advance"/> walks forward and builds the mapping table from the
    /// entries as it goes, and <see cref="TrySeek"/> jumps, which needs the tail table because a
    /// jump skips the entries that would have named the ids.
    /// </summary>
    public sealed class FrameRecordPlayer : IDisposable
    {
        private readonly FrameRecordReader _reader;
        private readonly List<string> _symbols = new List<string>();
        private readonly List<EventRecord> _events = new List<EventRecord>();
        private readonly HashSet<string> _reportedUnknownTypes = new HashSet<string>();

        private long _frameNumber = -1;
        private bool _atEnd;

        // Set aside when a frame boundary for the next frame is read: an entry belongs to the frame
        // whose boundary preceded it, so finding the next boundary is how a frame ends.
        private bool _pendingBoundary;
        private long _pendingFrameNumber;

        /// <summary>What the recording says about the run it came from.</summary>
        public FrameRecordHeader header => _reader.header;

        /// <summary>Frame most recently played, or -1 before the first.</summary>
        public long frameNumber => _frameNumber;

        /// <summary>True once there are no more frames.</summary>
        public bool atEnd => _atEnd;

        /// <summary>Inventory as of the current frame.</summary>
        public StructureBlock structure { get; } = new StructureBlock();

        /// <summary>
        /// Values as of the current frame.
        ///
        /// Blocks have to exist before a recording can be played into them: create them with
        /// <see cref="StateBlockSet.GetOrCreate{T}"/> for every type the recording carries. State
        /// for a type nothing has created goes nowhere and is counted in
        /// <see cref="unknownStateTypes"/>.
        /// </summary>
        public StateBlockSet state { get; } = new StateBlockSet();

        /// <summary>Events applied at the current frame's head, in the order they were applied.</summary>
        public IReadOnlyList<EventRecord> events => _events;

        /// <summary>
        /// Type names carried by the recording that nothing here knows how to hold. Not empty means
        /// the replay is missing part of the world rather than reproducing it.
        /// </summary>
        public IReadOnlyCollection<string> unknownStateTypes => _reportedUnknownTypes;

        /// <summary>True when the recording carries a tail index and can be jumped around in.</summary>
        public bool canSeek => _reader.hasIndex;

        /// <summary>Frames that carry the inventory, in order.</summary>
        public IReadOnlyList<long> keyframes => _reader.keyframes;

        /// <summary>
        /// Frames the recording holds, or zero for one that was cut short and carries no index.
        ///
        /// The range a scrub moves through. What a position within it means is
        /// <see cref="FrameNumberAt"/>'s to say -- a frame number is not a position, because a run
        /// that drops below rate skips numbers.
        /// </summary>
        public int frameCount => _reader.indexedFrameCount;

        /// <summary>Frame number the recording starts at. Zero when it carries no index.</summary>
        public long firstFrameNumber => _reader.firstFrameNumber;

        /// <summary>
        /// The frame number at a position within <see cref="frameCount"/>, or -1. What to hand
        /// <see cref="TrySeek"/> to land on the nth frame of a recording.
        /// </summary>
        public long FrameNumberAt(int index) => _reader.FrameNumberAt(index);

        /// <summary>
        /// Where a frame sits within <see cref="frameCount"/>, or -1. The other direction: what to
        /// put a scrubber at for the frame being shown.
        /// </summary>
        public int IndexOfFrame(long frameNumber) => _reader.IndexOfFrame(frameNumber);

        public FrameRecordPlayer(Stream stream, bool leaveOpen = false)
        {
            _reader = new FrameRecordReader(stream, leaveOpen);
        }

        /// <summary>Resolves an id the way the recording meant it, or an empty string.</summary>
        public string Resolve(int id)
        {
            if (id < 0 || id >= _symbols.Count) return string.Empty;

            return _symbols[id] ?? string.Empty;
        }

        /// <summary>
        /// Plays the next frame: applies its structure and state, and collects its events. False at
        /// the end of the recording.
        /// </summary>
        public bool Advance()
        {
            if (_atEnd) return false;

            _events.Clear();

            if (!_pendingBoundary && !_AdvanceToNextBoundary())
            {
                _atEnd = true;
                return false;
            }

            _frameNumber = _pendingFrameNumber;
            _pendingBoundary = false;

            while (_reader.TryReadEntry(out var entry))
            {
                if (entry.kind == FrameEntryKind.FrameBoundary)
                {
                    // The next frame has started. Hold on to it so the next call does not have to
                    // find it again.
                    _pendingBoundary = true;
                    _pendingFrameNumber = entry.frameNumber;
                    return true;
                }

                _Apply(in entry);
            }

            _atEnd = true;
            return true;
        }

        /// <summary>
        /// Jumps to a frame and plays it.
        ///
        /// The mapping table comes from the tail rather than from the entries, because a jump skips
        /// the ones that would have named the ids. The state is whatever that frame carries -- with
        /// no keyframes yet, a jump lands on a frame whose state blocks are complete but whose
        /// structure is only as current as the last time it changed before the jump.
        /// </summary>
        public bool TrySeek(long frame)
        {
            if (!_reader.hasIndex) return false;
            if (!_reader.TrySeekFrame(frame)) return false;

            _AdoptTailSymbols();
            _events.Clear();
            _atEnd = false;
            _pendingBoundary = false;

            if (!_AdvanceToNextBoundary())
            {
                _atEnd = true;
                return false;
            }

            _frameNumber = _pendingFrameNumber;
            _pendingBoundary = false;

            while (_reader.TryReadEntry(out var entry))
            {
                if (entry.kind == FrameEntryKind.FrameBoundary)
                {
                    _pendingBoundary = true;
                    _pendingFrameNumber = entry.frameNumber;
                    return true;
                }

                _Apply(in entry);
            }

            _atEnd = true;
            return true;
        }

        /// <summary>
        /// Jumps to a frame with the shape of the world intact: starts from the last keyframe at or
        /// before it and runs forward to it.
        ///
        /// <see cref="TrySeek"/> lands on the values but inherits whatever inventory the player was
        /// already holding, because only a keyframe carries one. Use this when scrubbing, where the
        /// world may have gained or lost objects between where the player was and where it is going.
        ///
        /// The walk forward is cheap for the state -- each frame overwrites the last -- so the cost
        /// is the distance to the previous keyframe, which is what the interval buys.
        /// </summary>
        public bool TrySeekWithStructure(long frame)
        {
            var keyframe = _reader.KeyframeAtOrBefore(frame);
            if (keyframe < 0) return TrySeek(frame);

            if (!TrySeek(keyframe)) return false;
            while (_frameNumber < frame && !_atEnd)
            {
                if (!Advance()) return false;
            }

            return _frameNumber == frame;
        }

        /// <summary>Goes back to the start. The structure and state built up so far are dropped.</summary>
        public void Rewind()
        {
            _reader.Rewind();
            _symbols.Clear();
            _events.Clear();
            structure.Reset();
            state.Reset();
            _frameNumber = -1;
            _atEnd = false;
            _pendingBoundary = false;
        }

        public void Dispose()
        {
            _reader.Dispose();
            structure.Dispose();
            state.Dispose();
        }

        private bool _AdvanceToNextBoundary()
        {
            while (_reader.TryReadEntry(out var entry))
            {
                if (entry.kind == FrameEntryKind.FrameBoundary)
                {
                    _pendingFrameNumber = entry.frameNumber;
                    return true;
                }

                // Symbols can sit before the first boundary; everything else there belongs to a
                // frame that was already played.
                if (entry.kind == FrameEntryKind.Symbol) _Apply(in entry);
            }

            return false;
        }

        private void _AdoptTailSymbols()
        {
            var table = _reader.symbols;
            if (table == null) return;

            _symbols.Clear();
            for (int i = 0; i < table.Count; i++) _symbols.Add(table[i]);
        }

        private void _Apply(in FrameEntry entry)
        {
            switch (entry.kind)
            {
                case FrameEntryKind.Symbol:
                    _ApplySymbol(entry.payload);
                    break;

                case FrameEntryKind.Structure:
                    _ApplyStructure(entry.payload);
                    break;

                case FrameEntryKind.State:
                    _ApplyState(entry.payload);
                    break;

                case FrameEntryKind.Event:
                    _ApplyEvent(entry.payload);
                    break;
            }
        }

        private void _ApplySymbol(ReadOnlySpan<byte> payload)
        {
            var id = BitConverter.ToInt32(payload.Slice(0, 4));
            var length = BitConverter.ToInt32(payload.Slice(4, 4));
            var value = Encoding.UTF8.GetString(payload.Slice(8, length));

            // Ids are positions, and the writer emits them in order, so a gap would mean a lost
            // entry rather than a sparse table. Filled rather than dropped so later ids still land
            // where they belong.
            while (_symbols.Count < id) _symbols.Add(null);

            if (_symbols.Count == id) _symbols.Add(value);
            else _symbols[id] = value;
        }

        private void _ApplyStructure(ReadOnlySpan<byte> payload)
        {
            var count = BitConverter.ToInt32(payload.Slice(8, 4));

            // Reconciled rather than assigned: what the recording does not list has to go, or an
            // object spawned later in the run would survive a scrub back past its own creation.
            var seen = new HashSet<int>();
            var offset = 12;

            for (int i = 0; i < count; i++)
            {
                var id = BitConverter.ToInt32(payload.Slice(offset, 4));
                var typeId = BitConverter.ToInt32(payload.Slice(offset + 4, 4));
                var parentId = BitConverter.ToInt32(payload.Slice(offset + 8, 4));
                var recipeId = BitConverter.ToInt32(payload.Slice(offset + 12, 4));
                offset += 16;

                structure.AddOrUpdate(id, typeId, parentId, recipeId);
                seen.Add(id);
            }

            for (int i = structure.count - 1; i >= 0; i--)
            {
                var id = structure[i].id;
                if (!seen.Contains(id)) structure.Remove(id);
            }
        }

        private void _ApplyState(ReadOnlySpan<byte> payload)
        {
            var typeId = BitConverter.ToInt32(payload.Slice(0, 4));
            var elementSize = BitConverter.ToInt32(payload.Slice(4, 4));
            var count = BitConverter.ToInt32(payload.Slice(8, 4));

            var typeName = Resolve(typeId);

            // Made on demand rather than required up front. A recording names its types, and any
            // type that has announced itself can be given a block here -- otherwise a take plays
            // into an application that has the producer but has not published that type yet, and
            // the whole lane is dropped with only a warning.
            var block = StateTypeRegistry.EnsureBlock(state, typeName);

            if (block == null)
            {
                if (_reportedUnknownTypes.Add(typeName))
                {
                    Debug.LogWarning(
                        $"[RemoteControl] Recording carries state for '{typeName}', which nothing here holds. " +
                        "Register the type before playing, or that part of the world stays empty.");
                }

                return;
            }

            if (block.elementSize != elementSize)
            {
                // The layout moved, which means the build moved. Refused rather than read as
                // garbage: the bytes would land in the wrong fields and look like a value.
                if (_reportedUnknownTypes.Add(typeName))
                {
                    Debug.LogError(
                        $"[RemoteControl] Recording stores '{typeName}' at {elementSize} bytes but this build " +
                        $"uses {block.elementSize}. The recording is from a different build.");
                }

                return;
            }

            // The case the width cannot see. Two builds that disagree about the order of two
            // members of the same size produce elements that measure alike, and reading one as the
            // other lands each value in the wrong member -- which looks like values, not like an
            // error, and is the one failure worth refusing loudly.
            var layoutHash = BitConverter.ToUInt64(payload.Slice(12, 8));
            if (!StateLayoutRegistry.Matches(typeName, layoutHash))
            {
                if (_reportedUnknownTypes.Add(typeName))
                {
                    Debug.LogError(
                        $"[RemoteControl] Recording stores '{typeName}' with a different layout than this " +
                        "build has. The elements are the same width, so reading them would put each value " +
                        "in the wrong member. The recording is from a different build.");
                }

                return;
            }

            block.ReadFrom(payload.Slice(20), count);
        }

        private void _ApplyEvent(ReadOnlySpan<byte> payload)
        {
            var sequence = BitConverter.ToInt64(payload.Slice(0, 8));
            var kind = (EventKind)BitConverter.ToInt32(payload.Slice(8, 4));
            var sourceId = BitConverter.ToInt32(payload.Slice(12, 4));
            var targetId = BitConverter.ToInt32(payload.Slice(16, 4));
            var verbId = BitConverter.ToInt32(payload.Slice(20, 4));
            var payloadTypeId = BitConverter.ToInt32(payload.Slice(24, 4));
            var flags = (EventFlags)payload[28];
            var payloadLength = BitConverter.ToInt32(payload.Slice(29, 4));

            var record = new EventRecord(sequence, kind, sourceId, targetId, flags, verbId);

            // Copied as bytes, not decoded: what the payload means is the reader's business, and
            // going through text on the way in would round-trip a value that never was one.
            if (payloadLength > 0)
            {
                record.SetPayload(payload.Slice(33, payloadLength), payloadTypeId);
            }

            _events.Add(record);
        }
    }
}
