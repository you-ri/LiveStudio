// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace Lilium.RemoteControl.Frames
{
    /// <summary>
    /// Moves state for a type whose members were declared by a <see cref="LiveClassAsset"/>.
    ///
    /// Writes go through the same accessors the REST path uses, so a replayed value arrives with the
    /// notifications a remote write would bring. Reads -- every member of every object, every frame
    /// -- go through a typed mover per slot instead, which neither boxes the value nor marshals it
    /// (see <see cref="SlotMover"/>). It is still more work than a generated bridge does -- that one
    /// compiles down to field assignments -- so this exists for the types the generator cannot
    /// reach, not as an alternative to it.
    ///
    /// <para>
    /// The generator cannot reach them because an asset is not something Unity hands a generator:
    /// its additional-file channel takes only specially named files under Assets and passes them
    /// to every assembly alike, and a declaration in a bundle does not exist at compile time at all.
    /// </para>
    ///
    /// The block is a <see cref="DeclaredStateBlock"/> sized from the declaration, so a type pays
    /// for the values it declared and nothing more. What it does not get is the check a generated
    /// block gets for free: an element's width no longer says what is inside it, because two
    /// declarations of the same total size can lay their members out differently.
    ///
    /// That used to be answered by a hash of the declaration led into every element's payload. It is
    /// answered by <see cref="StateSchema"/> now, which says the same thing for both kinds of block
    /// and says it member by member rather than as one number -- so a recording made before the
    /// declaration moved is read for the members it still shares instead of being refused whole.
    /// The eight bytes an element spent saying it are gone with it.
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

            /// <summary>Byte offset from the start of the payload.</summary>
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

            /// <summary>
            /// Moves the value without boxing it, or null for the rare width the typed copy would
            /// get wrong -- those stay on the marshalling path below.
            /// </summary>
            public readonly SlotMover mover;

            public Slot(string name, Type valueType, int offset, int size, LivePropertyType property,
                SlotMover mover)
            {
                this.name = name;
                this.valueType = valueType;
                this.offset = offset;
                this.size = size;
                this.property = property;
                this.mover = mover;
            }
        }

        /// <summary>
        /// Moves one declared value between its member and its slot without going through
        /// <c>object</c>.
        ///
        /// The accessor a declaration otherwise reads through answers in <c>object</c>, so every
        /// value-typed member of every object was boxed once to capture it and again to compare it
        /// on replay, and turned into bytes by the marshaller -- garbage at frame rate for the types
        /// the asset exists to expose. This reads the member as the type it is: through the
        /// generator's typed accessor where one was registered, an open delegate over a property's
        /// getter, or a field's offset inside the object.
        ///
        /// Writes still go through the property, because a write is the one step that has to look
        /// like a remote one -- the default captured, the change announced -- and it only happens
        /// when the value actually moved.
        /// </summary>
        private abstract unsafe class SlotMover
        {
            /// <summary>
            /// Reads the member into the slot, in the slot's format. Leaves the slot as it is when
            /// the member cannot be read.
            /// </summary>
            /// <param name="target">
            /// The object, when the slot's member is known to be on it; null to go through
            /// <paramref name="property"/> instead.
            /// </param>
            public abstract void Capture(object target, in LiveProperty property, byte* slot);

            /// <summary>
            /// Writes the slot into the member, unless the member already holds it.
            /// <paramref name="scratch"/> is at least as wide as the slot.
            /// </summary>
            public abstract void Apply(object target, in LiveProperty property, byte* slot, byte* scratch);

            /// <summary>A mover for one slot, or null when the slot has to stay on the marshaller.</summary>
            public static SlotMover For(LivePropertyType member, Type valueType, int size)
            {
                if (valueType == null) return null;

                // The slot's format is what the marshaller writes, which a typed copy reproduces
                // wherever the value's own width is the marshalled one. bool is the exception worth
                // making -- four bytes in the slot, one in memory, and one of the likeliest things
                // to declare. Anything else that disagrees (char marshals to one byte) keeps the
                // marshalling path rather than a second format.
                if (valueType == typeof(bool))
                {
                    if (size != sizeof(int)) return null;
                }
                else if (UnsafeUtility.SizeOf(valueType) != size)
                {
                    return null;
                }

                var moverType = typeof(SlotMover<>).MakeGenericType(valueType);
                return (SlotMover)Activator.CreateInstance(moverType, member);
            }
        }

        private sealed unsafe class SlotMover<T> : SlotMover where T : struct
        {
            private static readonly bool _isBool = typeof(T) == typeof(bool);
            private static readonly int _size = _isBool ? sizeof(int) : UnsafeUtility.SizeOf<T>();

            // At most one of these. With neither, the value is read through the property, which is
            // still boxing-free where the generator registered a typed accessor.
            private readonly Func<object, T> _getter;
            private readonly int _fieldOffset = -1;

            public SlotMover(LivePropertyType member)
            {
                if (member == null || member.isStatic || member.isArrayElement) return;

                _getter = member.typedGetter as Func<object, T>;
                if (_getter != null) return;

                if (member.properyInfo != null)
                {
                    _getter = _OpenGetter(member.properyInfo);
                    return;
                }

                var field = member.fieldInfo;
                if (field != null && !field.IsStatic && field.FieldType == typeof(T)
                    && _IsPlainClass(field.DeclaringType))
                {
                    _fieldOffset = UnsafeUtility.GetFieldOffset(field);
                }
            }

            public override void Capture(object target, in LiveProperty property, byte* slot)
            {
                if (_TryRead(target, in property, out var value)) _Store(ref value, slot);
            }

            public override void Apply(object target, in LiveProperty property, byte* slot, byte* scratch)
            {
                // Compared in the slot's format, as bytes, for the reason _AlreadyHolds gives.
                if (_TryRead(target, in property, out var current))
                {
                    _Store(ref current, scratch);
                    if (UnsafeUtility.MemCmp(scratch, slot, _size) == 0) return;
                }

                property.TrySetValue(_Load(slot));
            }

            private bool _TryRead(object target, in LiveProperty property, out T value)
            {
                if (target == null || (_getter == null && _fieldOffset < 0))
                {
                    return property.TryGetValue(out value);
                }

                // What LivePropertyUtility.CanAccess asks, without the reflection behind it. The
                // type side of that question is already settled: a target is only handed over when
                // the handle names the class this slot was built from.
                if (target is UnityEngine.Object unity && unity == null)
                {
                    value = default;
                    return false;
                }

                if (_getter != null)
                {
                    value = _getter(target);
                    return true;
                }

                var address = (byte*)UnsafeUtility.PinGCObjectAndGetAddress(target, out var handle);
                UnsafeUtility.CopyPtrToStructure(address + _fieldOffset, out value);
                UnsafeUtility.ReleaseGCObject(handle);
                return true;
            }

            private static void _Store(ref T value, byte* slot)
            {
                if (_isBool) *(int*)slot = *(byte*)UnsafeUtility.AddressOf(ref value) != 0 ? 1 : 0;
                else UnsafeUtility.CopyStructureToPtr(ref value, slot);
            }

            private static T _Load(byte* slot)
            {
                var value = default(T);
                if (_isBool) *(bool*)UnsafeUtility.AddressOf(ref value) = *(int*)slot != 0;
                else UnsafeUtility.CopyPtrToStructure(slot, out value);
                return value;
            }

            private static bool _IsPlainClass(Type owner)
                => owner != null && owner.IsClass && !owner.ContainsGenericParameters;

            /// <summary>
            /// A getter over <c>object</c> that calls the property's own getter, typed. Null when
            /// the property is not a plain instance getter on a class, which is read through the
            /// property instead.
            /// </summary>
            private static Func<object, T> _OpenGetter(PropertyInfo property)
            {
                var get = property.GetMethod;
                if (get == null || get.IsStatic || property.PropertyType != typeof(T)) return null;
                if (get.GetParameters().Length != 0 || !_IsPlainClass(property.DeclaringType)) return null;

                var close = typeof(SlotMover<T>)
                    .GetMethod(nameof(_Close), BindingFlags.NonPublic | BindingFlags.Static)
                    .MakeGenericMethod(property.DeclaringType);

                return (Func<object, T>)close.Invoke(null, new object[] { get });
            }

            private static Func<object, T> _Close<TOwner>(MethodInfo get) where TOwner : class
            {
                // Asked not to throw: a getter that cannot be bound this way is read through the
                // property, which is what happened to every getter before this existed.
                var open = (Func<TOwner, T>)Delegate.CreateDelegate(typeof(Func<TOwner, T>), get,
                    throwOnBindFailure: false);

                if (open == null) return null;
                return target => open((TOwner)target);
            }
        }

        private readonly Slot[] _slots;
        private readonly int _payloadSize;

        /// <summary>
        /// The description this bridge declared, as the text it is interned by.
        ///
        /// Held so a block can be told apart from one built for an earlier version of the same
        /// declaration. Width alone cannot do it -- two declarations of the same total size lay
        /// their members out differently -- and that used to be the job of the hash inside each
        /// element. Asking once when the block is fetched is cheaper than asking per element, and it
        /// catches the case the per-element hash never could: a block still holding values captured
        /// under the old declaration.
        /// </summary>
        private readonly string _schemaSignature;

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

        private DeclaredStateBridge(Type owner, LiveClass liveClass, Slot[] slots,
            string schemaSignature, int payloadSize)
        {
            ownerType = owner;
            _liveClass = liveClass;
            _slots = slots;
            _schemaSignature = schemaSignature;
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

        /// <summary>
        /// The object a slot's mover may read directly, or null to have it go through the property.
        ///
        /// Only when the handle names the class the slots were built from: the movers were made
        /// against that class's members, and a handle bound by name to some other declaration may
        /// hold an object those members are not on.
        /// </summary>
        private object _DirectTarget(in LiveObjectHandle handle)
            => ReferenceEquals(handle.targetType, _liveClass) ? handle.target : null;

        public override Type ownerType { get; }

        public override Type blockType => typeof(DeclaredStateBlock);

        /// <summary>Bytes one object of this type carries.</summary>
        public int payloadSize => _payloadSize;

        /// <summary>How many declared values this carries.</summary>
        public int slotCount => _slots.Length;

        /// <summary>The description this bridge declared, as a recording interns it.</summary>
        public string schemaSignature => _schemaSignature;

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

            var offset = 0;

            foreach (var member in liveClass.propertyTypes)
            {
                if (member == null || member.lane != FrameLane.State) continue;

                // The lane is a round trip: capture reads the member out every frame and apply
                // writes it back on replay. A member that cannot take the write would be captured
                // and then refused sixty times a second -- and read-only is the one thing the
                // design forbids putting in a frame as a value anyway, because replaying an
                // application's own result and comparing against it agrees with itself. The
                // generated path asks this at compile time (LRC008); this is the same question on
                // the path that has no compile time.
                if (member.isReadOnly)
                {
                    Debug.LogWarning(
                        $"[RemoteControl] '{liveClass.typeName}.{member.name}' asks for the state lane " +
                        $"but is read-only, so a replay has no way to write it back. Left on the event lane.");
                    continue;
                }

                // The block holds an element per object, so one value shared by all of them has no
                // element to sit in: it would be written into every one and read back from whichever
                // came last. Refused on the generated path too (LRC008).
                if (member.isStatic)
                {
                    Debug.LogWarning(
                        $"[RemoteControl] '{liveClass.typeName}.{member.name}' asks for the state lane " +
                        $"but is static, and the lane carries a value per object. Left on the event lane.");
                    continue;
                }

                var valueType = member.resolvedValueType ?? member.valueType;
                if (valueType == null || !CanCarry(valueType))
                {
                    Debug.LogWarning(
                        $"[RemoteControl] '{liveClass.typeName}.{member.name}' asks for the state lane " +
                        $"but its type is not something a frame can carry as bytes. Left on the event lane.");
                    continue;
                }

                var size = SizeOf(valueType);
                slots.Add(new Slot(member.name, valueType, offset, size, member,
                    SlotMover.For(member, valueType, size)));

                offset += size;
            }

            if (slots.Count == 0) return null;

            // What this declaration holds, in the same terms the generated path publishes, so a
            // recording made before it changed can be read member by member rather than refused
            // whole. Re-declared on every build, because an asset's declaration can move while the
            // application is running and the description has to move with it.
            //
            // The bit each slot answers to in a mask is its position here, which is its position in
            // _slots -- the two are built from the same walk and must stay in step.
            var described = new StateSchemaMember[slots.Count];
            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                described[i] = new StateSchemaMember(slot.name, slot.valueType?.FullName,
                    slot.offset, slot.size);
            }

            var schema = StateSchemaBuilder.ForDeclared(DeclaredStateBlock.kMetaSize,
                DeclaredStateBlock.StrideFor(offset), described);

            StateTypes.DeclareSchema(liveClass.type.FullName, schema);

            return new DeclaredStateBridge(liveClass.type, liveClass, slots.ToArray(),
                schema.ToText(), offset);
        }

        public override StateBlock EnsureBlock(StateBlockSet state)
            => state?.GetOrCreateDeclared(ownerType, _payloadSize, _schemaSignature);

        public override bool Capture(object owner, int ownerId, StateBlockSet state,
            FrameSource source, long time, FrameSymbolTable symbols)
        {
            if (owner == null || state == null) return false;
            if (!_TryHandleFor(owner, out var handle)) return false;

            return Capture(in handle, ownerId, state, source, time);
        }

        /// <summary>
        /// How this bridge reaches an object it was handed without a handle.
        ///
        /// Registered is asked first, because that handle carries what the registration knows. An
        /// object the registry cannot be asked about is read through an unregistered one instead:
        /// values travel through the same accessors either way, so being registered was never the
        /// requirement, only the way a handle was found.
        ///
        /// Refusing here instead is what made the walk uneven. A declared type met as a component
        /// was carried -- the walk built the handle itself for that one case -- and the same type
        /// met as a nested object or as an element of a collection was dropped, with nothing said.
        /// </summary>
        private bool _TryHandleFor(object owner, out LiveObjectHandle handle)
        {
            if (LiveObjectRegistry.TryFindByTarget(owner, out handle)) return true;

            var found = LiveClass.Find(owner.GetType());
            if (found == null) return false;

            handle = LiveObjectHandle.CreateUnregistered(found, owner);
            return true;
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

            var block = state.GetOrCreateDeclared(ownerType, _payloadSize, _schemaSignature);
            var index = block.GetOrCreate(ownerId);
            block.SetMeta(index, source, time);

            var payload = block.Payload(index);
            var target = _DirectTarget(in handle);

            fixed (byte* bytes = payload)
            {
                for (int i = 0; i < _slots.Length; i++)
                {
                    var slot = _slots[i];
                    if (!_TryBind(in handle, in slot, out var property)) continue;

                    if (slot.mover != null)
                    {
                        slot.mover.Capture(target, in property, bytes + slot.offset);
                        continue;
                    }

                    // Read through the same accessor REST uses -- shadow fields travel through
                    // their property, so what is captured is what the setter would have applied.
                    var value = property.GetValue();
                    if (value == null) continue;

                    _Write(value, slot, bytes);
                }
            }

            return true;
        }

        public override bool Apply(object owner, int ownerId, StateBlockSet state,
            FrameSymbolTable symbols)
        {
            if (owner == null || state == null) return false;
            if (!_TryHandleFor(owner, out var handle)) return false;

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

            // The block was filled under a declaration that is no longer this one. Refused rather
            // than read as whatever the bytes happen to say under the current offsets, which would
            // land each value in the wrong member and look like values.
            //
            // Not asked when a plan filled the block: there the two descriptions are known to
            // differ, the members that survived have already been put where they belong, and the
            // ones that did not are outside the mask.
            if (!block.readThroughPlan
                && block.schemaSignature != null
                && _schemaSignature != null
                && !ReferenceEquals(block.schemaSignature, _schemaSignature)
                && !string.Equals(block.schemaSignature, _schemaSignature, StringComparison.Ordinal))
            {
                return false;
            }

            var mask = block.appliedMemberMask;
            var target = _DirectTarget(in handle);
            byte* scratch = stackalloc byte[_widestSlot];

            fixed (byte* bytes = payload)
            {
                for (int i = 0; i < _slots.Length; i++)
                {
                    // Outside the mask means the recording never carried this member. The bytes in
                    // its place are zero, and zero is a value -- writing it would empty a name or
                    // turn "no override" into "override with nothing".
                    if (i < StateReadPlan.kMaxMaskedMembers && (mask & 1UL << i) == 0) continue;

                    var slot = _slots[i];
                    if (!_TryBind(in handle, in slot, out var property)) continue;

                    if (slot.mover != null)
                    {
                        slot.mover.Apply(target, in property, bytes + slot.offset, scratch);
                        continue;
                    }

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
    }
}
