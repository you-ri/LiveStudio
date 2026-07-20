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

        /// <summary>
        /// Launch-arg marker always appended to the Fusion child process. FusionQuitSignalListener
        /// uses it as the "I am the Fusion child" gate; batch mode alone no longer identifies Fusion
        /// now that it ships as a Player (windowed) build instead of a Dedicated Server build.
        /// </summary>
        public const string kFusionChildArgument = "-fusion-child";

        /// <summary>
        /// Launch-arg asking the Fusion Player build to hide its own window on startup (tray-only).
        /// ProcessWindowStyle.Hidden is only a STARTUPINFO hint that the Unity Player may ignore,
        /// so Fusion also self-hides when it sees this argument. Fusion now starts hidden by
        /// default, so this only restates that default and is kept for explicitness.
        /// </summary>
        public const string kHideWindowArgument = "-hidewindow";

        /// <summary>
        /// Launch-arg asking the Fusion Player build to keep its window visible on startup.
        /// Fusion runs as a tray-resident process and starts hidden by default, so this
        /// argument is required to override that default.
        /// </summary>
        public const string kShowWindowArgument = "-showwindow";

        [SerializeField] PathType _fusionPathType = PathType.PackageRelative;

        [Tooltip("Application path relative to the resolved root (Tools~ folder for PackageRelative, Tools/ for ProjectRelative).")]
        [SerializeField] string _fusionApplicationPath = "VirgoMotionFusion/VirgoMotionFusion.exe";

        [Tooltip("Package name used to resolve Tools~ when pathType is PackageRelative.")]
        [SerializeField] string _fusionPackageName = "jp.lilium.livestudio.virgo";

        [SerializeField] string _fusionArguments = "";

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
            // with a hidden window, so no allow dialog would ever appear. Register a port-based
            // allow rule up front so capture packets reach Fusion regardless of its install path.
            if (_registerFusionFirewallRule)
            {
                WindowsFirewall.EnsureInboundUdpPortsAllowed(_fusionCapturePorts, kCaptureFirewallRuleName);
            }

            // Always mark the child so FusionQuitSignalListener activates regardless of batch mode
            // (the Player build is not batch mode). Fusion starts hidden by default, so the window
            // preference is always passed explicitly: without -showwindow, hideWindow: false would
            // launch a Fusion that hides itself anyway.
            var arguments = string.IsNullOrEmpty(_fusionArguments)
                ? kFusionChildArgument
                : _fusionArguments + " " + kFusionChildArgument;
            arguments += " " + (_fusionHideWindow ? kHideWindowArgument : kShowWindowArgument);

            _process = ChildProcessHost.Start(fullPath, arguments, _fusionHideWindow);
        }

        /// <summary>Stop the Fusion child process and release the handle.</summary>
        [ExposedFunction]
        public void StopFusion()
        {
            if (_process == null) return;

            // Fusion runs with a hidden window: WM_CLOSE cannot be delivered reliably to it,
            // so termination goes through a PID-keyed named-event signal. When the
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
