// Copyright (c) You-Ri, 2026

using System;

namespace Lilium.RemoteControl
{
    /// <summary>
    /// SMPTE-style timecode (HH:MM:SS:FF.fff) backed by a <see cref="FrameRate"/>.
    /// </summary>
    public struct Timecode : IEquatable<Timecode>
    {
        public bool dropFrameFormat;

        /// <summary>
        /// Sub-frame component represented in milli-frames (0..999).
        /// </summary>
        public int decimalFrames;

        /// <summary>
        /// Frame component within the current second (0..fps-1).
        /// </summary>
        public int frames;

        public int hours;

        public int minutes;

        public int seconds;

        public Timecode(int hours, int minutes, int seconds, int frames, int decimalFrames = 0, bool dropFrameFormat = false)
        {
            this.hours = hours;
            this.minutes = minutes;
            this.seconds = seconds;
            this.frames = frames;
            this.decimalFrames = decimalFrames;
            this.dropFrameFormat = dropFrameFormat;
        }

        public Timecode(double time, FrameRate frameRate, bool dropFrameFormat = false)
        {
            int itime = (int)(time);
            hours = (itime / 60 / 60);
            minutes = (itime / 60) % 60;
            seconds = itime % 60;
            frames = (int)(frameRate.AsFrameNumber(time - itime) % frameRate.denominator);
            decimalFrames = (int)(frameRate.AsFrameNumberDecimal(time));
            this.dropFrameFormat = dropFrameFormat;
        }

        public Timecode(long frameCount, FrameRate frameRate, bool dropFrameFormat = false)
        {
            double time = frameRate.AsSecounds(frameCount);
            int itime = (int)time;
            hours = (itime / 60 / 60);
            minutes = (itime / 60) % 60;
            seconds = itime % 60;
            frames = (int)(frameRate.AsFrameNumber(time - itime) % frameRate.denominator);
            decimalFrames = 0;
            this.dropFrameFormat = dropFrameFormat;
        }

        /// <summary>
        /// Total frame count represented by this timecode.
        /// </summary>
        public long ToFrameNumber(FrameRate frameRate)
        {
            return frameRate.AsFrameNumber((hours * 60 * 60) + (minutes * 60) + seconds) + frames;
        }

        /// <summary>
        /// Total elapsed seconds represented by this timecode.
        /// </summary>
        public double ToSecounds(FrameRate frameRate)
        {
            return frameRate.AsSecounds(ToFrameNumber(frameRate));
        }

        public override string ToString()
        {
            return $"{hours:00}:{minutes:00}:{seconds:00}:{frames:00}.{decimalFrames:000}";
        }

        public bool Equals(Timecode other)
        {
            return this.frames == other.frames && this.seconds == other.seconds && this.minutes == other.minutes && this.hours == other.hours && this.dropFrameFormat == other.dropFrameFormat && this.decimalFrames == other.decimalFrames;
        }

        public override bool Equals(object obj)
        {
            if (obj is Timecode)
            {
                return Equals((Timecode)obj);
            }
            else
            {
                return false;
            }
        }

        public override int GetHashCode()
        {
            return dropFrameFormat.GetHashCode() + seconds.GetHashCode() + minutes.GetHashCode() + hours.GetHashCode() + frames.GetHashCode() + decimalFrames.GetHashCode();
        }

        public static bool operator ==(Timecode left, Timecode right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(Timecode left, Timecode right)
        {
            return !left.Equals(right);
        }
    }
}
