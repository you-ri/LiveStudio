// Copyright (c) You-Ri, 2026
using System;

namespace Lilium.RemoteControl.Frames
{
    /// <summary>
    /// A state-lane string carried as the id the frame's symbol table gave it.
    ///
    /// For text drawn from a vocabulary rather than typed freely -- a bone path, an owner name, a
    /// mesh name, a layer. Those values repeat unchanged on every frame of a take, so spelling them
    /// out in the block costs the width of the longest one per object per frame, while the table
    /// holds each distinct string once for the whole file.
    ///
    /// The mechanism is not new here: every row already carries two interned ids in its header
    /// (<see cref="StateElement{T}.ownerId"/> and its source), and the event lane files its target
    /// paths, verbs and type names the same way. This is that, applied to a member's value.
    ///
    /// The gain that matters is not the four bytes. <see cref="LiveFixedString32"/> and its wider
    /// siblings drop a value that outgrows the width -- correctly, since a truncated string is a
    /// value nobody set -- which makes every fixed width a claim that can turn out to be wrong on
    /// some rig, in some scene, halfway through a take. An id has no length to outgrow.
    ///
    /// ⚠ The cost is paid by free text. The table is never emptied within a run, so a member whose
    /// value keeps taking new shapes grows it for as long as the session lasts. Text that is typed
    /// rather than chosen belongs in a fixed width or on the event lane.
    /// </summary>
    public readonly struct LiveTextId : IEquatable<LiveTextId>
    {
        // Zero is "nothing was written here", which a zeroed block has to mean: an id of zero is a
        // real symbol (the first string interned in the run), so storing ids as they come would make
        // an untouched slot read as whatever that happened to be. Same reason FrameSource offsets.
        private const int kUnset = 0;
        private const int kNullValue = -1;
        private const int kEmptyValue = -2;

        private readonly int _state;

        private LiveTextId(int state) => _state = state;

        /// <summary>Whether anything was written here at all.</summary>
        public bool hasValue => _state != kUnset;

        /// <summary>
        /// The symbol id behind this, or <see cref="FrameSymbolTable.kNone"/> when it stands for
        /// null, for the empty string, or for nothing at all.
        /// </summary>
        public int symbolId => _state > 0 ? _state - 1 : FrameSymbolTable.kNone;

        /// <summary>
        /// Interns a value into the table the frame carries.
        ///
        /// Null and empty are kept apart, the way the fixed widths keep them apart: a table cannot
        /// hold either (interning both answers <see cref="FrameSymbolTable.kNone"/>), and a replay
        /// that turned one into the other would clear a member that was only ever blank.
        /// </summary>
        public static LiveTextId From(string value, FrameSymbolTable symbols)
        {
            if (value == null) return new LiveTextId(kNullValue);
            if (value.Length == 0) return new LiveTextId(kEmptyValue);

            // No table to intern into. Written as "nothing here" rather than as null, so that a
            // replay leaves the member alone instead of clearing it.
            if (symbols == null) return default;

            var id = symbols.Intern(value);
            if (id == FrameSymbolTable.kNone) return new LiveTextId(kNullValue);

            return new LiveTextId(id + 1);
        }

        /// <summary>
        /// Hands back the stored string, unless there is nothing to say, nothing to resolve it
        /// with, or the target already holds it.
        ///
        /// <paramref name="current"/> is asked for the reason the fixed widths ask for it: the state
        /// lane says every member on every frame, so a replay would otherwise run a setter sixty
        /// times a second for a value that has not moved -- and a setter behind an asset reference
        /// answers that by loading.
        ///
        /// ⚠ An id the table cannot resolve is passed over rather than read as empty. A recording
        /// cut short mid-write, or one read against a table that never got the entry, would
        /// otherwise clear the member -- and clearing a reference is a change, where saying nothing
        /// is not.
        /// </summary>
        public bool TryGetValue(string current, FrameSymbolTable symbols, out string value)
        {
            value = null;

            switch (_state)
            {
                case kUnset:
                    return false;

                case kNullValue:
                    return current != null;

                case kEmptyValue:
                    if (current != null && current.Length == 0) return false;
                    value = string.Empty;
                    return true;
            }

            if (symbols == null || !symbols.TryResolve(_state - 1, out var text) || text == null)
            {
                LiveTextIdStats.CountUnresolved();
                return false;
            }

            if (string.Equals(text, current, StringComparison.Ordinal)) return false;

            value = text;
            return true;
        }

        /// <summary>The stored string, for display and for tests. Unset and null both read as null.</summary>
        public string Resolve(FrameSymbolTable symbols)
        {
            switch (_state)
            {
                case kUnset:
                case kNullValue:
                    return null;

                case kEmptyValue:
                    return string.Empty;
            }

            return symbols != null && symbols.TryResolve(_state - 1, out var text) ? text : null;
        }

        public bool Equals(LiveTextId other) => _state == other._state;

        public override bool Equals(object obj) => obj is LiveTextId other && Equals(other);

        public override int GetHashCode() => _state;

        public override string ToString()
        {
            switch (_state)
            {
                case kUnset: return "(unset)";
                case kNullValue: return "(null)";
                case kEmptyValue: return string.Empty;
            }

            return "#" + (_state - 1).ToString();
        }
    }

    /// <summary>
    /// How often a state-lane string could not be read back because its id resolved to nothing.
    ///
    /// Counted rather than logged, for the reason <see cref="LiveFixedStringStats"/> is: it would
    /// otherwise say the same thing on every frame of a take. A climbing count means a recording is
    /// being read against a table that does not have what it names -- a file cut short, or one whose
    /// symbols were never flushed.
    /// </summary>
    public static class LiveTextIdStats
    {
        private static long _unresolvedCount;

        /// <summary>Ids passed over because the table had nothing at them, since the last reset.</summary>
        public static long unresolvedCount => _unresolvedCount;

        /// <summary>Forgets the count. For tests and for the start of a recording.</summary>
        public static void Reset() => _unresolvedCount = 0;

        internal static void CountUnresolved() => _unresolvedCount++;
    }
}
