// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;
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
    ///
    /// ⚠ The field order is the element layout every block shares -- owner at 0, producer at 4,
    /// stamp at 8 -- and <see cref="StateBlock"/> reads the meta at those offsets without knowing
    /// the value type. Checked once per type when its first block is made.
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
    /// The elements of one type, packed densely in unmanaged memory.
    ///
    /// Dense and same-shaped every frame is what makes the things that read it cheap: a keyframe is
    /// a straight copy of the array and applying a recording is a write-back. Sparse or
    /// delta-encoded storage would break both to save space that general-purpose compression
    /// recovers anyway.
    ///
    /// Every element starts the same way -- owner, producer, stamp -- and only the value after that
    /// differs by type. So storage, lookup, the byte view and reading back from a recording live
    /// here once, and the two kinds of block differ only in how their value is reached: through a
    /// struct the generator wrote (<see cref="StateBlock{T}"/>) or at offsets a declaration
    /// computed (<see cref="DeclaredStateBlock"/>).
    /// </summary>
    public abstract unsafe class StateBlock : IDisposable
    {
        private const int kSourceOffset = 4;
        private const int kTimeOffset = 8;

        private RecordList _elements;
        private readonly int _metaSize;

        protected StateBlock(int stride, int metaSize)
        {
            _elements = new RecordList(stride);
            _metaSize = metaSize;
        }

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
        public int count => _elements.count;

        /// <summary>Size of one element in bytes, as it is written to a recording.</summary>
        public int elementSize => _elements.stride;

        /// <summary>
        /// Bytes an element spends on metadata before its value: the owner, the producer and the
        /// stamp. Fixed by the layout of the element, which is what the recording stores.
        /// </summary>
        public int metaSize => _metaSize;

        /// <summary>
        /// Which members of this block were actually spoken for by whatever last filled it.
        ///
        /// Every member, unless a recording made from a different build filled it. Read by the
        /// bridge on the way back to the object, so a member the recording did not carry keeps
        /// whatever the object already had rather than being written with the zero sitting in its
        /// place.
        /// </summary>
        public ulong appliedMemberMask { get; private set; } = StateReadPlan.kAllMembers;

        /// <summary>
        /// Whether what last filled this block described itself differently than this build does,
        /// so its bytes were rearranged by a plan on the way in rather than copied.
        /// </summary>
        public bool readThroughPlan { get; private set; }

        /// <summary>Index of an owner's element, or -1.</summary>
        public int IndexOfOwner(int ownerId) => _elements.IndexOf(ownerId);

        /// <summary>
        /// Drops an owner's element. The remaining elements keep their relative order, because the
        /// order is what a recording stores.
        /// </summary>
        public bool Remove(int ownerId) => _elements.Remove(ownerId);

        /// <summary>
        /// Drops every element. Not called between frames: a state block holds the current state,
        /// and an element that stops being written keeps its last value rather than snapping back
        /// to a default nobody asked for.
        /// </summary>
        public void Reset() => _elements.Clear();

        /// <summary>
        /// The elements as they sit in memory, for a recording to write out verbatim. Valid until
        /// the block is next written to.
        /// </summary>
        public ReadOnlySpan<byte> AsBytes() => _elements.AsBytes();

        /// <summary>
        /// Replaces the contents with elements read back from a recording.
        ///
        /// The whole block at once rather than element by element: the recorded form is the same
        /// dense array this holds, so restoring it is a copy rather than a merge. Anything that was
        /// here and is not in the recording is gone, which is the point -- a replayed frame is the
        /// state at that frame, not the state at that frame layered over whatever came before.
        /// </summary>
        public void ReadFrom(ReadOnlySpan<byte> bytes, int elementCount)
        {
            var stride = elementSize;
            if (elementCount < 0 || (long)elementCount * stride > bytes.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(elementCount),
                    $"[RemoteControl] {elementCount} elements of {stride} bytes do not fit in {bytes.Length}.");
            }

            var destination = _elements.ResizeForOverwrite(elementCount);
            appliedMemberMask = StateReadPlan.kAllMembers;
            readThroughPlan = false;

            if (elementCount == 0) return;

            bytes.Slice(0, elementCount * stride).CopyTo(new Span<byte>(destination, elementCount * stride));
        }

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
        public void ReadFrom(ReadOnlySpan<byte> bytes, int elementCount, StateReadPlan plan)
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

            var destination = _elements.ResizeForOverwrite(elementCount);
            appliedMemberMask = plan.appliedMemberMask;
            readThroughPlan = true;

            if (elementCount == 0) return;

            _RunPlan(bytes, elementCount, plan, destination, elementSize);
        }

        /// <summary>Owner of the element at an index, so two runs can be lined up by owner.</summary>
        public int OwnerIdAt(int index) => _elements.KeyAt(index);

        /// <summary>The producer that wrote an element, as an interned id, or none.</summary>
        public int SourceIdAt(int index)
        {
            var source = *(FrameSource*)(_elements.Get(index) + kSourceOffset);
            return source.isValid ? source.id : FrameSymbolTable.kNone;
        }

        /// <summary>
        /// The producer's own stamp on an element. Meaningful against that producer's clock only.
        /// </summary>
        public long TimeAt(int index) => *(long*)(_elements.Get(index) + kTimeOffset);

        /// <summary>
        /// One element's value -- the part after the metadata -- as bytes, so a reader can take a
        /// single element without knowing the element type. Valid until the block is next written to.
        /// </summary>
        public ReadOnlySpan<byte> ValueBytes(int index)
            => new ReadOnlySpan<byte>(_elements.Get(index) + _metaSize, elementSize - _metaSize);

        /// <summary>
        /// Releases the storage. The block stays usable and allocates again on next write, so
        /// tearing a run down and starting another does not have to rebuild the set of blocks.
        /// </summary>
        public void Dispose() => _elements.Dispose();

        /// <summary>The element at an index, for a derived block to reach its value through.</summary>
        protected byte* ElementAt(int index) => _elements.Get(index);

        /// <summary>
        /// Index of an owner's element, appending a zeroed one stamped with the owner if this is
        /// the first time it is seen.
        /// </summary>
        protected int GetOrCreateIndex(int ownerId) => _elements.GetOrAdd(ownerId);

        /// <summary>Writes who produced an element and when.</summary>
        protected void WriteMeta(int index, FrameSource source, long time)
        {
            var element = _elements.Get(index);

            // Written as the struct rather than as its bits: the handle keeps its id offset by one
            // so that a default reads as unresolved, and reproducing that here by hand would be a
            // second place that has to know.
            *(FrameSource*)(element + kSourceOffset) = source;
            *(long*)(element + kTimeOffset) = time;
        }

        private static void _RunPlan(ReadOnlySpan<byte> bytes, int elementCount,
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
                    // object -- the mask stops that -- but the block is also what the viewer shows,
                    // and there the honest answer for a member the take never carried is nothing,
                    // not the value the element at this index held for a different owner one frame
                    // ago.
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
    }

    /// <summary>
    /// A state block whose element is a struct: the value is <typeparamref name="T"/>, reached by
    /// reference so a producer writes straight into the storage.
    /// </summary>
    public sealed unsafe class StateBlock<T> : StateBlock where T : unmanaged
    {
        private static readonly int _valueOffset = _CheckedValueOffset();

        private readonly string _typeName;

        /// <param name="typeName">
        /// The name a recording calls this block by -- the owner's, where a bridge made the block
        /// for one. Null for a struct a producer publishes by hand, which has no owner and so is
        /// named after itself.
        /// </param>
        public StateBlock(string typeName = null) : base(sizeof(StateElement<T>), _valueOffset)
        {
            _typeName = string.IsNullOrEmpty(typeName) ? typeof(T).FullName : typeName;
        }

        public override Type elementType => typeof(T);

        public override string typeName => _typeName;

        public ref StateElement<T> this[int index] => ref UnsafeUtility.AsRef<StateElement<T>>(ElementAt(index));

        /// <summary>Index of an owner's element, or -1.</summary>
        public int IndexOf(int ownerId) => IndexOfOwner(ownerId);

        /// <summary>
        /// The element for an owner, appending one if this is the first time it is seen. Returned by
        /// reference so a producer writes into the storage in place; taking a copy and putting it
        /// back would double the cost of every frame for a struct this size.
        /// </summary>
        public ref StateElement<T> GetOrCreate(int ownerId)
            => ref UnsafeUtility.AsRef<StateElement<T>>(ElementAt(GetOrCreateIndex(ownerId)));

        public override string ToString() => $"{typeof(T).Name} x{count} ({elementSize} B each)";

        // The meta is read at fixed offsets by the base, so the compiler's layout of this element
        // has to agree. It does for every value type whose alignment is at most eight -- which is
        // every one a producer writes -- and this says so once instead of trusting it.
        private static int _CheckedValueOffset()
        {
            var type = typeof(StateElement<T>);

            if (UnsafeUtility.GetFieldOffset(type.GetField(nameof(StateElement<T>.ownerId))) != 0
                || UnsafeUtility.GetFieldOffset(type.GetField(nameof(StateElement<T>.source))) != kSourceOffset
                || UnsafeUtility.GetFieldOffset(type.GetField(nameof(StateElement<T>.time))) != kTimeOffset)
            {
                throw new NotSupportedException(
                    $"[RemoteControl] {typeof(T).FullName} lays out its state element in a way the block cannot read.");
            }

            return UnsafeUtility.GetFieldOffset(type.GetField(nameof(StateElement<T>.value)));
        }

        private const int kSourceOffset = 4;
        private const int kTimeOffset = 8;
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
        private readonly Dictionary<string, StateBlock> _byName = new Dictionary<string, StateBlock>(StringComparer.Ordinal);
        private readonly List<StateBlock> _ordered = new List<StateBlock>();

        /// <summary>
        /// Blocks in the order they were first created. Iterated rather than the map, so writing a
        /// recording lays the types out the same way every run.
        /// </summary>
        public IReadOnlyList<StateBlock> blocks => _ordered;

        /// <summary>The block for an element type, creating it on first use.</summary>
        public StateBlock<T> GetOrCreate<T>(string typeName = null) where T : unmanaged
        {
            if (_blocks.TryGetValue(typeof(T), out var existing)) return (StateBlock<T>)existing;

            var created = new StateBlock<T>(typeName);

            // Making a block is also how a struct published by hand announces that it belongs on the
            // lane, so a player meeting the name in a recording can make one too. A type with a
            // bridge was announced by it already, and this does not displace it.
            StateTypes.RegisterStruct<T>(created.typeName);

            _Add(typeof(T), created);
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
        /// like an error. The description catches it here, once, where the block is fetched.
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

                _Remove(ownerType, found);
                found.Dispose();
            }

            // Not announced from here: a declared type reaches the lane through its bridge, which
            // is what makes its block on a replay (see StateTypes).
            var created = new DeclaredStateBlock(ownerType, payloadSize) { schemaSignature = schemaSignature };

            _Add(ownerType, created);
            return created;
        }

        /// <summary>The declared block for an exposed type, or null when nothing has written one.</summary>
        public DeclaredStateBlock FindDeclared(Type ownerType)
            => ownerType != null && _blocks.TryGetValue(ownerType, out var existing)
                ? existing as DeclaredStateBlock
                : null;

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
            => !string.IsNullOrEmpty(fullName) && _byName.TryGetValue(fullName, out var block) ? block : null;

        private void _Add(Type key, StateBlock block)
        {
            _blocks.Add(key, block);
            _byName[block.typeName] = block;
            _ordered.Add(block);
        }

        private void _Remove(Type key, StateBlock block)
        {
            _blocks.Remove(key);
            _ordered.Remove(block);

            if (_byName.TryGetValue(block.typeName, out var named) && ReferenceEquals(named, block))
            {
                _byName.Remove(block.typeName);
            }
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
