// Copyright (c) You-Ri, 2026
using System;

namespace Lilium.RemoteControl.Frames
{
    /// <summary>Outcome of a lookup. The two failures are kept apart on purpose.</summary>
    public enum FrameLookup
    {
        Found = 0,

        /// <summary>
        /// Ahead of what has been committed -- the reader is early, or the producer is behind.
        /// Waiting is the right response.
        /// </summary>
        NotYetCommitted = 1,

        /// <summary>
        /// Already dropped out of the buffer -- the reader is too far behind to catch up. Waiting
        /// will not help; it has to resynchronise.
        /// </summary>
        Evicted = 2,

        /// <summary>
        /// The request was made at a different rate, so its frame number is not comparable with
        /// what is held. Refused rather than answered with the wrong frame.
        /// </summary>
        RateMismatch = 3,
    }

    /// <summary>
    /// Holds the last few committed frames so any of them can be read back by frame number.
    ///
    /// Same shape and slot-identity trick as <see cref="FrameBuffer{T}"/>, which is what the
    /// capture receivers and <c>VirgoMotionSource</c> already use, but not that type: a ring stores
    /// one fixed-size value per key, and a frame holds however many inputs arrived.
    ///
    /// Splitting it into two rings -- fixed-size headers keyed by frame number, records keyed by
    /// sequence number -- was tried and reverted. It would reuse the existing ring, but it lets the
    /// records of a frame be pushed out from under a header that is still held, so a burst leaves
    /// frames that look present and cannot be read. One ring keeps a frame whole: if it is there,
    /// all of it is there.
    ///
    /// The point of retaining frames is to let a reader run behind the producer on purpose. A
    /// standby machine kept a few frames back absorbs arrival jitter without a barrier, so a slow
    /// standby can never stall the machine actually on air.
    /// </summary>
    public sealed class InputFrameBuffer
    {
        private readonly object _lock = new object();
        private readonly InputFrame[] _slots;
        private readonly long[] _slotFrameNumbers;
        private long _frameCount;

        public InputFrameBuffer(int frameCapacity)
        {
            if (frameCapacity < 1) throw new ArgumentOutOfRangeException(nameof(frameCapacity));

            _slots = new InputFrame[frameCapacity];
            _slotFrameNumbers = new long[frameCapacity];
            for (int i = 0; i < frameCapacity; i++)
            {
                _slots[i] = new InputFrame();
                _slotFrameNumbers[i] = -1;
            }
        }

        /// <summary>How many frames are retained.</summary>
        public int size => _slots.Length;

        /// <summary>One past the highest committed frame number.</summary>
        public long frameCount { get { lock (_lock) { return _frameCount; } } }

        /// <summary>
        /// Takes the next slot so the driver can fill it. Main thread only, and it must be followed
        /// by <see cref="Commit"/>.
        ///
        /// The slot is invalidated up front rather than after filling: once the ring wraps, the
        /// slot being overwritten is one a reader can still reach, and it would otherwise watch it
        /// change mid-read.
        /// </summary>
        internal InputFrame BeginFrame(long frameNumber, FrameRate frameRate)
        {
            var index = (int)(frameNumber % _slots.Length);

            lock (_lock)
            {
                _slotFrameNumbers[index] = -1;
            }

            var slot = _slots[index];
            slot.Reset(frameNumber, frameRate);
            return slot;
        }

        /// <summary>Publishes the slot handed out by <see cref="BeginFrame"/>.</summary>
        internal void Commit(long frameNumber)
        {
            var index = (int)(frameNumber % _slots.Length);

            lock (_lock)
            {
                _slotFrameNumbers[index] = frameNumber;
                if (frameNumber + 1 > _frameCount) _frameCount = frameNumber + 1;
            }
        }

        /// <summary>
        /// Copies the frame with the given number into <paramref name="destination"/>.
        ///
        /// Copying rather than handing out the held instance: slots are reused as the ring wraps,
        /// so a reader on another thread holding a reference would see it change underneath.
        /// </summary>
        public FrameLookup TryRead(long frameNumber, InputFrame destination)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));

            lock (_lock)
            {
                if (frameNumber < 0 || frameNumber >= _frameCount) return FrameLookup.NotYetCommitted;

                var index = (int)(frameNumber % _slots.Length);
                if (_slotFrameNumbers[index] != frameNumber) return FrameLookup.Evicted;

                _slots[index].CopyTo(destination);
                return FrameLookup.Found;
            }
        }

        /// <summary>
        /// Copies the frame at the given timecode. The timecode is converted with its own rate, and
        /// the read is refused when that rate disagrees with what was committed.
        /// </summary>
        public FrameLookup TryRead(Timecode timecode, FrameRate frameRate, InputFrame destination)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));

            lock (_lock)
            {
                if (_frameCount > 0)
                {
                    var newestIndex = (int)((_frameCount - 1) % _slots.Length);
                    if (_slotFrameNumbers[newestIndex] == _frameCount - 1 &&
                        _slots[newestIndex].frameRate != frameRate)
                    {
                        return FrameLookup.RateMismatch;
                    }
                }
            }

            return TryRead(timecode.ToFrameNumber(frameRate), destination);
        }

        /// <summary>Copies the most recently committed frame.</summary>
        public FrameLookup TryReadLatest(InputFrame destination)
        {
            lock (_lock)
            {
                if (_frameCount == 0) return FrameLookup.NotYetCommitted;
            }

            return TryRead(frameCount - 1, destination);
        }

        public void Reset()
        {
            lock (_lock)
            {
                for (int i = 0; i < _slotFrameNumbers.Length; i++) _slotFrameNumbers[i] = -1;
                _frameCount = 0;
            }
        }
    }
}
