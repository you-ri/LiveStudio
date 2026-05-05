// Copyright (c) You-Ri, 2026
// Consolidated from Lilium.Virgo.FrameRate into Lilium.RemoteControl.
// Kept original truncation semantics, FPS60 constant, and AsFrameNumberDecimal helper.

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
        public double AsDecimal()
        {
            return denominator / numerator;
        }

        /// <summary>
        /// Convert seconds to a frame number, truncating toward zero.
        /// </summary>
        public long AsFrameNumber(double time)
        {
            float ftime = (float)time;
            return (int)((ftime) * denominator / numerator);
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
