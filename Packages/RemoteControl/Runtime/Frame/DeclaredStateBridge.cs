// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Lilium.RemoteControl.Frames
{
    /// <summary>
    /// Moves state for a type whose members were declared by a <see cref="LiveClassAsset"/>.
    ///
    /// Reads and writes through the same accessors the REST path uses rather than by reflecting per
    /// frame. It is still more work than a generated bridge does -- that one compiles down to field
    /// assignments -- so this exists for the types the generator cannot reach, not as an alternative
    /// to it.
    ///
    /// The block is a <see cref="DeclaredStateBlock"/> sized from the declaration, so a type pays
    /// for the values it declared and nothing more. What it does not get is the check a generated
    /// block gets for free: an element's width no longer says what is inside it, because two
    /// declarations of the same total size can lay their members out differently. That is what
    /// <see cref="layout"/> is for -- it leads the payload of every element and is checked before
    /// anything is written back.
    /// </summary>
    public sealed class DeclaredStateBridge : StateBridge
    {
        /// <summary>
        /// One declared value as seen from outside: what it is called, what it is, and where it sits
        /// in the element's payload.
        ///
        /// A generated block is a struct, so anything wanting to read one field out of it can ask
        /// reflection where that field is. A declared block has no such type -- the payload is bytes
        /// and the layout lives here -- so this is the equivalent question answered, and without it
        /// a reader holding the bytes has no way to tell one value from the next.
        /// </summary>
        public readonly struct Field
        {
            /// <summary>The exposed member's name.</summary>
            public readonly string name;

            /// <summary>The value's type.</summary>
            public readonly Type valueType;

            /// <summary>Byte offset from the start of the payload (past the layout hash).</summary>
            public readonly int offset;

            /// <summary>Bytes the value occupies.</summary>
            public readonly int size;

            public Field(string name, Type valueType, int offset, int size)
            {
                this.name = name;
                this.valueType = valueType;
                this.offset = offset;
                this.size = size;
            }
        }

        /// <summary>One declared value: where it sits in the buffer, and how to move it.</summary>
        private readonly struct Slot
        {
            public readonly string name;
            public readonly Type valueType;
            public readonly int offset;
            public readonly int size;

            /// <summary>
            /// The member's declaration, kept rather than looked up again per frame.
            ///
            /// Resolving it by name is not the cheap dictionary hit it looks like: the handle parses
            /// the name as a property path and the span-keyed lookup walks the whole property list
            /// comparing strings, so a type with n declared members pays that walk n times a frame,
            /// for every object of it, for the length of a take. The answer cannot change under us
            /// -- a bridge is rebuilt whenever its live class is (see LiveClassAssetSystem) -- so it
            /// is settled once here.
            /// </summary>
            public readonly LivePropertyType property;

            public Slot(string name, Type valueType, int offset, int size, LivePropertyType property)
            {
                this.name = name;
                this.valueType = valueType;
                this.offset = offset;
                this.size = size;
                this.property = property;
            }
        }

        /// <summary>Bytes the layout hash takes at the head of every element's payload.</summary>
        public const int kLayoutSize = 8;

        private readonly Slot[] _slots;
        private readonly ulong _layout;
        private readonly int _payloadSize;

        /// <summary>Widest slot, which is all the scratch the comparison below ever needs.</summary>
        private readonly int _widestSlot;

        // Where a member's current value is marshalled so it can be compared with the recorded
        // bytes. One per bridge rather than one per call: apply runs on the main thread, once per
        // object per frame, and allocating here would put the garbage back that the comparison
        // exists to avoid. Built on first use, because a bridge in a run that never replays
        // anything should not carry it.
        private byte[] _scratch;

        // Built on first ask. Nothing on the per-frame path wants it -- the bridge itself works off
        // the slots -- so it stays unbuilt in a run where nobody is looking at the lane.
        private Field[] _fields;

        private DeclaredStateBridge(Type owner, LiveClass liveClass, Slot[] slots, ulong layout, int payloadSize)
        {
            ownerType = owner;
            _liveClass = liveClass;
            _slots = slots;
            _layout = layout;
            _payloadSize = payloadSize;

            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i].size > _widestSlot) _widestSlot = slots[i].size;
            }
        }

        /// <summary>
        /// The declaration the slots were taken from, kept to tell whether a handle's cached member
        /// declarations still apply to it. A handle holds the class it was made against, and
        /// re-registering a type makes a new one -- so a handle that predates the rebuild names the
        /// old class, and reading it through the new one's members would read the wrong thing.
        /// </summary>
        private readonly LiveClass _liveClass;

        /// <summary>
        /// Binds one slot to an object. Direct when the handle names the class this was built from,
        /// which is the ordinary case and the one worth making cheap; by name otherwise, the way
        /// this did for everything before the declarations were cached.
        /// </summary>
        private bool _TryBind(in LiveObjectHandle handle, in Slot slot, out LiveProperty property)
        {
            if (ReferenceEquals(handle.targetType, _liveClass))
            {
                property = new LiveProperty(slot.property, handle, handle.target);
                return true;
            }

            var found = handle.FindProperty(slot.name);
            property = found ?? default;
            return found != null;
        }

        public override Type ownerType { get; }

        public override Type blockType => typeof(DeclaredStateBlock);

        /// <summary>Bytes one object of this type carries, the layout hash included.</summary>
        public int payloadSize => _payloadSize;

        /// <summary>How many declared values this carries.</summary>
        public int slotCount => _slots.Length;

        /// <summary>The declaration's hash, which a recording is checked against.</summary>
        public ulong layout => _layout;

        /// <inheritdoc/>
        public override bool Carries(string memberName)
        {
            // By the name the member is exposed under, which is what a declaration names it by. A
            // member the declaration asked for but that could not be laid out never became a slot,
            // so asking the slots is asking what is actually moved.
            for (int i = 0; i < _slots.Length; i++)
            {
                if (string.Equals(_slots[i].name, memberName, StringComparison.Ordinal)) return true;
            }

            return false;
        }

        /// <summary>
        /// The declared values and where they sit, in payload order. For anything that has the bytes
        /// of an element and needs to read them as values.
        /// </summary>
        public IReadOnlyList<Field> fields
        {
            get
            {
                if (_fields == null)
                {
                    var built = new Field[_slots.Length];
                    for (int i = 0; i < _slots.Length; i++)
                    {
                        var slot = _slots[i];
                        built[i] = new Field(slot.name, slot.valueType, slot.offset, slot.size);
                    }

                    _fields = built;
                }

                return _fields;
            }
        }

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

            // The values start after the layout hash, which leads every element's payload.
            var offset = kLayoutSize;
            var hash = 14695981039346656037UL;

            foreach (var member in liveClass.propertyTypes)
            {
                if (member == null || member.lane != FrameLane.State) continue;

                var valueType = member.resolvedValueType ?? member.valueType;
                if (valueType == null || !CanCarry(valueType))
                {
                    Debug.LogWarning(
                        $"[RemoteControl] '{liveClass.typeName}.{member.name}' asks for the state lane " +
                        $"but its type is not something a frame can carry as bytes. Left on the evt lane.");
                    continue;
                }

                var size = SizeOf(valueType);
                slots.Add(new Slot(member.name, valueType, offset, size, member));

                // Name, type and position all go into the hash: moving a member is as much a change
                // of layout as adding one, and a recording written before the move must not be read
                // after it.
                hash = _Mix(hash, member.name);
                hash = _Mix(hash, valueType.FullName);
                hash = _Mix(hash, offset.ToString());

                offset += size;
            }

            return slots.Count == 0
                ? null
                : new DeclaredStateBridge(liveClass.type, liveClass, slots.ToArray(), hash, offset);
        }

        public override StateBlock EnsureBlock(StateBlockSet state)
            => state?.GetOrCreateDeclared(ownerType, _payloadSize);

        public override bool Capture(object owner, int ownerId, StateBlockSet state,
            FrameSource source, long time)
        {
            if (owner == null || state == null) return false;

            // Only an object registered in its own right can be found this way. One reached through
            // whatever owns it -- a component of an exposed GameObject -- is not, which is what the
            // overload below is for: the caller walking to it already knows how to read it.
            if (!LiveObjectRegistry.TryFindByTarget(owner, out var handle)) return false;

            return Capture(in handle, ownerId, state, source, time);
        }

        /// <summary>
        /// Reads state through a handle the caller already has, for an object the registry cannot
        /// be asked about.
        ///
        /// The handle is the whole of what this needs -- values are read through the same accessors
        /// REST uses -- so being registered was never the real requirement, only the way one was
        /// found. Saying so here is what lets an exposed component be carried under the address its
        /// owner gives it.
        /// </summary>
        public unsafe bool Capture(in LiveObjectHandle handle, int ownerId, StateBlockSet state,
            FrameSource source, long time)
        {
            if (state == null) return false;

            var block = state.GetOrCreateDeclared(ownerType, _payloadSize);
            var index = block.GetOrCreate(ownerId);
            block.SetMeta(index, source, time);

            var payload = block.Payload(index);

            fixed (byte* bytes = payload)
            {
                *(ulong*)bytes = _layout;

                for (int i = 0; i < _slots.Length; i++)
                {
                    var slot = _slots[i];
                    if (!_TryBind(in handle, in slot, out var property)) continue;

                    // Read through the same accessor REST uses -- shadow fields travel through
                    // their property, so what is captured is what the setter would have applied.
                    var value = property.GetValue();
                    if (value == null) continue;

                    _Write(value, slot, bytes);
                }
            }

            return true;
        }

        public override bool Apply(object owner, int ownerId, StateBlockSet state)
        {
            if (owner == null || state == null) return false;
            if (!LiveObjectRegistry.TryFindByTarget(owner, out var handle)) return false;

            return Apply(in handle, ownerId, state);
        }

        /// <inheritdoc cref="Capture(in LiveObjectHandle, int, StateBlockSet, FrameSource, long)"/>
        public unsafe bool Apply(in LiveObjectHandle handle, int ownerId, StateBlockSet state)
        {
            if (state == null) return false;

            var block = state.FindDeclared(ownerType);
            if (block == null) return false;

            var index = block.IndexOfOwner(ownerId);
            if (index < 0) return false;

            var payload = block.Payload(index);
            if (payload.Length < _payloadSize) return false;

            fixed (byte* bytes = payload)
            {
                if (*(ulong*)bytes != _layout)
                {
                    // The declaration moved since the recording was made. Refused rather than read
                    // as whatever the bytes happen to say under the current layout, which would land
                    // each value in the wrong member and look like a value.
                    return false;
                }

                for (int i = 0; i < _slots.Length; i++)
                {
                    var slot = _slots[i];
                    if (!_TryBind(in handle, in slot, out var property)) continue;
                    if (_AlreadyHolds(in property, in slot, bytes)) continue;

                    property.SetValue(_Read(slot, bytes));
                }
            }

            return true;
        }

        /// <summary>
        /// Whether the member already holds what the recording says, so nothing needs writing.
        ///
        /// The state lane restates every member on every frame, and this goes in through the same
        /// accessor a REST write does: without asking first, replaying a recording runs the full
        /// write -- the old value read back, the changing and changed notifications, the editor
        /// dirty mark -- sixty times a second for every declared member of every object, almost all
        /// of it for values that did not move. It is also what the design asks of a replay-only
        /// apply path: idempotent, and deciding by what is actually there rather than by what was
        /// written last.
        ///
        /// Compared as bytes rather than as values. <c>Equals</c> on a boxed struct with no override
        /// is a reflective field walk, which would cost more than the write it is trying to avoid,
        /// and the question here really is whether the memory says the same thing.
        /// </summary>
        private unsafe bool _AlreadyHolds(in LiveProperty property, in Slot slot, byte* bytes)
        {
            var current = property.GetValue();
            if (current == null) return false;

            // A reference member resolves to whatever it points at, which need not be this shape.
            if (current.GetType() != slot.valueType) return false;

            var scratch = _scratch ??= new byte[_widestSlot];

            fixed (byte* mine = scratch)
            {
                // The same call the capture side makes, so the two agree on how a value becomes
                // bytes -- including bool, which is four bytes here and not blittable at all.
                Marshal.StructureToPtr(current, (IntPtr)mine, false);

                var stored = bytes + slot.offset;
                for (int b = 0; b < slot.size; b++)
                {
                    if (mine[b] != stored[b]) return false;
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
        /// Width the value occupies in the buffer.
        ///
        /// An enum is measured by what it is underneath: <see cref="Marshal.SizeOf(Type)"/> refuses
        /// an enum type on some runtimes ("cannot be marshalled as an unmanaged structure"), and a
        /// declaration naming an enum is ordinary enough that finding out at registration time is
        /// not acceptable.
        /// </summary>
        public static int SizeOf(Type type)
        {
            if (type == null) return 0;

            return Marshal.SizeOf(type.IsEnum ? Enum.GetUnderlyingType(type) : type);
        }

        /// <summary>
        /// Whether a value can be moved as bytes at all.
        ///
        /// A fixed-width value with nothing pointing out of it. Anything holding a reference is
        /// refused rather than marshalled: the marshaller would happily allocate unmanaged memory
        /// for it and hand back a pointer that nothing ever frees, and a frame is not a place to
        /// leak once per object per frame.
        ///
        /// Public because asking for the state lane and getting it are two different things, and an
        /// editor showing which lane carries a member has to be able to tell them apart before the
        /// declaration is ever built.
        /// </summary>
        public static bool CanCarry(Type type)
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
