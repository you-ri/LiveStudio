// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Collections.LowLevel.Unsafe;
using Lilium.RemoteControl.Frames;
using Lilium.RemoteControl.Frames.Recording;

namespace Lilium.RemoteControl.Editor.LiveDataViewer
{
    /// <summary>
    /// A recording on disk, browsed frame by frame.
    ///
    /// Reads the raw entries rather than playing the file through <see cref="FrameRecordPlayer"/>.
    /// The player restores a recording into live blocks, so it needs every recorded type to exist
    /// here at the same size, and it drops the ones that do not -- which is the opposite of what a
    /// viewer is for. A file recorded by another build has to be openable, or the tool that answers
    /// "is this recording right?" is only available when the answer is already yes.
    ///
    /// One walk at open notes where each frame starts, so every later jump is a seek. The frames
    /// themselves are not held: a minute at sixty frames a second is more state than an editor
    /// window has any business keeping, and the file is right there.
    /// </summary>
    internal sealed class LiveDataFileFeed : ILiveDataFeed, IDisposable
    {
        /// <summary>
        /// Bytes an element spends before its value when the type cannot be resolved here:
        /// <c>int ownerId</c>, <c>FrameSource source</c>, <c>long time</c>. The stamp forces
        /// eight-byte alignment, so a value follows at sixteen for any type that does not want more
        /// than that itself. Taken from the type instead whenever this build still has it.
        /// </summary>
        private const int kAssumedMetaSize = 16;

        private sealed class FrameMark
        {
            public long frameNumber;

            /// <summary>Where this frame's boundary entry starts.</summary>
            public long offset;

            /// <summary>
            /// Offset of the last structure entry at or before this frame, or -1.
            ///
            /// The inventory is only written when it moves, so a frame in between carries none and
            /// showing it needs the one that still applies. Noted per frame on the way past rather
            /// than searched for, which is the whole reason the walk happens.
            /// </summary>
            public long structureOffset = -1;
        }

        private readonly List<FrameMark> _frames = new List<FrameMark>();
        private readonly List<string> _symbols = new List<string>();
        private readonly List<InputRow> _inputs = new List<InputRow>();
        private readonly LiveDataSnapshot _snapshot = new LiveDataSnapshot();
        private readonly Dictionary<string, Type> _elementTypes = new Dictionary<string, Type>();

        private FrameRecordReader _reader;
        private long _nextRowId = 1;
        private int _frameIndex = -1;

        public string path { get; private set; } = string.Empty;

        /// <summary>What the file says about the run it came from.</summary>
        public FrameRecordHeader header { get; private set; }

        /// <summary>True when the file carries a tail, so it was closed rather than cut short.</summary>
        public bool isComplete { get; private set; }

        public long version { get; private set; }

        public bool hasFrame => _frameIndex >= 0;

        public bool isAttached => _reader != null;

        public LiveDataSnapshot snapshot => _snapshot;

        public int inputCount => _inputs.Count;

        public InputRow GetInput(int index) => _inputs[index];

        /// <summary>A file's inputs are the frame's. Clearing them would only hide what it holds.</summary>
        public void ClearInputs() { }

        public string selectedType { get; private set; }

        public int selectedOwnerId { get; private set; } = InputSymbolTable.kNone;

        public void Select(string typeName, int ownerId)
        {
            selectedType = typeName;
            selectedOwnerId = ownerId;

            // Re-read rather than waiting for the next frame: a file does not produce one, so a
            // selection that only took effect on the next frame would never take effect at all.
            _Load();
        }

        public int frameCount => _frames.Count;

        public int frameIndex => _frameIndex;

        public string label => string.IsNullOrEmpty(path) ? string.Empty : Path.GetFileName(path);

        public void Seek(int index)
        {
            if (_frames.Count == 0) return;

            index = Math.Max(0, Math.Min(index, _frames.Count - 1));
            if (index == _frameIndex) return;

            _frameIndex = index;
            _Load();
        }

