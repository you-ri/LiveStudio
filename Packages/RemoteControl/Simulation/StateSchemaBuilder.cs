// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;
using Unity.Collections.LowLevel.Unsafe;

namespace Lilium.RemoteControl.Frames
{
    /// <summary>
    /// What the generator says about one member, which is only what it alone knows.
    ///
    /// The name, and the shape of the member's own fields where its type name does not pin them.
    /// Everything else -- where the member sits, how wide it is, what its type is called -- is read
    /// off the block struct at load, because the struct is the thing that decides those and asking
    /// it cannot disagree with it.
    /// </summary>
    public readonly struct StateSchemaMemberSpec
    {
        public readonly string name;
        public readonly ulong layout;

        public StateSchemaMemberSpec(string name, ulong layout = 0UL)
        {
            this.name = name;
            this.layout = layout;
        }
    }

    /// <summary>
    /// Builds a <see cref="StateSchema"/> from a block struct.
    ///
    /// Called once per type from the module initializer the generator emits, so the reflection here
    /// is paid at load and never on the path that runs every frame.
    ///
    /// The generator could have computed the offsets itself -- it knows the members and their order
    /// -- but only by emitting pointer arithmetic, and generated code lands in the author's own
    /// assembly. Plenty of those do not allow unsafe code, and the failure would arrive as a
    /// compiler error on a line nobody wrote. Asking the runtime costs one reflection walk per type
    /// at startup and nothing after.
    /// </summary>
    public static class StateSchemaBuilder
    {
        /// <summary>
        /// Describes <typeparamref name="TBlock"/> as it will sit in a frame.
        ///
        /// <paramref name="specs"/> is in declaration order, which is also the order the mask that
        /// travels with a read speaks in -- bit <c>i</c> is the <c>i</c>th spec. The generated
        /// mover counts its members the same way, and the two must not drift apart.
        /// </summary>
        public static StateSchema For<TBlock>(StateSchemaMemberSpec[] specs) where TBlock : unmanaged
        {
            var blockType = typeof(TBlock);
            var elementType = typeof(StateElement<TBlock>);

            // Where the value begins, rather than a constant sixteen. The metadata is three fields
            // of a struct and the compiler places the value after them under its own alignment
            // rules, which a block of a wider member can push further out.
            var valueField = elementType.GetField(nameof(StateElement<TBlock>.value));
            var metaSize = valueField != null ? UnsafeUtility.GetFieldOffset(valueField) : 0;
            var stride = UnsafeUtility.SizeOf(elementType);

            var members = new List<StateSchemaMember>(specs?.Length ?? 0);
            if (specs != null)
            {
                for (int i = 0; i < specs.Length; i++)
                {
                    var field = blockType.GetField(specs[i].name);

                    // Absent means the generator and this build disagree about the block, which is
                    // a bug rather than a recording problem. Skipped rather than thrown so that one
                    // broken type does not stop every other type from announcing itself at load.
                    if (field == null) continue;

                    members.Add(new StateSchemaMember(
                        specs[i].name,
                        field.FieldType.FullName,
                        UnsafeUtility.GetFieldOffset(field),
                        UnsafeUtility.SizeOf(field.FieldType),
                        specs[i].layout));
                }
            }

            return new StateSchema(metaSize, stride, members.ToArray());
        }

        /// <summary>
        /// Describes a block whose members were decided at load rather than compiled, laying its
        /// payload out at the offsets the declaration computed.
        /// </summary>
        public static StateSchema ForDeclared(int metaSize, int stride, StateSchemaMember[] members)
            => new StateSchema(metaSize, stride, members ?? Array.Empty<StateSchemaMember>());
    }
}
