// Copyright (c) You-Ri, 2026

using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Shared helper to start and gracefully stop external child processes
    /// launched from LiveStudio (e.g. ToolAppLauncher, FusionApp).
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

        /// <summary>
        /// Stop a child that shuts itself down on request (e.g. a windowless -batchmode app with its
        /// own quit listener, where <see cref="Process.CloseMainWindow"/> is a no-op). Invokes
        /// <paramref name="requestGracefulShutdown"/> and returns immediately WITHOUT waiting for or
        /// forcing exit, so the parent's own shutdown is never delayed; the child is responsible for
        /// exiting on its own. The reference is disposed and set to null.
        /// </summary>
        public static void RequestStopAndRelease(ref Process process, Action requestGracefulShutdown)
        {
            if (process == null) return;

            try
            {
                if (!process.HasExited)
                {
                    requestGracefulShutdown?.Invoke();
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[Studio] Error requesting child stop: {ex.Message}");
            }
            finally
            {
                try { process.Dispose(); } catch { /* ignore */ }
                process = null;
            }
        }

        /// <summary>
        /// Stop a child that may run EITHER windowed OR as a windowless self-quitting app, without
        /// knowing which at call time. Both graceful paths are attempted (each is a no-op in the other
        /// mode): <paramref name="requestGracefulShutdown"/> for a windowless app with its own quit
        /// listener (e.g. a PID-keyed quit signal), and <see cref="Process.CloseMainWindow"/> for a
        /// windowed app. It then waits up to <paramref name="timeoutMs"/> for a clean exit and finally
        /// Kills as a last resort, so the child never outlives the caller. The reference is disposed and
        /// set to null. Unlike <see cref="RequestStopAndRelease"/> this blocks until exit or timeout.
        /// </summary>
        public static void StopGraceful(ref Process process, Action requestGracefulShutdown, int timeoutMs = 5000)
        {
            if (process == null) return;

            try
            {
                if (!process.HasExited)
                {
                    requestGracefulShutdown?.Invoke();

                    // CloseMainWindow throws when the child has no GUI (e.g. -batchmode -nographics),
                    // so guard on the window handle instead of catching. Refresh first because
                    // MainWindowHandle is cached on first access and may be stale (0) otherwise.
                    process.Refresh();
                    if (process.MainWindowHandle != IntPtr.Zero)
                    {
                        process.CloseMainWindow();
                    }

                    if (!process.WaitForExit(timeoutMs))
                    {
                        process.Kill();
                        UnityEngine.Debug.LogWarning("[Studio] Child application did not exit gracefully and was terminated.");
                    }
                }
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
