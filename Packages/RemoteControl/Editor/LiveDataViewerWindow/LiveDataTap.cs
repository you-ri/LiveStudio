// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;
using Lilium.RemoteControl.Frames;
using Lilium.RemoteControl.Frames.Recording;

namespace Lilium.RemoteControl.Editor.LiveDataViewer
{
    /// <summary>
    /// Watches the gate and keeps the last frame, plus a ring of recent events, for whoever wants to
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
        /// <summary>Recent events kept for the log pane. Sparse, so this is minutes of a normal run.</summary>
        public const int kEventCapacity = 512;

        private sealed class Observer : IFrameObserver
        {
            public void OnFrameCompleted(in Frame frame, FrameSymbolTable symbols)
                => _Capture(in frame, symbols);
        }

        private static readonly Observer _observer = new Observer();
        private static readonly LiveDataSnapshot _snapshot = new LiveDataSnapshot();
        private static readonly EventRow[] _events = new EventRow[kEventCapacity];

        // Session frame in which each (type, owner) last moved its stamp. Keyed by the pair so two
        // producers writing the same owner under different types do not shadow each other.
        private static readonly Dictionary<long, long> _lastStamp = new Dictionary<long, long>();
        private static readonly Dictionary<long, long> _lastChangedFrame = new Dictionary<long, long>();

        private static int _attachCount;
        private static int _eventHead;

        // Never reset: it only has to be unique across what the ring is holding, and starting over
        // is exactly what makes the run's own sequence unusable as a key here.
        private static long _nextRowId = 1;
        private static int _eventCount;

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

        /// <summary>Events kept, up to <see cref="kEventCapacity"/>.</summary>
        public static int eventCount => _eventCount;

        /// <summary>Type and owner of the element whose bytes are taken each frame. Null for none.</summary>
        public static string selectedType { get; private set; }

        public static int selectedOwnerId { get; private set; } = FrameSymbolTable.kNone;

        /// <summary>
        /// Starts watching, if nothing was. Balanced by <see cref="Release"/> -- the tap keeps
        /// watching while any window still wants it.
        /// </summary>
        public static void Retain()
        {
            _attachCount++;
            if (_attachCount != 1) return;

            FrameGate.AddFrameObserver(_observer);

            // Watching the frame is not enough to see anything: the lanes are filled by producers,
            // and outside a recording nobody had asked for them. So the window asks -- otherwise it
            // opens on an empty list and "nothing is being produced" looks like "nothing exists".
            LiveStructureSystem.Retain();
            LiveStateSystem.Retain();
        }

        /// <summary>Stops watching once the last window is done.</summary>
        public static void Release()
        {
            if (_attachCount == 0) return;

            _attachCount--;
            if (_attachCount != 0) return;

            FrameGate.RemoveFrameObserver(_observer);

            // Counted on the systems' side, so a recording that is still running keeps them.
            LiveStructureSystem.Release();
            LiveStateSystem.Release();
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
        public static EventRow GetEvent(int index)
        {
            var start = _eventCount < kEventCapacity ? 0 : _eventHead;
            return _events[(start + index) % kEventCapacity];
        }

        /// <summary>Forgets the events kept so far. The state lane is per-frame and needs no clearing.</summary>
        public static void ClearEvents()
        {
            _eventHead = 0;
            _eventCount = 0;
            version++;
        }

        private static void _Capture(in Frame frame, FrameSymbolTable symbols)
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
            _CaptureEvents(in frame, resolve);

            hasFrame = true;
            version++;
        }

        private static Func<int, string> _ResolverFor(in Frame frame, FrameSymbolTable symbols)
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
            return id == FrameSymbolTable.kNone ? string.Empty : resolve(id);
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
                    parentName = entry.parentId == FrameSymbolTable.kNone
                        ? string.Empty
                        : resolve(entry.parentId),
                    recipe = resolve(entry.recipeId),
                    memberName = entry.memberId == FrameSymbolTable.kNone
                        ? string.Empty
                        : resolve(entry.memberId),
                    keyName = entry.keyId == FrameSymbolTable.kNone
                        ? string.Empty
                        : resolve(entry.keyId),
                    ordinal = entry.ordinal,
                });
            }
        }

        private static void _CaptureEvents(in Frame frame, Func<int, string> resolve)
        {
            var events = frame.events;
            if (events == null) return;

            for (int i = 0; i < events.eventCount; i++)
            {
                var record = events[i];

                var payload = record.payloadLength == 0 ? null : new byte[record.payloadLength];
                if (payload != null) record.CopyPayloadTo(payload);

                _events[_eventHead] = new EventRow
                {
                    rowId = _nextRowId++,
                    frameNumber = frame.frameNumber,
                    sequence = record.sequence,
                    kind = record.kind,
                    source = resolve(record.sourceId),
                    verb = resolve(record.verbId),
                    target = resolve(record.targetId),
                    payloadTypeName = resolve(record.payloadTypeId),
                    payload = payload,
                    faulted = record.faulted,
                    truncated = record.payloadTruncated,
                };

                _eventHead = (_eventHead + 1) % kEventCapacity;
                if (_eventCount < kEventCapacity) _eventCount++;
            }
        }

        private static long _Key(int blockIndex, int ownerId)
            => ((long)blockIndex << 32) | (uint)ownerId;
    }
}
