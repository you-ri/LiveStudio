// Copyright (c) You-Ri, 2026

using System.Collections.Generic;

using UnityEngine;
using Newtonsoft.Json.Linq;

using Lilium.RemoteControl;

namespace Lilium.RemoteControl.LiveScene
{
    /// <summary>
    /// Holds live-scene object entries that could not be applied at load time because their target does
    /// not exist yet, so they can be bound later when it appears — the "deferred bind" mechanism.
    ///
    /// A prop / avatar wrapper and its components are instantiated asynchronously, AFTER
    /// <see cref="LiveSceneSerializer.LiveSceneFromJson"/> has finished. Their saved state therefore lives
    /// in the file as top-level entries whose <c>@source</c> cannot be resolved during the restore passes.
    /// Rather than warn and drop them (the old behavior), the passes queue such entries here; the owning
    /// asset calls <see cref="ApplyFor"/> once it has loaded, which resolves and applies each matching
    /// entry. This makes the top-level diff the single source of truth for a loaded object's persisted
    /// state, so the per-asset state snapshot no longer has to be written into the live scene.
    ///
    /// Entries that are never bound (their asset never loads this session) are re-emitted verbatim on the
    /// next save (see <see cref="UnconsumedEntries"/>), so a load→save cycle does not lose them.
    ///
    /// Main-thread only; the live-scene restore and asset loading both run on the main thread.
    /// </summary>
    public static class LiveScenePendingStore
    {
        private struct Entry
        {
            public string sourceKey; // full @source (rootId + optional path)
            public string rootId;    // leading id segment, the key ApplyFor matches on
            public JObject json;      // the entry as read from the file, applied / re-emitted verbatim
        }

        private static readonly List<Entry> _entries = new List<Entry>();

        // Reset at runtime startup so a previous play session's queue does not survive when domain
        // reload is disabled.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void _Reset() => _entries.Clear();

        /// <summary>Drops every queued entry. Called at the start of each live-scene load.</summary>
        public static void Clear() => _entries.Clear();

        /// <summary>
        /// Queues an unresolved entry for a later <see cref="ApplyFor"/>. Deduplicates by
        /// <paramref name="sourceKey"/> (a re-queued key replaces the earlier entry).
        /// </summary>
        internal static void Add(string sourceKey, string rootId, JObject entry)
        {
            if (string.IsNullOrEmpty(sourceKey) || entry == null) return;
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].sourceKey == sourceKey)
                {
                    _entries[i] = new Entry { sourceKey = sourceKey, rootId = rootId, json = entry };
                    return;
                }
            }
            _entries.Add(new Entry { sourceKey = sourceKey, rootId = rootId, json = entry });
        }

        /// <summary>
        /// Applies every queued entry whose leading id equals <paramref name="rootId"/>, using
        /// <paramref name="resolver"/> to resolve targets. Successfully applied entries are removed;
        /// entries that still cannot resolve are kept (they round-trip on the next save). Returns the
        /// number applied.
        ///
        /// Callers (a loaded prop / avatar) MUST invoke this AFTER capturing the load-time defaults and
        /// applying any preset/carrier state, so the deferred top-level diff lands last and does not
        /// pollute the delta baseline used by "save as preset".
        /// </summary>
        public static int ApplyFor(string rootId, IExposedObjectResolver resolver)
        {
            if (string.IsNullOrEmpty(rootId) || resolver == null) return 0;

            int applied = 0;
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                if (_entries[i].rootId != rootId) continue;
                if (LiveSceneSerializer.ApplyPendingEntry(_entries[i].sourceKey, _entries[i].json, resolver))
                {
                    _entries.RemoveAt(i);
                    applied++;
                }
            }
            return applied;
        }

        /// <summary>
        /// The still-unbound entries, in queue order, for the serializer to re-emit on save so a
        /// load→save cycle does not drop the state of assets that never loaded this session.
        /// </summary>
        internal static IEnumerable<JObject> UnconsumedEntries
        {
            get
            {
                for (int i = 0; i < _entries.Count; i++) yield return _entries[i].json;
            }
        }

        /// <summary>
        /// A queued entry whose whole target object is absent this session — surfaced to the remote app
        /// as a deletable "missing" live-scene item. Only bare-id root entries qualify (their
        /// <c>sourceKey</c> carries no path): they mean an entire top-level object the saved scene
        /// referenced does not exist here. Path-carrying child entries (an async asset's component
        /// overrides) are excluded — those bind once their owning prop / avatar loads.
        /// </summary>
        public readonly struct OrphanEntry
        {
            public readonly string sourceKey;
            public readonly string type;
            public OrphanEntry(string sourceKey, string type)
            {
                this.sourceKey = sourceKey;
                this.type = type;
            }
        }

        /// <summary>
        /// The current bare-id root orphans (see <see cref="OrphanEntry"/>). Recomputed from the live
        /// queue on each call, so an entry that has since bound (its asset loaded, drained by
        /// <see cref="ApplyFor"/>) or been removed no longer appears.
        /// </summary>
        public static List<OrphanEntry> GetOrphans()
        {
            var result = new List<OrphanEntry>();
            for (int i = 0; i < _entries.Count; i++)
            {
                var key = _entries[i].sourceKey;
                if (string.IsNullOrEmpty(key)) continue;
                // A path ('.' / '[') marks a child override of an async asset, not a missing root.
                if (key.IndexOf('.') >= 0 || key.IndexOf('[') >= 0) continue;
                var type = _entries[i].json?["@type"]?.Value<string>();
                result.Add(new OrphanEntry(key, type));
            }
            return result;
        }

        /// <summary>
        /// Drops every queued entry rooted at <paramref name="rootId"/> (a missing object's id): its
        /// root override plus any child/component overrides of the same absent object. The next scene
        /// save then no longer re-emits them, so an explicit delete + save resolves the orphan. Returns
        /// true when at least one entry was removed.
        /// </summary>
        public static bool RemoveOrphan(string rootId)
        {
            if (string.IsNullOrEmpty(rootId)) return false;
            bool removed = false;
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                if (_entries[i].rootId == rootId)
                {
                    _entries.RemoveAt(i);
                    removed = true;
                }
            }
            return removed;
        }
    }
}
