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
    /// Lays a value out as bytes and back, so an event can carry what it did rather than the text
    /// that asked for it.
    ///
    /// A property write arrives as a request body, but what it applies is a value of a known type:
    /// a float, a Vector3, a pose. Recorded as text, every one of those costs a parse to read back
    /// and gives up precision on the way. Recorded as bytes it costs a copy, and the bytes are the
    /// same ones the property holds -- which is also what lets a viewer walk a payload with the
    /// machinery it already walks a state element with.
    ///
    /// Text is not an exception to that, only a shape: its bytes are UTF-8, and how many there are
    /// is the record's <see cref="EventRecord.payloadLength"/>. See <see cref="kStringTypeName"/>.
    /// </summary>
    public static class EventPayload
    {
        /// <summary>
        /// A string value, held inline as UTF-8. The record says how long it is.
        ///
        /// Inline, not interned: a payload is a value, and values change per event. Putting them
        /// in the symbol table would add an entry per distinct value, so a text field being typed
        /// into would grow the table for the length of the run.
        /// </summary>
        public const string kStringTypeName = "System.String";

        /// <summary>
        /// The request as it arrived, before anything worked out what it meant. Encoded the same
        /// way as <see cref="kStringTypeName"/>.
        ///
        /// Not a type: it is what a record holds when nothing said what the event applied. Kept
        /// apart from a string value because a replay treats them differently -- one is a value to
        /// write, the other is a request body to dispatch.
        /// </summary>
        public const string kRequestTypeName = "@request";

        private delegate void Packer(object value, Span<byte> destination);

        private delegate object Unpacker(ReadOnlySpan<byte> source);

        private sealed class Layout
        {
            public int size;
            public Packer pack;
            public Unpacker unpack;
        }

        // Reflection is done once per type and the result kept: the same handful of types come
        // through on every event, and MakeGenericMethod is far too slow to sit on that path.
        private static readonly ConcurrentDictionary<Type, Layout> _layouts =
            new ConcurrentDictionary<Type, Layout>();

        private static readonly ConcurrentDictionary<string, Type> _typesByName =
            new ConcurrentDictionary<string, Type>(StringComparer.Ordinal);

        /// <summary>The name a payload of this type is recorded under.</summary>
        public static string NameOf(Type type) => type == null ? null : type.FullName;

        /// <summary>True when the payload under this name is a string value.</summary>
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
            // event either, and the assembly scan behind that answer is not cheap.
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
        /// Writes a boxed value into <paramref name="destination"/>. For a caller that only has the
        /// value as an object -- a REST body parsed against a type found at run time. A caller that
        /// knows the type writes it with <see cref="Write{T}"/> and boxes nothing.
        ///
        /// False when the type has no fixed width, or when the destination is too small -- both
        /// mean the caller should keep the text rather than a partial value.
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

        /// <summary>Writes a value of a known type. The destination must hold <c>sizeof(T)</c> bytes.</summary>
        public static void Write<T>(in T value, Span<byte> destination) where T : unmanaged
        {
            var copy = value;
            MemoryMarshal.Write(destination, ref copy);
        }

        /// <summary>
        /// Reads a value back out as an object. False when the type has no fixed width, or when the
        /// recording holds fewer bytes than it takes -- reading past that would produce a plausible
        /// number out of whatever followed.
        /// </summary>
        public static bool TryUnpack(Type type, ReadOnlySpan<byte> source, out object value)
        {
            value = null;

            var layout = _LayoutOf(type);
            if (layout == null || source.Length < layout.size) return false;

            value = layout.unpack(source);
            return true;
        }

        /// <summary>Bytes a string takes as a payload.</summary>
        public static int ByteCountOf(string text)
            => string.IsNullOrEmpty(text) ? 0 : Encoding.UTF8.GetByteCount(text);

        /// <summary>
        /// Writes a string as a payload. The destination must hold <see cref="ByteCountOf"/> bytes;
        /// returns how many were written.
        /// </summary>
        public static int WriteString(string text, Span<byte> destination)
            => string.IsNullOrEmpty(text) ? 0 : Encoding.UTF8.GetBytes(text.AsSpan(), destination);

        /// <summary>Reads a string payload back. Empty for an empty payload.</summary>
        public static string ReadString(ReadOnlySpan<byte> source)
            => source.Length == 0 ? string.Empty : Encoding.UTF8.GetString(source);

        private static Layout _LayoutOf(Type type)
        {
            if (type == null) return null;
            if (_layouts.TryGetValue(type, out var cached)) return cached;

            var built = _Build(type);

            // Nulls are cached as a marker entry rather than left out, so a type that cannot be
            // laid out is not re-examined on every event that mentions it.
            _layouts[type] = built;
            return built;
        }

        private static Layout _Build(Type type)
        {
            if (!_IsUnmanaged(type)) return null;

            var packMethod = typeof(EventPayload)
                .GetMethod(nameof(_PackValue), BindingFlags.NonPublic | BindingFlags.Static)
                .MakeGenericMethod(type);

            var unpackMethod = typeof(EventPayload)
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
