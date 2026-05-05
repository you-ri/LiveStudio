// Copyright (c) You-Ri, 2026
// Consolidated from Lilium.Virgo.CompilerUtility / Lilium.LiveStudio.CompilerUtility into Lilium.RemoteControl.

using UnityEngine;

namespace Lilium.RemoteControl
{
    /// <summary>
    /// Compile-time and runtime checks for unmanaged / blittable / fixed-size struct contracts.
    /// </summary>
    public static class CompilerUtility
    {
        static CompilerUtility()
        {
            CheckSize<Quaternion>(QuaternionSize);
            CheckSize<Vector3>(Vector3Size);
            CheckSize<Vector2>(Vector2Size);
        }

        public const int QuaternionSize = 16; // float x 4
        public const int Vector3Size = 12;    // float x 3
        public const int Vector2Size = 8;     // float x 2

        /// <summary>
        /// Compile-time guarantee that <typeparamref name="T"/> is unmanaged.
        /// </summary>
        public static void CheckUnmanaged<T>() where T : unmanaged { }

        /// <summary>
        /// Runtime guarantee that <typeparamref name="T"/> is blittable. Throws if not.
        /// </summary>
        public static void CheckBlittable<T>() where T : unmanaged
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!Unity.Collections.LowLevel.Unsafe.UnsafeUtility.IsBlittable<T>())
            {
                throw new System.InvalidOperationException($"[RemoteControl] {typeof(T).Name} is not blittable.");
            }
#endif
        }

        /// <summary>
        /// Runtime guarantee that <c>sizeof(T)</c> matches the expected constant. Throws if not.
        /// </summary>
        public static unsafe void CheckSize<T>(int expectedSize) where T : unmanaged
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            int actualSize = sizeof(T);
            if (actualSize != expectedSize)
            {
                throw new System.InvalidOperationException($"[RemoteControl] {typeof(T).Name}.Size constant ({expectedSize}) does not match actual sizeof({actualSize})");
            }
#endif
        }
    }
}
