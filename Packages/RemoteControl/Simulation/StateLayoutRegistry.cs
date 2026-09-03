// Copyright (c) You-Ri, 2026
using System.Collections.Generic;

namespace Lilium.RemoteControl.Frames
{
    /// <summary>
    /// What each state block's layout is, as a number a recording can carry.
    ///
    /// A block is read back by position: the third four bytes are the third member because that is
    /// where the third member was put. The width of an element is the only thing a recording has
    /// been checking, and width does not say what is inside -- swap two floats in a declaration and
    /// every element still measures the same, so the bytes land in the wrong members and look like
    /// values. Reading a take that way is worse than refusing it, because nothing says it happened.
    ///
    /// So the layout gets a name of its own: a hash of the members, their types and their order,
    /// fixed at compile time by the generator and written beside the width. The declared path has
    /// had this since it was built (its declaration can change while the application runs, which
    /// made the danger obvious); the generated path had only the width.
    ///
    /// A type nobody declared a layout for hashes to zero, which means "do not check" rather than
    /// "does not match" -- a producer that hand-registers a struct (motion capture poses) keeps
    /// working, and gains nothing.
    /// </summary>
    public static class StateLayoutRegistry
    {
        private static readonly Dictionary<string, ulong> _byTypeName = new Dictionary<string, ulong>();

        /// <summary>Offered by the generator's module initializer, before anything runs.</summary>
        public static void Declare(string typeName, ulong layoutHash)
        {
            if (string.IsNullOrEmpty(typeName)) return;

            _byTypeName[typeName] = layoutHash;
        }

        /// <summary>The layout this build has for a type, or zero when none was declared.</summary>
        public static ulong HashFor(string typeName)
            => string.IsNullOrEmpty(typeName) || !_byTypeName.TryGetValue(typeName, out var hash) ? 0UL : hash;

        /// <summary>
        /// Whether a recording's layout can be read as this build's.
        ///
        /// Either side being silent about its layout is a pass. That is the honest answer rather
        /// than a lenient one: a zero means no claim was made, and refusing on the strength of an
        /// absent claim would break every hand-registered producer to catch nothing.
        /// </summary>
        public static bool Matches(string typeName, ulong recorded)
        {
            if (recorded == 0UL) return true;

            var mine = HashFor(typeName);
            return mine == 0UL || mine == recorded;
        }

        /// <summary>Drops every declaration. For tests.</summary>
        internal static void Clear() => _byTypeName.Clear();
    }
}
