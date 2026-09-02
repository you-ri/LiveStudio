// Copyright (c) You-Ri, 2026
using System;
using System.Text;

namespace Lilium.RemoteControl.Frames
{
    /// <summary>
    /// The text stored inside a state block, and the rules for putting it there and taking it back.
    ///
    /// The state lane carries a fixed-width copy of every declared member on every frame, which text
    /// cannot join as <c>string</c>: a reference points out of the block, and a block that points
    /// anywhere is no longer something a frame can move as bytes. So a member declared in the state
    /// lane keeps its <c>string</c> face -- what REST answers, what the scene file holds, what the
    /// author wrote -- and the block holds a fixed number of UTF-8 bytes standing in for it. The
    /// conversion lives here and in the generated movers, and nowhere else sees it.
    ///
    /// <para>
    /// ⚠ The width is asserted by whoever declared the member, and a value that outgrows it cannot
    /// be carried. It is not truncated: a shortened value is one nobody ever set, and unlike a
    /// missing value it would be written back on replay and agree with itself under comparison. The
    /// slot is marked <see cref="kUnrepresentable"/> instead, which the apply side reads as "say
    /// nothing" and leaves the target as it stands. That is a hole in what the recording carries,
    /// and it is meant to be a visible one -- see <see cref="LiveFixedStringStats"/>.
    /// </para>
    /// </summary>
    internal static unsafe class FixedText
    {
        /// <summary>The member held no string. Distinct from an empty one, which REST can tell apart.</summary>
        public const ushort kNull = 0xFFFF;

        /// <summary>
        /// The value did not fit the width the declaration asked for, so the slot says nothing at
        /// all rather than saying something shorter than the truth.
        /// </summary>
        public const ushort kUnrepresentable = 0xFFFE;

        /// <summary>Longest text any width may hold, kept below the two markers.</summary>
        public const int kMaxCapacity = 0xFFFD;

        /// <summary>
        /// Puts a string in the buffer and returns what the length field should say.
        ///
        /// Measured before it is written, because the encoder's own overflow is an exception and
        /// this is an ordinary outcome -- a declaration whose width is too small is a mistake to
        /// report, not a frame to fail.
        /// </summary>
        public static ushort Write(string value, byte* buffer, int capacity)
        {
            if (value == null) return kNull;
            if (value.Length == 0) return 0;

            var byteCount = Encoding.UTF8.GetByteCount(value);
            if (byteCount > capacity)
            {
                LiveFixedStringStats.CountUnrepresentable();
                return kUnrepresentable;
            }

            fixed (char* chars = value)
            {
                Encoding.UTF8.GetBytes(chars, value.Length, buffer, capacity);
            }

            return (ushort)byteCount;
        }

        /// <summary>
        /// Hands back the stored string, unless there is nothing to say or the target already holds
        /// it.
        ///
        /// The second case is why <paramref name="current"/> is asked for at all. The state lane
        /// writes every member every frame, so a replay would otherwise run a setter sixty times a
        /// second for a value that has not moved -- and a setter behind an asset reference answers
        /// that by loading. Comparing first also keeps the common frame allocation-free, since the
        /// string is only built when it is actually going to be assigned.
        /// </summary>
        public static bool TryRead(ushort length, byte* buffer, string current, out string value)
        {
            value = null;

            if (length == kUnrepresentable) return false;

            if (length == kNull)
            {
                if (current == null) return false;
                return true;
            }

            if (Matches(length, buffer, current)) return false;

            value = length == 0 ? string.Empty : Encoding.UTF8.GetString(buffer, length);
            return true;
        }

        /// <summary>Whether the stored bytes are the ones this string would encode to.</summary>
        public static bool Matches(ushort length, byte* buffer, string other)
        {
            if (other == null) return false;
            if (length == 0) return other.Length == 0;
            if (Encoding.UTF8.GetByteCount(other) != length) return false;

            // Encoded into a scratch buffer rather than decoding the stored bytes: decoding would
            // allocate the string this comparison exists to avoid allocating.
            var scratch = stackalloc byte[length];
            fixed (char* chars = other)
            {
                Encoding.UTF8.GetBytes(chars, other.Length, scratch, length);
            }

            for (int i = 0; i < length; i++)
            {
                if (scratch[i] != buffer[i]) return false;
            }

            return true;
        }

        /// <summary>The stored string, for display and for tests. Null and unrepresentable both read as null.</summary>
        public static string Read(ushort length, byte* buffer)
        {
            if (length == kNull || length == kUnrepresentable) return null;
            return length == 0 ? string.Empty : Encoding.UTF8.GetString(buffer, length);
        }
    }

