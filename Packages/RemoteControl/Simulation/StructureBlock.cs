// Copyright (c) You-Ri, 2026
using System;
using Unity.Collections.LowLevel.Unsafe;

namespace Lilium.RemoteControl.Frames
{
    /// <summary>One object in the inventory: what exists, of what type, under whom.</summary>
    public struct ObjectEntry
    {
        /// <summary>
        /// Interned object id. See <see cref="FrameSymbolTable"/>. First, because the inventory is
        /// filed under it (<see cref="RecordList"/> keys on the leading int).
        /// </summary>
        public int id;

        /// <summary>Interned type name.</summary>
        public int typeId;

        /// <summary>Interned id of the parent, or <see cref="FrameSymbolTable.kNone"/>.</summary>
        public int parentId;

        /// <summary>
        /// Interned key naming how to make this object again, or
        /// <see cref="FrameSymbolTable.kNone"/> for one that cannot be.
        ///
        /// The type name is not enough to build from. Two makers can both produce the same type from
        /// different prefabs, so a replay given only the type would pick one of them and be right by
        /// luck. What is recorded is the key of the maker itself -- see <see cref="ILiveRecipe"/>.
        ///
        /// None is a legitimate value and means the object is not something a replay stands up: it
        /// was in the scene from the start, or nothing has said how to make it. Such an object is
        /// still in the inventory, still addressed by the state lane, and still removed when the
        /// recording stops listing it.
        /// </summary>
        public int recipeId;

        /// <summary>
        /// Interned name of the collection member this is an element of, or
        /// <see cref="FrameSymbolTable.kNone"/> when the entry is an object in its own right.
        ///
        /// Held apart from <see cref="id"/> rather than parsed back out of it. The id is a composed
        /// address (<c>{owner}/{member}[{key}]</c>) and nothing has ever read it as anything but an
        /// opaque token -- capture and apply both go through the one function that builds it. Making
        /// the apply side parse it would turn the address into a grammar, and a key holding a
        /// <c>/</c> or a <c>]</c> (a mesh path, a Blender name) would break it. So what the apply
        /// side needs is carried in its own fields, and the id stays a token.
        /// </summary>
        public int memberId;

        /// <summary>
        /// Interned key identifying the element within its collection -- the value of the member
        /// marked <c>[LiveKey]</c>. <see cref="FrameSymbolTable.kNone"/> for a non-element.
        /// </summary>
        public int keyId;

        /// <summary>
        /// Position in the collection as of this frame, or -1 for a non-element. Carried because the
        /// order of a collection is visible to whoever reads it, and standing the elements back up
        /// in the wrong order would be a different world from the recorded one.
        /// </summary>
        public int ordinal;

        /// <summary>True when this entry is an element of a collection.</summary>
        public bool isElement => memberId != FrameSymbolTable.kNone && ordinal >= 0;

        /// <summary>
        /// True when this entry is the collection itself rather than something in it.
        ///
        /// One of these is written for every recorded collection, empty or not. Without it an empty
        /// collection is indistinguishable from one the walk never reached, and the difference
        /// matters twice: a replay may not empty a collection it cannot tell was recorded, and the
        /// viewer cannot show "this holds nothing" apart from "nothing looked".
        /// </summary>
        public bool isCollection => memberId != FrameSymbolTable.kNone && ordinal < 0;

        public override string ToString() => memberId == FrameSymbolTable.kNone
            ? $"#{id} type:{typeId} parent:{parentId}"
            : $"#{id} type:{typeId} parent:{parentId} member:{memberId}[{keyId}] @{ordinal}";
    }

    /// <summary>
    /// Shape: what exists and how many, as opposed to what the values are.
    ///
    /// The inventory is a dense array rather than a map because **the order is part of the
    /// recording**. Lookup by id goes through a side index (<see cref="RecordList"/>), but the
    /// array is what is iterated and written out, so two machines fed the same events lay it out
    /// the same way.
    ///
    /// Applying this is not assignment but a reconcile against reality: in the inventory and not in
    /// reality means create, in reality and not in the inventory means **destroy**, in both means do
    /// nothing. The last of those is why applying the same keyframe twice does not reload an avatar,
    /// and the second is why scrubbing back past a spawn makes it disappear again.
    /// </summary>
    public sealed unsafe class StructureBlock : IDisposable
    {
        private RecordList _objects = new RecordList(sizeof(ObjectEntry));
        private long _epoch;

