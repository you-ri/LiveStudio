// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;

namespace Lilium.RemoteControl.Frames
{
    /// <summary>
    /// The element types the state lane can hold, by the name a recording calls them.
    ///
    /// A recording names its blocks by type name, and a player has to turn that name back into a
    /// block. It cannot do that from the name alone -- a block is generic over an unmanaged struct,
    /// and the name is a string -- so the types announce themselves here and the player asks.
    ///
    /// Without this, a recording plays back into an application that has the producer but has not
    /// happened to publish that type yet, and the state goes nowhere: the take carries the world and
    /// the replay shows none of it, with only a warning to say so. That is exactly what a
    /// replay-only machine looks like, since nothing there ever publishes live.
    ///
    /// Types register themselves the first time a block is made for them, so anything that has ever
    /// been on the lane in this process is known. A producer that has to be readable before it has
    /// published anything registers up front instead.
    /// </summary>
    public static class StateTypeRegistry
    {
        private static readonly Dictionary<string, Func<StateBlockSet, StateBlock>> _factories =
            new Dictionary<string, Func<StateBlockSet, StateBlock>>(StringComparer.Ordinal);

        /// <summary>
        /// Bumped whenever the table is emptied, so the per-type guards that skip re-announcing know
        /// their announcement is gone.
        ///
        /// The guard has to be a static field per type to stay off the per-frame path, and a static
        /// field outlives any amount of clearing -- which left a cleared registry unable to be
        /// refilled by the very calls that filled it the first time. A version compare costs the
        /// same as the bool it replaces.
        /// </summary>
        internal static int generation { get; private set; }

        /// <summary>Type names that can be given a block. For diagnostics.</summary>
        public static IReadOnlyCollection<string> knownTypeNames => _factories.Keys;

        /// <summary>
        /// Announces that <typeparamref name="T"/> can appear on the state lane. Idempotent.
        ///
        /// Call this from a producer that publishes a type by hand, at load rather than on first
        /// use, so a machine that only ever replays can still receive it.
        /// </summary>
        public static void Register<T>() where T : unmanaged
        {
            var name = typeof(T).FullName;
            if (name == null || _factories.ContainsKey(name)) return;

            _factories[name] = set => set.GetOrCreate<T>();
        }

        /// <summary>
        /// Announces a type whose state block is described by a declaration rather than by a struct.
        ///
        /// The width is part of the announcement because it is not discoverable from the name: a
        /// declared block is as wide as its declaration says, and a player has to be able to build
        /// one before it has seen a single frame.
        /// </summary>
        public static void RegisterDeclared(Type ownerType, int payloadSize)
        {
            var name = ownerType?.FullName;
            if (name == null) return;

            // Overwritten rather than kept: an asset can be edited and re-registered while the
            // application runs, and the last declaration is the one a block should be built from.
            _factories[name] = set => set.GetOrCreateDeclared(ownerType, payloadSize);
        }

        /// <summary>
        /// Makes sure <paramref name="set"/> has a block for the named type, creating it if the type
        /// is known here. Null when nothing has announced that name.
        /// </summary>
        public static StateBlock EnsureBlock(StateBlockSet set, string fullName)
        {
            if (set == null || string.IsNullOrEmpty(fullName)) return null;

            var existing = set.FindByTypeName(fullName);
            if (existing != null) return existing;

            return _factories.TryGetValue(fullName, out var factory) ? factory(set) : null;
        }

        /// <summary>
        /// Forgets every announcement. For tests that need a playing side which has never heard of a
        /// type -- in one process, recording a type is itself an announcement, so there is no other
        /// way to stand in for a machine that only ever replays.
        /// </summary>
        internal static void Clear()
        {
            // The generation moves first so that a type announcing itself again during the same
            // frame is not filed under the generation that was just thrown away.
            generation++;
            _factories.Clear();
        }

        // No reset at startup, deliberately. Producers announce their types from the same
        // initialization phase, and nothing orders the two -- a clear that happens to run second
        // wipes the announcements and the whole lane goes quiet with no way to tell why. Which is
        // what happened: the registry was empty in play mode and every recorded pose was dropped.
        //
        // There is nothing to clear anyway. A domain reload throws these statics away wholesale, and
        // when it is disabled the types are still loaded, so an announcement from an earlier session
        // is still true. Re-announcing is idempotent.
    }
}
