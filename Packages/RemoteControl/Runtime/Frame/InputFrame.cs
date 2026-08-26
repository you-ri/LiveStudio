// Copyright (c) You-Ri, 2026
using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Lilium.RemoteControl.Frames
{
    /// <summary>
    /// One committed frame: the inputs applied at its head, in the order they were applied,
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
    /// Instances are owned and reused by <see cref="InputFrameBuffer"/>, so the storage is a
    /// capacity buffer. Read <see cref="inputCount"/>, never the storage length. Whoever creates one
    /// disposes it; a frame that never received a record never allocates and costs nothing to drop.
    /// </summary>
    public sealed unsafe class InputFrame : IDisposable
    {
        private NativeArray<InputRecord> _inputs;
        private int _inputCount;

        /// <summary>Monotonic frame number since the start of the run.</summary>
        public long frameNumber { get; private set; }

        /// <summary>Rate the frame number was counted at. Needed to read it back as time.</summary>
        public FrameRate frameRate { get; private set; }

        /// <summary>Readable position, derived from the frame number and the rate.</summary>
        public Timecode timecode => new Timecode(frameNumber, frameRate);

        /// <summary>Number of valid entries in <see cref="inputs"/>.</summary>
        public int inputCount => _inputCount;

        /// <summary>Storage. Only the first <see cref="inputCount"/> entries are valid.</summary>
        public NativeArray<InputRecord> inputs => _inputs;

        public InputRecord this[int index]
        {
            get
            {
                if ((uint)index >= (uint)_inputCount) throw new ArgumentOutOfRangeException(nameof(index));

                return _inputs[index];
            }
        }

        internal void Reset(long number, FrameRate rate)
        {
            frameNumber = number;
            frameRate = rate;
            _inputCount = 0;
        }

        internal void Add(in InputRecord record)
        {
            _EnsureCapacity(_inputCount + 1);

            UnsafeUtility.WriteArrayElement(_inputs.GetUnsafePtr(), _inputCount, record);
            _inputCount++;
        }

        /// <summary>
        /// Copies this frame into <paramref name="destination"/>, growing its storage if needed. A
        /// reader that reuses one destination settles at the high-water mark and stops allocating.
        /// </summary>
        internal void CopyTo(InputFrame destination)
        {
            destination.frameNumber = frameNumber;
            destination.frameRate = frameRate;
            destination._inputCount = _inputCount;

            if (_inputCount == 0) return;

            destination._EnsureCapacity(_inputCount);

            UnsafeUtility.MemCpy(destination._inputs.GetUnsafePtr(), _inputs.GetUnsafeReadOnlyPtr(),
                (long)_inputCount * sizeof(InputRecord));
        }

        /// <summary>
        /// Releases the storage. The frame stays usable and allocates again on next write, so a
        /// buffer that is released between runs does not have to rebuild its slots.
        /// </summary>
        public void Dispose()
        {
            if (_inputs.IsCreated) _inputs.Dispose();

            _inputs = default;
            _inputCount = 0;
        }

        private void _EnsureCapacity(int required)
        {
            var capacity = _inputs.IsCreated ? _inputs.Length : 0;
            if (capacity >= required) return;

            // Grow to the high-water mark and stay there; steady state does not reallocate.
            var grown = Math.Max(required, capacity == 0 ? 8 : capacity * 2);
            var replacement = new NativeArray<InputRecord>(grown, Allocator.Persistent,
                NativeArrayOptions.ClearMemory);

            if (_inputs.IsCreated)
            {
                UnsafeUtility.MemCpy(replacement.GetUnsafePtr(), _inputs.GetUnsafeReadOnlyPtr(),
                    (long)_inputCount * sizeof(InputRecord));

                _inputs.Dispose();
            }

            _inputs = replacement;
        }

        public override string ToString() => $"frame {frameNumber} @ {timecode} ({_inputCount} inputs)";
    }
}
