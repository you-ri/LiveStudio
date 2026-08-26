// Copyright (c) You-Ri, 2026
using System;
using Lilium.RemoteControl.Frames;

namespace Lilium.RemoteControl
{
    /// <summary>
    /// Declares a source of input to the deterministic frame. Applied at assembly level and
    /// collected at start-up, the same way an external enum is declared.
    ///
    /// The declaration is static on purpose: the set of sources has to be settled by the time a
    /// recording starts, so it can be written into the file header and a replay can tell what is
    /// missing. Registering on first use instead would leave a source that has not initialised yet
    /// out of that list.
    ///
    /// Two things follow from having sources be real rather than self-reported strings: a
    /// misspelling fails when it is resolved instead of quietly becoming a second source, and the
    /// symbol table cannot grow without bound.
    /// </summary>
    /// <example>
    /// [assembly: FrameSource("rest")]
    /// </example>
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class FrameSourceAttribute : Attribute
    {
        /// <summary>Wire name of the source. Interned once and referred to by id afterwards.</summary>
        public string name { get; }

        public FrameSourceAttribute(string name)
        {
            this.name = name;
        }
    }

    /// <summary>
    /// A resolved input source: the interned id of a declared source name.
    ///
    /// Held rather than the string so submitting an input costs no hashing, and so the id in a
    /// record can be traced back to something that was declared. Ids of declared sources are
    /// assigned in a fixed order every time the gate resets, which is what makes it safe to resolve
    /// one into a static field and keep it across a domain reload.
    /// </summary>
    public readonly struct FrameSource : IEquatable<FrameSource>
    {
        /// <summary>Where an input whose source was never declared is filed.</summary>
        public const string kUnknown = "unknown";

        /// <summary>
        /// The interned id, offset by one. Stored offset so that <c>default(FrameSource)</c> is
        /// distinguishable: a plain id of zero is a real source (unknown is interned first), so an
        /// unresolved handle would otherwise look valid and file its inputs under it.
        /// </summary>
        private readonly int _idPlusOne;

        internal FrameSource(int id)
        {
            _idPlusOne = id == InputSymbolTable.kNone ? 0 : id + 1;
        }

        internal int id => _idPlusOne - 1;

        /// <summary>False for the default value, which was never resolved against a declaration.</summary>
        public bool isValid => _idPlusOne != 0;

        public bool Equals(FrameSource other) => _idPlusOne == other._idPlusOne;

        public override bool Equals(object obj) => obj is FrameSource other && Equals(other);

        public override int GetHashCode() => _idPlusOne;

        public static bool operator ==(FrameSource a, FrameSource b) => a._idPlusOne == b._idPlusOne;

        public static bool operator !=(FrameSource a, FrameSource b) => a._idPlusOne != b._idPlusOne;

        public override string ToString() => isValid ? $"source:{id}" : "source:unresolved";
    }
}
