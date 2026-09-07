// Copyright (c) You-Ri, 2026

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Lilium.RemoteControl
{
    /// <summary>
    /// The prefabs the remote can stand up, and the page each one is offered on.
    ///
    /// Filled from the <see cref="LiveClassAsset.prefabs"/> list of every applied live class asset,
    /// so what can be instantiated is declared in the same place as what is exposed -- and an asset
    /// that arrives with a bundle brings its prefabs with it and takes them away again on unload.
    /// The UI reads this rather than holding its own list: a page's "+" is a view of the catalogue,
    /// not a second place to author one.
    ///
    /// Registering an entry also puts its prefab in the <see cref="PrefabRegistry"/>, which is what
    /// lets a saved <c>@prefab</c> entry be rebuilt after a restart. That pairing is the reason
    /// registration lives here instead of at the page: a prefab has to be resolvable whether or not
    /// any page ever lists it.
    /// </summary>
    public static class LivePrefabCatalog
    {
        /// <summary>One offered prefab: the maker, and the category page that also lists it.</summary>
        public readonly struct Entry
        {
            /// <summary>
            /// Category page whose "+" offers this prefab, or empty for none. The scene page offers
            /// every entry regardless.
            /// </summary>
            public readonly string category;

            public readonly ILiveObjectFactory factory;

            public Entry(string category, ILiveObjectFactory factory)
            {
                this.category = category;
                this.factory = factory;
            }
        }

        // Owners in registration order, so an index into the snapshot means the same thing for as
        // long as nothing is applied or dropped. The dictionary holds what each owner contributed.
        private static readonly List<LiveClassAsset> _owners = new List<LiveClassAsset>();

        private static readonly Dictionary<LiveClassAsset, List<Entry>> _byOwner
            = new Dictionary<LiveClassAsset, List<Entry>>();

        // Immutable snapshot read by HTTP worker threads (the "+" list is fetched off the main
        // thread); rebuilt on every change so a single reference read is a consistent view.
        private static volatile Entry[] _snapshot = Array.Empty<Entry>();

        /// <summary>Every registered entry, in registration order. Safe to read from any thread.</summary>
        public static Entry[] entries => _snapshot;

        /// <summary>Raised after the catalogue changed, on the caller's thread (expected: main).</summary>
        public static event Action onChanged;

        /// <summary>
        /// Registers (or re-registers) the prefabs an asset declares, and puts each one in the
        /// <see cref="PrefabRegistry"/>. Idempotent: applying the same asset again replaces what it
        /// contributed, keeping its position so indices stay put.
        /// </summary>
        public static void Register(LiveClassAsset asset)
        {
            if (asset == null) return;

            List<Entry> built = null;
            var declarations = asset.prefabs;
            if (declarations != null)
            {
                for (int i = 0; i < declarations.Count; i++)
                {
                    var declaration = declarations[i];
                    var factory = declaration?.factory;
                    if (factory == null) continue;

                    factory.RegisterPrefabs();
                    (built ??= new List<Entry>()).Add(new Entry(declaration.category, factory));
                }
            }

            var had = _byOwner.ContainsKey(asset);
            if (built == null)
            {
                // Nothing to offer. Drop a previous contribution rather than leaving it standing:
                // an asset whose list was emptied should stop offering what it used to.
                if (!had) return;
                _byOwner.Remove(asset);
                _owners.Remove(asset);
            }
            else
            {
                if (!had) _owners.Add(asset);
                _byOwner[asset] = built;
            }

            _RebuildSnapshot();
        }

        /// <summary>
        /// Re-reads an asset that is already applied, and does nothing for one that is not.
        ///
        /// For an edit to a live list (the inspector, while playing): applying the asset is the
        /// decision of whoever brought it in, and an asset merely being looked at must not start
        /// offering its prefabs.
        /// </summary>
        public static void Refresh(LiveClassAsset asset)
        {
            if (asset == null || !_byOwner.ContainsKey(asset)) return;
            Register(asset);
        }

        /// <summary>
        /// Drops what an asset contributed. The prefabs stay in the <see cref="PrefabRegistry"/>:
        /// an object made from one may still be in the scene, and restoring it has to keep working
        /// until it is gone.
        /// </summary>
        public static void Unregister(LiveClassAsset asset)
        {
            if (asset == null) return;
            if (!_byOwner.Remove(asset)) return;

            _owners.Remove(asset);
            _RebuildSnapshot();
        }

        /// <summary>
        /// The makers a page offers: every entry when <paramref name="category"/> is empty (the
        /// scene page, which offers all of them), otherwise the entries declaring that category.
        /// </summary>
        public static List<ILiveObjectFactory> FactoriesFor(string category)
        {
            var snapshot = _snapshot;
            var result = new List<ILiveObjectFactory>(snapshot.Length);
            var all = string.IsNullOrEmpty(category);

            for (int i = 0; i < snapshot.Length; i++)
            {
                if (!all && snapshot[i].category != category) continue;
                result.Add(snapshot[i].factory);
            }
            return result;
        }

        /// <summary>Forgets every entry. For tests, and for tearing a session down.</summary>
        public static void Clear()
        {
            if (_owners.Count == 0 && _snapshot.Length == 0) return;

            _owners.Clear();
            _byOwner.Clear();
            _snapshot = Array.Empty<Entry>();
            onChanged?.Invoke();
        }

        // Reset statics at runtime startup so disabling Domain Reload does not leak entries from
        // the previous play session.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void _ClearStatics()
        {
            _owners.Clear();
            _byOwner.Clear();
            _snapshot = Array.Empty<Entry>();
        }

        private static void _RebuildSnapshot()
        {
            var total = 0;
            for (int i = 0; i < _owners.Count; i++)
            {
                if (_byOwner.TryGetValue(_owners[i], out var entries)) total += entries.Count;
            }

            var rebuilt = total == 0 ? Array.Empty<Entry>() : new Entry[total];
            var at = 0;
            for (int i = 0; i < _owners.Count; i++)
            {
                if (!_byOwner.TryGetValue(_owners[i], out var entries)) continue;
                for (int j = 0; j < entries.Count; j++) rebuilt[at++] = entries[j];
            }

            _snapshot = rebuilt;
            onChanged?.Invoke();
        }
    }
}
