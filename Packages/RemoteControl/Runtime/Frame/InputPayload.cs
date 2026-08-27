// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Unity.Collections.LowLevel.Unsafe;

namespace Lilium.RemoteControl.Frames
{
    /// <summary>
    /// Lays a value out as bytes and back, so an input can carry what it did rather than the text
    /// that asked for it.
    ///
    /// A property write arrives as a request body, but what it applies is a value of a known type:
    /// a float, a Vector3, a pose. Recorded as text, every one of those costs a parse to read back
    /// and gives up precision on the way. Recorded as bytes it costs a copy, and the bytes are the
    /// same ones the property holds -- which is also what lets a viewer walk a payload with the
    /// machinery it already walks a state element with.
    ///
    /// Text is not an exception to that, only a shape: a filename has no fixed width, so it travels
    /// as a length-prefixed string -- the byte count first, then the UTF-8 -- and is read back the
    /// same way. See <see cref="kStringTypeName"/>.
    /// </summary>
    public static class InputPayload
    {
        /// <summary>
        /// A string value, held inline as a length-prefixed string: a two-byte UTF-8 length
        /// followed by that many bytes.
        ///
        /// The length goes in front so the bytes describe themselves. A reader knows where the
        /// text ends without scanning for a terminator, and the same encoding nests -- a string
        /// inside a payload that holds more than one thing still says where it stops.
        ///
        /// Inline, not interned: a payload is a value, and values change per input. Putting them
        /// in the symbol table would add an entry per distinct value, so a text field being typed
        /// into would grow the table for the length of the run.
        /// </summary>
        public const string kStringTypeName = "System.String";

        /// <summary>
        /// The request as it arrived, before anything worked out what it meant. Encoded the same
        /// way as <see cref="kStringTypeName"/>.
        ///
        /// Not a type: it is what a record holds when nothing said what the input applied. Kept
        /// apart from a string value because a replay treats them differently -- one is a value to
        /// write, the other is a request body to dispatch.
        /// </summary>
        public const string kRequestTypeName = "@request";

        /// <summary>Bytes the length prefix occupies. Two, because a payload cannot exceed 64 KB.</summary>
        public const int kLengthPrefixSize = 2;

        private delegate void Packer(object value, Span<byte> destination);

        private delegate object Unpacker(ReadOnlySpan<byte> source);

        private sealed class Layout
        {
            public int size;
            public Packer pack;
            public Unpacker unpack;
        }

        // Reflection is done once per type and the result kept: the same handful of types come
        // through on every frame, and MakeGenericMethod is far too slow to sit on that path.
        private static readonly ConcurrentDictionary<Type, Layout> _layouts =
            new ConcurrentDictionary<Type, Layout>();

        private static readonly ConcurrentDictionary<string, Type> _typesByName =
            new ConcurrentDictionary<string, Type>(StringComparer.Ordinal);

        /// <summary>The name a payload of this type is recorded under.</summary>
        public static string NameOf(Type type) => type == null ? null : type.FullName;

        /// <summary>True when the payload under this name is a length-prefixed string value.</summary>
        public static bool IsString(string typeName) => typeName == kStringTypeName;

        /// <summary>True when the bytes under this name are an unexplained request body.</summary>
        public static bool IsRequest(string typeName) => typeName == kRequestTypeName;

        /// <summary>True when the payload is text of some kind rather than a laid-out value.</summary>
        public static bool IsTextual(string typeName) => IsString(typeName) || IsRequest(typeName);

        /// <summary>
        /// Finds the type a recorded name refers to, or null when this build does not have it.
        ///
        /// Null is an answer, not a failure: a recording can name a type that has since been
        /// removed, and refusing to read the rest of it over that would be worse than saying so.
        /// </summary>
        public static Type Resolve(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return null;
            if (_typesByName.TryGetValue(typeName, out var cached)) return cached;

            var found = Type.GetType(typeName, throwOnError: false);

            if (found == null)
            {
                var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                for (int i = 0; i < assemblies.Length && found == null; i++)
                {
                    found = assemblies[i].GetType(typeName, throwOnError: false);
                }
            }

            // Misses are cached too. A name that is not here now will not be here on the next
            // frame either, and the assembly scan behind that answer is not cheap.
            _typesByName[typeName] = found;
            return found;
        }

        /// <summary>
        /// Bytes this type occupies when written, or -1 when it has no fixed width and has to
        /// travel as text.
        /// </summary>
        public static int SizeOf(Type type)
        {
            var layout = _LayoutOf(type);
            return layout?.size ?? -1;
        }

        /// <summary>
        /// Writes <paramref name="value"/> into <paramref name="destination"/>. False when the type
        /// has no fixed width, or when the value does not fit -- both mean the caller should fall
        /// back to text rather than keep a partial value.
        /// </summary>
        public static bool TryPack(Type type, object value, Span<byte> destination, out int written)
        {
            written = 0;
            if (value == null) return false;

            var layout = _LayoutOf(type);
            if (layout == null || layout.size > destination.Length) return false;

            layout.pack(value, destination);
            written = layout.size;
            return true;
        }

