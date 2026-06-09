// Copyright (c) You-Ri, 2026

using UnityEngine;

using Lilium.LiveStudio;
using Lilium.RemoteControl;

namespace Lilium.LiveStudio.Virgo
{
    /// <summary>
    /// Owns the bundled VirgoMotionFusion child process and ties its lifetime to this
    /// component's lifecycle. The process is started in <see cref="OnEnable"/> and stopped
    /// in <see cref="OnDisable"/> / <see cref="OnDestroy"/>, so enabling/disabling the
    /// component (or its GameObject) directly controls whether Fusion runs.
    ///
    /// This replaces the former static <c>FusionAppHost</c> +
    /// <c>RuntimeInitializeOnLoadMethod</c> approach: a MonoBehaviour receives
    /// OnDisable/OnDestroy on Play Mode exit and on application quit, so no
    /// <c>playModeStateChanged</c> / <c>Application.quitting</c> wiring and no static
    /// reset for disabled Domain Reload are needed.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [ExposedClass("FusionApp", Icon = "memory", Category = "Motion")]
    public class FusionApp : MonoBehaviour
    {
        /// <summary>Name of the inbound firewall rule that allows the capture UDP ports.</summary>
        public const string kCaptureFirewallRuleName = "VirgoMotion Capture (UDP)";

        /// <summary>Default netsh localport value for the capture inbound firewall rule.</summary>
        public const string kDefaultCapturePorts = "9005-9010";

        [SerializeField] PathType _fusionPathType = PathType.PackageRelative;

        [Tooltip("Application path relative to the resolved root (Tools~ folder for PackageRelative, Tools/ for ProjectRelative).")]
        [SerializeField] string _fusionApplicationPath = "VirgoMotionFusion/VirgoMotionFusion.exe";

        [Tooltip("Package name used to resolve Tools~ when pathType is PackageRelative.")]
        [SerializeField] string _fusionPackageName = "jp.lilium.livestudio.virgo";

        [SerializeField] string _fusionArguments = "-batchmode -nographics";

        [SerializeField] bool _fusionHideWindow = true;

        [Tooltip("If enabled, an inbound UDP allow rule for the capture ports is registered (one-time UAC prompt) before Fusion launches. The default inbound firewall policy blocks LAN capture packets otherwise.")]
        [SerializeField] bool _registerFusionFirewallRule = true;

        [Tooltip("netsh localport value for the capture inbound firewall rule. Covers capture channels (9005/9006) and camera frames (9007).")]
        [SerializeField] string _fusionCapturePorts = kDefaultCapturePorts;

        System.Diagnostics.Process _process;

        /// <summary>True while the Fusion child process is running.</summary>
        public bool IsRunning => _process != null && !_process.HasExited;

        void OnEnable() => StartFusion();

        void OnDisable() => StopFusion();

        // Idempotent: RequestStopAndRelease returns immediately when _process is null,
        // so a second call after OnDisable is harmless.
        void OnDestroy() => StopFusion();

        /// <summary>Resolve the launch options and start the Fusion child process.</summary>
        [ExposedFunction]
        public void StartFusion()
        {
            if (IsRunning) return;

            var fullPath = ToolAppLauncher.ResolveToolApplicationPath(
                _fusionPathType, _fusionApplicationPath, _fusionPackageName);

            // The default Windows inbound policy blocks LAN capture UDP, and Fusion is launched
            // with -batchmode (no UI), so no allow dialog would ever appear. Register a port-based
            // allow rule up front so capture packets reach Fusion regardless of its install path.
            if (_registerFusionFirewallRule)
            {
                WindowsFirewall.EnsureInboundUdpPortsAllowed(_fusionCapturePorts, kCaptureFirewallRuleName);
            }

            _process = ChildProcessHost.Start(fullPath, _fusionArguments, _fusionHideWindow);
        }

        /// <summary>Stop the Fusion child process and release the handle.</summary>
        [ExposedFunction]
        public void StopFusion()
        {
            if (_process == null) return;

            // Fusion runs windowless with -batchmode -nographics: with no main window it cannot
            // receive WM_CLOSE, so termination goes through a PID-keyed named-event signal. When the
            // signal reaches a listening Fusion, its quit listener (FusionQuitSignalListener) runs
            // save-on-quit + Application.Quit() and exits on its own (QuitTerminationGuard inside
            // Fusion bounds this to ~5s even if native teardown wedges), so we release the handle
            // WITHOUT waiting and application quit / Play Mode exit is never delayed.
            if (ChildProcessQuitSignal.Signal(_process.Id))
            {
                ChildProcessHost.RequestStopAndRelease(ref _process, null);
            }
            else
            {
                // No listener was reachable: Fusion is still booting (its event isn't created yet) or
                // is an orphan from a previous run. The graceful signal was dropped and nothing will
                // quit it, so hard-kill now to avoid a lingering process. Kill does not block.
                ChildProcessHost.Kill(ref _process);
            }
        }
    }
}
