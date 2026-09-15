// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Concurrent;
using System.Threading;

namespace Lilium.RemoteControl.Frames
{
    /// <summary>
    /// Maps the strings an event refers to -- property paths, source names -- onto small integer
    /// ids, so a record can stay a fixed-size unmanaged struct.
    ///
    /// This is the mapping a recording carries in its header: resolving a record needs the
    /// table, and the table is written once rather than repeated on every frame. Dragging a slider
    /// sends the same path sixty times a second, and interning turns that into one string plus
    /// sixty integers.
    ///
    /// Ids are handed out in order from zero and never reused within a run, so a table can be
    /// appended to while it is being read.
    ///
    /// <para>
    /// Only appending takes a lock. Lookups in both directions are lock-free, because they are
    /// the per-frame side: an id is resolved for every text member a replay writes back and every
    /// element a reconcile matches, while a new string arrives a handful of times a take.
    /// </para>
    /// </summary>
    public sealed class FrameSymbolTable
    {
        /// <summary>Id standing for no string at all. Never appears in the table.</summary>
        public const int kNone = -1;

        private readonly ConcurrentDictionary<string, int> _ids = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
        private readonly object _appendLock = new object();

        // Read without the lock. The array is replaced -- never shrunk -- under the lock before the
        // count that needs the larger one is published, so a reader that reads the count first and
        // the array second always holds an array at least that long.
        private string[] _symbols = new string[64];
        private int _count;

        /// <summary>Number of distinct strings interned so far.</summary>
        public int count => Volatile.Read(ref _count);

        /// <summary>
        /// Returns the id for <paramref name="value"/>, adding it if this is the first time it has
        /// been seen. Safe to call from any thread.
        /// </summary>
        public int Intern(string value)
        {
            if (string.IsNullOrEmpty(value)) return kNone;

            if (_ids.TryGetValue(value, out var existing)) return existing;

            lock (_appendLock)
            {
                // Another thread may have interned it between the miss above and this lock.
                if (_ids.TryGetValue(value, out existing)) return existing;

                var id = _count;
                _EnsureCapacity(id + 1);
                _symbols[id] = value;
                Volatile.Write(ref _count, id + 1);

                // Published last: a reader that finds the id in the dictionary is then guaranteed
                // to find the string behind it.
                _ids[value] = id;
                return id;
            }
        }

        /// <summary>
        /// The id already standing for a string, without adding one.
        ///
        /// The question an apply asks. Interning would answer it by handing out a fresh id, which
        /// reads as "no row is filed under this" either way for a live table -- but silently grows
        /// a recording's table with a string the take never had.
        /// </summary>
        public bool TryGetId(string value, out int id)
        {
            id = kNone;
            if (string.IsNullOrEmpty(value)) return false;

            return _ids.TryGetValue(value, out id);
        }

        /// <summary>
        /// Puts a string at an id somebody else chose, for reading a table back rather than
        /// building one.
        ///
        /// A recording's ids are already assigned. Re-interning them in the order they happen to be
        /// read would renumber the take, which is the whole fault this exists to make impossible.
        /// Gaps stay empty: a file may name an id before the ones below it, and those resolve to
        /// nothing until they arrive.
        /// </summary>
        public void SetAt(int id, string value)
        {
            if (id < 0) return;

            lock (_appendLock)
            {
                _EnsureCapacity(id + 1);

                var previous = _symbols[id];
                if (!string.IsNullOrEmpty(previous)) _ids.TryRemove(previous, out _);

                _symbols[id] = value;
                if (id >= _count) Volatile.Write(ref _count, id + 1);

                if (!string.IsNullOrEmpty(value)) _ids[value] = id;
            }
        }

        /// <summary>Returns the string behind an id, or null for <see cref="kNone"/>.</summary>
        public bool TryResolve(int id, out string value)
        {
            value = null;
            if (id < 0) return false;

            // The count first, then the array: see the note on the fields.
            if (id >= Volatile.Read(ref _count)) return false;

            value = Volatile.Read(ref _symbols)[id];
            return true;
        }

        /// <summary>Returns the string behind an id, or an empty string when it is not known.</summary>
        public string Resolve(int id) => TryResolve(id, out var value) && value != null ? value : string.Empty;

        /// <summary>
        /// Copies the whole table, oldest id first, for writing into a recording header.
        /// </summary>
        public string[] ToArray()
        {
            lock (_appendLock)
            {
                var copy = new string[_count];
                Array.Copy(_symbols, copy, _count);
                return copy;
            }
        }

        /// <summary>Empties the table. Ids handed out before this are no longer meaningful.</summary>
        public void Reset()
        {
            lock (_appendLock)
            {
                Volatile.Write(ref _count, 0);
                _ids.Clear();
                Array.Clear(_symbols, 0, _symbols.Length);
            }
        }

        private void _EnsureCapacity(int required)
        {
            if (required <= _symbols.Length) return;

            var size = _symbols.Length;
            while (size < required) size *= 2;

            var grown = new string[size];
            Array.Copy(_symbols, grown, _symbols.Length);
            Volatile.Write(ref _symbols, grown);
        }
    }
}
