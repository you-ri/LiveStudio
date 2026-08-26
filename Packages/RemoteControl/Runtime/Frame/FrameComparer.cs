// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;

namespace Lilium.RemoteControl.Frames
{
    /// <summary>Why two runs disagreed about one element.</summary>
    public enum MismatchReason
    {
        /// <summary>Both had the element and its bytes differ.</summary>
        ValueDiffers = 0,

        /// <summary>One run has it and the other does not.</summary>
        Missing = 1,

        /// <summary>Neither run has a block of this type where the other does.</summary>
        BlockMissing = 2,
    }

    /// <summary>One place two runs came apart. Named by type and owner, which is what a fix starts from.</summary>
    public readonly struct FrameMismatch
    {
        public readonly string typeName;
        public readonly int ownerId;
        public readonly MismatchReason reason;

        public FrameMismatch(string typeName, int ownerId, MismatchReason reason)
        {
            this.typeName = typeName;
            this.ownerId = ownerId;
            this.reason = reason;
        }

        public override string ToString() => $"{typeName}#{ownerId}: {reason}";
    }

    /// <summary>
    /// What comparing two runs found.
    ///
    /// The number that matters is <see cref="matchRate"/>. A run that looks right on screen while
    /// its state quietly drifts is the failure this exists to catch, and only a measured rate tells
    /// the two apart.
    /// </summary>
    public sealed class FrameComparisonReport
    {
        /// <summary>Mismatches kept for inspection. Capped -- the count is what matters at scale.</summary>
        public const int kMaxRecordedMismatches = 64;

        private readonly List<FrameMismatch> _mismatches = new List<FrameMismatch>();

        /// <summary>Elements looked at, counting one for each element either side had.</summary>
        public int comparedElements { get; internal set; }

        /// <summary>Elements that agreed.</summary>
        public int matchedElements { get; internal set; }

        /// <summary>Mismatches found, including any past the recorded cap.</summary>
        public int mismatchCount { get; internal set; }

        /// <summary>True when the inventories agree on contents and order.</summary>
        public bool structureMatches { get; internal set; } = true;

        /// <summary>The first <see cref="kMaxRecordedMismatches"/> mismatches.</summary>
        public IReadOnlyList<FrameMismatch> mismatches => _mismatches;

        /// <summary>
        /// Share of elements that agreed, 1 when there was nothing to compare. The structure is not
        /// part of this: it is discrete and either matches or does not.
        /// </summary>
        public float matchRate => comparedElements == 0 ? 1f : (float)matchedElements / comparedElements;

        /// <summary>True when nothing came apart.</summary>
        public bool isClean => structureMatches && mismatchCount == 0;

        internal void Add(in FrameMismatch mismatch)
        {
            mismatchCount++;
            if (_mismatches.Count < kMaxRecordedMismatches) _mismatches.Add(mismatch);
        }

        internal void Reset()
        {
            _mismatches.Clear();
            comparedElements = 0;
            matchedElements = 0;
            mismatchCount = 0;
            structureMatches = true;
        }

        public override string ToString()
            => $"{matchedElements}/{comparedElements} elements match ({matchRate:P2}), " +
               $"structure {(structureMatches ? "matches" : "differs")}";
    }

    /// <summary>
    /// Compares what two runs ended up holding.
    ///
    /// This is an instrument, not a feature of a live run. Production correctness rests on periodic
    /// keyframes, which bound how long a divergence can last without measuring anything; what a
    /// measurement adds is knowing whether the frame data is doing the work or the keyframes are
    /// carrying it. That question belongs to development, so this is allowed to be slow.
    ///
    /// Reusable: one instance and one report, so running it every frame in a verification pass does
    /// not allocate its way through a session.
    /// </summary>
    public sealed class FrameComparer
    {
        private readonly FrameComparisonReport _report = new FrameComparisonReport();
        private readonly HashSet<int> _seenOwners = new HashSet<int>();

        /// <summary>
        /// Compares two worlds. The report is reused, so read it before comparing again.
        /// </summary>
        public FrameComparisonReport Compare(
            StructureBlock expectedStructure, StateBlockSet expectedState,
            StructureBlock actualStructure, StateBlockSet actualState)
        {
            _report.Reset();

            _CompareStructure(expectedStructure, actualStructure);
            _CompareState(expectedState, actualState);

            return _report;
        }

        private void _CompareStructure(StructureBlock expected, StructureBlock actual)
        {
            if (expected == null || actual == null)
            {
                _report.structureMatches = expected == actual;
                return;
            }

            if (expected.count != actual.count)
            {
                _report.structureMatches = false;
                return;
            }

            for (int i = 0; i < expected.count; i++)
            {
                var a = expected[i];
                var b = actual[i];

                // Positional, not by id: the inventory's order is part of the recording, so two runs
                // holding the same objects in a different order have already come apart.
                if (a.id == b.id && a.typeId == b.typeId && a.parentId == b.parentId) continue;

                _report.structureMatches = false;
                return;
            }
        }

        private void _CompareState(StateBlockSet expected, StateBlockSet actual)
        {
            if (expected == null || actual == null) return;

            for (int i = 0; i < expected.blocks.Count; i++)
            {
                var expectedBlock = expected.blocks[i];
                var typeName = expectedBlock.elementType.FullName;
                var actualBlock = actual.FindByTypeName(typeName);

                if (actualBlock == null)
                {
                    if (expectedBlock.count == 0) continue;

                    _report.comparedElements += expectedBlock.count;
                    _report.Add(new FrameMismatch(typeName, InputSymbolTable.kNone, MismatchReason.BlockMissing));
                    continue;
                }

                _CompareBlock(typeName, expectedBlock, actualBlock);
            }

            // Anything the other side has and this one does not is also a divergence, and looking
            // only one way would call a run clean that grew a whole type out of nowhere.
            for (int i = 0; i < actual.blocks.Count; i++)
            {
                var actualBlock = actual.blocks[i];
                if (actualBlock.count == 0) continue;

                var typeName = actualBlock.elementType.FullName;
                if (expected.FindByTypeName(typeName) != null) continue;

                _report.comparedElements += actualBlock.count;
                _report.Add(new FrameMismatch(typeName, InputSymbolTable.kNone, MismatchReason.BlockMissing));
            }
        }

        private void _CompareBlock(string typeName, StateBlock expected, StateBlock actual)
        {
            _seenOwners.Clear();

            for (int i = 0; i < expected.count; i++)
            {
                var ownerId = expected.OwnerIdAt(i);
                _seenOwners.Add(ownerId);
                _report.comparedElements++;

                // Matched by owner rather than by position: an element that moved in the array is a
                // structural difference, and reporting every element after it as wrong would bury
                // the one thing that actually changed.
                var otherIndex = actual.IndexOfOwner(ownerId);
                if (otherIndex < 0)
                {
                    _report.Add(new FrameMismatch(typeName, ownerId, MismatchReason.Missing));
                    continue;
                }

                if (expected.ElementEquals(i, actual, otherIndex))
                {
                    _report.matchedElements++;
                    continue;
                }

                _report.Add(new FrameMismatch(typeName, ownerId, MismatchReason.ValueDiffers));
            }

            for (int i = 0; i < actual.count; i++)
            {
                var ownerId = actual.OwnerIdAt(i);
                if (_seenOwners.Contains(ownerId)) continue;

                _report.comparedElements++;
                _report.Add(new FrameMismatch(typeName, ownerId, MismatchReason.Missing));
            }
        }
    }
}