        /// <summary>
        /// Sequence of the most recent structural change. State can only be read against the
        /// structure it belongs to, so a state block stamped with a different epoch must not be
        /// applied -- the offsets it was written against no longer hold.
        /// </summary>
        public long epoch => _epoch;

        /// <summary>Number of entries.</summary>
        public int count => _objects.count;

        /// <summary>The entry at an index, read in place.</summary>
        public ref readonly ObjectEntry this[int index] => ref UnsafeUtility.AsRef<ObjectEntry>(_objects.Get(index));

        /// <summary>Index of an object by id, or -1.</summary>
        public int IndexOf(int id) => _objects.IndexOf(id);

        /// <summary>
        /// The entries as they sit in memory -- seven ints each, in declaration order -- for a
        /// recording to write out in one copy. Valid until the block is next written to.
        /// </summary>
        public ReadOnlySpan<byte> AsBytes() => _objects.AsBytes();

        public bool Contains(int id) => IndexOf(id) >= 0;

        /// <summary>
        /// Adds an object, or updates its type and parent if it is already there. Returns true when
        /// something actually changed, which is also when <see cref="epoch"/> advances -- a
        /// re-declaration of what is already known must not invalidate the state written against it.
        /// </summary>
        public bool AddOrUpdate(int id, int typeId, int parentId,
            int recipeId = FrameSymbolTable.kNone)
            => AddOrUpdate(id, typeId, parentId, recipeId,
                FrameSymbolTable.kNone, FrameSymbolTable.kNone, -1);

        /// <summary>
        /// The same, for an element of a collection: <paramref name="memberId"/> names the member
        /// holding it, <paramref name="keyId"/> identifies it within that member, and
        /// <paramref name="ordinal"/> is where it sits.
        ///
        /// A move counts as a change, so reordering a collection advances the epoch and therefore
        /// writes a keyframe. The order is part of what a replay has to put back.
        /// </summary>
        public bool AddOrUpdate(int id, int typeId, int parentId, int recipeId,
            int memberId, int keyId, int ordinal)
        {
            var existingIndex = _objects.IndexOf(id);
            var index = existingIndex >= 0 ? existingIndex : _objects.GetOrAdd(id);

            ref var entry = ref UnsafeUtility.AsRef<ObjectEntry>(_objects.Get(index));

            if (existingIndex >= 0
                && entry.typeId == typeId && entry.parentId == parentId
                && entry.recipeId == recipeId && entry.memberId == memberId
                && entry.keyId == keyId && entry.ordinal == ordinal)
            {
                return false;
            }

            entry.typeId = typeId;
            entry.parentId = parentId;
            entry.recipeId = recipeId;
            entry.memberId = memberId;
            entry.keyId = keyId;
            entry.ordinal = ordinal;

            _epoch++;
            return true;
        }

        /// <summary>
        /// Removes an object. Returns true when it was there. The remaining entries keep their
        /// relative order: the array is the order of record, so filling the hole with the last
        /// entry would reorder the inventory behind everyone's back.
        /// </summary>
        public bool Remove(int id)
        {
            if (!_objects.Remove(id)) return false;

            _epoch++;
            return true;
        }

        /// <summary>
        /// Empties the inventory. Used when a run restarts, not between frames -- the structure
        /// carries over from one frame to the next.
        /// </summary>
        public void Reset()
        {
            _objects.Clear();
            _epoch = 0;
        }

        /// <summary>
        /// Releases the storage. The block stays usable and allocates again on next write.
        /// </summary>
        public void Dispose()
        {
            _objects.Dispose();
            _epoch = 0;
        }

        public override string ToString() => $"structure epoch {_epoch} ({count} objects)";
    }
}
