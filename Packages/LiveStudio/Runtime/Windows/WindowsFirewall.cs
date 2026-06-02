// Copyright (c) You-Ri, 2026
using System.ComponentModel;
using System.Diagnostics;
using UnityEngine;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Registers Windows Defender Firewall inbound rules via <c>netsh</c>.
    /// The default inbound policy on Windows is Block, so a child tool that
    /// listens on a LAN UDP port (e.g. the bundled capture receiver) silently
    /// drops packets until an allow rule exists. Rules are keyed by the listening
    /// port (not the program path) so a single rule survives the tool being
    /// relocated, reinstalled, or shipped from a package cache at a different path.
    /// Adding a rule requires elevation, so the call elevates only when the rule
    /// is missing (the existence check itself runs unelevated).
    /// </summary>
    public static class WindowsFirewall
    {
        // ShellExecute returns this Win32 error code when the user dismisses the UAC prompt.
        const int kErrorCancelled = 1223;

        /// <summary>
        /// Ensures an inbound allow rule for the given UDP port(s) exists, adding it
        /// (with a one-time UAC prompt) when missing.
        /// </summary>
        /// <param name="ports">netsh localport value, e.g. "9005-9010" or "9005,9006".</param>
        /// <param name="ruleName">Stable, unique rule name used for both lookup and creation.</param>
        /// <returns>True if the rule already existed or was added; false if creation failed or was cancelled.</returns>
        public static bool EnsureInboundUdpPortsAllowed(string ports, string ruleName)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            if (string.IsNullOrEmpty(ports) || string.IsNullOrEmpty(ruleName))
                return true;

            // Idempotent: an existing rule means we never raise the UAC prompt again.
            if (_RuleExists(ruleName))
                return true;

            string args = "advfirewall firewall add rule" +
                $" name=\"{ruleName}\" dir=in action=allow protocol=UDP" +
                $" localport={ports} profile=any enable=yes";

            var startInfo = new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = args,
                // "runas" triggers elevation; it requires UseShellExecute and forbids output redirection.
                Verb = "runas",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };

            try
            {
                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    UnityEngine.Debug.LogWarning("[Studio] Failed to launch netsh to add firewall rule.");
                    return false;
                }
                process.WaitForExit();
                if (process.ExitCode != 0)
                {
                    UnityEngine.Debug.LogWarning($"[Studio] netsh add firewall rule exited with code {process.ExitCode}.");
                    return false;
                }
                UnityEngine.Debug.Log($"[Studio] Added inbound firewall rule '{ruleName}' for UDP {ports}.");
                return true;
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == kErrorCancelled)
            {
                UnityEngine.Debug.LogWarning(
                    "[Studio] Firewall allow was cancelled. Capture (UDP) may be blocked. " +
                    $"Run as administrator: netsh advfirewall firewall add rule name=\"{ruleName}\" " +
                    $"dir=in action=allow protocol=UDP localport={ports} profile=any");
                return false;
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[Studio] Failed to add firewall rule: {ex.Message}");
                return false;
            }
#else
            return true;
#endif
        }

        /// <summary>
        /// Returns true when an inbound rule with the given name is already present.
        /// Always false off Windows. Lets callers tell whether
        /// <see cref="EnsureInboundUdpPortsAllowed"/> would be a no-op (so they can
        /// surface "already allowed" instead of silently doing nothing).
        /// </summary>
        public static bool IsInboundRulePresent(string ruleName)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            if (string.IsNullOrEmpty(ruleName))
                return false;
            return _RuleExists(ruleName);
#else
            return false;
#endif
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        /// <summary>
        /// Returns true when a firewall rule with the given name exists. Read-only,
        /// so it runs without elevation. The rule listing text is localized, so the
        /// exit code is used for the decision (netsh returns non-zero when no rule matches).
        /// </summary>
        static bool _RuleExists(string ruleName)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = $"advfirewall firewall show rule name=\"{ruleName}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            try
            {
                using var process = Process.Start(startInfo);
                if (process == null)
                    return false;
                // Drain the pipes so a full buffer can never deadlock WaitForExit.
                process.StandardOutput.ReadToEnd();
                process.StandardError.ReadToEnd();
                process.WaitForExit();
                return process.ExitCode == 0;
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[Studio] Failed to query firewall rule: {ex.Message}");
                return false;
            }
        }
#endif
    }
}
