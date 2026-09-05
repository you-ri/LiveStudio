// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;

namespace Lilium.RemoteControl.Frames
{
    /// <summary>Copies an object's state-lane members into its block.</summary>
    /// <param name="symbols">
    /// The table a member carried as an id belongs to (<see cref="LiveTextId"/>). The frame's own,
    /// so that reading a recorded id and interning a live one cannot end up asking different
    /// tables -- which is the whole reason the frame carries one.
    /// </param>
    public delegate void StateCapture<in TOwner, TBlock>(TOwner source, ref TBlock block,
        FrameSymbolTable symbols)
        where TBlock : unmanaged;

    /// <summary>Writes a block back onto the object it came from.</summary>
    /// <inheritdoc cref="StateCapture{TOwner,TBlock}" path="/param[@name='symbols']"/>
    public delegate void StateApply<in TOwner, TBlock>(in TBlock block, TOwner target,
        FrameSymbolTable symbols)
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
            FrameSource source, long time, FrameSymbolTable symbols);

        /// <summary>Writes the element back onto the object. False when the set has nothing for it.</summary>
        public abstract bool Apply(object owner, int ownerId, StateBlockSet state,
            FrameSymbolTable symbols);

        /// <summary>
        /// Whether this bridge actually moves the named member.
        ///
        /// Asked because declaring the state lane and being carried by it are two different things.
        /// A member can ask for the lane and not reach the block -- text with no width, a type that
        /// is not unmanaged -- and the generator says so at compile time, but nothing said so at
        /// runtime: the record path read the declaration, saw <see cref="FrameLane.State"/>, and
        /// left the member to a lane that was not carrying it.
        /// A member no lane carries is a hole in the recording that nothing reports, so the question
        /// is put to whatever is doing the carrying rather than to the declaration.
        ///
        /// The name is the member's own, as reflection spells it. A bridge that keys its members by
        /// the name they are exposed under answers for that spelling instead; callers try both.
        /// </summary>
        public abstract bool Carries(string memberName);
    }

    /// <inheritdoc/>
    public sealed class StateBridge<TOwner, TBlock> : StateBridge
        where TOwner : class
        where TBlock : unmanaged
    {
        private readonly StateCapture<TOwner, TBlock> _capture;
        private readonly StateApply<TOwner, TBlock> _apply;
        private readonly string[] _memberNames;

        public StateBridge(StateCapture<TOwner, TBlock> capture, StateApply<TOwner, TBlock> apply,
            string[] memberNames = null)
        {
            _capture = capture ?? throw new ArgumentNullException(nameof(capture));
            _apply = apply ?? throw new ArgumentNullException(nameof(apply));

            // Empty rather than null when a bridge does not say: the answer is then "carries
            // nothing", which leaves every member to the other lane. Wasteful where the block does
            // carry it -- the value ends up in both -- and that is the direction to be wrong in,
            // because the other one loses the member from the recording entirely.
            _memberNames = memberNames ?? Array.Empty<string>();
        }

        public override Type ownerType => typeof(TOwner);

        public override Type blockType => typeof(TBlock);

        public override StateBlock EnsureBlock(StateBlockSet state) => state.GetOrCreate<TBlock>();

        /// <inheritdoc/>
        public override bool Carries(string memberName)
        {
            for (int i = 0; i < _memberNames.Length; i++)
            {
                if (string.Equals(_memberNames[i], memberName, StringComparison.Ordinal)) return true;
            }

            return false;
        }

        public override bool Capture(object owner, int ownerId, StateBlockSet state,
            FrameSource source, long time, FrameSymbolTable symbols)
        {
            if (!(owner is TOwner typed) || state == null) return false;

            ref var element = ref state.GetOrCreate<TBlock>().GetOrCreate(ownerId);
            element.source = source;
            element.time = time;

            // Written straight into the block's storage. Capturing into a local and assigning it
            // back would copy the whole struct twice for every object, every frame.
            _capture(typed, ref element.value, symbols);
            return true;
        }

        public override bool Apply(object owner, int ownerId, StateBlockSet state,
            FrameSymbolTable symbols)
        {
            if (!(owner is TOwner typed) || state == null) return false;

            var block = state.Find<TBlock>();
            if (block == null) return false;

            var index = block.IndexOf(ownerId);
            if (index < 0) return false;

            _apply(in block[index].value, typed, symbols);
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
            StateCapture<TOwner, TBlock> capture, StateApply<TOwner, TBlock> apply,
            params string[] memberNames)
            where TOwner : class
            where TBlock : unmanaged
        {
            Register(new StateBridge<TOwner, TBlock>(capture, apply, memberNames));
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
