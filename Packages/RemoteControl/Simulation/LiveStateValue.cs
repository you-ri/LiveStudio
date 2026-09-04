// Copyright (c) You-Ri, 2026
namespace Lilium.RemoteControl.Frames
{
    /// <summary>
    /// Comparisons the generated state movers make before they write.
    ///
    /// The state lane carries every member on every frame, so applying a recording writes each one
    /// sixty times a second whether or not it moved. Where a member is a plain field that costs a
    /// store and nothing else, the generated mover assigns it outright. Where it travels through a
    /// property it costs whatever that property's setter does -- pairing an input device, reloading
    /// an asset, notifying whatever watches it -- so the mover asks first and writes only what
    /// actually differs.
    /// </summary>
    public static unsafe class LiveStateValue
    {
        /// <summary>
        /// Whether two values of the same shape are the same bytes.
        ///
        /// Bytes rather than <c>Equals</c>, for the reason the declared bridge compares them the
        /// same way: a struct with no equality of its own falls back to a reflective field walk,
        /// which costs more than the write it is meant to avoid. Being wrong is one-sided -- two
        /// values that mean the same thing but differ in padding compare as different and get
        /// written, which is a wasted store; nothing that actually moved can compare as equal.
        /// </summary>
        public static bool SameBytes<T>(T a, T b) where T : unmanaged
        {
            var left = (byte*)&a;
            var right = (byte*)&b;

            for (int i = 0; i < sizeof(T); i++)
            {
                if (left[i] != right[i]) return false;
            }

            return true;
        }
    }
}
