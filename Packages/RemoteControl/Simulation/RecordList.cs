// Copyright (c) You-Ri, 2026
using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Lilium.RemoteControl.Frames
{
    /// <summary>
    /// A dense run of fixed-width records, each led by an <c>int</c> it is filed under, kept in the
    /// order they were added.
    ///
    /// The one container behind every block a frame carries: a state element starts with its owner,
    /// an inventory entry with its object id. Before this there were three hand-written copies of the
    /// same array -- growth, lookup, removal, byte view -- and they had drifted in small ways.
    ///
    /// <para>
    /// **The order is the record.** Lookup goes through a side index, but iteration and the byte view
    /// never do: two machines fed the same writes lay their blocks out identically, because the
    /// array is appended to in the order writes arrive and nothing reorders it. Removal closes the
    /// hole by moving what follows rather than filling it with the last record for the same reason.
    /// </para>
    ///
    /// <para>
    /// The index is rebuilt lazily, after anything that moves records wholesale (a read from a
    /// recording, a removal). Those happen on structural changes and supplied frames; the steady
    /// state is lookups against an index that is already right.
    /// </para>
    ///
    /// A mutable struct: keep it in a field and call it there. A copy shares the storage but not the
    /// count, and the two would disagree about what is valid.
    /// </summary>
    internal unsafe struct RecordList : IDisposable
    {
        private UnsafeList<byte> _bytes;
        private UnsafeHashMap<int, int> _index;
        private readonly int _stride;
        private int _count;
        private bool _indexed;

        public RecordList(int stride)
        {
            if (stride < sizeof(int)) throw new ArgumentOutOfRangeException(nameof(stride));

            _bytes = default;
            _index = default;
            _stride = stride;
            _count = 0;
            _indexed = false;
        }

        /// <summary>Records currently held.</summary>
        public int count => _count;

        /// <summary>Bytes one record occupies.</summary>
        public int stride => _stride;

        /// <summary>The record at an index, bounds-checked.</summary>
        public byte* Get(int index)
        {
            if ((uint)index >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(index));

            return _bytes.Ptr + (long)index * _stride;
        }

        /// <summary>The int a record is filed under.</summary>
        public int KeyAt(int index) => *(int*)Get(index);

        /// <summary>Index of the record filed under <paramref name="key"/>, or -1.</summary>
        public int IndexOf(int key)
        {
            if (_count == 0) return -1;

            _EnsureIndex();
            return _index.TryGetValue(key, out var index) ? index : -1;
        }

        /// <summary>
        /// Index of the record filed under <paramref name="key"/>, appending a zeroed one if there is
        /// none.
        ///
        /// Zeroed explicitly: the storage can be left over from a longer earlier run, and a record
        /// inheriting a stranger's bytes would read as a value rather than as garbage.
        /// </summary>
        public int GetOrAdd(int key)
        {
            var index = IndexOf(key);
            if (index >= 0) return index;

            index = _count;
            _SetCount(_count + 1);

            var record = _bytes.Ptr + (long)index * _stride;
            UnsafeUtility.MemClear(record, _stride);
            *(int*)record = key;

            // IndexOf above built the index, unless the list was empty -- in which case the next
            // lookup builds it with this record already in place.
            if (_indexed) _index.TryAdd(key, index);

            return index;
        }

        /// <summary>
        /// Drops the record filed under <paramref name="key"/>. What follows keeps its relative
        /// order, because the order is what a recording stores.
        /// </summary>
        public bool Remove(int key)
        {
            var index = IndexOf(key);
            if (index < 0) return false;

            var record = _bytes.Ptr + (long)index * _stride;
            UnsafeUtility.MemMove(record, record + _stride, (long)(_count - index - 1) * _stride);

            _SetCount(_count - 1);

            // Everything after the hole moved down by one. Rebuilt on the next lookup rather than
            // patched here: removals come in bursts at a structural change, and one rebuild after
            // the burst is cheaper than renumbering the tail once per removal.
            _indexed = false;
            return true;
        }

        /// <summary>Drops every record, keeping the storage.</summary>
        public void Clear()
        {
            _SetCount(0);
            _indexed = false;
        }

        /// <summary>
        /// The records as they sit in memory. Native storage does not move, so the span stays valid
        /// with nothing pinning it -- until the next write that grows the list.
        /// </summary>
        public ReadOnlySpan<byte> AsBytes()
            => _count == 0 ? ReadOnlySpan<byte>.Empty : new ReadOnlySpan<byte>(_bytes.Ptr, _count * _stride);

        /// <summary>
        /// Makes room for exactly <paramref name="recordCount"/> records and hands back where they
        /// go, for a caller about to overwrite all of them at once (a block read from a recording).
        /// The index is dropped, since what the records are filed under is about to change.
        /// </summary>
        public byte* ResizeForOverwrite(int recordCount)
        {
            if (recordCount < 0) throw new ArgumentOutOfRangeException(nameof(recordCount));

            _SetCount(recordCount);
            _indexed = false;
            return _bytes.IsCreated ? _bytes.Ptr : null;
        }

        /// <summary>
        /// Releases the storage. The list stays usable and allocates again on the next write, so a
        /// run torn down and started again keeps the blocks it had.
        /// </summary>
        public void Dispose()
        {
            if (_bytes.IsCreated) _bytes.Dispose();
            if (_index.IsCreated) _index.Dispose();

            _bytes = default;
            _index = default;
            _count = 0;
            _indexed = false;
        }

        // The list's own length is kept equal to the bytes in use, because growth copies only that
        // much. Capacity doubles so a list that settles at its high-water mark stops reallocating.
        private void _SetCount(int recordCount)
        {
            var bytes = recordCount * _stride;

            if (!_bytes.IsCreated)
            {
                if (recordCount == 0)
                {
                    _count = 0;
                    return;
                }

                _bytes = new UnsafeList<byte>(Math.Max(bytes, 4 * _stride), Allocator.Persistent);
            }
            else if (bytes > _bytes.Capacity)
            {
                _bytes.SetCapacity(Math.Max(bytes, _bytes.Capacity * 2));
            }

            _bytes.Resize(bytes);
            _count = recordCount;
        }

        private void _EnsureIndex()
        {
            if (_indexed) return;

            if (!_index.IsCreated) _index = new UnsafeHashMap<int, int>(Math.Max(_count, 8), Allocator.Persistent);
            else _index.Clear();

            // First occurrence wins, which is what a linear scan would have answered.
            for (int i = 0; i < _count; i++)
            {
                _index.TryAdd(*(int*)(_bytes.Ptr + (long)i * _stride), i);
            }

            _indexed = true;
        }
    }
}
