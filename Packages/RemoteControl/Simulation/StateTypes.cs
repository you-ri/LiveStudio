// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;

namespace Lilium.RemoteControl.Frames
{
    /// <summary>
    /// One type the state lane can carry: the name a recording calls it by, and how to make a block
    /// for it in a set.
    ///
    /// A bridge is one of these (<see cref="StateBridge"/>) -- it knows its block because it moves
    /// state into it. A struct a producer publishes by hand is the other kind, with a block and no
    /// owner to move it from.
    /// </summary>
    public abstract class StateType
    {
        /// <summary>The name a recording calls this type's block by.</summary>
        public abstract string typeName { get; }

        /// <summary>Makes sure <paramref name="state"/> has this type's block, and hands it back.</summary>
        public abstract StateBlock EnsureBlock(StateBlockSet state);
    }

    /// <summary>A struct published by hand, with no owner behind it.</summary>
    internal sealed class StructStateType<T> : StateType where T : unmanaged
    {
        private readonly string _typeName;

        public StructStateType(string typeName) => _typeName = typeName;

        public override string typeName => _typeName;

        public override StateBlock EnsureBlock(StateBlockSet state) => state.GetOrCreate<T>(_typeName);
    }

    /// <summary>
    /// Everything the state lane knows about its types, by the name a recording calls them: what
    /// each holds (its <see cref="StateSchema"/>), how to make its block, and -- for a type with an
    /// owner -- the bridge that moves its state in and out.
    ///
    /// One table rather than three. There used to be a registry of block factories, one of schemas
    /// and one of bridges, each keyed its own way and each with its own reset rules, and a type had
    /// to reach all three to be recorded, described and replayed. A generated type now arrives with
    /// one call carrying all of it.
    ///
    /// Filled at load -- by the generator's module initializers, by the asset declarations, and by
    /// producers that publish a struct by hand -- so a machine that only ever replays still knows
    /// every type it may meet in a recording.
    /// </summary>
    public static class StateTypes
    {
        private sealed class Entry
        {
            public StateType type;
            public StateSchema schema;
        }

        private static readonly Dictionary<string, Entry> _byName = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private static readonly Dictionary<Type, StateBridge> _bridges = new Dictionary<Type, StateBridge>();
        private static readonly List<StateBridge> _bridgeOrder = new List<StateBridge>();
        private static readonly List<string> _knownNames = new List<string>();

        /// <summary>Bridges in registration order.</summary>
        public static IReadOnlyList<StateBridge> bridges => _bridgeOrder;

        /// <summary>Names of the types a block can be made for. For diagnostics.</summary>
        public static IReadOnlyList<string> knownTypeNames
        {
            get
            {
                _knownNames.Clear();
                foreach (var pair in _byName)
                {
                    if (pair.Value.type != null) _knownNames.Add(pair.Key);
                }

                return _knownNames;
            }
        }

        /// <summary>
        /// Registers a generated type: its bridge, and what its block holds. The one call the
        /// generator's module initializer makes per type.
        ///
        /// <paramref name="specs"/> are the members that reached the block, in declaration order --
        /// which is also the order the mask a replay carries speaks in, and the set of names the
        /// bridge answers for.
        /// </summary>
        public static void Register<TOwner, TBlock>(
            StateCapture<TOwner, TBlock> capture, StateApply<TOwner, TBlock> apply,
            StateSchemaMemberSpec[] specs)
            where TOwner : class
            where TBlock : unmanaged
        {
            var names = new string[specs?.Length ?? 0];
            for (int i = 0; i < names.Length; i++) names[i] = specs[i].name;

            var bridge = new StateBridge<TOwner, TBlock>(capture, apply, names);
            Register(bridge);
            DeclareSchema(bridge.typeName, StateSchemaBuilder.For<TBlock>(specs));
        }

        /// <summary>
        /// Registers a bridge written by hand, for a type the generator cannot reach. Carries no
        /// description, so a recording made from a different build is read on width alone.
        /// </summary>
        public static void Register<TOwner, TBlock>(
            StateCapture<TOwner, TBlock> capture, StateApply<TOwner, TBlock> apply,
            params string[] memberNames)
            where TOwner : class
            where TBlock : unmanaged
        {
            Register(new StateBridge<TOwner, TBlock>(capture, apply, memberNames));
        }

