// Copyright (c) You-Ri, 2026
using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Lilium.RemoteControl.Frames
{
    /// <summary>
    /// One committed frame: the events applied at its head, in the order they were applied,
    /// stamped with the frame number they belong to.
    ///
    /// Position is held as a frame number plus a <see cref="FrameRate"/>, matching how the rest of
    /// the product indexes time. <see cref="timecode"/> derives the readable form from those two; it
    /// is not stored, so the two can never disagree.
    ///
    /// Records and the values they carry sit in two native lists: the records at a fixed width, the
    /// values back to back in an arena each record points into. A value costs what it says rather
    /// than a fixed slot, and has no ceiling -- a long text write is carried whole instead of being
    /// cut short and marked.
    ///
    /// Instances are owned and reused (<see cref="EventFrameBuffer"/>, a player, a replayer), so the
    /// storage is a capacity buffer that settles at its high-water mark. Whoever creates one
    /// disposes it; a frame that never received a record never allocates.
    /// </summary>
    public sealed unsafe class EventFrame : IDisposable
    {
        private UnsafeList<EventRecord> _events;
        private UnsafeList<byte> _payloads;

        /// <summary>Monotonic frame number since the start of the run.</summary>
        public long frameNumber { get; private set; }

        /// <summary>Rate the frame number was counted at. Needed to read it back as time.</summary>
        public FrameRate frameRate { get; private set; }

        /// <summary>Readable position, derived from the frame number and the rate.</summary>
        public Timecode timecode => new Timecode(frameNumber, frameRate);

        /// <summary>Number of records held.</summary>
        public int eventCount => _events.IsCreated ? _events.Length : 0;

        /// <summary>The record at an index, read in place.</summary>
        public ref readonly EventRecord this[int index]
        {
            get
            {
                if ((uint)index >= (uint)eventCount) throw new ArgumentOutOfRangeException(nameof(index));

                return ref _events.ElementAt(index);
            }
        }

        /// <summary>
        /// The value a record held here carries. Valid until this frame is next written to -- the
        /// arena moves when it grows.
        /// </summary>
        public ReadOnlySpan<byte> PayloadOf(in EventRecord record)
        {
            if (record.payloadLength <= 0) return ReadOnlySpan<byte>.Empty;

            if (record.payloadOffset < 0 || record.payloadOffset + record.payloadLength > _payloads.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(record),
                    "[RemoteControl] The record's payload does not lie in this frame.");
            }

            return new ReadOnlySpan<byte>(_payloads.Ptr + record.payloadOffset, record.payloadLength);
        }

        /// <summary>The value the record at an index carries.</summary>
        public ReadOnlySpan<byte> PayloadAt(int index) => PayloadOf(in this[index]);

        internal void Reset(long number, FrameRate rate)
        {
            frameNumber = number;
            frameRate = rate;
            Clear();
        }

        /// <summary>Drops every record and value, keeping the storage.</summary>
        internal void Clear()
        {
            if (_events.IsCreated) _events.Clear();
            if (_payloads.IsCreated) _payloads.Clear();
        }

        /// <summary>
        /// Appends a record and the value it carries. The record's payload position is rewritten to
        /// where the bytes land here, so whatever arena it came from is not referred to again.
        /// </summary>
        internal void Add(in EventRecord record, ReadOnlySpan<byte> payload)
        {
            _EnsureCreated();

            var stored = record;
            stored.payloadOffset = _payloads.Length;
            stored.payloadLength = payload.Length;

            if (payload.Length > 0)
            {
                var at = _payloads.Length;
                _Reserve(ref _payloads, at + payload.Length);
                _payloads.Resize(at + payload.Length);
                payload.CopyTo(new Span<byte>(_payloads.Ptr + at, payload.Length));
            }

            _Reserve(ref _events, _events.Length + 1);
            _events.Add(stored);
        }

        /// <summary>
        /// Copies this frame into <paramref name="destination"/>, growing its storage if needed. A
        /// reader that reuses one destination settles at the high-water mark and stops allocating.
        /// </summary>
        internal void CopyTo(EventFrame destination)
        {
            destination.frameNumber = frameNumber;
            destination.frameRate = frameRate;
            destination.Clear();

            var count = eventCount;
            if (count == 0) return;

            destination._EnsureCreated();

            _Reserve(ref destination._events, count);
            destination._events.Resize(count);
            UnsafeUtility.MemCpy(destination._events.Ptr, _events.Ptr, (long)count * sizeof(EventRecord));

            var bytes = _payloads.Length;
            if (bytes == 0) return;

            _Reserve(ref destination._payloads, bytes);
            destination._payloads.Resize(bytes);
            UnsafeUtility.MemCpy(destination._payloads.Ptr, _payloads.Ptr, bytes);
        }

        /// <summary>
        /// Releases the storage. The frame stays usable and allocates again on next write, so a
        /// buffer that is released between runs does not have to rebuild its slots.
        /// </summary>
        public void Dispose()
        {
            if (_events.IsCreated) _events.Dispose();
            if (_payloads.IsCreated) _payloads.Dispose();

            _events = default;
            _payloads = default;
        }

        public override string ToString() => $"frame {frameNumber} @ {timecode} ({eventCount} events)";

        private void _EnsureCreated()
        {
            if (!_events.IsCreated) _events = new UnsafeList<EventRecord>(8, Allocator.Persistent);
            if (!_payloads.IsCreated) _payloads = new UnsafeList<byte>(256, Allocator.Persistent);
        }

        // Doubling, so a frame that settles at its high-water mark stops reallocating.
        private static void _Reserve<T>(ref UnsafeList<T> list, int required) where T : unmanaged
        {
            if (required > list.Capacity) list.SetCapacity(Math.Max(required, list.Capacity * 2));
        }
    }
}
