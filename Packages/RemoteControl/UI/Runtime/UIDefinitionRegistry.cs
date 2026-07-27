// Copyright (c) You-Ri, 2026

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Lilium.RemoteControl.UI
{
    /// <summary>
    /// Process-wide registry of extra <see cref="UIDefinition"/>s that active Remote Control
    /// side menus merge on top of each host's primary definition. Lets a feature add or remove
    /// its pages at runtime (e.g. embedded capture) without owning a server or a second
    /// <c>/ui/sidemenu</c> route.
    ///
    /// A live <see cref="UIHandler"/> registers the added definition's selectors and records a UI
    /// change in <see cref="ExposedChangeLog"/> so polling clients refetch the side menu.
    /// </summary>
    public static class UIDefinitionRegistry
    {
        static readonly List<UIDefinition> _definitions = new List<UIDefinition>();

        // Immutable snapshot read by HTTP worker threads (UIHandler.HandleGetSideMenu); rebuilt on
        // every change so a single reference read is a consistent view without locking.
        static volatile UIDefinition[] _snapshot = Array.Empty<UIDefinition>();

        /// <summary>Current additional definitions. Safe to read from any thread.</summary>
        public static UIDefinition[] definitions => _snapshot;

        /// <summary>
        /// Raised on the caller's thread (expected: main thread) after a definition is added
        /// (added=true) or removed (added=false), so live UIHandlers can (un)register its
        /// selectors and notify clients.
        /// </summary>
        public static event Action<UIDefinition, bool> onChanged;

        /// <summary>Add a definition. No-op if null or already present.</summary>
        public static void Add(UIDefinition definition)
        {
            if (definition == null || _definitions.Contains(definition)) return;
            _definitions.Add(definition);
            _snapshot = _definitions.ToArray();
            onChanged?.Invoke(definition, true);
        }

        /// <summary>Remove a definition. No-op if null or not present.</summary>
        public static void Remove(UIDefinition definition)
        {
            if (definition == null) return;
            if (!_definitions.Remove(definition)) return;
            _snapshot = _definitions.ToArray();
            onChanged?.Invoke(definition, false);
        }

        // Reset statics so a stale definition/subscription never survives when Domain Reload is off.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void _ResetStatics()
        {
            _definitions.Clear();
            _snapshot = Array.Empty<UIDefinition>();
            onChanged = null;
        }
    }
}
