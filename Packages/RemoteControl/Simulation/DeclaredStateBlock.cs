// Copyright (c) You-Ri, 2026
using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

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
    /// Nothing is lost by not being typed here, because the declared path was never typed: values
    /// are marshalled in and out at offsets the declaration computed, and no code ever names a field
    /// of this block.
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
        private readonly int _stride;

        private NativeArray<byte> _storage;
        private int _count;

        /// <param name="ownerType">
        /// The exposed type whose state this holds. It names the block in a recording, which is why
        /// it is the owner rather than some shared placeholder: one block per declared type means
        /// each one is addressed by the type it belongs to, and two declarations cannot land in the
        /// same array.
        /// </param>
        /// <param name="payloadSize">Bytes of declared state one object carries.</param>
        public DeclaredStateBlock(Type ownerType, int payloadSize)
        {
            if (payloadSize < 0) throw new ArgumentOutOfRangeException(nameof(payloadSize));

            _ownerType = ownerType ?? throw new ArgumentNullException(nameof(ownerType));
            _payloadSize = payloadSize;

            _stride = StrideFor(payloadSize);
        }

        public override Type elementType => _ownerType;

        public override int count => _count;

        public override int elementSize => _stride;

        public override int metaSize => kMetaSize;

        /// <summary>Bytes of declared state one object carries, excluding the metadata.</summary>
        public int payloadSize => _payloadSize;

        /// <summary>
        /// The payload of one element, to read or write in place.
        ///
        /// A span into the block's own storage rather than a copy: the caller is a bridge moving a
        /// handful of values per object per frame, and copying the payload out and back would double
        /// that for nothing.
        /// </summary>
        public Span<byte> Payload(int index)
        {
            if ((uint)index >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(index));

            return new Span<byte>((byte*)_storage.GetUnsafePtr() + (long)index * _stride + kMetaSize,
                _payloadSize);
        }

        /// <summary>Index of an owner's element, or -1.</summary>
        public int IndexOf(int ownerId)
        {
            if (!_storage.IsCreated) return -1;

            var bytes = (byte*)_storage.GetUnsafeReadOnlyPtr();
            for (int i = 0; i < _count; i++)
            {
                if (*(int*)(bytes + (long)i * _stride) == ownerId) return i;
            }

            return -1;
        }

        /// <summary>
        /// The element for an owner, appending one if this is the first time it is seen. Returns the
        /// index, which is what the payload and the metadata are then reached through.
        /// </summary>
        public int GetOrCreate(int ownerId)
        {
            var index = IndexOf(ownerId);
            if (index >= 0) return index;

            _EnsureCapacity(_count + 1);
            index = _count++;

            // Cleared explicitly: the storage can be left over from a longer earlier run, and an
            // element inheriting a stranger's value would read as state rather than as garbage.
            var element = (byte*)_storage.GetUnsafePtr() + (long)index * _stride;
            UnsafeUtility.MemClear(element, _stride);
            *(int*)element = ownerId;

            return index;
        }

        /// <summary>Stamps an element with who wrote it and when.</summary>
        public void SetMeta(int index, FrameSource source, long time)
        {
            if ((uint)index >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(index));

            var element = (byte*)_storage.GetUnsafePtr() + (long)index * _stride;

            // Written as the struct rather than as its bits: the handle keeps its id offset by one
            // so that a default reads as unresolved, and reproducing that here by hand would be a
            // second place that has to know.
            *(FrameSource*)(element + 4) = source;
            *(long*)(element + 8) = time;
        }

        /// <summary>
        /// Drops an owner's element. The remaining elements keep their relative order, because the
        /// order is what a recording stores.
        /// </summary>
        public bool Remove(int ownerId)
        {
            var index = IndexOf(ownerId);
            if (index < 0) return false;

            var bytes = (byte*)_storage.GetUnsafePtr();
            UnsafeUtility.MemMove(bytes + (long)index * _stride, bytes + (long)(index + 1) * _stride,
                (long)(_count - index - 1) * _stride);

            _count--;
            return true;
        }

        public override void Reset() => _count = 0;

        public override ReadOnlySpan<byte> AsBytes()
        {
            if (_count == 0 || !_storage.IsCreated) return ReadOnlySpan<byte>.Empty;

            return new ReadOnlySpan<byte>(_storage.GetUnsafeReadOnlyPtr(), _count * _stride);
        }

        public override void ReadFrom(ReadOnlySpan<byte> bytes, int elementCount)
        {
            if (elementCount < 0 || (long)elementCount * _stride > bytes.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(elementCount),
                    $"[RemoteControl] {elementCount} elements of {_stride} bytes do not fit in {bytes.Length}.");
            }

            _EnsureCapacity(elementCount);
            _count = elementCount;

            if (elementCount == 0) return;

            bytes.Slice(0, elementCount * _stride)
                .CopyTo(new Span<byte>(_storage.GetUnsafePtr(), elementCount * _stride));
        }

        public override int OwnerIdAt(int index) => *(int*)_ElementAt(index);

        public override int SourceIdAt(int index)
        {
            var source = *(FrameSource*)(_ElementAt(index) + 4);
            return source.isValid ? source.id : FrameSymbolTable.kNone;
        }

        public override long TimeAt(int index) => *(long*)(_ElementAt(index) + 8);

        public override void CopyValueTo(int index, byte[] destination)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));

            var length = _stride - kMetaSize;
            if (destination.Length < length)
                throw new ArgumentException("Destination is too small for one value.", nameof(destination));

            fixed (byte* target = destination)
            {
                UnsafeUtility.MemCpy(target, _ElementAt(index) + kMetaSize, length);
            }
        }

        public override int IndexOfOwner(int ownerId) => IndexOf(ownerId);

        public override bool ElementEquals(int index, StateBlock other, int otherIndex)
        {
            if (!(other is DeclaredStateBlock typed) || typed._stride != _stride) return false;
            if ((uint)index >= (uint)_count) return false;
            if ((uint)otherIndex >= (uint)typed._count) return false;

            return UnsafeUtility.MemCmp(_ElementAt(index), typed._ElementAt(otherIndex), _stride) == 0;
        }

        public override void Dispose()
        {
            if (_storage.IsCreated) _storage.Dispose();

            _storage = default;
            _count = 0;
        }

        public override string ToString() => $"{_ownerType.Name} x{_count} ({_stride} B each, declared)";

        private byte* _ElementAt(int index)
        {
            if ((uint)index >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(index));

            return (byte*)_storage.GetUnsafeReadOnlyPtr() + (long)index * _stride;
        }

        /// <summary>
        /// Bytes one element occupies for a given payload.
        ///
        /// Rounded up so that every element's 8-byte stamp stays 8-byte aligned. On x86 a misaligned
        /// read is merely slower; on ARM it can fault, and a recording is not the place to find that
        /// out. Public because the padding is part of what a frame costs, and anything quoting that
        /// cost has to quote this number rather than the sum of the values.
        /// </summary>
        public static int StrideFor(int payloadSize) => (kMetaSize + payloadSize + 7) & ~7;

        private void _EnsureCapacity(int required)
        {
            var capacity = _storage.IsCreated ? _storage.Length / _stride : 0;
            if (capacity >= required) return;

            var grown = Math.Max(required, capacity == 0 ? 4 : capacity * 2);
            var replacement = new NativeArray<byte>(grown * _stride, Allocator.Persistent,
                NativeArrayOptions.ClearMemory);

            if (_storage.IsCreated)
            {
                UnsafeUtility.MemCpy(replacement.GetUnsafePtr(), _storage.GetUnsafeReadOnlyPtr(),
                    (long)_count * _stride);

                _storage.Dispose();
            }

            _storage = replacement;
        }
    }
}
