// Copyright (c) You-Ri, 2026
using System;
using Unity.Collections;

namespace Lilium.RemoteControl.Frames
{
    /// <summary>What kind of outside event crossed the boundary into the application.</summary>
    public enum InputKind : int
    {
        /// <summary>A write to an exposed, writable property.</summary>
        PropertyWrite = 0,

        /// <summary>A call to an exposed method. Triggers do not show up as a value change.</summary>
        FunctionCall = 1,

        /// <summary>A structural change, such as re-parenting an object.</summary>
        StructureChange = 2,

        /// <summary>
        /// A source registered explicitly because determinism needs it even though there is no
        /// reason to expose it -- capture pose, time, random seed, device input, load completion.
        /// </summary>
        RegisteredSource = 3,
    }

    /// <summary>Things worth knowing about an input that are not part of what it asked for.</summary>
    [Flags]
    public enum InputFlags : byte
    {
        None = 0,

        /// <summary>Applying it threw, so replay can tell a failure apart from a no-op.</summary>
        Faulted = 1 << 0,

        /// <summary>
        /// The payload did not fit and was cut short. The input still applied correctly -- the
        /// original string was used for that -- but what was kept cannot be replayed faithfully.
        /// </summary>
        PayloadTruncated = 1 << 1,
    }

    /// <summary>
    /// What a frame keeps about one input once it is committed.
    ///
    /// Unmanaged and fixed size, so a frame is copied with a block move and slots are reused
    /// without allocating. The strings an input refers to are not held here: they are interned in
    /// a <see cref="InputSymbolTable"/> and referred to by id, which is what keeps this struct
    /// small even though the same property path arrives sixty times a second.
    /// </summary>
    public struct InputRecord
    {
        /// <summary>Order this input was accepted in. Gaps mean something was dropped.</summary>
        public long sequence;

        public InputKind kind;

        /// <summary>Id of where the input came from, or <see cref="InputSymbolTable.kNone"/>.</summary>
        public int sourceId;

        /// <summary>Id of what the input addressed, or <see cref="InputSymbolTable.kNone"/>.</summary>
        public int targetId;

        public InputFlags flags;

        /// <summary>The value or arguments, in the form they arrived in.</summary>
        public FixedString512Bytes payload;

        public InputRecord(long sequence, InputKind kind, int sourceId, int targetId,
            in FixedString512Bytes payload, InputFlags flags)
        {
            this.sequence = sequence;
            this.kind = kind;
            this.sourceId = sourceId;
            this.targetId = targetId;
            this.payload = payload;
            this.flags = flags;
        }

        public bool faulted => (flags & InputFlags.Faulted) != 0;

        public bool payloadTruncated => (flags & InputFlags.PayloadTruncated) != 0;

        public override string ToString() => $"#{sequence} {kind} target:{targetId}";
    }

    /// <summary>
    /// One operation being handed to the gate, before it has a place in the order.
    ///
    /// Several of these can be submitted as a group when they have to land in the same frame --
    /// a bundled request applies its parts together, and splitting them across two frames would
    /// change what the caller asked for.
    /// </summary>
    public readonly struct InputDescriptor
    {
        public readonly InputKind kind;

        /// <summary>What the operation addresses, e.g. the property path.</summary>
        public readonly string target;

        /// <summary>The value or arguments, in the form they arrived in.</summary>
        public readonly string payload;

        public InputDescriptor(InputKind kind, string target, string payload = null)
        {
            this.kind = kind;
            this.target = target;
            this.payload = payload;
        }

        public override string ToString() => $"{kind} {target}";
    }

    /// <summary>
    /// One input on its way through the gate: the records to keep, plus how to apply them.
    ///
    /// Separate from <see cref="InputRecord"/> because it carries delegates, and a frame that held
    /// one would pin every closure it captured for as long as the frame is retained.
    /// </summary>
    internal sealed class PendingInput
    {
        /// <summary>
        /// What this input will leave behind. Usually one record; more when a bundled request was
        /// submitted as a group, in which case they are kept apart so each stays small enough to
        /// record faithfully -- one record holding a whole bundle would always be truncated.
        /// </summary>
        public InputRecord[] records;

        public int recordCount;

        /// <summary>Runs the input at a frame head and completes whoever is waiting on it.</summary>
        public Action apply;

        /// <summary>
        /// Abandons the input, handing the waiter the reason instead. Every queued input has to end
        /// one way or the other: dropping one without calling this leaves its caller waiting for a
        /// frame head that will never come to it.
        /// </summary>
        public Action<Exception> fault;

        /// <summary>Sequence given to the first record. The rest follow it without gaps.</summary>
        public long firstSequence => recordCount > 0 ? records[0].sequence : 0;

        /// <summary>Marks every record of this input, used when applying it threw.</summary>
        public void SetFlags(InputFlags value)
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
