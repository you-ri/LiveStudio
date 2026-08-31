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
    /// the product indexes time (<see cref="FrameBuffer{T}"/>, the capture receivers, the recorded
    /// stream). <see cref="timecode"/> derives the readable form from those two; it is not stored,
    /// so the two can never disagree.
    ///
    /// The records sit in native storage, like the state blocks: they were always unmanaged, and
    /// holding them where the address does not move keeps a copy between frames a block move and
    /// leaves the door open to reading them from a job.
    ///
    /// Instances are owned and reused by <see cref="EventFrameBuffer"/>, so the storage is a
    /// capacity buffer. Read <see cref="eventCount"/>, never the storage length. Whoever creates one
    /// disposes it; a frame that never received a record never allocates and costs nothing to drop.
    /// </summary>
    public sealed unsafe class EventFrame : IDisposable
    {
        private NativeArray<EventRecord> _events;
        private int _eventCount;

        /// <summary>Monotonic frame number since the start of the run.</summary>
        public long frameNumber { get; private set; }

        /// <summary>Rate the frame number was counted at. Needed to read it back as time.</summary>
        public FrameRate frameRate { get; private set; }

        /// <summary>Readable position, derived from the frame number and the rate.</summary>
        public Timecode timecode => new Timecode(frameNumber, frameRate);

        /// <summary>Number of valid entries in <see cref="events"/>.</summary>
        public int eventCount => _eventCount;

        /// <summary>Storage. Only the first <see cref="eventCount"/> entries are valid.</summary>
        public NativeArray<EventRecord> events => _events;

        public EventRecord this[int index]
        {
            get
            {
                if ((uint)index >= (uint)_eventCount) throw new ArgumentOutOfRangeException(nameof(index));

                return _events[index];
            }
        }

        internal void Reset(long number, FrameRate rate)
        {
            frameNumber = number;
            frameRate = rate;
            _eventCount = 0;
        }

        internal void Add(in EventRecord record)
        {
            _EnsureCapacity(_eventCount + 1);

            UnsafeUtility.WriteArrayElement(_events.GetUnsafePtr(), _eventCount, record);
            _eventCount++;
        }

        /// <summary>
        /// Copies this frame into <paramref name="destination"/>, growing its storage if needed. A
        /// reader that reuses one destination settles at the high-water mark and stops allocating.
        /// </summary>
        internal void CopyTo(EventFrame destination)
        {
            destination.frameNumber = frameNumber;
            destination.frameRate = frameRate;
            destination._eventCount = _eventCount;

            if (_eventCount == 0) return;

            destination._EnsureCapacity(_eventCount);

            UnsafeUtility.MemCpy(destination._events.GetUnsafePtr(), _events.GetUnsafeReadOnlyPtr(),
                (long)_eventCount * sizeof(EventRecord));
        }

        /// <summary>
        /// Releases the storage. The frame stays usable and allocates again on next write, so a
        /// buffer that is released between runs does not have to rebuild its slots.
        /// </summary>
        public void Dispose()
        {
            if (_events.IsCreated) _events.Dispose();

            _events = default;
            _eventCount = 0;
        }

        private void _EnsureCapacity(int required)
        {
            var capacity = _events.IsCreated ? _events.Length : 0;
            if (capacity >= required) return;

            // Grow to the high-water mark and stay there; steady state does not reallocate.
            var grown = Math.Max(required, capacity == 0 ? 8 : capacity * 2);
            var replacement = new NativeArray<EventRecord>(grown, Allocator.Persistent,
                NativeArrayOptions.ClearMemory);

            if (_events.IsCreated)
            {
                UnsafeUtility.MemCpy(replacement.GetUnsafePtr(), _events.GetUnsafeReadOnlyPtr(),
                    (long)_eventCount * sizeof(EventRecord));

                _events.Dispose();
            }

            _events = replacement;
        }

        public override string ToString() => $"frame {frameNumber} @ {timecode} ({_eventCount} events)";
    }
}
