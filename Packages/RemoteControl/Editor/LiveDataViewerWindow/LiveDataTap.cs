// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;
using Lilium.RemoteControl.Frames;
using Lilium.RemoteControl.Frames.Recording;

namespace Lilium.RemoteControl.Editor.LiveDataViewer
{
    /// <summary>
    /// Watches the gate and keeps the last frame, plus a ring of recent inputs, for whoever wants to
    /// draw them.
    ///
    /// A plain static rather than the window itself. An EditorWindow that registers as an observer
    /// leaves a destroyed instance in the list when it is closed the wrong way or the domain
    /// reloads, and two windows would watch twice. One tap, attached while at least one window is
    /// open, sidesteps both.
    ///
    /// Everything here runs on the main thread: the gate pumps from the player loop or the editor
    /// heartbeat, and windows draw from the editor update. So the snapshot is written and read
    /// without locking, and <see cref="version"/> is what tells a reader something moved.
    /// </summary>
    internal static class LiveDataTap
    {
        /// <summary>Recent inputs kept for the log pane. Sparse, so this is minutes of a normal run.</summary>
        public const int kInputCapacity = 512;

        private sealed class Observer : IFrameObserver
        {
            public void OnFrameCompleted(in Frame frame, InputSymbolTable symbols)
                => _Capture(in frame, symbols);
        }

        private static readonly Observer _observer = new Observer();
        private static readonly LiveDataSnapshot _snapshot = new LiveDataSnapshot();
        private static readonly InputRow[] _inputs = new InputRow[kInputCapacity];

        // Session frame in which each (type, owner) last moved its stamp. Keyed by the pair so two
        // producers writing the same owner under different types do not shadow each other.
        private static readonly Dictionary<long, long> _lastStamp = new Dictionary<long, long>();
        private static readonly Dictionary<long, long> _lastChangedFrame = new Dictionary<long, long>();

        private static int _attachCount;
        private static int _inputHead;
        private static int _inputCount;

        /// <summary>Bumped every time a frame is taken. A reader redraws when it moves.</summary>
        public static long version { get; private set; }

        /// <summary>
        /// True once a frame has actually gone by. Until then the snapshot holds nothing real -- and
        /// a rate of zero, which is not something to do arithmetic with.
        /// </summary>
        public static bool hasFrame { get; private set; }

        /// <summary>The most recent frame. Valid for reading between pumps, which is all a draw needs.</summary>
        public static LiveDataSnapshot snapshot => _snapshot;

        /// <summary>True while the tap is watching.</summary>
        public static bool isAttached => _attachCount > 0;

        /// <summary>Inputs kept, up to <see cref="kInputCapacity"/>.</summary>
        public static int inputCount => _inputCount;

        /// <summary>Type and owner of the element whose bytes are taken each frame. Null for none.</summary>
        public static string selectedType { get; private set; }

        public static int selectedOwnerId { get; private set; } = InputSymbolTable.kNone;

        /// <summary>
        /// Starts watching, if nothing was. Balanced by <see cref="Release"/> -- the tap keeps
        /// watching while any window still wants it.
        /// </summary>
        public static void Retain()
        {
            _attachCount++;
            if (_attachCount != 1) return;

            FrameGate.AddFrameObserver(_observer);
        }

        /// <summary>Stops watching once the last window is done.</summary>
        public static void Release()
        {
            if (_attachCount == 0) return;

            _attachCount--;
            if (_attachCount != 0) return;

            FrameGate.RemoveFrameObserver(_observer);
        }

        /// <summary>
        /// Names the one element whose value bytes are worth copying every frame. Everything else is
        /// kept as metadata only.
        /// </summary>
        public static void Select(string typeName, int ownerId)
        {
            selectedType = typeName;
            selectedOwnerId = ownerId;
        }

        /// <summary>Reads the ring oldest-first.</summary>
        public static InputRow GetInput(int index)
        {
            var start = _inputCount < kInputCapacity ? 0 : _inputHead;
            return _inputs[(start + index) % kInputCapacity];
        }

        /// <summary>Forgets the inputs kept so far. The state lane is per-frame and needs no clearing.</summary>
        public static void ClearInputs()
        {
            _inputHead = 0;
            _inputCount = 0;
            version++;
        }

        private static void _Capture(in Frame frame, InputSymbolTable symbols)
        {
            // Ids in a supplied frame belong to the recording's table, not this run's. Resolving them
            // here would silently name whatever happens to hold that number now.
            var resolve = _ResolverFor(in frame, symbols);

            _snapshot.frameNumber = frame.frameNumber;
            _snapshot.frameRate = frame.frameRate;
            _snapshot.isSupplied = frame.isSupplied;
            _snapshot.structureEpoch = frame.structureEpoch;
            _snapshot.hasSink = FrameGate.sink != null;
            _snapshot.hasSource = FrameGate.source != null;

            _CaptureState(in frame, resolve);
            _CaptureStructure(in frame, resolve);
            _CaptureInputs(in frame, resolve);

            hasFrame = true;
            version++;
        }

