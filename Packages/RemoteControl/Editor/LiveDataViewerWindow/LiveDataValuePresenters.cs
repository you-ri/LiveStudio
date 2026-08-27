// Copyright (c) You-Ri, 2026

using System;
using System.Collections.Generic;

namespace Lilium.RemoteControl.Editor.LiveDataViewer
{
    /// <summary>One line of a presented value.</summary>
    public struct LiveDataValueRow
    {
        /// <summary>What it is called. Shown as it is, so it should read as a name, not a path.</summary>
        public string label;

        /// <summary>What it says.</summary>
        public string text;

        /// <summary>Indentation, for grouping.</summary>
        public int depth;

        public LiveDataValueRow(string label, string text, int depth = 0)
        {
            this.label = label;
            this.text = text;
            this.depth = depth;
        }
    }

    /// <summary>
    /// Type-specific readings of a value, for the parts reflection cannot describe.
    ///
    /// A fixed buffer carries its element type and its length and nothing else, so
    /// <c>fixed byte[880]</c> is 880 bytes as far as the type system knows -- the fact that it is 55
    /// quaternions in bone order lives in the code that reads it. The viewer cannot work that out,
    /// so whoever owns the type says it here.
    ///
    /// Registered from the editor assembly of whatever package owns the type. The viewer must not
    /// reach the other way: it sits under everything and cannot know about the packages built on it.
    /// </summary>
    public static class LiveDataValuePresenters
    {
        /// <summary>
        /// Turns the bytes of one value into readable lines.
        ///
        /// The bytes are the value alone, without the element's owner and stamp, laid out exactly as
        /// the type is. They may be shorter than the type expects when the recording came from a
        /// build whose layout has moved, so a presenter checks before it reads.
        /// </summary>
        public delegate void PresentDelegate(byte[] value, int length, List<LiveDataValueRow> rows);

        private static readonly Dictionary<Type, PresentDelegate> _presenters =
            new Dictionary<Type, PresentDelegate>();

        /// <summary>Types with a reading of their own.</summary>
        public static IReadOnlyCollection<Type> knownTypes => _presenters.Keys;

        /// <summary>
        /// Says how to read a type. The last registration wins, so a project can override what a
        /// package provides.
        /// </summary>
        public static void Register(Type type, PresentDelegate present)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            if (present == null) throw new ArgumentNullException(nameof(present));

            _presenters[type] = present;
        }

        /// <summary>Forgets a registration. Mostly for tests.</summary>
        public static void Unregister(Type type)
        {
            if (type == null) return;
            _presenters.Remove(type);
        }

        /// <summary>The reading for a type, or null to fall back to the generic walk.</summary>
        public static PresentDelegate Find(Type type)
            => type != null && _presenters.TryGetValue(type, out var present) ? present : null;
    }
}
