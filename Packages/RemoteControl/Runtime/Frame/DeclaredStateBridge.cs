// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Lilium.RemoteControl.Frames
{
    /// <summary>
    /// One element of the state lane for a type declared by an asset rather than by attributes.
    ///
    /// A single struct for every such type, because there is no compiled struct to use instead: a
    /// generated bridge gets one shaped like the members it carries, and a declaration read at
    /// runtime has nothing to generate from. So the values are packed into a fixed buffer and the
    /// declaration says where each one sits.
    ///
    /// The layout is therefore not in the type, which means a recording cannot be checked against
    /// it the way <c>elementSize</c> checks a generated block. That is what <see cref="layout"/> is
    /// for -- see there.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct DeclaredState
    {
        /// <summary>
        /// Room for one object's declared values. Sized for a handful of numbers and vectors, which
        /// is what a declaration made in an inspector realistically holds. A declaration that does
        /// not fit is reported at registration rather than truncated at frame rate.
        /// </summary>
        public const int kCapacity = 112;

        /// <summary>
        /// Hash of the declaration these bytes were packed by.
        ///
        /// Every declared type shares this struct, so its size says nothing about what is inside it
        /// -- two builds can disagree completely about the layout and still agree on the width. A
        /// recording restored under a different declaration would land each value in the wrong
        /// member and look plausible, which is the failure the generated path avoids by checking the
        /// element size. This is the equivalent check for a layout that is not in a type.
        /// </summary>
        public ulong layout;

        public fixed byte data[kCapacity];
    }

    /// <summary>
    /// Moves state for a type whose members were declared by a <see cref="LiveClassAsset"/>.
    ///
    /// Reads and writes through the same accessors the REST path uses rather than by reflecting per
    /// frame. It is still more work than a generated bridge does -- that one compiles down to field
    /// assignments -- so this exists for the types the generator cannot reach, not as an alternative
    /// to it.
    /// </summary>
    public sealed class DeclaredStateBridge : StateBridge
    {
        /// <summary>One declared value: where it sits in the buffer, and how to move it.</summary>
        private readonly struct Slot
        {
            public readonly string name;
            public readonly Type valueType;
            public readonly int offset;
            public readonly int size;

            public Slot(string name, Type valueType, int offset, int size)
            {
                this.name = name;
                this.valueType = valueType;
                this.offset = offset;
                this.size = size;
            }
        }

        private readonly Slot[] _slots;
        private readonly ulong _layout;

        private DeclaredStateBridge(Type owner, Slot[] slots, ulong layout)
        {
            ownerType = owner;
            _slots = slots;
            _layout = layout;
        }

        public override Type ownerType { get; }

        public override Type blockType => typeof(DeclaredState);

        /// <summary>How many declared values this carries.</summary>
        public int slotCount => _slots.Length;

        /// <summary>The declaration's hash, which a recording is checked against.</summary>
        public ulong layout => _layout;

        /// <summary>
        /// Builds a bridge for the state-lane members of a live class, or null when it has none.
        ///
        /// Null rather than an empty bridge: a type with nothing on the state lane should not make a
        /// block, because making one is how a type announces it belongs on the lane at all.
        /// </summary>
        public static DeclaredStateBridge Build(LiveClass liveClass)
        {
            if (liveClass?.type == null) return null;

            var slots = new List<Slot>();
            var offset = 0;
            var hash = 14695981039346656037UL;

            foreach (var member in liveClass.propertyTypes)
            {
                if (member == null || member.lane != FrameLane.State) continue;

                var valueType = member.resolvedValueType ?? member.valueType;
                if (valueType == null || !_IsCarryable(valueType))
                {
                    Debug.LogWarning(
                        $"[RemoteControl] '{liveClass.typeName}.{member.name}' asks for the state lane " +
                        $"but its type is not something a frame can carry as bytes. Left on the input lane.");
                    continue;
                }

                var size = Marshal.SizeOf(valueType);
                if (offset + size > DeclaredState.kCapacity)
                {
                    // Said once, here, rather than by quietly dropping values every frame.
                    Debug.LogError(
                        $"[RemoteControl] '{liveClass.typeName}' declares more state than one frame " +
                        $"element holds ({DeclaredState.kCapacity} bytes). '{member.name}' and anything " +
                        "after it stay off the lane.");
                    break;
                }

                slots.Add(new Slot(member.name, valueType, offset, size));

                // Name, type and position all go into the hash: moving a member is as much a change
                // of layout as adding one, and a recording written before the move must not be read
                // after it.
                hash = _Mix(hash, member.name);
                hash = _Mix(hash, valueType.FullName);
                hash = _Mix(hash, offset.ToString());

                offset += size;
            }

            return slots.Count == 0 ? null : new DeclaredStateBridge(liveClass.type, slots.ToArray(), hash);
        }

        public override StateBlock EnsureBlock(StateBlockSet state) => state?.GetOrCreate<DeclaredState>();

        public override unsafe void Capture(object owner, int ownerId, StateBlockSet state,
            FrameSource source, long time)
        {
            if (owner == null || state == null) return;
            if (!LiveObjectRegistry.TryFindByTarget(owner, out var handle)) return;

            ref var element = ref state.GetOrCreate<DeclaredState>().GetOrCreate(ownerId);
            element.source = source;
            element.time = time;
            element.value.layout = _layout;

            fixed (byte* bytes = element.value.data)
            {
                for (int i = 0; i < _slots.Length; i++)
                {
                    var slot = _slots[i];
                    var property = handle.FindProperty(slot.name);
                    if (property == null) continue;

                    var value = property.Value.GetValue();
                    if (value == null) continue;

                    _Write(value, slot, bytes);
                }
            }
        }

        public override unsafe bool Apply(object owner, int ownerId, StateBlockSet state)
        {
            if (owner == null || state == null) return false;

            var block = state.Find<DeclaredState>();
            if (block == null) return false;

            var index = block.IndexOf(ownerId);
            if (index < 0) return false;

            ref var element = ref block[index];
            if (element.value.layout != _layout)
            {
                // The declaration moved since the recording was made. Refused rather than read as
                // whatever the bytes happen to say under the current layout, which would land each
                // value in the wrong member and look like a value.
                return false;
            }

            if (!LiveObjectRegistry.TryFindByTarget(owner, out var handle)) return false;

            fixed (byte* bytes = element.value.data)
            {
                for (int i = 0; i < _slots.Length; i++)
                {
                    var slot = _slots[i];
                    var property = handle.FindProperty(slot.name);
                    if (property == null) continue;

                    property.Value.SetValue(_Read(slot, bytes));
                }
            }

            return true;
        }

        /// <summary>
        /// Writes one value into the buffer.
        ///
        /// Through the marshaller rather than by pinning the boxed value: <c>bool</c> is not
        /// blittable and cannot be pinned at all, and it is one of the likeliest things to declare.
        /// The marshalled width is what <see cref="Marshal.SizeOf"/> reserved, so the two agree --
        /// a bool costs four bytes here, which is the price of being able to carry one.
        /// </summary>
        private static unsafe void _Write(object value, Slot slot, byte* destination)
        {
            Marshal.StructureToPtr(value, (IntPtr)(destination + slot.offset), false);
        }

        private static unsafe object _Read(Slot slot, byte* source)
        {
            return Marshal.PtrToStructure((IntPtr)(source + slot.offset), slot.valueType);
        }

        /// <summary>
        /// Whether a value can be moved as bytes at all.
        ///
        /// A fixed-width value with nothing pointing out of it. Anything holding a reference is
        /// refused rather than marshalled: the marshaller would happily allocate unmanaged memory
        /// for it and hand back a pointer that nothing ever frees, and a frame is not a place to
        /// leak once per object per frame.
        /// </summary>
        private static bool _IsCarryable(Type type)
        {
            if (type == null || !type.IsValueType || type.IsGenericTypeDefinition) return false;
            if (type.IsEnum || type.IsPrimitive) return true;

            // A struct of blittable fields. Asked of the runtime rather than worked out from the
            // field list, so a type with a reference hidden somewhere inside it is refused here
            // rather than throwing at frame rate.
            try
            {
                var probe = GCHandle.Alloc(Activator.CreateInstance(type), GCHandleType.Pinned);
                probe.Free();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static ulong _Mix(ulong hash, string value)
        {
            if (string.IsNullOrEmpty(value)) return hash;

            for (int i = 0; i < value.Length; i++)
            {
                hash ^= value[i];
                hash *= 1099511628211UL;
            }

            return hash;
        }
    }
}