        /// <summary>
        /// Opens a recording and indexes it. Throws what the reader throws -- a file that is not a
        /// recording, or one from a format this build cannot read -- so the caller can say which.
        /// </summary>
        public void Open(string filePath)
        {
            Close();

            var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            try
            {
                _reader = new FrameRecordReader(stream);
            }
            catch
            {
                stream.Dispose();
                throw;
            }

            path = filePath;
            header = _reader.header;
            isComplete = _reader.hasIndex;

            _Index();

            _frameIndex = _frames.Count > 0 ? 0 : -1;
            _Load();
        }

        public void Close()
        {
            _reader?.Dispose();
            _reader = null;

            _frames.Clear();
            _symbols.Clear();
            _inputs.Clear();
            _elementTypes.Clear();
            _snapshot.TrimTypes(0);
            _snapshot.structure.Clear();
            _snapshot.frameNumber = -1;

            path = string.Empty;
            _frameIndex = -1;
            version++;
        }

        public void Dispose() => Close();

        /// <summary>
        /// Walks the file once, noting where each frame starts and collecting the mapping table.
        ///
        /// The table is taken from the tail when there is one, because a finished file has it whole;
        /// otherwise it is built from the symbol entries, which is what makes a file that was cut
        /// short readable up to the cut.
        /// </summary>
        private void _Index()
        {
            _symbols.Clear();

            var tail = _reader.symbols;
            if (tail != null)
            {
                for (int i = 0; i < tail.Count; i++) _symbols.Add(tail[i]);
            }

            _reader.Rewind();

            var structureOffset = -1L;

            while (true)
            {
                var offset = _reader.position;
                if (!_reader.TryReadEntry(out var entry)) break;

                switch (entry.kind)
                {
                    case FrameEntryKind.FrameBoundary:
                        _frames.Add(new FrameMark
                        {
                            frameNumber = entry.frameNumber,
                            offset = offset,
                            structureOffset = structureOffset,
                        });
                        break;

                    case FrameEntryKind.Structure:
                        structureOffset = offset;

                        // The frame carrying it is already on the list, so it is corrected in place.
                        // A frame's own inventory is the one it carries, not the previous one.
                        if (_frames.Count > 0) _frames[_frames.Count - 1].structureOffset = offset;
                        break;

                    case FrameEntryKind.Symbol:
                        if (tail == null) _ReadSymbol(entry.payload);
                        break;
                }
            }
        }

        /// <summary>Reads the frame at the current position into the snapshot.</summary>
        private void _Load()
        {
            _inputs.Clear();
            _snapshot.TrimTypes(0);
            _snapshot.structure.Clear();
            _snapshot.selectedType = null;
            _snapshot.selectedValueLength = 0;

            if (_reader == null || _frameIndex < 0 || _frameIndex >= _frames.Count)
            {
                _snapshot.frameNumber = -1;
                version++;
                return;
            }

            var mark = _frames[_frameIndex];

            _snapshot.frameNumber = mark.frameNumber;
            _snapshot.frameRate = header.frameRate;

            // A recording is by definition somebody else's frames, which is exactly what the pill
            // means; drawing it any other way would say the running gate produced this.
            _snapshot.isSupplied = true;
            _snapshot.hasSink = false;
            _snapshot.hasSource = false;

            _LoadStructure(mark);
            _LoadFrameEntries(mark);

            version++;
        }

        /// <summary>
        /// Reads the inventory that applies to a frame, which is usually not on the frame itself.
        /// </summary>
        private void _LoadStructure(FrameMark mark)
        {
            if (mark.structureOffset < 0) return;
            if (!_reader.TrySeekTo(mark.structureOffset)) return;
            if (!_reader.TryReadEntry(out var entry)) return;
            if (entry.kind != FrameEntryKind.Structure) return;

            _ReadStructure(entry.payload);
        }

