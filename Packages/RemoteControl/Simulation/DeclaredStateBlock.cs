// Copyright (c) You-Ri, 2026
using System;

namespace Lilium.RemoteControl.Frames
{
    /// <summary>
    /// A state block whose element size is decided at load rather than at compile time.
    ///
    /// <see cref="StateBlock{T}"/> is generic over a struct, which is what a type declared in code
    /// gets: the generator writes a struct shaped like that type's state-lane members. A type
    /// declared by an asset has no such struct and nothing to generate one from, so its width is
    /// only known once the declaration has been read.
    ///
    /// The obvious answer -- emit a struct at load and instantiate the generic block over it -- does
    /// not survive AOT, where there is no runtime codegen at all. This is the answer that does: the
    /// elements are bytes, and the stride is a number. Everything the lane asks of a block (a dense
    /// array, a fixed offset per element, a byte view to record, a write-back to apply) holds for a
    /// stride that was decided at load exactly as well as for one decided at compile time.
    ///
    /// Element layout, matching <see cref="StateElement{T}"/> so a reader needs no special case:
    /// <code>
    /// [ownerId : 4][source : 4][time : 8][payload : payloadSize]
    /// </code>
    /// </summary>
    public sealed unsafe class DeclaredStateBlock : StateBlock
    {
        /// <summary>Bytes before the payload: owner, producer, stamp. As <see cref="StateElement{T}"/>.</summary>
        public const int kMetaSize = 16;

        private readonly Type _ownerType;
        private readonly int _payloadSize;

        /// <param name="ownerType">
        /// The exposed type whose state this holds. It names the block in a recording, which is why
        /// it is the owner rather than some shared placeholder: one block per declared type means
        /// each one is addressed by the type it belongs to, and two declarations cannot land in the
        /// same array.
        /// </param>
        /// <param name="payloadSize">Bytes of declared state one object carries.</param>
        public DeclaredStateBlock(Type ownerType, int payloadSize) : base(StrideFor(payloadSize), kMetaSize)
        {
            _ownerType = ownerType ?? throw new ArgumentNullException(nameof(ownerType));
            _payloadSize = payloadSize;
        }

        public override Type elementType => _ownerType;

        public override string typeName => _ownerType.FullName;

        /// <summary>Bytes of declared state one object carries, excluding the metadata.</summary>
        public int payloadSize => _payloadSize;

        /// <summary>
        /// The declaration this block was made for, as a recording interns it, or null when nothing
        /// has said. Told apart from the width because two declarations of the same size lay their
        /// members out differently. See <see cref="StateBlockSet.GetOrCreateDeclared"/>.
        /// </summary>
        public string schemaSignature { get; set; }

        /// <summary>
        /// The payload of one element, to read or write in place.
        ///
        /// A span into the block's own storage rather than a copy: the caller is a bridge moving a
        /// handful of values per object per frame, and copying the payload out and back would double
        /// that for nothing.
        /// </summary>
        public Span<byte> Payload(int index) => new Span<byte>(ElementAt(index) + kMetaSize, _payloadSize);

        /// <summary>Index of an owner's element, or -1.</summary>
        public int IndexOf(int ownerId) => IndexOfOwner(ownerId);

        /// <summary>
        /// The element for an owner, appending one if this is the first time it is seen. Returns the
        /// index, which is what the payload and the metadata are then reached through.
        /// </summary>
        public int GetOrCreate(int ownerId) => GetOrCreateIndex(ownerId);

        /// <summary>Stamps an element with who wrote it and when.</summary>
        public void SetMeta(int index, FrameSource source, long time) => WriteMeta(index, source, time);

        public override string ToString() => $"{_ownerType.Name} x{count} ({elementSize} B each, declared)";

        /// <summary>
        /// Bytes one element occupies for a given payload.
        ///
        /// Rounded up so that every element's 8-byte stamp stays 8-byte aligned. On x86 a misaligned
        /// read is merely slower; on ARM it can fault, and a recording is not the place to find that
        /// out. Public because the padding is part of what a frame costs, and anything quoting that
        /// cost has to quote this number rather than the sum of the values.
        /// </summary>
        public static int StrideFor(int payloadSize)
        {
            if (payloadSize < 0) throw new ArgumentOutOfRangeException(nameof(payloadSize));

            return (kMetaSize + payloadSize + 7) & ~7;
        }
    }
}