        /// <summary>Registers a bridge already built. Re-registering the same owner type replaces it.</summary>
        public static void Register(StateBridge bridge)
        {
            if (bridge == null) throw new ArgumentNullException(nameof(bridge));

            if (_bridges.TryGetValue(bridge.ownerType, out var existing))
            {
                _bridgeOrder[_bridgeOrder.IndexOf(existing)] = bridge;
            }
            else
            {
                _bridgeOrder.Add(bridge);
            }

            _bridges[bridge.ownerType] = bridge;
            _EntryFor(bridge.typeName).type = bridge;
        }

        /// <summary>
        /// Announces a struct a producer publishes by hand, so a machine that only ever replays can
        /// still make its block. Call at load rather than on first use. Idempotent, and never
        /// displaces a bridge already registered under the name.
        /// </summary>
        /// <param name="typeName">
        /// The name a recording calls the block by. Null for the struct's own.
        /// </param>
        public static void RegisterStruct<T>(string typeName = null) where T : unmanaged
        {
            var name = string.IsNullOrEmpty(typeName) ? typeof(T).FullName : typeName;
            if (name == null) return;

            var entry = _EntryFor(name);
            if (entry.type == null) entry.type = new StructStateType<T>(name);
        }

        /// <summary>
        /// Takes a type's bridge off the lane, for a declaration that stopped declaring: it must stop
        /// being read every frame for members it no longer has.
        /// </summary>
        public static void Unregister(Type ownerType)
        {
            if (ownerType == null || !_bridges.TryGetValue(ownerType, out var existing)) return;

            _bridges.Remove(ownerType);
            _bridgeOrder.Remove(existing);

            if (_byName.TryGetValue(existing.typeName, out var entry) && ReferenceEquals(entry.type, existing))
            {
                entry.type = null;
            }
        }

        /// <summary>
        /// The bridge for a type, or null.
        ///
        /// Exact type only. A derived type gets its own bridge from the generator, carrying the
        /// members it added; falling back to the base one would silently drop them.
        /// </summary>
        public static StateBridge FindBridge(Type ownerType)
            => ownerType != null && _bridges.TryGetValue(ownerType, out var bridge) ? bridge : null;

        /// <summary>
        /// Describes a type's block. Re-declaring replaces, for a declaration that moved -- and for a
        /// test standing in for a build that laid a type out differently.
        /// </summary>
        public static void DeclareSchema(string typeName, StateSchema schema)
        {
            if (string.IsNullOrEmpty(typeName) || schema == null) return;

            _EntryFor(typeName).schema = schema;
        }

        /// <summary>The description this build has for a type, or null when none was offered.</summary>
        public static StateSchema FindSchema(string typeName)
            => !string.IsNullOrEmpty(typeName) && _byName.TryGetValue(typeName, out var entry) ? entry.schema : null;

        /// <summary>Takes a type's description away, leaving the rest of what is known about it.</summary>
        public static void RemoveSchema(string typeName)
        {
            if (!string.IsNullOrEmpty(typeName) && _byName.TryGetValue(typeName, out var entry)) entry.schema = null;
        }

        /// <summary>
        /// Makes sure <paramref name="state"/> has a block for the named type, creating it when the
        /// type is known here. Null when nothing has announced that name.
        /// </summary>
        public static StateBlock EnsureBlock(StateBlockSet state, string typeName)
        {
            if (state == null || string.IsNullOrEmpty(typeName)) return null;

            var existing = state.FindByTypeName(typeName);
            if (existing != null) return existing;

            return _byName.TryGetValue(typeName, out var entry) && entry.type != null
                ? entry.type.EnsureBlock(state)
                : null;
        }

        /// <summary>
        /// Forgets how to make a type's block, for a test standing in for a machine that only ever
        /// replays and has never heard of it. The test puts it back afterwards.
        ///
        /// ⚠ There is deliberately no "forget everything". What the generator registers is offered
        /// once, at load, and nothing offers it again -- emptying the table would take every type in
        /// the build off the lane for the rest of the process, with the symptom turning up wherever
        /// the next recording is read.
        /// </summary>
        internal static StateType Forget(string typeName)
        {
            if (string.IsNullOrEmpty(typeName) || !_byName.TryGetValue(typeName, out var entry)) return null;

            var forgotten = entry.type;
            entry.type = null;
            return forgotten;
        }

        /// <summary>Puts back what <see cref="Forget"/> took.</summary>
        internal static void Restore(StateType type)
        {
            if (type == null) return;

            _EntryFor(type.typeName).type = type;
        }

        private static Entry _EntryFor(string typeName)
        {
            if (_byName.TryGetValue(typeName, out var entry)) return entry;

            entry = new Entry();
            _byName.Add(typeName, entry);
            return entry;
        }
    }
}
