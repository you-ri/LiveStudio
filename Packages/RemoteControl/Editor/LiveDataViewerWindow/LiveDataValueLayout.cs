// Copyright (c) You-Ri, 2026

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace Lilium.RemoteControl.Editor.LiveDataViewer
{
    /// <summary>One readable line of a value: where it sits, and how to read it.</summary>
    internal sealed class ValueField
    {
        /// <summary>Dotted path from the value's root, e.g. <c>pose.hipPosition</c>.</summary>
        public string path;

        /// <summary>Indentation, so the tree reads as a tree.</summary>
        public int depth;

        /// <summary>Byte offset from the start of the value.</summary>
        public int offset;

        /// <summary>What sits there. Null for a heading with nothing of its own to show.</summary>
        public Type type;

        /// <summary>Element type of a fixed buffer, or null.</summary>
        public Type bufferElementType;

        /// <summary>How many elements a fixed buffer holds.</summary>
        public int bufferLength;

        /// <summary>Set when the row is a group rather than a value.</summary>
        public bool isHeading;
    }

    /// <summary>
    /// Turns a value type into a flat list of readable lines, once per type.
    ///
    /// The elements on the state lane are unmanaged structs, and what the viewer holds of one is its
    /// bytes. Reading a field means knowing its offset and its type, which the walk works out here
    /// and keeps -- doing it per redraw would reflect over the whole struct twenty times a second.
    ///
    /// ⚠ What reflection can say about these types is not the whole story. A fixed buffer declares
    /// only its element type and length, so <c>fixed byte[880]</c> is 880 bytes as far as the type
    /// system is concerned -- the fact that it is 55 quaternions in bone order lives in the code that
    /// reads it, not in the type. Those need a presenter (see <see cref="LiveDataValuePresenters"/>);
    /// this walk is what everything else gets for free.
    /// </summary>
    internal static class LiveDataValueLayout
    {
        /// <summary>Nesting the walk will follow before it stops describing and starts summarising.</summary>
        private const int kMaxDepth = 6;

        /// <summary>Elements of a fixed buffer written out one by one before the rest is summarised.</summary>
        public const int kBufferPreview = 64;

        private static readonly Dictionary<Type, List<ValueField>> _cache =
            new Dictionary<Type, List<ValueField>>();

        // Read as one line rather than walked into: three floats named x, y, z are more legible
        // together than as three rows, and everything here is a value everyone already pictures.
        private static readonly HashSet<Type> _inlineTypes = new HashSet<Type>
        {
            typeof(Vector2), typeof(Vector3), typeof(Vector4), typeof(Quaternion),
            typeof(Color), typeof(Color32), typeof(Vector2Int), typeof(Vector3Int),
        };

        /// <summary>The lines of a type, worked out on first use.</summary>
        public static List<ValueField> For(Type type)
        {
            if (type == null) return null;
            if (_cache.TryGetValue(type, out var cached)) return cached;

            var fields = new List<ValueField>();
            try
            {
                _Walk(type, "", 0, 0, fields);
            }
            catch (Exception e)
            {
                // A type the walk cannot describe is still worth showing as bytes, so this fails to
                // an empty list rather than taking the window down.
                Debug.LogWarning($"[RemoteControl] Could not describe '{type.FullName}': {e.Message}");
                fields.Clear();
            }

            _cache[type] = fields;
            return fields;
        }

        /// <summary>True for a type this can read a single number out of.</summary>
        public static bool IsReadable(Type type)
            => type != null && (type.IsPrimitive || type.IsEnum || _inlineTypes.Contains(type));

        private static void _Walk(Type type, string prefix, int depth, int offset, List<ValueField> into)
        {
            var members = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            for (int i = 0; i < members.Length; i++)
            {
                var field = members[i];
                var path = string.IsNullOrEmpty(prefix) ? field.Name : prefix + "." + field.Name;
                var at = offset + UnsafeUtility.GetFieldOffset(field);

                var buffer = field.GetCustomAttribute<FixedBufferAttribute>();
                if (buffer != null)
                {
                    into.Add(new ValueField
                    {
                        path = path,
                        depth = depth,
                        offset = at,
                        type = field.FieldType,
                        bufferElementType = buffer.ElementType,
                        bufferLength = buffer.Length,
                    });
                    continue;
                }

                if (IsReadable(field.FieldType))
                {
                    into.Add(new ValueField
                    {
                        path = path,
                        depth = depth,
                        offset = at,
                        type = field.FieldType,
                    });
                    continue;
                }

                if (!field.FieldType.IsValueType || depth >= kMaxDepth)
                {
                    into.Add(new ValueField
                    {
                        path = path,
                        depth = depth,
                        offset = at,
                        type = field.FieldType,
                        isHeading = true,
                    });
                    continue;
                }

                into.Add(new ValueField
                {
                    path = path,
                    depth = depth,
                    offset = at,
                    type = field.FieldType,
                    isHeading = true,
                });

                _Walk(field.FieldType, path, depth + 1, at, into);
            }
        }

        /// <summary>
        /// Reads one field out of a value's bytes. Returns an empty string when the bytes stop short,
        /// which happens whenever a recording was made by a build whose layout has since moved.
        /// </summary>
        public static string Read(byte[] bytes, int length, ValueField field)
        {
            if (bytes == null || field == null) return string.Empty;

            if (field.bufferElementType != null) return _ReadBuffer(bytes, length, field);
            if (field.isHeading) return string.Empty;

            return _ReadOne(bytes, length, field.offset, field.type);
        }

        private static string _ReadOne(byte[] bytes, int length, int offset, Type type)
        {
            if (type == null) return string.Empty;
            if (offset < 0 || offset >= length) return string.Empty;

            if (type.IsEnum)
            {
                var underlying = Enum.GetUnderlyingType(type);
                var raw = _ReadOne(bytes, length, offset, underlying);
                if (string.IsNullOrEmpty(raw)) return string.Empty;
                if (!long.TryParse(raw, out var value)) return raw;

                var name = Enum.GetName(type, Convert.ChangeType(value, underlying));
                return name ?? raw;
            }

            if (type == typeof(float)) return _Fits(offset, 4, length) ? BitConverter.ToSingle(bytes, offset).ToString("0.#####") : "";
            if (type == typeof(double)) return _Fits(offset, 8, length) ? BitConverter.ToDouble(bytes, offset).ToString("0.#####") : "";
            if (type == typeof(int)) return _Fits(offset, 4, length) ? BitConverter.ToInt32(bytes, offset).ToString() : "";
            if (type == typeof(uint)) return _Fits(offset, 4, length) ? BitConverter.ToUInt32(bytes, offset).ToString() : "";
            if (type == typeof(long)) return _Fits(offset, 8, length) ? BitConverter.ToInt64(bytes, offset).ToString() : "";
            if (type == typeof(ulong)) return _Fits(offset, 8, length) ? BitConverter.ToUInt64(bytes, offset).ToString() : "";
            if (type == typeof(short)) return _Fits(offset, 2, length) ? BitConverter.ToInt16(bytes, offset).ToString() : "";
            if (type == typeof(ushort)) return _Fits(offset, 2, length) ? BitConverter.ToUInt16(bytes, offset).ToString() : "";
            if (type == typeof(byte)) return bytes[offset].ToString();
            if (type == typeof(sbyte)) return ((sbyte)bytes[offset]).ToString();
            if (type == typeof(bool)) return bytes[offset] != 0 ? "true" : "false";

            if (type == typeof(Vector2)) return _Floats(bytes, length, offset, 2);
            if (type == typeof(Vector3)) return _Floats(bytes, length, offset, 3);
            if (type == typeof(Vector4) || type == typeof(Quaternion) || type == typeof(Color))
                return _Floats(bytes, length, offset, 4);
            if (type == typeof(Vector2Int)) return _Ints(bytes, length, offset, 2);
            if (type == typeof(Vector3Int)) return _Ints(bytes, length, offset, 3);
            if (type == typeof(Color32))
                return _Fits(offset, 4, length)
                    ? $"{bytes[offset]}, {bytes[offset + 1]}, {bytes[offset + 2]}, {bytes[offset + 3]}"
                    : "";

            return string.Empty;
        }

        private static string _ReadBuffer(byte[] bytes, int length, ValueField field)
        {
            var element = field.bufferElementType;
            var stride = _SizeOf(element);
            if (stride <= 0) return $"{field.bufferLength} × {element?.Name}";

            var shown = Math.Min(field.bufferLength, kBufferPreview);
            var parts = new List<string>(shown);

            for (int i = 0; i < shown; i++)
            {
                var at = field.offset + i * stride;
                if (!_Fits(at, stride, length)) break;

                parts.Add(_ReadOne(bytes, length, at, element));
            }

            var text = string.Join(", ", parts);
            return field.bufferLength > shown ? text + $", … (+{field.bufferLength - shown})" : text;
        }

        private static int _SizeOf(Type type)
        {
            if (type == typeof(byte) || type == typeof(sbyte) || type == typeof(bool)) return 1;
            if (type == typeof(short) || type == typeof(ushort)) return 2;
            if (type == typeof(int) || type == typeof(uint) || type == typeof(float)) return 4;
            if (type == typeof(long) || type == typeof(ulong) || type == typeof(double)) return 8;
            return 0;
        }

        private static bool _Fits(int offset, int size, int length) => offset >= 0 && offset + size <= length;

        private static string _Floats(byte[] bytes, int length, int offset, int count)
        {
            if (!_Fits(offset, 4 * count, length)) return string.Empty;

            var parts = new string[count];
            for (int i = 0; i < count; i++) parts[i] = BitConverter.ToSingle(bytes, offset + i * 4).ToString("0.###");
            return string.Join(", ", parts);
        }

        private static string _Ints(byte[] bytes, int length, int offset, int count)
        {
            if (!_Fits(offset, 4 * count, length)) return string.Empty;

            var parts = new string[count];
            for (int i = 0; i < count; i++) parts[i] = BitConverter.ToInt32(bytes, offset + i * 4).ToString();
            return string.Join(", ", parts);
        }
    }
}