    /// <summary>
    /// How often a state-lane string did not fit the width its declaration asked for.
    ///
    /// Counted rather than logged per occurrence: an undersized width is a property of the
    /// declaration, so it recurs every frame for the length of a take and a warning would say the
    /// same thing sixty times a second. A recording made while this was climbing is one whose state
    /// lane is missing a member, which is worth showing next to the recorder.
    /// </summary>
    public static class LiveFixedStringStats
    {
        private static long _unrepresentableCount;

        /// <summary>Values passed over because they outgrew their width, since the last reset.</summary>
        public static long unrepresentableCount => _unrepresentableCount;

        /// <summary>Forgets the count. For tests and for the start of a recording.</summary>
        public static void Reset() => _unrepresentableCount = 0;

        internal static void CountUnrepresentable() => _unrepresentableCount++;
    }

    /// <summary>Up to 32 UTF-8 bytes of text inside a state block. See <see cref="FixedText"/>.</summary>
    public unsafe struct LiveFixedString32 : IEquatable<LiveFixedString32>
    {
        /// <summary>UTF-8 bytes this width holds. Not characters -- a kanji costs three.</summary>
        public const int kCapacity = 32;

        private ushort _length;
        private fixed byte _utf8[kCapacity];

        /// <inheritdoc cref="FixedText.Write"/>
        public static LiveFixedString32 From(string value)
        {
            var result = default(LiveFixedString32);
            result.Set(value);
            return result;
        }

        /// <inheritdoc cref="FixedText.Write"/>
        public void Set(string value)
        {
            fixed (byte* buffer = _utf8) _length = FixedText.Write(value, buffer, kCapacity);
        }

        /// <inheritdoc cref="FixedText.TryRead"/>
        public bool TryGetValue(string current, out string value)
        {
            fixed (byte* buffer = _utf8) return FixedText.TryRead(_length, buffer, current, out value);
        }

        /// <summary>Whether this slot carries nothing because the value outgrew the width.</summary>
        public bool isUnrepresentable => _length == FixedText.kUnrepresentable;

        public override string ToString()
        {
            fixed (byte* buffer = _utf8) return FixedText.Read(_length, buffer);
        }

        public bool Equals(LiveFixedString32 other)
        {
            if (_length != other._length) return false;
            if (_length == FixedText.kNull || _length == FixedText.kUnrepresentable) return true;

            fixed (byte* mine = _utf8)
            {
                for (int i = 0; i < _length; i++)
                {
                    if (mine[i] != other._utf8[i]) return false;
                }
            }

            return true;
        }

        public override bool Equals(object obj) => obj is LiveFixedString32 other && Equals(other);

        public override int GetHashCode()
        {
            var hash = (int)_length;
            if (_length == FixedText.kNull || _length == FixedText.kUnrepresentable) return hash;

            fixed (byte* buffer = _utf8)
            {
                for (int i = 0; i < _length; i++) hash = hash * 31 + buffer[i];
            }

            return hash;
        }
    }

    /// <summary>Up to 64 UTF-8 bytes of text inside a state block. See <see cref="FixedText"/>.</summary>
    public unsafe struct LiveFixedString64 : IEquatable<LiveFixedString64>
    {
        /// <inheritdoc cref="LiveFixedString32.kCapacity"/>
        public const int kCapacity = 64;

        private ushort _length;
        private fixed byte _utf8[kCapacity];

        /// <inheritdoc cref="FixedText.Write"/>
        public static LiveFixedString64 From(string value)
        {
            var result = default(LiveFixedString64);
            result.Set(value);
            return result;
        }

        /// <inheritdoc cref="FixedText.Write"/>
        public void Set(string value)
        {
            fixed (byte* buffer = _utf8) _length = FixedText.Write(value, buffer, kCapacity);
        }

        /// <inheritdoc cref="FixedText.TryRead"/>
        public bool TryGetValue(string current, out string value)
        {
            fixed (byte* buffer = _utf8) return FixedText.TryRead(_length, buffer, current, out value);
        }

        /// <inheritdoc cref="LiveFixedString32.isUnrepresentable"/>
        public bool isUnrepresentable => _length == FixedText.kUnrepresentable;

        public override string ToString()
        {
            fixed (byte* buffer = _utf8) return FixedText.Read(_length, buffer);
        }

        public bool Equals(LiveFixedString64 other)
        {
            if (_length != other._length) return false;
            if (_length == FixedText.kNull || _length == FixedText.kUnrepresentable) return true;

            fixed (byte* mine = _utf8)
            {
                for (int i = 0; i < _length; i++)
                {
                    if (mine[i] != other._utf8[i]) return false;
                }
            }

            return true;
        }

