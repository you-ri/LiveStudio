// Copyright (c) You-Ri, 2026

using System.Diagnostics;
using UnityEngine;

namespace Lilium.LiveStudio.Virgo
{
    /// <summary>
    /// Owns the VirgoMotionFusion child process for the entire lifetime of the
    /// application (or Editor Play Mode session). Reads launch options from
    /// <see cref="LiveStudioVirgoProjectSettings"/> and ties Start/Stop to
    /// <c>RuntimeInitializeOnLoadMethod</c> + <c>Application.quitting</c> instead
    /// of any in-scene component lifecycle.
    /// </summary>
    public static class FusionAppHost
    {
        static Process _process;
        static bool _initialized;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void _Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            var settings = LiveStudioVirgoProjectSettings.Instance;
            if (settings == null)
            {
                UnityEngine.Debug.LogWarning("[Studio] LiveStudioVirgoProjectSettings is not assigned. Fusion app will not be launched.");
                return;
            }
            if (!settings.launchFusionOnStartup)
            {
                return;
            }

            var fullPath = ToolAppLauncher.ResolveToolApplicationPath(
                settings.fusionPathType,
                settings.fusionApplicationPath,
                settings.fusionPackageName);

            // The default Windows inbound policy blocks LAN capture UDP, and Fusion is launched
            // with -batchmode (no UI), so no allow dialog would ever appear. Register a port-based
            // allow rule up front so capture packets reach Fusion regardless of its install path.
            if (settings.registerFusionFirewallRule)
            {
                WindowsFirewall.EnsureInboundUdpPortsAllowed(
                    settings.fusionCapturePorts, LiveStudioVirgoProjectSettings.kCaptureFirewallRuleName);
            }

            _process = ChildProcessHost.Start(fullPath, settings.fusionArguments, settings.fusionHideWindow);
            if (_process == null) return;

            Application.quitting += _Stop;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.playModeStateChanged += _OnPlayModeStateChanged;
#endif
        }

        static void _Stop()
        {
            if (_process == null) return;

            // Fusion runs with -batchmode -nographics (no main window), so just signal it to quit by
            // PID and return immediately. Fusion's own quit listener handles Application.Quit() and
            // save-on-quit; we do not wait for its exit so Studio shuts down without delay.
            int pid = _process.Id;
            ChildProcessHost.RequestStopAndRelease(ref _process, () => ChildProcessQuitSignal.Signal(pid));
        }

#if UNITY_EDITOR
        static void _OnPlayModeStateChanged(UnityEditor.PlayModeStateChange state)
        {
            // Belt-and-braces: Application.quitting also fires when leaving Play Mode,
            // but ChildProcessHost.Stop is idempotent so a second call is safe.
            if (state == UnityEditor.PlayModeStateChange.ExitingPlayMode)
            {
                _Stop();
                UnityEditor.EditorApplication.playModeStateChanged -= _OnPlayModeStateChanged;
            }
        }

        // Reset statics in case Domain Reload is disabled.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void _ResetStatics()
        {
            _process = null;
            _initialized = false;
        }
#endif
    }
}
