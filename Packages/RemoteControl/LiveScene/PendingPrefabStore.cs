// Copyright (c) You-Ri, 2026

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using UnityEngine;
using Newtonsoft.Json.Linq;

namespace Lilium.RemoteControl.LiveScene
{
    /// <summary>
    /// Holds live-scene <c>@prefab</c> root entries whose prefab could not be instantiated during the
    /// restore (Pass 1), because the prefab is resolved asynchronously from a source that is not registered
    /// yet — e.g. an external <c>*.prop.lsb</c> prop whose owning asset is discovered by the project crawl
    /// AFTER the restore has finished. It is the counterpart of <see cref="LiveScenePendingStore"/> (which
    /// defers binding PROPERTIES onto an externally-created instance); this defers the INSTANTIATION itself.
    ///
    /// Queued entries are drained by <see cref="DrainAsync"/> once a registered provider can resolve their
    /// prefab key (the asset manager registers it after its crawl). An entry that still cannot resolve stays
    /// queued and is re-emitted verbatim on the next save, so a load→save cycle never drops it — which also
    /// fixes the pre-existing loss of any unresolved <c>@prefab</c> entry (those carry <c>@id</c>, not
    /// <c>@source</c>, so the property-store re-emit never picked them up). Main-thread only.
    /// </summary>
    public static class PendingPrefabStore
    {
        private struct Entry
        {
            public string id;         // the instance's @id
            public string prefabKey;  // the @prefab key to resolve
            public JObject json;      // the entry as read from the file, applied / re-emitted verbatim
        }

        private static readonly List<Entry> _entries = new List<Entry>();

        // Resolves a prefab key to its root prefab (the caller instantiates copies), or null when the key's
        // asset is not available yet. Registered by the LiveStudio asset manager; a single provider by design
        // (unlike a resolver list, there is exactly one owner of instanceable-prop resolution).
        private static Func<string, Task<GameObject>> _provider;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void _Reset()
        {
            _entries.Clear();
            _provider = null;
        }

        /// <summary>Drops every queued entry. Called at the start of each live-scene load.</summary>
        public static void Clear() => _entries.Clear();

        /// <summary>Registers the async prefab resolver used by <see cref="DrainAsync"/>.</summary>
        public static void SetProvider(Func<string, Task<GameObject>> provider) => _provider = provider;

        /// <summary>Queues an unresolved <c>@prefab</c> root entry. Deduplicates by <c>@id</c>.</summary>
        internal static void Add(string id, string prefabKey, JObject entry)
        {
            if (string.IsNullOrEmpty(id) || entry == null) return;
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].id == id)
                {
                    _entries[i] = new Entry { id = id, prefabKey = prefabKey, json = entry };
                    return;
                }
            }
            _entries.Add(new Entry { id = id, prefabKey = prefabKey, json = entry });
        }

        /// <summary>True when an entry with this <c>@id</c> is queued (so the restore passes skip it).</summary>
        internal static bool Contains(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            for (int i = 0; i < _entries.Count; i++) if (_entries[i].id == id) return true;
            return false;
        }

        /// <summary>The still-unresolved entries for the serializer to re-emit on save (verbatim).</summary>
        internal static IEnumerable<JObject> UnconsumedEntries
        {
            get { for (int i = 0; i < _entries.Count; i++) yield return _entries[i].json; }
        }

        /// <summary>
        /// Instantiates every queued entry whose prefab the provider can now resolve, registering it into the
        /// live scene under its saved id (and applying its deferred child overrides). Entries that still
        /// cannot resolve are kept. Safe to call repeatedly (e.g. after each asset discovery) and re-entrancy
        /// safe (the queue may be mutated by a nested restore while awaiting the provider).
        /// </summary>
        public static async Task DrainAsync()
        {
            if (_provider == null || _entries.Count == 0) return;

            var snapshot = new List<Entry>(_entries);
            for (int i = 0; i < snapshot.Count; i++)
            {
                var e = snapshot[i];
                if (!Contains(e.id)) continue; // already drained by a concurrent pass / re-load

                GameObject prefab;
                try
                {
                    prefab = await _provider(e.prefabKey);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[RemoteControl] PendingPrefabStore: provider failed for '{e.prefabKey}': {ex.Message}");
                    continue;
                }
                if (prefab == null) continue; // asset still unavailable; keep queued for a later drain / save

                if (!Contains(e.id)) continue; // a concurrent load cleared the queue while awaiting
                if (LiveSceneSerializer.TryInstantiateDeferredPrefab(e.id, e.prefabKey, e.json, prefab))
                    _Remove(e.id);
            }
        }

        private static void _Remove(string id)
        {
            for (int i = _entries.Count - 1; i >= 0; i--)
                if (_entries[i].id == id) _entries.RemoveAt(i);
        }
    }
}
