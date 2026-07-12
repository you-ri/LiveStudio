// Copyright (c) You-Ri, 2026

using UnityEngine;

namespace Lilium.LiveStudio.Virgo
{
    /// <summary>
    /// Runs inside the Fusion child process and listens for a PID-keyed graceful-quit signal from
    /// Studio (see <see cref="ChildProcessQuitSignal"/>), invoking <c>Application.Quit()</c> so
    /// Fusion's normal save-on-quit path runs instead of being hard-killed. The "I am the Fusion
    /// child" gate is the <see cref="FusionApp.kFusionChildArgument"/> launch-arg marker that
    /// FusionApp always appends, with batch mode kept as a fallback for older headless (Server
    /// subtarget) Fusion builds that were launched without the marker. An interactive Studio
    /// process has neither and skips this. When this does not run, shutdown simply falls back to
    /// the existing Kill path (no data loss beyond what a kill already implied).
    /// </summary>
    public static class FusionQuitSignalListener
    {
        static bool _initialized;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void _Initialize()
        {
            if (_initialized) return;
            if (!Application.isBatchMode && !_HasFusionChildArgument()) return;
            _initialized = true;

            ChildProcessQuitSignal.StartListening(() => Application.Quit());
        }

        static bool _HasFusionChildArgument()
        {
            foreach (var arg in System.Environment.GetCommandLineArgs())
            {
                if (arg == FusionApp.kFusionChildArgument) return true;
            }
            return false;
        }

        // Reset statics in case Domain Reload is disabled.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void _ResetStatics()
        {
            _initialized = false;
        }
    }
}
