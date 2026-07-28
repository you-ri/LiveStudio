// Copyright (c) You-Ri, 2026
using System.Collections.Generic;
using UnityEngine;

namespace Lilium.RemoteControl
{
    /// <summary>
    /// Records which exposed objects changed, so a remote app can find out what to refetch with one
    /// small GET instead of being pushed every changed value.
    ///
    /// The log keeps the latest revision per object id rather than a queue of events. That makes the
    /// memory bounded by the number of distinct objects (no ring buffer, no eviction) and removes the
    /// "cursor fell off the end" case entirely: a client that has been away for any length of time
    /// still gets exactly the set of ids that changed since its revision.
    ///
    /// Only ids are recorded — never values. A client that does not hold an object simply ignores it,
    /// which is the whole point: nothing is sent to anyone who is not looking at it.
    /// </summary>
    public static class LiveChangeLog
    {
        /// <summary>Pseudo id recorded when the LiveClass / LiveEnum tables are rebuilt.</summary>
        public const string kTypesId = "@types";

        /// <summary>Pseudo id recorded when the UI definition registry changes (pages added/removed).</summary>
        public const string kUiId = "@ui";

        // Guards both fields. Writes come from the main thread (property setters), reads from HTTP
        // worker threads, so every access is inside the lock. Contention is negligible: a write is a
        // dictionary store and a read is a walk over a few hundred entries.
        private static readonly object _kLock = new object();
        private static readonly Dictionary<string, long> _revisionsById = new Dictionary<string, long>();
        private static long _revision;

        /// <summary>
        /// The current revision. A client passes the value it last saw back as <c>since</c>.
        /// </summary>
        public static long revision
        {
            get { lock (_kLock) return _revision; }
        }

        /// <summary>
        /// Marks <paramref name="objectId"/> as changed. Cheap enough to call from any property
        /// setter — it takes a lock and stores one dictionary entry, with no serialization at all.
        /// </summary>
        public static void Record(string objectId)
        {
            if (string.IsNullOrEmpty(objectId)) return;
            lock (_kLock)
            {
                _revision++;
                _revisionsById[objectId] = _revision;
            }
        }

        /// <summary>
        /// Collects the ids whose revision is newer than <paramref name="since"/> into
        /// <paramref name="buffer"/> (cleared first) and returns the current revision.
        /// A <paramref name="since"/> of 0 reports everything recorded so far, which is what a
        /// freshly connected client wants.
        /// </summary>
        public static long GetChangesSince(long since, List<string> buffer)
        {
            if (buffer == null) return revision;
            buffer.Clear();
            lock (_kLock)
            {
                foreach (var kv in _revisionsById)
                {
                    if (kv.Value > since) buffer.Add(kv.Key);
                }
                return _revision;
            }
        }

        /// <summary>
        /// Drops every recorded id and resets the revision. Runtime start only — a client holding a
        /// stale revision from a previous run would otherwise see no changes until the counter caught
        /// up with where it left off.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Clear()
        {
            lock (_kLock)
            {
                _revisionsById.Clear();
                _revision = 0;
            }
        }
    }
}
