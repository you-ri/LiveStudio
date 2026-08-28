// Copyright (c) You-Ri, 2026

using System;
using UnityEngine;

namespace Lilium.RemoteControl
{
    /// <summary>
    /// Frame rate represented as a numerator / denominator fraction (e.g. 60fps = 1/60).
    /// </summary>
    [Serializable]
    public struct FrameRate : IEquatable<FrameRate>
    {
        public static readonly FrameRate FPS60 = new FrameRate(1, 60);

        /// <summary>
        /// Denominator of the frame rate fraction (e.g. 60 for 60fps).
        /// </summary>
        public uint denominator;

        /// <summary>
        /// Numerator of the frame rate fraction (e.g. 1 for 60fps).
        /// </summary>
        public uint numerator;

        public FrameRate(uint numerator, uint denominator)
        {
            this.numerator = numerator;
            this.denominator = denominator;
        }

        /// <summary>
        /// FPS as a decimal value (e.g. 60.0 for 60fps).
        /// </summary>
        /// <remarks>
        /// The numerator is promoted to double before dividing. Dividing the two uints directly is
        /// integer division, which truncated every rate that is not a whole number of frames per
        /// second: 60000/1001 came back as 59 instead of 59.94, so a caller scaling a time by this
        /// drifted by roughly one frame per second.
        /// </remarks>
        public double AsDecimal()
        {
            return denominator / (double)numerator;
        }

        /// <summary>
        /// Frames per second rounded to a whole number, which is what a timecode counts in.
        ///
        /// A timecode's frame field runs 0..n-1 at the nominal rate -- 0..29 at 29.97, not 0..28.97
        /// -- so a rate that is not a whole number still counts in whole frames and the timecode
        /// drifts from the clock instead. Correcting that drift is what drop-frame is for; this is
        /// the count either way.
        ///
        /// Never zero: a rate slower than one frame a second would otherwise divide by it.
        /// </summary>
        public long framesPerSecondNominal
        {
            get
            {
                if (numerator == 0) return 1;

                var rounded = (denominator + (numerator / 2)) / numerator;
                return rounded < 1 ? 1 : rounded;
            }
        }

        /// <summary>
        /// Convert seconds to a frame number, truncating toward zero.
        /// </summary>
        /// <remarks>
        /// Keep the math in double. Casting to float here lost precision once the frame number
        /// exceeded float's 2^24 integer limit (~3.2 days at 60fps), which made consecutive frame
        /// numbers collapse onto the same value and stuttered the Studio playback buffer routing.
        /// double is exact for integers up to 2^53 (~4700 years at 60fps).
        /// </remarks>
        public long AsFrameNumber(double time)
        {
            return (long)(time * denominator / numerator);
        }

        /// <summary>
        /// Fractional frame component scaled to milli-frames (0..999), wrapped.
        /// </summary>
        public float AsFrameNumberDecimal(double time)
        {
            float ftime = (float)time;
            return Mathf.Repeat((ftime * denominator / numerator) * 1000, 1000);
        }

        /// <summary>
        /// Convert a frame number to seconds.
        /// </summary>
        public double AsSecounds(long frameNumber)
        {
            return (double)frameNumber * numerator / denominator;
        }

        public override string ToString()
        {
            return $"{numerator}/{denominator}";
        }

        public bool Equals(FrameRate other)
        {
            return this.denominator == other.denominator && this.numerator == other.numerator;
        }

        public override bool Equals(object obj)
        {
            if (obj is FrameRate)
            {
                return Equals((FrameRate)obj);
            }
            else
            {
                return false;
            }
        }

        public override int GetHashCode()
        {
            return denominator.GetHashCode() + numerator.GetHashCode();
        }

        public static bool operator ==(FrameRate left, FrameRate right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(FrameRate left, FrameRate right)
        {
            return !left.Equals(right);
        }
    }
}
