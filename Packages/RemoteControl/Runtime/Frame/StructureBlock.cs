// Copyright (c) You-Ri, 2026
using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Lilium.RemoteControl.Frames
{
    /// <summary>One object in the inventory: what exists, of what type, under whom.</summary>
    public struct ObjectEntry
    {
        /// <summary>Interned object id. See <see cref="InputSymbolTable"/>.</summary>
        public int id;

        /// <summary>Interned type name.</summary>
        public int typeId;

        /// <summary>Interned id of the parent, or <see cref="InputSymbolTable.kNone"/>.</summary>
        public int parentId;

        public override string ToString() => $"#{id} type:{typeId} parent:{parentId}";
    }

    /// <summary>
    /// Shape: what exists and how many, as opposed to what the values are.
    ///
    /// The inventory is a dense array rather than a map because **the order is part of the
    /// recording**. A hash map would iterate in whatever order it felt like, and two machines fed
    /// the same inputs would lay their state out differently. Lookup by id walks the array; the
    /// counts involved (a few avatars, tens of lights) do not justify a side index yet.
    ///
    /// Applying this is not assignment but a reconcile against reality: in the inventory and not in
    /// reality means create, in reality and not in the inventory means **destroy**, in both means do
    /// nothing. The last of those is why applying the same keyframe twice does not reload an avatar,
    /// and the second is why scrubbing back past a spawn makes it disappear again.
    ///
    /// Native storage, like the state blocks: the entries were always unmanaged, and holding them
    /// where the address does not move keeps writing them out a plain pointer copy.
    /// </summary>
    public sealed unsafe class StructureBlock : IDisposable
    {
        private NativeArray<ObjectEntry> _objects;
        private int _count;
        private long _epoch;

        /// <summary>
        /// Sequence of the most recent structural change. State can only be read against the
        /// structure it belongs to, so a state block stamped with a different epoch must not be
        /// applied -- the offsets it was written against no longer hold.
        /// </summary>
        public long epoch => _epoch;

        /// <summary>Number of valid entries in <see cref="objects"/>.</summary>
        public int count => _count;

        /// <summary>Storage. Only the first <see cref="count"/> entries are valid.</summary>
        public NativeArray<ObjectEntry> objects => _objects;

        public ObjectEntry this[int index]
        {
            get
            {
                if ((uint)index >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(index));

                return _objects[index];
            }
        }

        /// <summary>Index of an object by id, or -1.</summary>
        public int IndexOf(int id)
        {
            if (!_objects.IsCreated) return -1;

            var items = (ObjectEntry*)_objects.GetUnsafeReadOnlyPtr();
            for (int i = 0; i < _count; i++)
            {
                if (items[i].id == id) return i;
            }

            return -1;
        }

        public bool Contains(int id) => IndexOf(id) >= 0;

        /// <summary>
        /// Adds an object, or updates its type and parent if it is already there. Returns true when
        /// something actually changed, which is also when <see cref="epoch"/> advances -- a
        /// re-declaration of what is already known must not invalidate the state written against it.
        /// </summary>
        public bool AddOrUpdate(int id, int typeId, int parentId)
        {
            var index = IndexOf(id);
            if (index >= 0)
            {
                ref var existing = ref UnsafeUtility.ArrayElementAsRef<ObjectEntry>(
                    _objects.GetUnsafePtr(), index);

                if (existing.typeId == typeId && existing.parentId == parentId) return false;

                existing.typeId = typeId;
                existing.parentId = parentId;
                _epoch++;
                return true;
            }

            _EnsureCapacity(_count + 1);

            ref var created = ref UnsafeUtility.ArrayElementAsRef<ObjectEntry>(
                _objects.GetUnsafePtr(), _count);

            created.id = id;
            created.typeId = typeId;
            created.parentId = parentId;

            _count++;
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
            var index = IndexOf(id);
            if (index < 0) return false;

            var items = (ObjectEntry*)_objects.GetUnsafePtr();
            UnsafeUtility.MemMove(items + index, items + index + 1,
                (long)(_count - index - 1) * sizeof(ObjectEntry));

            _count--;
            _epoch++;
            return true;
        }

        /// <summary>
        /// Empties the inventory. Used when a run restarts, not between frames -- the structure
        /// carries over from one frame to the next.
        /// </summary>
        public void Reset()
        {
            _count = 0;
            _epoch = 0;
        }

        /// <summary>
        /// Releases the storage. The block stays usable and allocates again on next write.
        /// </summary>
        public void Dispose()
        {
            if (_objects.IsCreated) _objects.Dispose();

            _objects = default;
            _count = 0;
            _epoch = 0;
        }

        private void _EnsureCapacity(int required)
        {
            var capacity = _objects.IsCreated ? _objects.Length : 0;
            if (capacity >= required) return;

            var grown = Math.Max(required, capacity == 0 ? 8 : capacity * 2);
            var replacement = new NativeArray<ObjectEntry>(grown, Allocator.Persistent,
                NativeArrayOptions.ClearMemory);

            if (_objects.IsCreated)
            {
                UnsafeUtility.MemCpy(replacement.GetUnsafePtr(), _objects.GetUnsafeReadOnlyPtr(),
                    (long)_count * sizeof(ObjectEntry));

                _objects.Dispose();
            }

            _objects = replacement;
        }

        public override string ToString() => $"structure epoch {_epoch} ({_count} objects)";
    }
}
