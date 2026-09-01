// Copyright (c) You-Ri, 2026

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using Lilium.RemoteControl.Frames;

namespace Lilium.RemoteControl.Editor.LiveDataViewer
{
    /// <summary>One readable line of a value: what to call it, where it sits, and how to read it.</summary>
    internal sealed class ValueField
    {
        /// <summary>What the line is called. A field name, or an element's label.</summary>
        public string label;

        /// <summary>Full path from the value's root, for the tooltip.</summary>
        public string path;

        /// <summary>Indentation, so the tree reads as a tree.</summary>
        public int depth;

        /// <summary>Byte offset from the start of the value.</summary>
        public int offset;

        /// <summary>What sits there. Null for a heading with nothing of its own to show.</summary>
        public Type type;

        /// <summary>Set when the row groups the ones under it rather than holding a value.</summary>
        public bool isHeading;

        /// <summary>Bytes to show as hex when nothing has said what they are. Zero otherwise.</summary>
        public int rawLength;

        /// <summary>Offset of a value shown beside this one, or -1.</summary>
        public int pairedOffset = -1;

        /// <summary>Type of the value shown beside this one.</summary>
        public Type pairedType;
    }

    /// <summary>
    /// Turns a value type into a flat list of readable lines, once per type.
    ///
    /// The elements on the state lane are unmanaged structs, and what the viewer holds of one is its
    /// bytes. Reading a field means knowing its offset and its type, which this works out once and
    /// keeps -- doing it per redraw would reflect over the whole struct ten times a second.
    ///
    /// Fixed buffers are the one place the type system runs out: it reports an element type and a
    /// length, so a byte buffer standing in for quaternions is just bytes. <see cref="LiveArrayAttribute"/>
    /// is where that is written down; without it such a buffer is shown as hex rather than guessed at.
    /// </summary>
    internal static class LiveDataValueLayout
    {
        /// <summary>Nesting the walk will follow before it stops describing and starts summarising.</summary>
        private const int kMaxDepth = 6;

        /// <summary>Elements written out one by one before the rest is summarised.</summary>
        private const int kMaxElements = 256;

        /// <summary>Bytes of an unexplained buffer shown as hex.</summary>
        private const int kHexPreview = 24;

        private static readonly Dictionary<Type, List<ValueField>> _cache =
            new Dictionary<Type, List<ValueField>>();

        // Declaration a cached declared layout was built from. A declaration can be edited while
        // the window is open, and a layout kept from the previous one would point every row at the
        // wrong bytes -- which reads as values rather than as a stale cache.
        private static readonly Dictionary<Type, ulong> _declaredLayouts = new Dictionary<Type, ulong>();

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

            // A type declared by an asset has no struct to reflect over: the element is a payload of
            // bytes and the declaration says what is in it. Asked first, because the type here is
            // the exposed type itself (UnityEngine.Light), and walking that as if it were the value
            // would describe the component rather than the bytes.
            var declared = StateBridgeRegistry.Find(type) as DeclaredStateBridge;
            if (declared != null)
            {
                if (_cache.TryGetValue(type, out var cachedDeclared)
                    && _declaredLayouts.TryGetValue(type, out var builtFrom)
                    && builtFrom == declared.layout)
                {
                    return cachedDeclared;
                }

                var described = _DescribeDeclared(declared);
                _cache[type] = described;
                _declaredLayouts[type] = declared.layout;
                return described;
            }

            if (_cache.TryGetValue(type, out var cached)) return cached;

