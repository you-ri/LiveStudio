// Copyright (c) You-Ri, 2026
using System;

namespace Lilium.RemoteControl.Frames
{
    /// <summary>
    /// Runs an event somebody is waiting on, and hands them the outcome.
    ///
    /// The waiting half of a submitted event. A posted one has nobody waiting and carries a plain
    /// <see cref="Action"/> instead.
    /// </summary>
    internal interface IPendingCompletion
    {
        /// <summary>Applies the event and completes the waiter. Rethrows what applying threw.</summary>
        void Run();

        /// <summary>Abandons the event, handing the waiter the reason instead.</summary>
        void Fault(Exception reason);
    }

    /// <summary>
    /// One event waiting for the next frame head. Its records and their values are held by the
    /// <see cref="EventBatch"/> it sits in, so queueing one costs no allocation of its own.
    /// </summary>
    internal struct PendingEvent
    {
        /// <summary>Index of the first record in the batch. The rest follow it.</summary>
        public int firstRecord;

        /// <summary>
        /// How many records it leaves behind. Usually one; more when a bundled request was submitted
        /// as a group, in which case they are kept apart so each stays small and addressable.
        /// </summary>
        public int recordCount;

        /// <summary>What a producer inside the frame posted. Null for a submitted event.</summary>
        public Action apply;

        /// <summary>What a waiting caller submitted. Null for a posted event.</summary>
        public IPendingCompletion completion;
    }

    /// <summary>
    /// Everything accepted between two frame heads: the events, their records, and the values the
    /// records carry, each in one reused array.
    ///
    /// Arrays rather than lists of objects, so a queue at its high-water mark accepts events without
    /// allocating. The sequencer keeps two and swaps them, so the frame head drains one while the
    /// workers fill the other.
    /// </summary>
    internal sealed class EventBatch
    {
        private PendingEvent[] _events = new PendingEvent[16];
        private EventRecord[] _records = new EventRecord[16];
        private byte[] _payloads = new byte[256];
        private int _eventCount;
        private int _recordCount;
        private int _payloadLength;

        /// <summary>Events in the batch.</summary>
        public int Count => _eventCount;

        /// <summary>The event at an index, in place.</summary>
        public ref PendingEvent this[int index]
        {
            get
            {
                if ((uint)index >= (uint)_eventCount) throw new ArgumentOutOfRangeException(nameof(index));

                return ref _events[index];
            }
        }

        /// <summary>A record, in place, so applying an event can mark or restamp it.</summary>
        public ref EventRecord RecordAt(int index)
        {
            if ((uint)index >= (uint)_recordCount) throw new ArgumentOutOfRangeException(nameof(index));

            return ref _records[index];
        }

        /// <summary>The value a record in this batch carries.</summary>
        public ReadOnlySpan<byte> PayloadOf(in EventRecord record)
            => record.payloadLength <= 0
                ? ReadOnlySpan<byte>.Empty
                : new ReadOnlySpan<byte>(_payloads, record.payloadOffset, record.payloadLength);

        /// <summary>
        /// Makes room for a new value of a record and points the record at it, for the caller to
        /// fill. What the record carried before is left where it was: it is only garbage until the
        /// batch is cleared, which is the next frame head.
        /// </summary>
        public Span<byte> ReservePayload(ref EventRecord record, int length, int typeId)
        {
            var at = _ReserveBytes(length);

            record.payloadTypeId = typeId;
            record.payloadOffset = at;
            record.payloadLength = length;

            return new Span<byte>(_payloads, at, length);
        }

        /// <summary>
        /// Appends one event: its records, stamped with consecutive sequence numbers from
        /// <paramref name="firstSequence"/>, and their values. A record's payload position is taken
        /// as relative to <paramref name="payloads"/> and moved to where the bytes land here.
        /// </summary>
        internal void Append(ReadOnlySpan<EventRecord> records, ReadOnlySpan<byte> payloads,
            long firstSequence, Action apply, IPendingCompletion completion)
        {
            var baseOffset = _ReserveBytes(payloads.Length);
            payloads.CopyTo(new Span<byte>(_payloads, baseOffset, payloads.Length));

            _Grow(ref _records, _recordCount + records.Length);

            var first = _recordCount;
            for (int i = 0; i < records.Length; i++)
            {
                var record = records[i];
                record.sequence = firstSequence + i;
                if (record.payloadLength > 0) record.payloadOffset += baseOffset;

                _records[_recordCount++] = record;
            }

            _Grow(ref _events, _eventCount + 1);
            _events[_eventCount++] = new PendingEvent
            {
                firstRecord = first,
                recordCount = records.Length,
                apply = apply,
                completion = completion,
            };
        }

        /// <summary>
        /// Empties the batch. The delegates and completions are let go of, so nothing an event
        /// captured is kept alive past the frame head it ran at.
        /// </summary>
        public void Clear()
        {
            Array.Clear(_events, 0, _eventCount);
            _eventCount = 0;
            _recordCount = 0;
            _payloadLength = 0;
        }

        private int _ReserveBytes(int length)
        {
            var at = _payloadLength;
            _Grow(ref _payloads, at + length);
            _payloadLength = at + length;
            return at;
        }

        private static void _Grow<T>(ref T[] array, int required)
        {
            if (required <= array.Length) return;

            Array.Resize(ref array, Math.Max(required, array.Length * 2));
        }
    }

    /// <summary>
    /// Decides the order of events arriving from several paths and threads.
    ///
    /// Numbering and hand-off happen under one lock so that sequence order is the order events were
    /// accepted. Without that, order is settled by whichever worker thread reached the queue first,
    /// which is well-defined on one machine but different on the next -- so two machines fed the
    /// same events would drift apart permanently.
    ///
    /// Draining swaps the pending batch for a spare instead of copying, so a frame with no new
    /// events costs nothing and a frame with some allocates nothing.
    /// </summary>
    internal sealed class EventSequencer
    {
        private readonly object _lock = new object();
        private EventBatch _pending = new EventBatch();
        private EventBatch _spare = new EventBatch();
        private long _nextSequence = 1;

        /// <summary>Sequence number the next accepted event will get.</summary>
        public long nextSequence
        {
            get { lock (_lock) { return _nextSequence; } }
        }

        /// <summary>Number of events waiting for the next frame head.</summary>
        public int pendingCount
        {
            get { lock (_lock) { return _pending.Count; } }
        }

        /// <summary>
        /// Accepts an event and stamps its records with their place in the order. Returns the first
        /// sequence number given.
        ///
        /// A group is numbered and handed over inside the one lock, so its records get a run of
        /// consecutive numbers and reach the same drain together. That is what keeps a bundled
        /// request from being split across two frames.
        /// </summary>
        public long Submit(ReadOnlySpan<EventRecord> records, ReadOnlySpan<byte> payloads,
            Action apply, IPendingCompletion completion)
        {
            lock (_lock)
            {
                var first = _nextSequence;
                _pending.Append(records, payloads, first, apply, completion);
                _nextSequence = first + records.Length;
                return first;
            }
        }

        /// <summary>
        /// Hands over everything accepted so far. The batch belongs to the caller until the next
        /// drain, at which point it is cleared and reused.
        /// </summary>
        public EventBatch Drain()
        {
            lock (_lock)
            {
                var drained = _pending;
                _spare.Clear();
                _pending = _spare;
                _spare = drained;
                return drained;
            }
        }

        /// <summary>Drops everything pending. Used when the run restarts.</summary>
        public void Reset()
        {
            lock (_lock)
            {
                _pending.Clear();
                _spare.Clear();
                _nextSequence = 1;
            }
        }
    }
}
