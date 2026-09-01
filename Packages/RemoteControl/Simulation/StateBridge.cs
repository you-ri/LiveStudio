// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;

namespace Lilium.RemoteControl.Frames
{
    /// <summary>Copies an object's state-lane members into its block.</summary>
    public delegate void StateCapture<in TOwner, TBlock>(TOwner source, ref TBlock block)
        where TBlock : unmanaged;

    /// <summary>Writes a block back onto the object it came from.</summary>
    public delegate void StateApply<in TOwner, TBlock>(in TBlock block, TOwner target)
        where TBlock : unmanaged;

    /// <summary>
    /// Moves one type's exposed state between the object and a state block.
    ///
    /// Generated rather than reflected. Reading a handful of members off every exposed object sixty
    /// times a second is the one thing reflection is worst at, and it is exactly what the state lane
    /// asks for; the generator turns it into field assignments.
    ///
    /// The non-generic face exists so a driver can walk objects of mixed types without knowing any
    /// of them.
    /// </summary>
    public abstract class StateBridge
    {
        /// <summary>The exposed type this moves state for.</summary>
        public abstract Type ownerType { get; }

        /// <summary>The block type its state lives in.</summary>
        public abstract Type blockType { get; }

        /// <summary>Creates this type's block in a set, so a replay has somewhere to put it.</summary>
        public abstract StateBlock EnsureBlock(StateBlockSet state);

        /// <summary>
        /// Reads the object's state into its element of the set. False when nothing was written.
        ///
        /// Answered rather than assumed, because a caller counting objects it "carried" is how a
        /// recording that carries nothing comes to look like a recording that found nothing to say.
        /// </summary>
        public abstract bool Capture(object owner, int ownerId, StateBlockSet state,
            FrameSource source, long time);

        /// <summary>Writes the element back onto the object. False when the set has nothing for it.</summary>
        public abstract bool Apply(object owner, int ownerId, StateBlockSet state);
    }

    /// <inheritdoc/>
    public sealed class StateBridge<TOwner, TBlock> : StateBridge
        where TOwner : class
        where TBlock : unmanaged
    {
        private readonly StateCapture<TOwner, TBlock> _capture;
        private readonly StateApply<TOwner, TBlock> _apply;

        public StateBridge(StateCapture<TOwner, TBlock> capture, StateApply<TOwner, TBlock> apply)
        {
            _capture = capture ?? throw new ArgumentNullException(nameof(capture));
            _apply = apply ?? throw new ArgumentNullException(nameof(apply));
        }

        public override Type ownerType => typeof(TOwner);

        public override Type blockType => typeof(TBlock);

        public override StateBlock EnsureBlock(StateBlockSet state) => state.GetOrCreate<TBlock>();

        public override bool Capture(object owner, int ownerId, StateBlockSet state,
            FrameSource source, long time)
        {
            if (!(owner is TOwner typed) || state == null) return false;

            ref var element = ref state.GetOrCreate<TBlock>().GetOrCreate(ownerId);
            element.source = source;
            element.time = time;

            // Written straight into the block's storage. Capturing into a local and assigning it
            // back would copy the whole struct twice for every object, every frame.
            _capture(typed, ref element.value);
            return true;
        }

        public override bool Apply(object owner, int ownerId, StateBlockSet state)
        {
            if (!(owner is TOwner typed) || state == null) return false;

            var block = state.Find<TBlock>();
            if (block == null) return false;

            var index = block.IndexOf(ownerId);
            if (index < 0) return false;

            _apply(in block[index].value, typed);
            return true;
        }
    }

    /// <summary>
    /// Every generated bridge, by the type it moves state for.
    ///
    /// Filled by module initializers the generator emits, so a bridge is there before anything runs
    /// rather than being discovered on first use.
    /// </summary>
    public static class StateBridgeRegistry
    {
        private static readonly Dictionary<Type, StateBridge> _byOwner = new Dictionary<Type, StateBridge>();
        private static readonly List<StateBridge> _ordered = new List<StateBridge>();

        /// <summary>Bridges in registration order.</summary>
        public static IReadOnlyList<StateBridge> all => _ordered;

        /// <summary>Registers a generated bridge. Re-registering the same owner type replaces it.</summary>
        public static void Register<TOwner, TBlock>(
            StateCapture<TOwner, TBlock> capture, StateApply<TOwner, TBlock> apply)
            where TOwner : class
            where TBlock : unmanaged
        {
            Register(new StateBridge<TOwner, TBlock>(capture, apply));
        }

        /// <summary>Registers a bridge built by hand, for a type the generator cannot reach.</summary>
        public static void Register(StateBridge bridge)
        {
            if (bridge == null) throw new ArgumentNullException(nameof(bridge));

            if (_byOwner.TryGetValue(bridge.ownerType, out var existing))
            {
                _ordered[_ordered.IndexOf(existing)] = bridge;
                _byOwner[bridge.ownerType] = bridge;
                return;
            }

            _byOwner.Add(bridge.ownerType, bridge);
            _ordered.Add(bridge);
        }

        /// <summary>
        /// Takes a type off the lane.
        ///
        /// For a declaration that can change while running: an asset that stops declaring state has
        /// to stop being read every frame for members it no longer has.
        /// </summary>
        public static void Unregister(Type ownerType)
        {
            if (ownerType == null || !_byOwner.TryGetValue(ownerType, out var existing)) return;

            _byOwner.Remove(ownerType);
            _ordered.Remove(existing);
        }

        /// <summary>
        /// The bridge for a type, or null.
        ///
        /// Exact type only. A derived type gets its own bridge from the generator, carrying the
        /// members it added; falling back to the base one would silently drop them.
        /// </summary>
        public static StateBridge Find(Type ownerType)
            => ownerType != null && _byOwner.TryGetValue(ownerType, out var bridge) ? bridge : null;

        /// <summary>Drops every registration. For tests.</summary>
        internal static void Clear()
        {
            _byOwner.Clear();
            _ordered.Clear();
        }
    }
}
