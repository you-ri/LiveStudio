// Copyright (c) You-Ri, 2026
using System.Threading;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;

namespace Lilium.RemoteControl
{
    /// <summary>
    /// Samples lightweight runtime performance metrics on the main thread and caches them so that
    /// worker-thread HTTP handlers (e.g. <see cref="StatusHandler"/>) can read them safely.
    ///
    /// FPS is a rolling one-second average; memory is the process working set (physical RAM in use),
    /// read through <see cref="ProcessMemoryReader"/> because Unity's Mono returns 0 from the managed
    /// Process APIs.
    ///
    /// Sampling is driven by a PlayerLoop hook so it stays alive regardless of the active scene and
    /// never allocates per frame.
    /// </summary>
    public static class PerformanceMonitor
    {
        // Length of the FPS averaging window in seconds.
        private const float kWindowSeconds = 1f;

        // Cached samples. Written on the main thread, read from worker threads. A reader only uses
        // these for a status readout, but we still go through volatile / Interlocked so the reads
        // are well-defined rather than relying on incidental atomicity.
        private static volatile float _fps;
        private static long _memoryBytes;

        // Rolling-window accumulators (main thread only).
        private static int _frameCount;
        private static float _elapsed;

        /// <summary>Most recent one-second average frame rate.</summary>
        public static float fps => _fps;

        /// <summary>Most recent process memory usage (working set) in bytes.</summary>
        public static long memoryBytes => Interlocked.Read(ref _memoryBytes);

        // Dedicated PlayerLoop subsystem type so the hook can be identified unambiguously.
        private struct PerformanceMonitorUpdate { }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void _Initialize()
        {
            // Domain-reload-disabled safe: reset every static on each play start.
            _fps = 0f;
            _memoryBytes = 0;
            _frameCount = 0;
            _elapsed = 0f;

            _InstallPlayerLoopHook();
        }

        private static void _InstallPlayerLoopHook()
        {
            var playerLoop = PlayerLoop.GetCurrentPlayerLoop();

            for (int i = 0; i < playerLoop.subSystemList.Length; i++)
            {
                if (playerLoop.subSystemList[i].type != typeof(Update)) continue;

                var update = playerLoop.subSystemList[i];
                var children = update.subSystemList;

                // Guard against a double install (PlayerLoop edits can survive a disabled domain reload).
                for (int c = 0; c < children.Length; c++)
                {
                    if (children[c].type == typeof(PerformanceMonitorUpdate)) return;
                }

                var appended = new PlayerLoopSystem[children.Length + 1];
                System.Array.Copy(children, appended, children.Length);
                appended[children.Length] = new PlayerLoopSystem
                {
                    type = typeof(PerformanceMonitorUpdate),
                    updateDelegate = _Tick,
                };
                update.subSystemList = appended;
                playerLoop.subSystemList[i] = update;

                PlayerLoop.SetPlayerLoop(playerLoop);
                return;
            }
        }

        private static void _Tick()
        {
            _frameCount++;
            _elapsed += Time.unscaledDeltaTime;

            if (_elapsed < kWindowSeconds) return;

            _fps = _frameCount / _elapsed;
            _frameCount = 0;
            _elapsed = 0f;

            // Sample memory once per window alongside the FPS.
            Interlocked.Exchange(ref _memoryBytes, ProcessMemoryReader.GetWorkingSetBytes());
        }
    }
}
