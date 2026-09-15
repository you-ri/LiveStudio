// Copyright (c) You-Ri, 2026
using System;

namespace Lilium.RemoteControl.Frames
{
    /// <summary>
    /// Whether an event can be folded to the last one per target, which is the only question
    /// anything in this lane asks about an event.
    ///
    /// ⚠ Not the HTTP verb and not what the event does. A seek replays a span of frames at once and
    /// has to decide, per record, between "the value ended up here" and "this happened, this many
    /// times, in this order". That decision is the whole content of this enum, and every other way
    /// an event differs is carried elsewhere: what was asked for in <see cref="EventRecord.verbId"/>
    /// and the target, where it came from in <see cref="EventRecord.sourceId"/>, and facts about
    /// the record itself in <see cref="EventFlags"/>.
    ///
    /// So an idempotent call whose target names what it acts on belongs in <see cref="Set"/> even
    /// though it is a call: replaying it twice changes nothing, and folding it is what lets a seek
    /// land on it.
    ///
    /// ⚠ Being addressed by an id is not enough -- the id has to be in the target. The fold keys on
    /// <see cref="EventRecord.targetId"/> alone and reads neither the verb nor the payload, so a
    /// call that takes what it acts on as a payload (destroying an object by id in the body, say)
    /// collapses to whichever one came last and loses the rest.
    /// </summary>
    public enum EventKind : int
    {
        /// <summary>
        /// A value arriving at a target, foldable to the last one per target.
        ///
        /// A write to an exposed property is the usual case, but so is anything else whose result
        /// depends only on the last one to arrive.
        /// </summary>
        Set = 0,

        /// <summary>
        /// Something that happened, where the count and the order are the content.
        ///
        /// A seek does not replay these: there is nothing to fold them with, and nothing that says
        /// how many a destination already has behind it. Forward play sees them in order.
        /// </summary>
        Call = 1,
    }

    /// <summary>Things worth knowing about an event that are not part of what it asked for.</summary>
    [Flags]
    public enum EventFlags : byte
    {
        None = 0,

        /// <summary>Applying it threw, so replay can tell a failure apart from a no-op.</summary>
        Faulted = 1 << 0,

        /// <summary>
        /// Applied, but deliberately left out of the frame.
        ///
        /// For a write whose target the state lane already carries: the value arrives in the state
        /// lane every frame regardless, so keeping the event as well says the same thing twice. The
        /// write still goes through the gate, because ordering is the other half of what the gate is
        /// for.
        ///
        /// Never reaches a recording: the frame drops these before committing.
        /// </summary>
        NotRecorded = 1 << 1,
    }

    /// <summary>
    /// What a frame keeps about one event once it is committed.
    ///
    /// Unmanaged and fixed size, so a frame is copied with a block move and slots are reused without
    /// allocating. Neither the strings an event refers to nor the value it carries are held here:
    /// the strings are interned in a <see cref="FrameSymbolTable"/> and referred to by id, and the
    /// value sits in the arena of whatever holds the record (<see cref="EventFrame.PayloadOf"/>),
    /// which is what keeps this struct small whatever it carries.
    /// </summary>
    public struct EventRecord
    {
        /// <summary>Order this event was accepted in. Gaps mean something was dropped.</summary>
        public long sequence;

        public EventKind kind;

        /// <summary>Id of where the event came from, or <see cref="FrameSymbolTable.kNone"/>.</summary>
        public int sourceId;

        /// <summary>Id of what the event addressed, or <see cref="FrameSymbolTable.kNone"/>.</summary>
        public int targetId;

        /// <summary>
        /// Id of which operation was asked for on the target, since a target on its own does not
        /// say. Interned like everything else, so the handful of distinct values cost one symbol
        /// each however many records use them.
        ///
        /// The vocabulary belongs to whoever submitted the event -- over REST it is the HTTP method
        /// -- and nothing in this lane interprets it.
        /// </summary>
        public int verbId;

        /// <summary>
        /// Id of the type name the payload holds, or <see cref="FrameSymbolTable.kNone"/> when the
        /// record carries no payload. Bytes with no type are unreadable, so the two travel together.
        /// </summary>
        public int payloadTypeId;

        /// <summary>Where the value starts in the arena of whatever holds this record.</summary>
        public int payloadOffset;

        /// <summary>How many bytes the value takes.</summary>
        public int payloadLength;

        public EventFlags flags;

        public EventRecord(long sequence, EventKind kind, int sourceId, int targetId,
            EventFlags flags, int verbId = FrameSymbolTable.kNone)
        {
            this.sequence = sequence;
            this.kind = kind;
            this.sourceId = sourceId;
            this.targetId = targetId;
            this.verbId = verbId;
            this.flags = flags;
            payloadTypeId = FrameSymbolTable.kNone;
            payloadOffset = 0;
            payloadLength = 0;
        }

        public bool faulted => (flags & EventFlags.Faulted) != 0;

        /// <summary>True when this record carries a value at all.</summary>
        public bool hasPayload => payloadTypeId != FrameSymbolTable.kNone;

        public override string ToString() => $"#{sequence} {kind} target:{targetId}";
    }

    /// <summary>
    /// One operation being handed to the gate, before it has a place in the order.
    ///
    /// Several of these can be submitted as a group when they have to land in the same frame --
    /// a bundled request applies its parts together, and splitting them across two frames would
    /// change what the caller asked for.
    /// </summary>
    public readonly struct EventDescriptor
    {
        public readonly EventKind kind;

        /// <summary>What the operation addresses, e.g. the property path.</summary>
        public readonly string target;

        /// <summary>
        /// Which operation is being asked for on the target. Named by the submitter in its own terms
        /// -- the HTTP method for anything arriving over REST. The gate interns it and never reads it.
        /// </summary>
        public readonly string verb;

        /// <summary>
        /// The request as it arrived, before anything has worked out what it means.
        ///
        /// Kept as the fallback payload: at submit time the target has not been resolved, so its
        /// type is not known yet. Whoever applies the event knows the value it really wrote and
        /// replaces this with it -- see <c>FrameGate.StampAppliedPayload</c> on the host side.
        /// </summary>
        public readonly string requestText;

        public EventDescriptor(EventKind kind, string verb, string target, string requestText = null)
        {
            this.kind = kind;
            this.verb = verb;
            this.target = target;
            this.requestText = requestText;
        }

        public override string ToString() => $"{verb} {target} ({kind})";
    }
}
