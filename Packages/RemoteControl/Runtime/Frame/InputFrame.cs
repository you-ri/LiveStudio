// Copyright (c) You-Ri, 2026
using System;

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
    /// Instances are owned and reused by <see cref="InputFrameBuffer"/>, so the input array is a
    /// capacity buffer. Read <see cref="inputCount"/>, never <c>inputs.Length</c>.
    /// </summary>
    public sealed class InputFrame
    {
        private InputRecord[] _inputs = Array.Empty<InputRecord>();
        private int _inputCount;

        /// <summary>Monotonic frame number since the start of the run.</summary>
        public long frameNumber { get; private set; }

        /// <summary>Rate the frame number was counted at. Needed to read it back as time.</summary>
        public FrameRate frameRate { get; private set; }

        /// <summary>Readable position, derived from the frame number and the rate.</summary>
        public Timecode timecode => new Timecode(frameNumber, frameRate);

        /// <summary>Number of valid entries in <see cref="inputs"/>.</summary>
        public int inputCount => _inputCount;

        /// <summary>Backing store. Only the first <see cref="inputCount"/> entries are valid.</summary>
        public InputRecord[] inputs => _inputs;

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
            if (_inputCount == _inputs.Length)
            {
                // Grow to the high-water mark and stay there; steady state does not reallocate.
                var grown = new InputRecord[_inputs.Length == 0 ? 8 : _inputs.Length * 2];
                Array.Copy(_inputs, grown, _inputCount);
                _inputs = grown;
            }

            _inputs[_inputCount++] = record;
        }

        /// <summary>
        /// Copies this frame into <paramref name="destination"/>, growing its buffer if needed. A
        /// reader that reuses one destination settles at the high-water mark and stops allocating.
        /// </summary>
        internal void CopyTo(InputFrame destination)
        {
            destination.frameNumber = frameNumber;
            destination.frameRate = frameRate;

            if (destination._inputs.Length < _inputCount)
            {
                destination._inputs = new InputRecord[_inputCount];
            }

            Array.Copy(_inputs, destination._inputs, _inputCount);
            destination._inputCount = _inputCount;
        }

        public override string ToString() => $"frame {frameNumber} @ {timecode} ({_inputCount} inputs)";
    }
}
