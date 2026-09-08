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

        // 2 and 3 were StructureChange and RegisteredSource, retired 2026-09-08. Both asked
        // something this lane never answered -- one what the event did, the other where it came
        // from -- and neither reached the fold, which is what the kind is for. Nothing wrote 3.
        // Recordings still hold 2; the reader normalises it, see FrameRecordPlayer.
    }

    /// <summary>Things worth knowing about an event that are not part of what it asked for.</summary>
    [Flags]
    public enum EventFlags : byte
    {
        None = 0,

        /// <summary>Applying it threw, so replay can tell a failure apart from a no-op.</summary>
        Faulted = 1 << 0,

        /// <summary>
        /// The payload did not fit and was cut short. The event still applied correctly -- what
        /// arrived was used for that -- but what was kept cannot be replayed faithfully.
        ///
        /// Only reachable for a payload with no fixed size, which in practice means text: a typed
        /// value is written at its own width and either fits or was never going to.
        /// </summary>
        PayloadTruncated = 1 << 1,

        /// <summary>
        /// Applied, but deliberately left out of the frame.
        ///
        /// For a write whose target the state lane already carries: the value arrives in the state
        /// lane every frame regardless, so keeping the event as well says the same thing twice --
        /// and the event record costs its full width to say it. The write still goes through the
        /// gate, because ordering is the other half of what the gate is for.
        ///
        /// Never reaches a recording: the frame drops these before committing.
        /// </summary>
        NotRecorded = 1 << 2,

        /// <summary>
        /// Not something that happened, but the value as it stood -- written into a frame so a
        /// recording carries a point it can be read from rather than only the changes made after it
        /// started.
        ///
        /// ⚠ Nothing writes this any more. Keyframes used to carry a restatement of every event-lane
        /// member, and stopped on 2026-09-04: the walk cost scaled with the exposed surface and all
        /// of it landed on one frame. Carrying a value across a seek is the state lane's job now,
        /// one member at a time as they are declared into it.
        ///
        /// Kept because recordings made before that still hold these records, and reading one has to
        /// keep telling them apart from real writes: a real write is replayed because someone made
        /// it, a restated one only to make the world match, so applying it when the value already
        /// matches is work with no effect. The apply side skips those, which is what keeps a restated
        /// asset reference from reloading the asset once a keyframe.
        /// </summary>
        Reemitted = 1 << 3,
    }

    /// <summary>
    /// What a frame keeps about one event once it is committed.
    ///
    /// Unmanaged and fixed size, so a frame is copied with a block move and slots are reused
    /// without allocating. The strings an event refers to are not held here: they are interned in
    /// a <see cref="FrameSymbolTable"/> and referred to by id, which is what keeps this struct
    /// small even though the same property path arrives sixty times a second.
    /// </summary>
    public unsafe struct EventRecord
    {
        /// <summary>
        /// Room for one payload. Wide enough for any value with a fixed width and for the text an
        /// event carries when it has no such width, which is the only case that can overflow it.
        /// </summary>
        public const int kPayloadCapacity = 512;

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
        /// Without this a replay has to guess: the same target answers to more than one verb, and
        /// picking the wrong one is the difference between setting a value and resetting it.
        ///
        /// The vocabulary belongs to whoever submitted the event -- over REST it is the HTTP method
        /// -- and nothing in this lane interprets it. It is a symbol id and stays one.
        /// </summary>
        public int verbId;

        /// <summary>
        /// Id of the type name <see cref="payload"/> holds, or <see cref="FrameSymbolTable.kNone"/>
        /// when the record carries no payload.
        ///
        /// Bytes with no type are unreadable, so the two always travel together. The name is what
        /// <see cref="EventPayload"/> resolves to lay the bytes back out, and it is what lets a
        /// viewer walk a payload with the same machinery it walks a state element with.
        /// </summary>
        public int payloadTypeId;

        /// <summary>How much of <see cref="payload"/> is used.</summary>
        public int payloadLength;

        public EventFlags flags;

        /// <summary>
        /// The value, as bytes of the type <see cref="payloadTypeId"/> names.
        ///
        /// Bytes rather than text because the values that arrive here are values: a float is four
        /// bytes and a pose is a struct, and turning either into digits and back costs the parse,
        /// the allocation, and the precision. Text is still a payload -- it is simply one whose
        /// type says so.
        /// </summary>
        public fixed byte payload[kPayloadCapacity];

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
            payloadLength = 0;
        }

        public bool faulted => (flags & EventFlags.Faulted) != 0;

        public bool payloadTruncated => (flags & EventFlags.PayloadTruncated) != 0;

        /// <summary>True when this record carries a value at all.</summary>
        public bool hasPayload => payloadTypeId != FrameSymbolTable.kNone;

        /// <summary>True when the record restates a value rather than reporting a change.</summary>
        public bool reemitted => (flags & EventFlags.Reemitted) != 0;

        /// <summary>
        /// Puts a value in the record, replacing whatever was there. Returns false when it did not
        /// fit, in which case what fits is kept and the caller is expected to raise
        /// <see cref="EventFlags.PayloadTruncated"/> -- half a value is not a value, and only the
        /// caller knows whether saying so matters.
        /// </summary>
        public bool SetPayload(ReadOnlySpan<byte> value, int typeId)
        {
            payloadTypeId = typeId;

            var length = value.Length;
            var fits = length <= kPayloadCapacity;
            if (!fits) length = kPayloadCapacity;

            payloadLength = length;

            fixed (byte* destination = payload)
            {
                value.Slice(0, length).CopyTo(new Span<byte>(destination, kPayloadCapacity));
            }

            return fits;
        }

        /// <summary>Copies the payload out. Returns how many bytes were written.</summary>
        public int CopyPayloadTo(Span<byte> destination)
        {
            var length = Math.Min(payloadLength, destination.Length);

            fixed (byte* source = payload)
            {
                new ReadOnlySpan<byte>(source, length).CopyTo(destination);
            }

            return length;
        }

        /// <summary>
        /// The payload as bytes, valid only while <paramref name="record"/> stays put.
        ///
        /// Takes the record by reference on purpose: a span over a copy would point into a struct
        /// that is about to go out of scope, and the compiler cannot see that for a fixed buffer.
        /// </summary>
        public static ReadOnlySpan<byte> PayloadOf(ref EventRecord record)
        {
            fixed (byte* bytes = record.payload)
            {
                return new ReadOnlySpan<byte>(bytes, record.payloadLength);
            }
        }

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
        /// Which operation is being asked for on the target. A target answers to more than one
        /// verb, so a replay that only had the target would have to guess.
        ///
        /// Named by the submitter in its own terms -- the HTTP method for anything arriving over
        /// REST. The gate interns it and never reads it.
        /// </summary>
        public readonly string verb;

        /// <summary>
        /// The request as it arrived, before anything has worked out what it means.
        ///
        /// Kept as the fallback payload: at submit time the target has not been resolved, so its
        /// type is not known yet. Whoever applies the event knows the value it really wrote and
        /// replaces this with it -- see <c>FrameGate.StampAppliedPayload</c> on the host side. What stays
        /// text is what has no other form.
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

    /// <summary>
    /// One event on its way through the gate: the records to keep, plus how to apply them.
    ///
    /// Separate from <see cref="EventRecord"/> because it carries delegates, and a frame that held
    /// one would pin every closure it captured for as long as the frame is retained.
    /// </summary>
    internal sealed class PendingEvent
    {
        /// <summary>
        /// What this event will leave behind. Usually one record; more when a bundled request was
        /// submitted as a group, in which case they are kept apart so each stays small enough to
        /// record faithfully -- one record holding a whole bundle would always be truncated.
        /// </summary>
        public EventRecord[] records;

        public int recordCount;

        /// <summary>Runs the event at a frame head and completes whoever is waiting on it.</summary>
        public Action apply;

        /// <summary>
        /// Abandons the event, handing the waiter the reason instead. Every queued event has to end
        /// one way or the other: dropping one without calling this leaves its caller waiting for a
        /// frame head that will never come to it.
        /// </summary>
        public Action<Exception> fault;

        /// <summary>Sequence given to the first record. The rest follow it without gaps.</summary>
        public long firstSequence => recordCount > 0 ? records[0].sequence : 0;

        /// <summary>Marks every record of this event, used when applying it threw.</summary>
        public void SetFlags(EventFlags value)
        {
            for (int i = 0; i < recordCount; i++) records[i].flags |= value;
        }

        public void Clear()
        {
            records = null;
            recordCount = 0;
            apply = null;
            fault = null;
        }
    }
}
