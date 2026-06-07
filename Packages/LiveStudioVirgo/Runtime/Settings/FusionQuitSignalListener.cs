// Copyright (c) You-Ri, 2026

using UnityEngine;

namespace Lilium.LiveStudio.Virgo
{
    /// <summary>
    /// Runs inside the Fusion child process and listens for a PID-keyed graceful-quit signal from
    /// Studio (see <see cref="ChildProcessQuitSignal"/>), invoking <c>Application.Quit()</c> so
    /// Fusion's normal save-on-quit path runs instead of being hard-killed. Fusion launches with
    /// -batchmode (FusionApp), so the batch-mode check is used as the "I am the Fusion child"
    /// gate; an interactive Studio process is not batch mode and skips this. The alternative would
    /// be a dedicated launch-arg marker, but batch mode is sufficient and needs no extra plumbing.
    /// When this does not run, shutdown simply falls back to the existing Kill path (no data loss
    /// beyond what a kill already implied).
    /// </summary>
    public static class FusionQuitSignalListener
    {
        static bool _initialized;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void _Initialize()
        {
            if (_initialized) return;
            if (!Application.isBatchMode) return;
            _initialized = true;

            ChildProcessQuitSignal.StartListening(() => Application.Quit());
        }

        // Reset statics in case Domain Reload is disabled.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void _ResetStatics()
        {
            _initialized = false;
        }
    }
}
