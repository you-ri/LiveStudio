// Copyright (c) You-Ri, 2026
namespace Lilium.RemoteControl.Frames
{
    /// <summary>
    /// Whether the state lane is actually carrying a member, as opposed to having been asked to.
    ///
    /// Declaring <see cref="FrameLane.State"/> is a request, not a fact. A member can ask for the
    /// lane and never reach a block -- text with no declared width, a type that is not unmanaged, an
    /// owner type that is not <c>partial</c> or whose assembly does not reference the simulation --
    /// and the generator says so at compile time. Nothing said so at runtime: the write path took
    /// the declaration at its word and omitted the member's event records because it was "state",
    /// which left a member nothing was carrying written down nowhere at all.
    ///
    /// So the question is put to whatever is doing the carrying rather than to the declaration. Get
    /// it wrong towards "carried" and the value is lost from the recording; wrong the other way and
    /// a member the block already moves pays for an event record as well.
    /// </summary>
    public static class LiveStateCarriage
    {
        /// <summary>
        /// Whether a write to this member should be kept out of the recording.
        ///
        /// Neither of the other two lanes wants a record, for opposite reasons. State copies the
        /// member every frame, so an event record would be the same value written twice -- and the
        /// event pays its full width to say what the state lane already said. None is not carried by
        /// the frame at all: a setting of this machine rather than of the world, which recorded and
        /// replayed reaches over and changes the operator's own settings.
        ///
        /// Here rather than at each write, because it was not: the REST path, the reset path and a
        /// deck key are the same write, and each had its own copy of the rule. They drifted -- the
        /// deck key never honoured <see cref="FrameLane.None"/>, so a setting changed from a deck
        /// stayed in the take, and it kept reading the declaration after the REST path had stopped.
        /// A write recorded once or twice depending on which control was used is the shape of every
        /// bug this has had.
        ///
        /// <para>
        /// ⚠ The question put to the block is "is this value already carried", not "was the state
        /// lane asked for". The two are not the same, and not only in the direction this class was
        /// built for: a <c>[LiveField]</c> that says nothing about its lane is carried by the block
        /// (the generator's default for a field), while the attribute it was declared with reports
        /// <see cref="FrameLane.Event"/>. Reading the declaration therefore kept an event record for
        /// a value the block was already copying sixty times a second -- the same value in both
        /// lanes, which is exactly what this is here to prevent.
        /// </para>
        /// </summary>
        public static bool OmitsRecord(LivePropertyType member, object owner)
        {
            var lane = member?.lane ?? FrameLane.Event;

            // None is never carried by anything, so there is nothing to ask: the member is off the
            // live data by declaration, whatever any block happens to hold.
            if (lane == FrameLane.None) return true;

            return IsCarriedByState(member, owner);
        }

        /// <summary>
        /// Whether the block for <paramref name="owner"/>'s type moves this member.
        ///
        /// The exact runtime type, because that is how bridges are keyed -- a derived type gets its
        /// own bridge carrying the members it added, rather than inheriting its base's. A member
        /// reached through a nested object therefore asks about that object's own type, and gets
        /// the answer for it.
        /// </summary>
        public static bool IsCarriedByState(LivePropertyType member, object owner)
        {
            if (owner == null) return false;

            return IsCarriedByState(member, StateBridgeRegistry.Find(owner.GetType()));
        }

        /// <summary>
        /// The same question against a bridge already in hand, for a caller walking a whole type.
        ///
        /// Tried under every name the member goes by, because the two kinds of bridge key their
        /// members differently: the generated one by the member's own spelling, the declared one by
        /// the name it is exposed under. Being wrong towards "carried" writes the value twice; being
        /// wrong the other way loses it from the recording, so every spelling is tried before the
        /// answer is no.
        /// </summary>
        public static bool IsCarriedByState(LivePropertyType member, StateBridge bridge)
        {
            if (member == null || bridge == null) return false;

            if (member.properyInfo != null && bridge.Carries(member.properyInfo.Name)) return true;
            if (member.fieldInfo != null && bridge.Carries(member.fieldInfo.Name)) return true;
            if (member.shadowField != null && bridge.Carries(member.shadowField.Name)) return true;

            return bridge.Carries(member.name);
        }
    }
}