        /// <summary>Reads everything between this frame's boundary and the next one.</summary>
        private void _LoadFrameEntries(FrameMark mark)
        {
            if (!_reader.TrySeekTo(mark.offset)) return;

            // The boundary itself. Read and dropped: the rate it carries is the header's, and the
            // frame number is already known from the index.
            if (!_reader.TryReadEntry(out _)) return;

            var typeIndex = 0;

            while (_reader.TryReadEntry(out var entry))
            {
                if (entry.kind == FrameEntryKind.FrameBoundary) break;

                switch (entry.kind)
                {
                    case FrameEntryKind.State:
                        _ReadState(entry.payload, typeIndex++);
                        break;

                    case FrameEntryKind.Input:
                        _ReadInput(entry.payload, mark.frameNumber);
                        break;

                    case FrameEntryKind.Structure:
                        _ReadStructure(entry.payload);
                        break;
                }
            }

            _snapshot.TrimTypes(typeIndex);
        }

        private void _ReadSymbol(ReadOnlySpan<byte> payload)
        {
            var id = BitConverter.ToInt32(payload.Slice(0, 4));
            var length = BitConverter.ToInt32(payload.Slice(4, 4));
            var value = Encoding.UTF8.GetString(payload.Slice(8, length));

            // Ids are positions and the writer emits them in order, so a gap is a lost entry rather
            // than a sparse table. Filled so later ids still land where they belong.
            while (_symbols.Count < id) _symbols.Add(null);

            if (_symbols.Count == id) _symbols.Add(value);
            else _symbols[id] = value;
        }

        private string _Resolve(int id)
        {
            if (id == InputSymbolTable.kNone) return string.Empty;
            if (id < 0 || id >= _symbols.Count) return $"#{id}";

            return _symbols[id] ?? $"#{id}";
        }

        private void _ReadStructure(ReadOnlySpan<byte> payload)
        {
            _snapshot.structureEpoch = BitConverter.ToInt64(payload.Slice(0, 8));
            var count = BitConverter.ToInt32(payload.Slice(8, 4));

            _snapshot.structure.Clear();

            var offset = 12;
            for (int i = 0; i < count; i++)
            {
                var id = BitConverter.ToInt32(payload.Slice(offset, 4));
                var typeId = BitConverter.ToInt32(payload.Slice(offset + 4, 4));
                var parentId = BitConverter.ToInt32(payload.Slice(offset + 8, 4));
                offset += 12;

                _snapshot.structure.Add(new StructureRow
                {
                    objectId = id,
                    objectName = _Resolve(id),
                    typeId = typeId,
                    typeName = _Resolve(typeId),
                    parentId = parentId,
                    parentName = parentId == InputSymbolTable.kNone ? string.Empty : _Resolve(parentId),
                });
            }
        }

