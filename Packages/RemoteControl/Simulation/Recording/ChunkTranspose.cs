// Copyright (c) You-Ri, 2026
using Unity.Burst;

namespace Lilium.RemoteControl.Frames.Recording
{
    /// <summary>
    /// The byte transposition at the heart of the chunk codec, compiled by Burst.
    ///
    /// Byte <c>i</c> of a frame's elements goes to <c>plane[i * frames]</c>, so the same byte of the
    /// same element sits next to itself for every frame in the chunk -- which is what lets deflate
    /// find the repeats. Pure pointer arithmetic over bytes, with nothing managed in reach, which
    /// is the one shape of work in a recording Burst can take whole.
    ///
    /// Public only because Burst calls into static methods it can see; nothing outside the codec
    /// has a reason to use it.
    /// </summary>
    [BurstCompile]
    public static unsafe class ChunkTranspose
    {
        /// <summary>Spreads one frame's elements across their planes.</summary>
        [BurstCompile]
        public static void Scatter(byte* elements, int count, int elementSize, byte* plane, int frames)
        {
            var total = count * elementSize;
            for (int i = 0; i < total; i++)
            {
                plane[(long)i * frames] = elements[i];
            }
        }

        /// <summary>Collects one frame's elements back out of their planes.</summary>
        [BurstCompile]
        public static void Gather(byte* plane, int count, int elementSize, int frames, byte* elements)
        {
            var total = count * elementSize;
            for (int i = 0; i < total; i++)
            {
                elements[i] = plane[(long)i * frames];
            }
        }
    }
}