        public override bool Equals(object obj) => obj is LiveFixedString64 other && Equals(other);

        public override int GetHashCode()
        {
            var hash = (int)_length;
            if (_length == FixedText.kNull || _length == FixedText.kUnrepresentable) return hash;

            fixed (byte* buffer = _utf8)
            {
                for (int i = 0; i < _length; i++) hash = hash * 31 + buffer[i];
            }

            return hash;
        }
    }

    /// <summary>Up to 128 UTF-8 bytes of text inside a state block. See <see cref="FixedText"/>.</summary>
    public unsafe struct LiveFixedString128 : IEquatable<LiveFixedString128>
    {
        /// <inheritdoc cref="LiveFixedString32.kCapacity"/>
        public const int kCapacity = 128;

        private ushort _length;
        private fixed byte _utf8[kCapacity];

        /// <inheritdoc cref="FixedText.Write"/>
        public static LiveFixedString128 From(string value)
        {
            var result = default(LiveFixedString128);
            result.Set(value);
            return result;
        }

        /// <inheritdoc cref="FixedText.Write"/>
        public void Set(string value)
        {
            fixed (byte* buffer = _utf8) _length = FixedText.Write(value, buffer, kCapacity);
        }

        /// <inheritdoc cref="FixedText.TryRead"/>
        public bool TryGetValue(string current, out string value)
        {
            fixed (byte* buffer = _utf8) return FixedText.TryRead(_length, buffer, current, out value);
        }

        /// <inheritdoc cref="LiveFixedString32.isUnrepresentable"/>
        public bool isUnrepresentable => _length == FixedText.kUnrepresentable;

        public override string ToString()
        {
            fixed (byte* buffer = _utf8) return FixedText.Read(_length, buffer);
        }

        public bool Equals(LiveFixedString128 other)
        {
            if (_length != other._length) return false;
            if (_length == FixedText.kNull || _length == FixedText.kUnrepresentable) return true;

            fixed (byte* mine = _utf8)
            {
                for (int i = 0; i < _length; i++)
                {
                    if (mine[i] != other._utf8[i]) return false;
                }
            }

            return true;
        }

        public override bool Equals(object obj) => obj is LiveFixedString128 other && Equals(other);

        public override int GetHashCode()
        {
            var hash = (int)_length;
            if (_length == FixedText.kNull || _length == FixedText.kUnrepresentable) return hash;

            fixed (byte* buffer = _utf8)
            {
                for (int i = 0; i < _length; i++) hash = hash * 31 + buffer[i];
            }

            return hash;
        }
    }

    /// <summary>Up to 256 UTF-8 bytes of text inside a state block. See <see cref="FixedText"/>.</summary>
    public unsafe struct LiveFixedString256 : IEquatable<LiveFixedString256>
    {
        /// <inheritdoc cref="LiveFixedString32.kCapacity"/>
        public const int kCapacity = 256;

        private ushort _length;
        private fixed byte _utf8[kCapacity];

        /// <inheritdoc cref="FixedText.Write"/>
        public static LiveFixedString256 From(string value)
        {
            var result = default(LiveFixedString256);
            result.Set(value);
            return result;
        }

        /// <inheritdoc cref="FixedText.Write"/>
        public void Set(string value)
        {
            fixed (byte* buffer = _utf8) _length = FixedText.Write(value, buffer, kCapacity);
        }

        /// <inheritdoc cref="FixedText.TryRead"/>
        public bool TryGetValue(string current, out string value)
        {
            fixed (byte* buffer = _utf8) return FixedText.TryRead(_length, buffer, current, out value);
        }

        /// <inheritdoc cref="LiveFixedString32.isUnrepresentable"/>
        public bool isUnrepresentable => _length == FixedText.kUnrepresentable;

        public override string ToString()
        {
            fixed (byte* buffer = _utf8) return FixedText.Read(_length, buffer);
        }

        public bool Equals(LiveFixedString256 other)
        {
            if (_length != other._length) return false;
            if (_length == FixedText.kNull || _length == FixedText.kUnrepresentable) return true;

            fixed (byte* mine = _utf8)
            {
                for (int i = 0; i < _length; i++)
                {
                    if (mine[i] != other._utf8[i]) return false;
                }
            }

            return true;
        }

        public override bool Equals(object obj) => obj is LiveFixedString256 other && Equals(other);

        public override int GetHashCode()
        {
            var hash = (int)_length;
            if (_length == FixedText.kNull || _length == FixedText.kUnrepresentable) return hash;

            fixed (byte* buffer = _utf8)
            {
                for (int i = 0; i < _length; i++) hash = hash * 31 + buffer[i];
            }

            return hash;
        }
    }
}