        /// <summary>
        /// Reads a value back out. False when the type has no fixed width, or when the recording
        /// holds fewer bytes than it takes -- reading past that would produce a plausible number
        /// out of whatever followed.
        /// </summary>
        public static bool TryUnpack(Type type, ReadOnlySpan<byte> source, out object value)
        {
            value = null;

            var layout = _LayoutOf(type);
            if (layout == null || source.Length < layout.size) return false;

            value = layout.unpack(source);
            return true;
        }

        /// <summary>
        /// Writes a length-prefixed string: a two-byte UTF-8 length, then that many bytes.
        ///
        /// False when the text did not all fit, in which case <paramref name="written"/> covers
        /// what was kept and the prefix says so -- the value is still readable, just shorter than
        /// what arrived. The caller decides whether that matters, because half a filename is a
        /// different problem from half a log line.
        /// </summary>
        public static bool TryWriteString(string text, Span<byte> destination, out int written)
        {
            written = 0;
            if (destination.Length < kLengthPrefixSize) return false;

            var body = destination.Slice(kLengthPrefixSize);
            var length = 0;
            var fits = true;

            if (!string.IsNullOrEmpty(text))
            {
                var needed = Encoding.UTF8.GetByteCount(text);

                if (needed <= body.Length)
                {
                    length = Encoding.UTF8.GetBytes(text, body);
                }
                else
                {
                    // Cut on a character boundary, so what is kept is still readable text rather
                    // than a string ending in half a rune.
                    var kept = _FitCharacters(text, body.Length);
                    length = Encoding.UTF8.GetBytes(text.AsSpan(0, kept), body);
                    fits = false;
                }
            }

            var prefix = (ushort)length;
            MemoryMarshal.Write(destination, ref prefix);

            written = kLengthPrefixSize + length;
            return fits;
        }

        /// <summary>
        /// Reads a length-prefixed string back, or null when the payload cannot hold one.
        ///
        /// The prefix is trusted only as far as what is actually there: a truncated file would
        /// otherwise have its last record read past the end of its own bytes.
        /// </summary>
        public static string ReadString(ReadOnlySpan<byte> source)
        {
            if (source.Length < kLengthPrefixSize) return null;

            int length = MemoryMarshal.Read<ushort>(source);
            var available = source.Length - kLengthPrefixSize;
            if (length > available) length = available;

            return length == 0
                ? string.Empty
                : Encoding.UTF8.GetString(source.Slice(kLengthPrefixSize, length));
        }

        private static int _FitCharacters(string text, int budget)
        {
            var encoder = Encoding.UTF8;
            int low = 0, high = text.Length;

            while (low < high)
            {
                var middle = (low + high + 1) / 2;
                if (encoder.GetByteCount(text.AsSpan(0, middle)) <= budget) low = middle;
                else high = middle - 1;
            }

            return low;
        }

        private static Layout _LayoutOf(Type type)
        {
            if (type == null) return null;
            if (_layouts.TryGetValue(type, out var cached)) return cached;

            var built = _Build(type);

            // Nulls are cached as a marker entry rather than left out, so a type that cannot be
            // laid out is not re-examined on every input that mentions it.
            _layouts[type] = built;
            return built;
        }

        private static Layout _Build(Type type)
        {
            if (!_IsUnmanaged(type)) return null;

            var packMethod = typeof(InputPayload)
                .GetMethod(nameof(_PackValue), BindingFlags.NonPublic | BindingFlags.Static)
                .MakeGenericMethod(type);

            var unpackMethod = typeof(InputPayload)
                .GetMethod(nameof(_UnpackValue), BindingFlags.NonPublic | BindingFlags.Static)
                .MakeGenericMethod(type);

            return new Layout
            {
                // Unity's, not Marshal's: the bytes written are the ones the type occupies in
                // memory, and Marshal.SizeOf would report the interop width instead -- four bytes
                // for a bool that MemoryMarshal writes as one.
                size = UnsafeUtility.SizeOf(type),
                pack = (Packer)packMethod.CreateDelegate(typeof(Packer)),
                unpack = (Unpacker)unpackMethod.CreateDelegate(typeof(Unpacker)),
            };
        }

        private static void _PackValue<T>(object value, Span<byte> destination) where T : unmanaged
        {
            var typed = (T)value;
            MemoryMarshal.Write(destination, ref typed);
        }

        private static object _UnpackValue<T>(ReadOnlySpan<byte> source) where T : unmanaged
            => MemoryMarshal.Read<T>(source);

        /// <summary>
        /// Whether the type is all value, all the way down. Written out rather than taken from
        /// UnsafeUtility so it reads the same on every Unity version this package supports.
        /// </summary>
        private static bool _IsUnmanaged(Type type)
        {
            if (!type.IsValueType || type.IsGenericTypeDefinition) return false;
            if (type.IsPrimitive || type.IsEnum || type.IsPointer) return true;

            var fields = type.GetFields(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            for (int i = 0; i < fields.Length; i++)
            {
                if (!_IsUnmanaged(fields[i].FieldType)) return false;
            }

            return true;
        }
    }
}
