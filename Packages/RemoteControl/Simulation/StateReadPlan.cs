// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;

namespace Lilium.RemoteControl.Frames
{
    /// <summary>
    /// How to read a recorded element into the element this build holds.
    ///
    /// Built once for a pair of descriptions, then run for every element of every frame. The pair is
    /// what identifies it, not the type: a recording can carry two descriptions of the same type
    /// (an asset whose declaration changed mid-take), and a plan built for one of them puts values
    /// in the wrong place when run against the other.
    ///
    /// What it does not do is decide anything about members that did not line up. A member the
    /// recording never carried has no bytes to copy, and the bytes sitting in its place mean
    /// nothing -- the block on the replay side is only ever written from a recording, so "leave it
    /// alone" and "fill it with zero" come to the same thing there. The value that must survive is
    /// on the object, which is why the answer is a mask rather than a fill: see
    /// <see cref="appliedMemberMask"/>.
    /// </summary>
    public sealed class StateReadPlan
    {
        /// <summary>Most members a mask can speak for. One bit each.</summary>
        public const int kMaxMaskedMembers = 64;

        /// <summary>A stretch of bytes that lands somewhere, both offsets within the payload.</summary>
        public readonly struct Run
        {
            public readonly int sourceOffset;
            public readonly int destinationOffset;
            public readonly int length;

            public Run(int sourceOffset, int destinationOffset, int length)
            {
                this.sourceOffset = sourceOffset;
                this.destinationOffset = destinationOffset;
                this.length = length;
            }
        }

        /// <summary>The copies to make, in destination order. Adjacent members are one run.</summary>
        public readonly Run[] runs;

        /// <summary>
        /// Which of this build's members the recording actually spoke for, one bit per member in
        /// declaration order.
        ///
        /// Handed to whatever writes the block back onto the object, so a member the take never
        /// carried is not written at all. The alternative -- writing the zero that sits in its place
        /// -- is not "leaving it at its default": a struct default is zero, and zero is a real and
        /// often wrong value. A camera whose field of view defaults to forty gets nothing, an
        /// override whose "no override" is minus one gets "override with zero", a name gets emptied.
        /// The rule that avoids all of those at once is not to write what was not recorded.
        /// </summary>
        public readonly ulong appliedMemberMask;

        /// <summary>Bytes one recorded element occupies, metadata included.</summary>
        public readonly int sourceStride;

        /// <summary>Bytes before the payload in a recorded element.</summary>
        public readonly int sourceMetaSize;

        /// <summary>Bytes before the payload in this build's element.</summary>
        public readonly int destinationMetaSize;

        /// <summary>Bytes of value this build's element carries.</summary>
        public readonly int destinationPayloadSize;

        /// <summary>Members the recording carries that this build has no place for.</summary>
        public readonly string[] droppedMembers;

        /// <summary>Members this build has that the recording never carried.</summary>
        public readonly string[] unwrittenMembers;

        /// <summary>
        /// True when the two descriptions agree in every way, so the whole block is one copy.
        ///
        /// Worth telling apart rather than treating as a plan of one run, because this is the case
        /// that happens on every ordinary replay and the cost of the general path -- a loop per
        /// element instead of a copy per block -- should not be paid for it.
        /// </summary>
        public readonly bool isIdentical;

        private StateReadPlan(Run[] runs, ulong appliedMemberMask, int sourceStride, int sourceMetaSize,
            int destinationMetaSize, int destinationPayloadSize, string[] droppedMembers,
            string[] unwrittenMembers, bool isIdentical)
        {
            this.runs = runs;
            this.appliedMemberMask = appliedMemberMask;
            this.sourceStride = sourceStride;
            this.sourceMetaSize = sourceMetaSize;
            this.destinationMetaSize = destinationMetaSize;
            this.destinationPayloadSize = destinationPayloadSize;
            this.droppedMembers = droppedMembers;
            this.unwrittenMembers = unwrittenMembers;
            this.isIdentical = isIdentical;
        }

        /// <summary>
        /// Works out how to read <paramref name="recorded"/> as <paramref name="mine"/>, or null
        /// when it cannot be done.
        ///
        /// Null for a build whose type has more members than a mask can speak for. Refusing there is
        /// the honest answer: a plan without a mask would put back the members that lined up and
        /// silently write zero over the ones that did not, which is the failure this exists to
        /// prevent.
        /// </summary>
        public static StateReadPlan Build(StateSchema recorded, StateSchema mine)
        {
            if (recorded == null || mine == null) return null;
            if (mine.members.Length > kMaxMaskedMembers) return null;

            var identical = recorded.stride == mine.stride
                            && recorded.metaSize == mine.metaSize
                            && recorded.members.Length == mine.members.Length;

            var mask = 0UL;
            var runs = new List<Run>(mine.members.Length);
            List<string> unwritten = null;

            for (int i = 0; i < mine.members.Length; i++)
            {
                var wanted = mine.members[i];
                var found = -1;

                for (int j = 0; j < recorded.members.Length; j++)
                {
                    if (!recorded.members[j].SameValueAs(in wanted)) continue;

                    found = j;
                    break;
                }

                if (found < 0)
                {
                    identical = false;
                    (unwritten ??= new List<string>()).Add(wanted.name);
                    continue;
                }

                var from = recorded.members[found];
                if (from.offset != wanted.offset || found != i) identical = false;

                mask |= 1UL << i;

                // Joined to the run before it only when both ends continue it. Two members that sit
                // side by side here but came from opposite ends of the recorded element are two
                // copies however adjacent they look on one side.
                if (runs.Count > 0)
                {
                    var last = runs[runs.Count - 1];
                    if (last.sourceOffset + last.length == from.offset
                        && last.destinationOffset + last.length == wanted.offset)
                    {
                        runs[runs.Count - 1] = new Run(last.sourceOffset, last.destinationOffset,
                            last.length + wanted.size);
                        continue;
                    }
                }

                runs.Add(new Run(from.offset, wanted.offset, wanted.size));
            }

            List<string> dropped = null;
            for (int j = 0; j < recorded.members.Length; j++)
            {
                if (mine.IndexOf(recorded.members[j].name) >= 0) continue;

                identical = false;
                (dropped ??= new List<string>()).Add(recorded.members[j].name);
            }

            return new StateReadPlan(
                runs.ToArray(),
                mask,
                recorded.stride,
                recorded.metaSize,
                mine.metaSize,
                mine.payloadSize,
                dropped?.ToArray() ?? Array.Empty<string>(),
                unwritten?.ToArray() ?? Array.Empty<string>(),
                identical);
        }

        /// <summary>
        /// A mask that speaks for every member, for a block filled by capture rather than by a
        /// recording. Nothing was left out, so nothing is held back on the way to the object.
        /// </summary>
        public const ulong kAllMembers = ulong.MaxValue;
    }
}
