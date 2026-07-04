// Copyright (c) You-Ri, 2026

using System;
using System.Threading;
using System.Threading.Tasks;

using UnityEngine;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Serializes AssetBundle container opens across the runtime prop / avatar loaders so that no two
    /// of those bundles are open at the same moment.
    ///
    /// LiveStudio prop bundles are built individually (one prefab per bundle) by BundleBuildUtility,
    /// and Unity assigns every such single-content bundle the SAME internal serialized-file id
    /// (<c>CAB-...</c>). Opening two of them concurrently makes Unity reject the second with
    /// "another AssetBundle with the same files is already loaded". The prop and avatar loaders each
    /// free their container immediately after reading it (<c>Unload(false)</c>), so routing the
    /// open→read→unload window through this single lock guarantees the colliding containers never
    /// overlap — bundles that share a CAB can then be loaded one after another without re-exporting.
    ///
    /// Set bundles are intentionally NOT gated: a set keeps its container open for the lifetime of its
    /// additive scene, so it cannot take part in a one-open-at-a-time lock, and it is a streamed scene
    /// bundle whose id space does not collide with the prefab bundles.
    /// </summary>
    internal static class BundleLoadGate
    {
        private static SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

        // Recreate the gate at runtime startup so a leftover count from a previous play session does
        // not survive when domain reload is disabled.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void _Reset() => _gate = new SemaphoreSlim(1, 1);

        /// <summary>
        /// Awaits exclusive access, runs <paramref name="open"/> — a bundle open→read→unload sequence —
        /// and always releases the lock. The value returned by <paramref name="open"/> is passed through.
        /// </summary>
        public static async Task<T> RunExclusiveAsync<T>(Func<Task<T>> open)
        {
            await _gate.WaitAsync();
            try
            {
                return await open();
            }
            finally
            {
                _gate.Release();
            }
        }
    }
}