        private static Func<int, string> _ResolverFor(in Frame frame, InputSymbolTable symbols)
        {
            if (frame.isSupplied && FrameGate.source is FrameReplayer replayer)
            {
                var player = replayer.player;
                return id => player.Resolve(id);
            }

            return id => symbols.Resolve(id);
        }

        private static void _CaptureState(in Frame frame, Func<int, string> resolve)
        {
            var state = frame.state;
            if (state == null)
            {
                _snapshot.TrimTypes(0);
                return;
            }

            var blocks = state.blocks;
            for (int i = 0; i < blocks.Count; i++)
            {
                var block = blocks[i];
                var row = _snapshot.GetOrAddType(i);

                row.elementType = block.elementType;
                row.typeName = block.elementType?.FullName ?? "?";
                row.elementSize = block.elementSize;
                row.elements.Clear();

                var isSelected = row.typeName == selectedType;

                for (int e = 0; e < block.count; e++)
                {
                    var ownerId = block.OwnerIdAt(e);
                    var key = _Key(i, ownerId);
                    var stamp = _StampAt(block, e);

                    if (!_lastStamp.TryGetValue(key, out var previous) || previous != stamp)
                    {
                        _lastStamp[key] = stamp;
                        _lastChangedFrame[key] = frame.frameNumber;
                    }

                    _lastChangedFrame.TryGetValue(key, out var changedFrame);

                    row.elements.Add(new ElementRow
                    {
                        ownerId = ownerId,
                        owner = resolve(ownerId),
                        source = _SourceAt(block, e, resolve),
                        time = stamp,
                        lastChangedFrame = changedFrame,
                    });

                    if (isSelected && ownerId == selectedOwnerId) _CaptureSelected(block, e, row);
                }
            }

            _snapshot.TrimTypes(blocks.Count);
        }

        // The element's metadata sits at a fixed offset the file format already depends on, so it is
        // read through the block rather than guessed at here.
        private static long _StampAt(StateBlock block, int index) => block.TimeAt(index);

        private static string _SourceAt(StateBlock block, int index, Func<int, string> resolve)
        {
            var id = block.SourceIdAt(index);
            return id == InputSymbolTable.kNone ? string.Empty : resolve(id);
        }

        private static void _CaptureSelected(StateBlock block, int index, TypeRow row)
        {
            var size = block.elementSize;
            if (_snapshot.selectedValue.Length < size) _snapshot.selectedValue = new byte[size];

            block.CopyValueTo(index, _snapshot.selectedValue);
            _snapshot.selectedValueLength = size;
            _snapshot.selectedType = row.typeName;
            _snapshot.selectedOwnerId = selectedOwnerId;
        }

        private static void _CaptureStructure(in Frame frame, Func<int, string> resolve)
        {
            _snapshot.structure.Clear();

            var structure = frame.structure;
            if (structure == null) return;

            for (int i = 0; i < structure.count; i++)
            {
                var entry = structure[i];
                _snapshot.structure.Add(new StructureRow
                {
                    objectId = entry.id,
                    objectName = resolve(entry.id),
                    typeId = entry.typeId,
                    typeName = resolve(entry.typeId),
                    parentId = entry.parentId,
                    parentName = entry.parentId == InputSymbolTable.kNone
                        ? string.Empty
                        : resolve(entry.parentId),
                });
            }
        }

        private static void _CaptureInputs(in Frame frame, Func<int, string> resolve)
        {
            var inputs = frame.inputs;
            if (inputs == null) return;

            for (int i = 0; i < inputs.inputCount; i++)
            {
                var record = inputs[i];

                _inputs[_inputHead] = new InputRow
                {
                    frameNumber = frame.frameNumber,
                    sequence = record.sequence,
                    kind = record.kind,
                    source = resolve(record.sourceId),
                    method = resolve(record.methodId),
                    target = resolve(record.targetId),
                    payload = record.payload.Length == 0 ? string.Empty : record.payload.ToString(),
                    faulted = record.faulted,
                    truncated = record.payloadTruncated,
                };

                _inputHead = (_inputHead + 1) % kInputCapacity;
                if (_inputCount < kInputCapacity) _inputCount++;
            }
        }

        private static long _Key(int blockIndex, int ownerId)
            => ((long)blockIndex << 32) | (uint)ownerId;
    }
}
