// Copyright (c) You-Ri, 2026

using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Shared helper to start and gracefully stop external child processes
    /// launched from LiveStudio (e.g. ToolAppLauncher, FusionAppHost).
    /// </summary>
    public static class ChildProcessHost
    {
        public static Process Start(string applicationFullPath, string arguments, bool hideWindow)
        {
            if (string.IsNullOrEmpty(applicationFullPath))
            {
                UnityEngine.Debug.LogWarning("[Studio] Application path is not set.");
                return null;
            }

            // Strip Mark-of-the-Web so SmartScreen does not block downloaded binaries.
            WindowsFileUnblock.UnblockFile(applicationFullPath);
            var appDir = Path.GetDirectoryName(applicationFullPath);
            if (!string.IsNullOrEmpty(appDir))
            {
                WindowsFileUnblock.UnblockDirectory(appDir);
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = applicationFullPath,
                Arguments = arguments ?? string.Empty,
                UseShellExecute = true,
                WindowStyle = hideWindow ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal,
            };

            var process = Process.Start(startInfo);
            if (process == null)
            {
                UnityEngine.Debug.LogError($"[Studio] Failed to start child application. path:{applicationFullPath}");
            }
            return process;
        }

        /// <summary>
        /// Try CloseMainWindow with a 5-second wait, then fall back to Kill. The reference
        /// is set to null after disposal so the caller's field is left in a clean state.
        /// </summary>
        public static void Stop(ref Process process)
        {
            if (process == null) return;

            try
            {
                if (process.HasExited)
                {
                    return;
                }

                if (process.CloseMainWindow())
                {
                    if (process.WaitForExit(5000))
                    {
                        UnityEngine.Debug.Log("[Studio] Child application terminated via CloseMainWindow.");
                        return;
                    }
                }

                process.Kill();
                UnityEngine.Debug.LogWarning("[Studio] Child application was forcibly terminated.");
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[Studio] Error stopping child application: {ex.Message}");
            }
            finally
            {
                try { process.Dispose(); } catch { /* ignore */ }
                process = null;
            }
        }
    }
}