            var fields = new List<ValueField>();
            try
            {
                _Walk(type, "", 0, 0, fields);
                _SortByOffset(fields);
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

        /// <summary>Forgets the worked-out layouts. For tests, and after a type changes.</summary>
        internal static void Clear()
        {
            _cache.Clear();
            _declaredLayouts.Clear();
        }

        /// <summary>
        /// The lines of a declared type, taken from the declaration instead of from reflection.
        ///
        /// The layout hash leads the payload and is a real part of what a recording holds, so it is
        /// shown rather than hidden: when a take will not apply, this is the number that says why.
        /// </summary>
        private static List<ValueField> _DescribeDeclared(DeclaredStateBridge bridge)
        {
            var fields = new List<ValueField>
            {
                new ValueField
                {
                    label = "layout",
                    path = "layout",
                    offset = 0,
                    type = typeof(ulong),
                },
            };

            foreach (var field in bridge.fields)
            {
                // Emitted the way _Walk emits a member of a struct, so a declared Color reads as
                // one line exactly like a generated one does rather than as four floats.
                if (IsReadable(field.valueType))
                {
                    fields.Add(new ValueField
                    {
                        label = field.name,
                        path = field.name,
                        offset = field.offset,
                        type = field.valueType,
                    });
                    continue;
                }

                fields.Add(new ValueField
                {
                    label = field.name,
                    path = field.name,
                    offset = field.offset,
                    type = field.valueType,
                    isHeading = true,
                });

                var before = fields.Count;
                if (field.valueType != null && field.valueType.IsValueType)
                {
                    _Walk(field.valueType, field.name, 1, field.offset, fields);
                }

                // A struct nothing could describe. Shown as its own bytes rather than left as a
                // heading with nothing under it.
                if (fields.Count == before)
                {
                    fields[fields.Count - 1].isHeading = false;
                    fields[fields.Count - 1].rawLength = field.size;
                }
            }

            _SortByOffset(fields);
            return fields;
        }

        /// <summary>True for a type this can read a single value out of.</summary>
        public static bool IsReadable(Type type)
            => type != null && (type.IsPrimitive || type.IsEnum || _inlineTypes.Contains(type));

        /// <summary>
        /// Puts the lines in the order the bytes are in.
        ///
        /// The walk follows reflection, which reports fields in declaration order -- usually the
        /// same thing, but not promised, and this list is read against a hex dump of the very bytes
        /// it describes. A row out of order there is a row that quietly points at the wrong number.
        ///
        /// Stable, so a heading keeps its children: a nested value and its first member share an
        /// offset, and the heading was walked first.
        /// </summary>
        private static void _SortByOffset(List<ValueField> fields)
        {
            for (int i = 1; i < fields.Count; i++)
            {
                var item = fields[i];
                int j = i - 1;

                while (j >= 0 && fields[j].offset > item.offset)
                {
                    fields[j + 1] = fields[j];
                    j--;
                }

                fields[j + 1] = item;
            }
        }

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
                    _WalkBuffer(type, field, buffer, path, depth, at, offset, into);
                    continue;
                }

                if (IsReadable(field.FieldType))
                {
                    into.Add(new ValueField
                    {
                        label = field.Name,
                        path = path,
                        depth = depth,
                        offset = at,
                        type = field.FieldType,
                    });
                    continue;
                }

                into.Add(new ValueField
                {
                    label = field.Name,
                    path = path,
                    depth = depth,
                    offset = at,
                    type = field.FieldType,
                    isHeading = true,
                });