        private void _ReadState(ReadOnlySpan<byte> payload, int typeIndex)
        {
            var typeId = BitConverter.ToInt32(payload.Slice(0, 4));
            var elementSize = BitConverter.ToInt32(payload.Slice(4, 4));
            var count = BitConverter.ToInt32(payload.Slice(8, 4));
            var bytes = payload.Slice(12);

            var typeName = _Resolve(typeId);
            var row = _snapshot.GetOrAddType(typeIndex);

            row.typeName = typeName;
            row.elementType = _ElementType(typeName);
            row.elementSize = elementSize;
            row.elements.Clear();

            var meta = _MetaSize(row.elementType, elementSize);
            var isSelected = typeName == selectedType;

            for (int i = 0; i < count; i++)
            {
                var start = i * elementSize;
                if (start + elementSize > bytes.Length) break;

                var element = bytes.Slice(start, elementSize);
                var ownerId = BitConverter.ToInt32(element.Slice(0, 4));

                // Stored offset by one so that a default source is distinguishable from the first
                // declared one. Undone here rather than through FrameSource, whose id is internal.
                var sourcePlusOne = BitConverter.ToInt32(element.Slice(4, 4));

                row.elements.Add(new ElementRow
                {
                    ownerId = ownerId,
                    owner = _Resolve(ownerId),
                    source = sourcePlusOne == 0 ? string.Empty : _Resolve(sourcePlusOne - 1),
                    time = BitConverter.ToInt64(element.Slice(8, 8)),

                    // A file is not a run: nothing here is being written, so freshness would be a
                    // claim about the past. Stamped as this frame so a row does not read as stale.
                    lastChangedFrame = _snapshot.frameNumber,
                });

                if (!isSelected || ownerId != selectedOwnerId) continue;

                var length = elementSize - meta;
                if (length <= 0) continue;

                if (_snapshot.selectedValue.Length < length) _snapshot.selectedValue = new byte[length];

                element.Slice(meta, length).CopyTo(_snapshot.selectedValue);
                _snapshot.selectedValueLength = length;
                _snapshot.selectedType = typeName;
                _snapshot.selectedOwnerId = ownerId;
            }
        }

        private void _ReadInput(ReadOnlySpan<byte> payload, long frameNumber)
        {
            var payloadLength = BitConverter.ToInt32(payload.Slice(29, 4));

            byte[] bytes = null;
            if (payloadLength > 0 && 33 + payloadLength <= payload.Length)
            {
                bytes = payload.Slice(33, payloadLength).ToArray();
            }

            var flags = (InputFlags)payload[28];

            _inputs.Add(new InputRow
            {
                rowId = _nextRowId++,
                frameNumber = frameNumber,
                sequence = BitConverter.ToInt64(payload.Slice(0, 8)),
                kind = (InputKind)BitConverter.ToInt32(payload.Slice(8, 4)),
                source = _Resolve(BitConverter.ToInt32(payload.Slice(12, 4))),
                target = _Resolve(BitConverter.ToInt32(payload.Slice(16, 4))),
                verb = _Resolve(BitConverter.ToInt32(payload.Slice(20, 4))),
                payloadTypeName = _Resolve(BitConverter.ToInt32(payload.Slice(24, 4))),
                payload = bytes,
                faulted = (flags & InputFlags.Faulted) != 0,
                truncated = (flags & InputFlags.PayloadTruncated) != 0,
            });
        }

        private Type _ElementType(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return null;
            if (_elementTypes.TryGetValue(typeName, out var cached)) return cached;

            // Null is an answer: the recording may name a type this build no longer has, and the
            // row is still worth drawing by name and size.
            var found = InputPayload.Resolve(typeName);
            _elementTypes[typeName] = found;
            return found;
        }

        /// <summary>
        /// Where an element's value starts.
        ///
        /// Taken from the field's own offset when this build still has the type, which is the same
        /// place <c>StateBlock&lt;T&gt;.metaSize</c> takes it from -- so the two cannot disagree
        /// about where a recorded value begins.
        ///
        /// Not the element's size minus the value's: an element is padded out to its own alignment,
        /// so that subtraction lands past the value and reads the padding instead. It did, and the
        /// selected value came back as zero.
        /// </summary>
        private static int _MetaSize(Type elementType, int elementSize)
        {
            var assumed = Math.Min(kAssumedMetaSize, elementSize);
            if (elementType == null) return assumed;

            try
            {
                var element = typeof(StateElement<>).MakeGenericType(elementType);
                var field = element.GetField(nameof(StateElement<int>.value));
                if (field == null) return assumed;

                var offset = UnsafeUtility.GetFieldOffset(field);
                return offset > 0 && offset < elementSize ? offset : assumed;
            }
            catch (Exception)
            {
                // A recorded type that is no longer unmanaged cannot close the generic. Falling
                // back beats refusing to draw the lane over a type that has since changed.
                return assumed;
            }
        }
    }
}
