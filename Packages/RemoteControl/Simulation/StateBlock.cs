// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Lilium.RemoteControl.Frames
{
    /// <summary>
    /// One element of a state block: the value, plus who it belongs to and when it is from.
    ///
    /// The meta is carried by the element rather than derived from the structure because the
    /// elements this applies to are coarse -- one avatar, one light, one DMX universe. At that size
    /// sixteen bytes of bookkeeping is noise. Fine-grained arrays (curve keys, blend shape weights)
    /// go inside <typeparamref name="T"/> at a fixed size and carry no meta of their own.
    ///
    /// That the meta is here is also what makes tracks work: <see cref="source"/> says which
    /// producer an element came from, so playback can select, disable or offset by producer without
    /// the recording having been split into separate files.
    /// </summary>
    public struct StateElement<T> where T : unmanaged
    {
        /// <summary>Interned id of the object this belongs to.</summary>
        public int ownerId;

        /// <summary>
        /// The producer. The unit tracks are selected, disabled and offset by.
        ///
        /// The resolved handle rather than a bare id, so a producer cannot put an arbitrary number
        /// here and so the default value reads as "nobody has claimed this yet".
        /// </summary>
        public FrameSource source;

        /// <summary>
        /// Position on the producer's own time axis, in its own frames.
        ///
        /// Not the session frame number. A capture sender numbers frames from its own clock and
        /// there is no reason for the two to agree; holding the producer's own stamp is what lets an
        /// alignment be applied afterwards instead of being baked in at record time.
        /// </summary>
        public long time;

        public T value;

        public override string ToString() => $"owner:{ownerId} {source} t:{time}";
    }

    /// <summary>
    /// Non-generic view of a state block, so a <see cref="StateBlockSet"/> can hold blocks of
    /// different element types together.
    /// </summary>
    public abstract class StateBlock : IDisposable
    {
        /// <summary>Element type this block carries.</summary>
        public abstract Type elementType { get; }

        /// <summary>
        /// The name a recording calls this block by: the exposed type whose state it holds.
        ///
        /// The owner's name rather than the block's own. A block is an implementation detail -- a
        /// struct written by the generator inside its owner or beside it, or a stride read from a
        /// declaration -- and each of those spelled the same state differently, so a take made from
        /// a <c>partial</c> owner could not be read by a build where that type was not partial. The
        /// owner has one name in every case, and it is the name an author would recognise.
        /// </summary>
        public abstract string typeName { get; }

        /// <summary>Number of elements currently held.</summary>
        public abstract int count { get; }

        /// <summary>Size of one element in bytes, as it is written to a recording.</summary>
        public abstract int elementSize { get; }

        /// <summary>
        /// Drops every element. Not called between frames: a state block holds the current state,
        /// and an element that stops being written keeps its last value rather than snapping back
        /// to a default nobody asked for.
        /// </summary>
        public abstract void Reset();

        /// <summary>
        /// The elements as they sit in memory, for a recording to write out verbatim.
        ///
        /// Valid until the block is next written to. Elements are unmanaged and packed, so this is
        /// the storage seen as bytes rather than a copy of it.
        /// </summary>
        public abstract ReadOnlySpan<byte> AsBytes();

        /// <summary>
        /// Replaces the contents with elements read back from a recording.
        ///
        /// The whole block at once rather than element by element: the recorded form is the same
        /// dense array this holds, so restoring it is a copy rather than a merge. Anything that was
        /// here and is not in the recording is gone, which is the point -- a replayed frame is the
        /// state at that frame, not the state at that frame layered over whatever came before.
        /// </summary>
        public abstract void ReadFrom(ReadOnlySpan<byte> bytes, int elementCount);

        /// <summary>
        /// Reads elements a build laid out differently, member by member.
        ///
        /// The plan says which members the two builds agree on and where each one moved to. What it
        /// cannot say is what a member the recording never carried should be -- so those are left
        /// out of <see cref="appliedMemberMask"/> instead of being invented here.
        ///
        /// A null or identical plan is the ordinary read, so a caller does not have to ask which
        /// case it is in.
        /// </summary>
        public abstract void ReadFrom(ReadOnlySpan<byte> bytes, int elementCount, StateReadPlan plan);

        /// <summary>
        /// Which members of this block were actually spoken for by whatever last filled it.
        ///
        /// Every member, unless a recording made from a different build filled it. Read by the
        /// bridge on the way back to the object, so a member the recording did not carry keeps
        /// whatever the object already had rather than being written with the zero sitting in its
        /// place.
        /// </summary>
        public ulong appliedMemberMask { get; protected set; } = StateReadPlan.kAllMembers;

        /// <summary>
        /// Whether what last filled this block described itself differently than this build does.
        ///
        /// The declared path keeps a hash of its layout at the head of every element and refuses an
        /// element that does not match it. That check is right for bytes copied verbatim and wrong
        /// for bytes a plan rearranged, where disagreeing about the layout is the premise rather
        /// than the fault. ⚠ Two mechanisms saying the same thing, which is one more than there
        /// should be -- the hash is on its way out, and until it goes this is what keeps it from
        /// refusing the reads it was never asked about.
        /// </summary>
        public bool readThroughPlan { get; protected set; }

        /// <summary>
        /// Runs a plan into raw element storage. Shared by the typed and the declared block, which
        /// differ in how they hold their elements and not at all in how a plan is run.
        /// </summary>
        protected static unsafe void RunPlan(ReadOnlySpan<byte> bytes, int elementCount,
            StateReadPlan plan, byte* destination, int destinationStride)
        {
            fixed (byte* source = bytes)
            {
                for (int i = 0; i < elementCount; i++)
                {
                    var from = source + (long)i * plan.sourceStride;
                    var to = destination + (long)i * destinationStride;

                    // Who and when, which every element carries in the same shape whatever its
                    // value looks like.
                    UnsafeUtility.MemCpy(to, from, Math.Min(plan.sourceMetaSize, plan.destinationMetaSize));

                    // Cleared rather than left standing. Nothing reads these bytes on the way to the
                    // object -- the mask stops that -- but the block is also what the viewer shows
                    // and what a comparison walks, and there the honest answer for a member the take
                    // never carried is nothing, not the value the element at this index held for a
                    // different owner one frame ago.
                    UnsafeUtility.MemClear(to + plan.destinationMetaSize, plan.destinationPayloadSize);

                    var runs = plan.runs;
                    for (int r = 0; r < runs.Length; r++)
                    {
                        var run = runs[r];
                        UnsafeUtility.MemCpy(
                            to + plan.destinationMetaSize + run.destinationOffset,
                            from + plan.sourceMetaSize + run.sourceOffset,
                            run.length);
                    }
                }
            }
        }

        /// <summary>Owner of the element at an index, so two runs can be lined up by owner.</summary>
        public abstract int OwnerIdAt(int index);

        /// <summary>The producer that wrote an element, as an interned id, or none.</summary>
        public abstract int SourceIdAt(int index);

        /// <summary>
        /// The producer's own stamp on an element. Meaningful against that producer's clock only.
        /// </summary>
        public abstract long TimeAt(int index);

        /// <summary>
        /// Copies one element's value -- the part after the metadata -- into <paramref name="destination"/>.
        /// Written this way so a reader can take a single element without knowing the element type,
        /// and without the metadata layout being guessed at anywhere else.
        /// </summary>
        public abstract void CopyValueTo(int index, byte[] destination);

        /// <summary>
        /// Bytes an element spends on metadata before its value: the owner, the producer and the
        /// stamp. Fixed by the layout of the element, which is what the recording stores.
        /// </summary>
        public abstract int metaSize { get; }

        /// <summary>Index of an owner's element, or -1. The non-generic form of the lookup.</summary>
        public abstract int IndexOfOwner(int ownerId);

        /// <summary>
        /// Whether two elements are the same, meta and value.
        ///
        /// Exact rather than approximate. On one machine a deterministic run reproduces its floats
        /// bit for bit, and anything less would hide the divergence this exists to find. Comparing
        /// across machines needs a tolerance, and that belongs to whoever adds mirroring -- pretending
        /// to have it now would make the first number look better than it is.
        /// </summary>
        public abstract bool ElementEquals(int index, StateBlock other, int otherIndex);

        /// <summary>
        /// Releases the storage. The block stays usable and allocates again on next write, so
        /// tearing a run down and starting another does not have to rebuild the set of blocks.
        /// </summary>
        public abstract void Dispose();
    }

    /// <summary>
    /// The elements of one type, packed densely in unmanaged memory.
    ///
    /// Dense and same-shaped every frame is what makes the three things that read it cheap: a
    /// keyframe is a straight copy of the array, comparing two runs is an element-by-element
    /// comparison, and applying a recording is a write-back. Sparse or delta-encoded storage would
    /// break all three to save space that general-purpose compression recovers anyway.
    ///
    /// The storage is native rather than a managed array. The elements were always unmanaged; what
    /// this adds is an address that does not move, so the byte view a recording writes is a plain
    /// pointer with nothing to pin, and the data sits where a job can reach it.
    /// </summary>
    public sealed unsafe class StateBlock<T> : StateBlock where T : unmanaged
    {
        private NativeArray<StateElement<T>> _elements;
        private int _count;
        private readonly string _typeName;

        /// <param name="typeName">
        /// The name a recording calls this block by -- the owner's, where a bridge made the block
        /// for one. Null for a struct a producer publishes by hand, which has no owner and so is
        /// named after itself.
        /// </param>
        public StateBlock(string typeName = null)
        {
            _typeName = string.IsNullOrEmpty(typeName) ? typeof(T).FullName : typeName;
        }

        public override Type elementType => typeof(T);

        public override string typeName => _typeName;

        public override int count => _count;

        // sizeof rather than Marshal.SizeOf: this is the stride the elements actually sit at, and
        // it folds to a constant instead of a call per frame.
        public override int elementSize => sizeof(StateElement<T>);

        /// <summary>Storage. Only the first <see cref="count"/> entries are valid.</summary>
        public NativeArray<StateElement<T>> elements => _elements;

        public ref StateElement<T> this[int index]
        {
            get
            {
                if ((uint)index >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(index));

                return ref UnsafeUtility.ArrayElementAsRef<StateElement<T>>(
                    _elements.GetUnsafePtr(), index);
            }
        }

        /// <summary>Index of an owner's element, or -1.</summary>
        public int IndexOf(int ownerId)
        {
            if (!_elements.IsCreated) return -1;

            var items = (StateElement<T>*)_elements.GetUnsafeReadOnlyPtr();
            for (int i = 0; i < _count; i++)
            {
                if (items[i].ownerId == ownerId) return i;
            }

            return -1;
        }

        /// <summary>
        /// The element for an owner, appending one if this is the first time it is seen. Returned by
        /// reference so a producer writes into the storage in place; taking a copy and putting it
        /// back would double the cost of every frame for a struct this size.
        ///
        /// A linear scan rather than a side index: the owners of one type number in the ones and
        /// tens, and a map would iterate in an order that is not the order of record.
        /// </summary>
        public ref StateElement<T> GetOrCreate(int ownerId)
        {
            var index = IndexOf(ownerId);
            if (index < 0)
            {
                _EnsureCapacity(_count + 1);
                index = _count++;

                ref var created = ref UnsafeUtility.ArrayElementAsRef<StateElement<T>>(
                    _elements.GetUnsafePtr(), index);

                // Cleared explicitly: the storage can be left over from a longer earlier run, and an
                // element inheriting a stranger's value would read as state rather than as garbage.
                created = default;
                created.ownerId = ownerId;
                return ref created;
            }

            return ref UnsafeUtility.ArrayElementAsRef<StateElement<T>>(_elements.GetUnsafePtr(), index);
        }

        /// <summary>
        /// Drops an owner's element. The remaining elements keep their relative order, because the
        /// order is what a recording stores.
        /// </summary>
        public bool Remove(int ownerId)
        {
            var index = IndexOf(ownerId);
            if (index < 0) return false;

            var items = (StateElement<T>*)_elements.GetUnsafePtr();
            UnsafeUtility.MemMove(items + index, items + index + 1,
                (long)(_count - index - 1) * sizeof(StateElement<T>));

            _count--;
            return true;
        }

        public override void Reset() => _count = 0;

        public override ReadOnlySpan<byte> AsBytes()
        {
            if (_count == 0 || !_elements.IsCreated) return ReadOnlySpan<byte>.Empty;

            // Legitimate here in a way it would not be over a managed array: native storage does not
            // move, so a span built on the pointer stays valid with nothing pinning it.
            return new ReadOnlySpan<byte>(_elements.GetUnsafeReadOnlyPtr(),
                _count * sizeof(StateElement<T>));
        }

        public override void ReadFrom(ReadOnlySpan<byte> bytes, int elementCount)
        {
            var source = MemoryMarshal.Cast<byte, StateElement<T>>(bytes);
            if (elementCount < 0 || elementCount > source.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(elementCount),
                    $"[RemoteControl] {elementCount} elements do not fit in {bytes.Length} bytes.");
            }

            _EnsureCapacity(elementCount);
            _count = elementCount;
            appliedMemberMask = StateReadPlan.kAllMembers;
            readThroughPlan = false;

            if (elementCount == 0) return;

            source.Slice(0, elementCount)
                .CopyTo(new Span<StateElement<T>>(_elements.GetUnsafePtr(), elementCount));
        }

        public override void ReadFrom(ReadOnlySpan<byte> bytes, int elementCount, StateReadPlan plan)
        {
            if (plan == null || plan.isIdentical)
            {
                ReadFrom(bytes, elementCount);
                return;
            }

            if (elementCount < 0 || (long)elementCount * plan.sourceStride > bytes.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(elementCount),
                    $"[RemoteControl] {elementCount} elements of {plan.sourceStride} bytes do not fit in {bytes.Length}.");
            }

            _EnsureCapacity(elementCount);
            _count = elementCount;
            appliedMemberMask = plan.appliedMemberMask;
            readThroughPlan = true;

            if (elementCount == 0) return;

            RunPlan(bytes, elementCount, plan, (byte*)_elements.GetUnsafePtr(), sizeof(StateElement<T>));
        }

        public override int OwnerIdAt(int index) => this[index].ownerId;

        public override int SourceIdAt(int index)
        {
            var source = this[index].source;
            return source.isValid ? source.id : FrameSymbolTable.kNone;
        }

        public override long TimeAt(int index) => this[index].time;

        // Taken from the type rather than assumed: the value sits wherever the compiler put it after
        // the three metadata fields, and that offset is what the recording's bytes already follow.
        public override int metaSize => (int)UnsafeUtility.GetFieldOffset(
            typeof(StateElement<T>).GetField(nameof(StateElement<T>.value)));

        public override void CopyValueTo(int index, byte[] destination)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));

            var offset = metaSize;
            var length = elementSize - offset;
            if (destination.Length < length)
                throw new ArgumentException("Destination is too small for one value.", nameof(destination));

            ref var element = ref this[index];
            fixed (byte* target = destination)
            {
                UnsafeUtility.MemCpy(target, (byte*)UnsafeUtility.AddressOf(ref element) + offset, length);
            }
        }

        public override int IndexOfOwner(int ownerId) => IndexOf(ownerId);

        public override bool ElementEquals(int index, StateBlock other, int otherIndex)
        {
            if (!(other is StateBlock<T> typed)) return false;
            if ((uint)index >= (uint)_count) return false;
            if ((uint)otherIndex >= (uint)typed._count) return false;

            var mine = (byte*)_elements.GetUnsafeReadOnlyPtr() + (long)index * sizeof(StateElement<T>);
            var theirs = (byte*)typed._elements.GetUnsafeReadOnlyPtr() + (long)otherIndex * sizeof(StateElement<T>);

            return UnsafeUtility.MemCmp(mine, theirs, sizeof(StateElement<T>)) == 0;
        }

        public override void Dispose()
        {
            if (_elements.IsCreated) _elements.Dispose();

            _elements = default;
            _count = 0;
        }

        private void _EnsureCapacity(int required)
        {
            var capacity = _elements.IsCreated ? _elements.Length : 0;
            if (capacity >= required) return;

            var grown = Math.Max(required, capacity == 0 ? 4 : capacity * 2);
            var replacement = new NativeArray<StateElement<T>>(grown, Allocator.Persistent,
                NativeArrayOptions.ClearMemory);

            if (_elements.IsCreated)
            {
                UnsafeUtility.MemCpy(replacement.GetUnsafePtr(), _elements.GetUnsafeReadOnlyPtr(),
                    (long)_count * sizeof(StateElement<T>));

                _elements.Dispose();
            }

            _elements = replacement;
        }

        public override string ToString() => $"{typeof(T).Name} x{_count} ({elementSize} B each)";
    }

    /// <summary>
    /// Every state block of the current frame, one per element type.
    ///
    /// Owned by the gate and carried from frame to frame rather than rebuilt: this is the current
    /// state of the world, not a per-frame delta. A producer that stops writing leaves its last
    /// value standing, which is what keeps a dropped connection or a disabled track from snapping
    /// everything back to defaults.
    ///
    /// The blocks hold native storage, so whoever owns this has to dispose it.
    /// </summary>
    public sealed class StateBlockSet : IDisposable
    {
        private readonly Dictionary<Type, StateBlock> _blocks = new Dictionary<Type, StateBlock>();
        private readonly List<StateBlock> _ordered = new List<StateBlock>();

        /// <summary>
        /// Blocks in the order they were first created. Iterated rather than the map, so writing a
        /// recording lays the types out the same way every run.
        /// </summary>
        public IReadOnlyList<StateBlock> blocks => _ordered;

        /// <summary>The block for an element type, creating it on first use.</summary>
        public StateBlock<T> GetOrCreate<T>(string typeName = null) where T : unmanaged
        {
            // Making a block is also how a type announces that it belongs on the lane, so a player
            // meeting the name in a recording can make one too. Guarded per type rather than by a
            // lookup, because this sits on the per-frame path of every producer.
            //
            // Ahead of the lookup, not after it: a set that already holds the block returns early,
            // and a registry emptied underneath a long-lived set would then never be refilled by the
            // calls that filled it the first time.
            if (_Announced<T>.generation != StateTypeRegistry.generation)
            {
                _Announced<T>.generation = StateTypeRegistry.generation;
                StateTypeRegistry.Register<T>(typeName);
            }

            if (_blocks.TryGetValue(typeof(T), out var existing)) return (StateBlock<T>)existing;

            var created = new StateBlock<T>(typeName);
            _blocks.Add(typeof(T), created);
            _ordered.Add(created);
            return created;
        }

        /// <summary>
        /// The block for a type declared by an asset, creating it on first use.
        ///
        /// Keyed by the exposed type itself, which is what gives each declared type a block of its
        /// own rather than a shared one sized for the worst case. The width comes from the
        /// declaration, so two builds that disagree about it produce blocks of different sizes --
        /// which is the mismatch a recording is already checked for.
        /// </summary>
        /// <param name="schemaSignature">
        /// The declaration this block is being asked for, as a recording interns it, or null when
        /// the caller does not know.
        ///
        /// ⚠ Width alone is not enough to tell two declarations apart. Two of the same total size
        /// lay their members out differently, and reusing a block across that hands back values
        /// captured under one layout and read under another -- which looks like values rather than
        /// like an error. It used to be caught per element by a hash led into every payload; the
        /// description catches it here instead, once, where the block is fetched.
        /// </param>
        public DeclaredStateBlock GetOrCreateDeclared(Type ownerType, int payloadSize,
            string schemaSignature = null)
        {
            if (ownerType == null) throw new ArgumentNullException(nameof(ownerType));

            if (_blocks.TryGetValue(ownerType, out var existing))
            {
                var found = (DeclaredStateBlock)existing;

                // The declaration changed under a block that is already carrying values. Replaced
                // rather than reused: the stride is the layout, and reading the old elements at the
                // new stride would hand back values sliced out of the middle of their neighbours.
                var sameShape = found.payloadSize == payloadSize
                                && (schemaSignature == null
                                    || found.schemaSignature == null
                                    || string.Equals(found.schemaSignature, schemaSignature, StringComparison.Ordinal));

                if (sameShape)
                {
                    // Remembered rather than ignored, so a block first made by a replay -- which
                    // knows the width and not the declaration -- takes on the description the first
                    // producer to write it names.
                    if (schemaSignature != null) found.schemaSignature = schemaSignature;
                    return found;
                }

                _blocks.Remove(ownerType);
                _ordered.Remove(found);
                found.Dispose();
            }

            var created = new DeclaredStateBlock(ownerType, payloadSize) { schemaSignature = schemaSignature };
            StateTypeRegistry.RegisterDeclared(ownerType, payloadSize);

            _blocks.Add(ownerType, created);
            _ordered.Add(created);
            return created;
        }

        /// <summary>The declared block for an exposed type, or null when nothing has written one.</summary>
        public DeclaredStateBlock FindDeclared(Type ownerType)
            => ownerType != null && _blocks.TryGetValue(ownerType, out var existing)
                ? existing as DeclaredStateBlock
                : null;

        /// <summary>
        /// Which generation of the registry this type last announced itself to.
        ///
        /// Not a bool: a static field outlives the registry being emptied, so a plain "already done"
        /// left a cleared registry with no way of being refilled -- the calls that announced the
        /// type the first time all skip it forever after.
        /// </summary>
        private static class _Announced<T> where T : unmanaged
        {
            public static int generation = -1;
        }

        /// <summary>The block for an element type, or null when nothing has written one yet.</summary>
        public StateBlock<T> Find<T>() where T : unmanaged
            => _blocks.TryGetValue(typeof(T), out var existing) ? (StateBlock<T>)existing : null;

        /// <summary>
        /// The block for a type named the way a recording names it, or null.
        ///
        /// Only finds types something has already created a block for. Replaying into an app that
        /// does not have the producer is not a failure to paper over -- the state has nowhere to go,
        /// and the caller is told rather than silently given an empty world.
        /// </summary>
        public StateBlock FindByTypeName(string fullName)
        {
            if (string.IsNullOrEmpty(fullName)) return null;

            for (int i = 0; i < _ordered.Count; i++)
            {
                if (_ordered[i].typeName == fullName) return _ordered[i];
            }

            return null;
        }

        /// <summary>
        /// Empties every block but keeps them, so the type layout of a run stays put. Used when a
        /// run restarts, not between frames.
        /// </summary>
        public void Reset()
        {
            for (int i = 0; i < _ordered.Count; i++) _ordered[i].Reset();
        }

        /// <summary>
        /// Releases every block's storage. The blocks stay, so a set that is disposed and used again
        /// keeps its type layout and allocates fresh storage on demand.
        /// </summary>
        public void Dispose()
        {
            for (int i = 0; i < _ordered.Count; i++) _ordered[i].Dispose();
        }

        public override string ToString() => $"state ({_ordered.Count} blocks)";
    }
}
