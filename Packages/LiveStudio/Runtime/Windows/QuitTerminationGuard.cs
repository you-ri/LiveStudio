// Copyright (c) You-Ri, 2026

using UnityEngine;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Watchdog that guarantees the Windows player process actually exits.
    ///
    /// Unity's native shutdown (PlayerCleanup) can deadlock on the main thread while tearing
    /// down the Windows.Gaming.Input backend: the WGI device-watcher thread blocks the loader
    /// lock and the process lingers forever, gone from the tray but still in Task Manager.
    /// The hang is intermittent (it depends on the WGI thread's state at exit), which is why
    /// the process "sometimes" fails to terminate.
    ///
    /// When the app starts quitting we arm a background thread. A normal exit completes and
    /// kills this thread along with the process before the grace period elapses, so healthy
    /// shutdowns are untouched. Only if the process is still alive after the grace period --
    /// i.e. native teardown is wedged -- do we force-terminate. <c>TerminateProcess</c> is used
    /// (not <c>ExitProcess</c>) because it skips <c>DLL_PROCESS_DETACH</c> entirely, which is the
    /// exact path the WGI loader-lock deadlock lives in.
    ///
    /// Editor-excluded: in the Editor this would kill the Editor itself when leaving Play Mode.
    /// </summary>
    public static class QuitTerminationGuard
    {
#if !UNITY_EDITOR && UNITY_STANDALONE_WIN
        // Native teardown normally completes in well under a second; 5s leaves generous room for
        // slow disk flushes while still bounding how long a wedged process can linger.
        private const int kGracePeriodMs = 5000;

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool TerminateProcess(System.IntPtr hProcess, uint exitCode);

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern System.IntPtr GetCurrentProcess();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            Application.quitting += _OnQuitting;
        }

        private static void _OnQuitting()
        {
            // Arm after every other quitting handler has run (this is the last managed callback
            // before native teardown, so saves and graceful cleanup are already done).
            var thread = new System.Threading.Thread(_WatchdogLoop)
            {
                IsBackground = true,
                Name = "QuitTerminationGuard",
            };
            thread.Start();
        }

        private static void _WatchdogLoop()
        {
            System.Threading.Thread.Sleep(kGracePeriodMs);

            // Reaching here means the process outlived a normal shutdown: native teardown is
            // wedged (known Windows.Gaming.Input exit hang). Force-terminate so it never lingers.
            Debug.LogWarning("[Studio] Quit watchdog fired: native shutdown is wedged, forcing process termination.");
            TerminateProcess(GetCurrentProcess(), 0);
        }
#endif
    }
}