                if (field.FieldType.IsValueType && depth < kMaxDepth)
                {
                    _Walk(field.FieldType, path, depth + 1, at, into);
                }
            }
        }

        private static void _WalkBuffer(Type owner, FieldInfo field, FixedBufferAttribute buffer,
            string path, int depth, int at, int ownerOffset, List<ValueField> into)
        {
            var declared = buffer.ElementType;
            var declaredSize = _SizeOf(declared);
            var totalBytes = declaredSize * buffer.Length;

            var array = field.GetCustomAttribute<LiveArrayAttribute>();
            var element = array?.elementType ?? declared;
            var stride = _SizeOf(element);

            // Nothing has said what these are, and bytes almost never mean bytes. Shown as hex rather
            // than as several hundred numbers that read as data but are not.
            if (array == null && element == typeof(byte) && buffer.Length > 8)
            {
                into.Add(new ValueField
                {
                    label = field.Name,
                    path = path,
                    depth = depth,
                    offset = at,
                    rawLength = totalBytes,
                });
                return;
            }

            if (stride <= 0 || totalBytes < stride)
            {
                into.Add(new ValueField
                {
                    label = field.Name,
                    path = path,
                    depth = depth,
                    offset = at,
                    rawLength = totalBytes,
                });
                return;
            }

            var count = totalBytes / stride;
            var labels = array?.labels;
            var paired = _ResolvePaired(owner, array?.pairedWith, ownerOffset);

            into.Add(new ValueField
            {
                label = field.Name,
                path = path,
                depth = depth,
                offset = at,
                isHeading = true,
            });

            var shown = Math.Min(count, kMaxElements);
            for (int i = 0; i < shown; i++)
            {
                var elementOffset = at + i * stride;
                var name = _LabelFor(labels, i);
                var elementPath = path + "." + name;

                if (IsReadable(element))
                {
                    into.Add(new ValueField
                    {
                        label = name,
                        path = elementPath,
                        depth = depth + 1,
                        offset = elementOffset,
                        type = element,
                        pairedOffset = paired.offset < 0 ? -1 : paired.offset + i * paired.stride,
                        pairedType = paired.type,
                    });
                    continue;
                }

                into.Add(new ValueField
                {
                    label = name,
                    path = elementPath,
                    depth = depth + 1,
                    offset = elementOffset,
                    type = element,
                    isHeading = true,
                });

                if (element.IsValueType && depth + 1 < kMaxDepth)
                {
                    _Walk(element, elementPath, depth + 2, elementOffset, into);
                }
            }

            if (count > shown)
            {
                into.Add(new ValueField
                {
                    label = "…",
                    path = path,
                    depth = depth + 1,
                    offset = -1,
                    rawLength = 0,
                    isHeading = true,
                });
            }
        }

        private static (int offset, int stride, Type type) _ResolvePaired(Type owner, string fieldName,
            int ownerOffset)
        {
            if (string.IsNullOrEmpty(fieldName)) return (-1, 0, null);

            var field = owner.GetField(fieldName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null) return (-1, 0, null);

            var at = ownerOffset + UnsafeUtility.GetFieldOffset(field);
            var buffer = field.GetCustomAttribute<FixedBufferAttribute>();

            if (buffer == null) return (at, 0, field.FieldType);

            var element = field.GetCustomAttribute<LiveArrayAttribute>()?.elementType ?? buffer.ElementType;
            return (at, _SizeOf(element), element);
        }

        private static string _LabelFor(Type labels, int index)
        {
            if (labels == null || !labels.IsEnum) return $"[{index}]";

            var name = Enum.GetName(labels, Enum.ToObject(labels, index));
            return string.IsNullOrEmpty(name) ? $"[{index}]" : name;
        }

        /// <summary>
        /// Reads one line out of a value's bytes. Empty when the bytes stop short, which happens
        /// whenever a recording was made by a build whose layout has since moved.
        /// </summary>
        public static string Read(byte[] bytes, int length, ValueField field)
        {
            if (bytes == null || field == null) return string.Empty;
            if (field.rawLength > 0) return _Hex(bytes, length, field);
            if (field.isHeading) return string.Empty;

            var text = _ReadOne(bytes, length, field.offset, field.type);
            if (field.pairedOffset < 0 || field.pairedType == null) return text;

            var paired = _ReadOne(bytes, length, field.pairedOffset, field.pairedType);
            return string.IsNullOrEmpty(paired) ? text : $"{text}   ({paired})";
        }

        private static string _Hex(byte[] bytes, int length, ValueField field)
        {
            var shown = Math.Min(Math.Min(field.rawLength, kHexPreview), Math.Max(0, length - field.offset));

            var text = new System.Text.StringBuilder();
            text.Append(RemoteControlEditorLocalization.Tr("LDV_RAW_BYTES", field.rawLength)).Append("  ");
            for (int i = 0; i < shown; i++) text.Append(bytes[field.offset + i].ToString("X2")).Append(' ');
            if (field.rawLength > shown) text.Append("…");

            // Not a failure -- just nothing said what they are. Naming the way to say it beats a
            // reader working out that the tool has a gap.
            text.Append("  ").Append(RemoteControlEditorLocalization.Tr("LDV_RAW_HINT"));
            return text.ToString();
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

        private static int _SizeOf(Type type)
        {
            if (type == null) return 0;
            if (type == typeof(byte) || type == typeof(sbyte) || type == typeof(bool)) return 1;
            if (type == typeof(short) || type == typeof(ushort)) return 2;
            if (type == typeof(int) || type == typeof(uint) || type == typeof(float)) return 4;
            if (type == typeof(long) || type == typeof(ulong) || type == typeof(double)) return 8;
            if (type.IsEnum) return _SizeOf(Enum.GetUnderlyingType(type));

            if (!type.IsValueType) return 0;

            try
            {
                return UnsafeUtility.SizeOf(type);
            }
            catch (Exception)
            {
                // Not an unmanaged struct, so it cannot be sitting in a fixed buffer anyway.
                return 0;
            }
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
