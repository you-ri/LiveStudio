// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Concurrent;

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
    /// </summary>
    public sealed class FrameSymbolTable
    {
        /// <summary>Id standing for no string at all. Never appears in the table.</summary>
        public const int kNone = -1;

        // Lookup is lock-free for symbols already interned, which is the steady state: a path is
        // interned once and then hit on every later write to it.
        private readonly ConcurrentDictionary<string, int> _ids = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
        private readonly object _appendLock = new object();

        private string[] _symbols = new string[64];
        private int _count;

        /// <summary>Number of distinct strings interned so far.</summary>
        public int count
        {
            get { lock (_appendLock) { return _count; } }
        }

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

                if (_count == _symbols.Length)
                {
                    var grown = new string[_symbols.Length * 2];
                    Array.Copy(_symbols, grown, _count);
                    _symbols = grown;
                }

                var id = _count;
                _symbols[id] = value;
                _count = id + 1;

                // Published last: a reader that finds the id in the dictionary is then guaranteed
                // to find the string behind it.
                _ids[value] = id;
                return id;
            }
        }

        /// <summary>Returns the string behind an id, or null for <see cref="kNone"/>.</summary>
        public bool TryResolve(int id, out string value)
        {
            value = null;
            if (id == kNone) return false;

            lock (_appendLock)
            {
                if ((uint)id >= (uint)_count) return false;

                value = _symbols[id];
                return true;
            }
        }

        /// <summary>Returns the string behind an id, or an empty string when it is not known.</summary>
        public string Resolve(int id) => TryResolve(id, out var value) ? value : string.Empty;

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
                _ids.Clear();
                Array.Clear(_symbols, 0, _count);
                _count = 0;
            }
        }
    }
}
