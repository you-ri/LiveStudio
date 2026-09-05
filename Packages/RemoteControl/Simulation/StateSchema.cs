// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Lilium.RemoteControl.Frames
{
    /// <summary>
    /// One member of a state block, as a recording has to describe it.
    ///
    /// The four fields are what a reader needs to find the value again in a build that lays the
    /// block out differently. <see cref="layout"/> is the fifth thing, and the one that is easy to
    /// leave out: a member whose type is a struct of its own can change on the inside while its
    /// name, its type name and even its size all stay put, and copying it then lands each of its
    /// fields on a neighbour. That is the failure worth refusing rather than reading, so the shape
    /// of the inside travels with the member.
    /// </summary>
    public readonly struct StateSchemaMember
    {
        /// <summary>Member name, as the bridge that carries it spells it.</summary>
        public readonly string name;

        /// <summary>CLR name of the value's type. Nested types are spelled with '+'.</summary>
        public readonly string typeName;

        /// <summary>Byte offset from the start of the payload, past the element metadata.</summary>
        public readonly int offset;

        /// <summary>Bytes the value occupies.</summary>
        public readonly int size;

        /// <summary>
        /// Shape of the value's own fields, or zero when the type name already says everything.
        ///
        /// Zero for the types whose name pins their layout -- the primitives, and the fixed-shape
        /// values this package owns. Non-zero for a struct declared elsewhere, where the name is
        /// stable and the inside is not.
        /// </summary>
        public readonly ulong layout;

        public StateSchemaMember(string name, string typeName, int offset, int size, ulong layout = 0UL)
        {
            this.name = name ?? string.Empty;
            this.typeName = typeName ?? string.Empty;
            this.offset = offset;
            this.size = size;
            this.layout = layout;
        }

        /// <summary>
        /// Whether two descriptions are the same value, so one can be copied into the other.
        ///
        /// The offsets are deliberately not compared: a member that moved is still the same member,
        /// and moving it is the whole reason a plan exists. Everything else has to agree, because
        /// everything else is what makes the bytes mean what they say.
        /// </summary>
        public bool SameValueAs(in StateSchemaMember other)
            => size == other.size
               && layout == other.layout
               && string.Equals(name, other.name, StringComparison.Ordinal)
               && string.Equals(typeName, other.typeName, StringComparison.Ordinal);

        public override string ToString() => name + ":" + typeName + "@" + offset + "+" + size;
    }

    /// <summary>
    /// What one state block holds, member by member.
    ///
    /// A block is read back by position -- the third four bytes are the third member because that is
    /// where the third member was put -- and until this existed a recording said only how wide an
    /// element was. Width cannot survive a member being added or taken away, so a take made before
    /// the change was refused whole: every member of that type stopped replaying because one of them
    /// had moved. This is the description that lets a reader put back the members both builds have
    /// and leave the rest alone.
    ///
    /// Carried in a recording as a string, through the mapping table that is already there. That is
    /// not a shortcut -- the mapping table is exactly the right shape for this. It is append-only,
    /// it is written into the stream as it grows so a take cut short by a crash still resolves what
    /// it reached, and it is written again in full at the tail so a seek can adopt it whole. A
    /// second table beside it would have to reproduce all three properties to say the same thing.
    /// </summary>
    public sealed class StateSchema
    {
        /// <summary>Marks the text form, so a later shape can be told from this one.</summary>
        private const string kVersion = "s1";

        /// <summary>Bytes before the payload: owner, producer, stamp.</summary>
        public readonly int metaSize;

        /// <summary>Bytes one whole element occupies, metadata included.</summary>
        public readonly int stride;

        /// <summary>The members, in the order they sit in the payload.</summary>
        public readonly StateSchemaMember[] members;

        // The text form, built once. A schema is interned by its text every frame it is written, so
        // rebuilding the string per frame would be the one allocation this design adds to the path
        // that runs sixty times a second.
        private string _text;

        public StateSchema(int metaSize, int stride, StateSchemaMember[] members)
        {
            this.metaSize = metaSize;
            this.stride = stride;
            this.members = members ?? Array.Empty<StateSchemaMember>();
        }

        /// <summary>Bytes of value one element carries, excluding the metadata.</summary>
        public int payloadSize => stride - metaSize;

        /// <summary>The member of a given name, or -1.</summary>
        public int IndexOf(string name)
        {
            for (int i = 0; i < members.Length; i++)
            {
                if (string.Equals(members[i].name, name, StringComparison.Ordinal)) return i;
            }

            return -1;
        }

        /// <summary>
        /// The text a recording carries this as.
        ///
        /// Deterministic, so two runs of the same build intern the same string and the mapping table
        /// gains one entry per type rather than one per frame.
        /// </summary>
        public string ToText()
        {
            if (_text != null) return _text;

            var sb = new StringBuilder(64 + members.Length * 32);
            sb.Append(kVersion).Append(';')
              .Append(metaSize.ToString(CultureInfo.InvariantCulture)).Append(';')
              .Append(stride.ToString(CultureInfo.InvariantCulture));

            for (int i = 0; i < members.Length; i++)
            {
                var member = members[i];
                sb.Append(';')
                  .Append(member.name).Append(':')
                  .Append(member.typeName).Append(':')
                  .Append(member.offset.ToString(CultureInfo.InvariantCulture)).Append(':')
                  .Append(member.size.ToString(CultureInfo.InvariantCulture));

                // Left off when it says nothing, so an ordinary member costs no characters for it.
                if (member.layout != 0UL)
                {
                    sb.Append(':').Append(member.layout.ToString("x", CultureInfo.InvariantCulture));
                }
            }

            _text = sb.ToString();
            return _text;
        }

        /// <summary>
        /// Reads a schema back, or null when the text is not one.
        ///
        /// Null rather than a throw: the text comes out of the mapping table, where every other
        /// string is an address or a type name, and asking "is this a schema" is how a reader tells
        /// them apart.
        /// </summary>
        public static StateSchema TryParse(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            if (!text.StartsWith(kVersion + ";", StringComparison.Ordinal)) return null;

            var parts = text.Split(';');
            if (parts.Length < 3) return null;

            if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var metaSize))
            {
                return null;
            }

            if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var stride))
            {
                return null;
            }

            var members = new StateSchemaMember[parts.Length - 3];
            for (int i = 3; i < parts.Length; i++)
            {
                var fields = parts[i].Split(':');
                if (fields.Length < 4) return null;

                if (!int.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var offset))
                {
                    return null;
                }

                if (!int.TryParse(fields[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var size))
                {
                    return null;
                }

                var layout = 0UL;
                if (fields.Length > 4
                    && !ulong.TryParse(fields[4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out layout))
                {
                    return null;
                }

                members[i - 3] = new StateSchemaMember(fields[0], fields[1], offset, size, layout);
            }

            return new StateSchema(metaSize, stride, members) { _text = text };
        }

        public override string ToString() => ToText();
    }

    /// <summary>
    /// What each state block holds, by the name a recording calls the block.
    ///
    /// Offered by the generator's module initializer for a type declared in code, and by the
    /// declaration reader for one declared by an asset. A type nobody described has no entry, which
    /// means "read it the way it was read before" -- a producer that hand-registers a struct keeps
    /// working and gains nothing, exactly as it did when the width was the only check.
    /// </summary>
    public static class StateSchemaRegistry
    {
        private static readonly Dictionary<string, StateSchema> _byTypeName =
            new Dictionary<string, StateSchema>(StringComparer.Ordinal);

        /// <summary>Describes a type's block. Re-declaring replaces, for a declaration that moved.</summary>
        public static void Declare(string typeName, StateSchema schema)
        {
            if (string.IsNullOrEmpty(typeName) || schema == null) return;

            _byTypeName[typeName] = schema;
        }

        /// <summary>The description this build has for a type, or null when none was offered.</summary>
        public static StateSchema Find(string typeName)
            => string.IsNullOrEmpty(typeName) || !_byTypeName.TryGetValue(typeName, out var schema)
                ? null
                : schema;

        /// <summary>
        /// Takes a type's description away, for a declaration that stopped declaring.
        ///
        /// ⚠ There is deliberately no "drop everything". The generated descriptions are offered
        /// once, by a module initializer at load, and nothing offers them again -- so emptying this
        /// table takes every type in the build off the lane's description for the rest of the
        /// process, with the symptom appearing wherever the next recording is read rather than
        /// where it was emptied. A test that declares a description takes back that one.
        /// </summary>
        public static void Remove(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return;

            _byTypeName.Remove(typeName);
        }
    }
}
