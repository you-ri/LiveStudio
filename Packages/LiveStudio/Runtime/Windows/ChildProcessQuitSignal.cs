// Copyright (c) You-Ri, 2026

using System;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Cross-process graceful-quit signal targeted at a specific child process by PID, using a
    /// Windows named event in the per-session <c>Local\</c> namespace. The parent (e.g. Studio)
    /// calls <see cref="Signal"/> with the child's PID; the child (e.g. a -batchmode Fusion that
    /// has no main window and so cannot receive WM_CLOSE) calls <see cref="StartListening"/> to
    /// quit gracefully when signaled. Set is instant and non-blocking, and the PID-keyed name
    /// means multiple child instances are addressed individually.
    /// </summary>
    public static class ChildProcessQuitSignal
    {
        private static string _EventName(int pid) => $"Local\\LiveStudioChildQuit_{pid}";

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        private const uint kEventModifyState = 0x0002;
        private const uint kWaitObject0 = 0;
        private const uint kInfinite = 0xFFFFFFFF;

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern IntPtr CreateEventW(IntPtr lpEventAttributes, bool bManualReset, bool bInitialState, string lpName);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern IntPtr OpenEventW(uint dwDesiredAccess, bool bInheritHandle, string lpName);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetEvent(IntPtr hEvent);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        /// <summary>
        /// Parent side: signal the child with the given PID to quit. Returns true if a listening
        /// child was found and signaled, false if none is listening on that PID (not running /
        /// not yet ready). A false return tells the caller no graceful self-quit will happen, so a
        /// hard kill is required to avoid an orphan. Never blocks.
        /// </summary>
        public static bool Signal(int pid)
        {
            var handle = OpenEventW(kEventModifyState, false, _EventName(pid));
            if (handle == IntPtr.Zero) return false; // child not listening (not running / not ready)
            try { return SetEvent(handle); }
            finally { CloseHandle(handle); }
        }

        /// <summary>
        /// Child side: create the PID-keyed event for this process and invoke
        /// <paramref name="onQuitRequested"/> on the main thread when the parent signals it.
        /// The wait runs on a background thread for the process lifetime.
        /// </summary>
        public static void StartListening(Action onQuitRequested)
        {
            if (onQuitRequested == null) return;

            // Captured on the main thread (this runs from RuntimeInitializeOnLoad) so the
            // background wait can marshal the quit back onto Unity's main thread.
            var mainThreadContext = System.Threading.SynchronizationContext.Current;

            int pid = System.Diagnostics.Process.GetCurrentProcess().Id;
            var handle = CreateEventW(IntPtr.Zero, true, false, _EventName(pid));
            if (handle == IntPtr.Zero)
            {
                UnityEngine.Debug.LogWarning("[Studio] Failed to create child quit signal event.");
                return;
            }

            var thread = new System.Threading.Thread(() =>
            {
                if (WaitForSingleObject(handle, kInfinite) == kWaitObject0)
                {
                    if (mainThreadContext != null)
                        mainThreadContext.Post(_ => onQuitRequested(), null);
                    else
                        onQuitRequested();
                }
                CloseHandle(handle);
            })
            {
                IsBackground = true,
                Name = "ChildProcessQuitSignalListener",
            };
            thread.Start();
        }
#else
        public static bool Signal(int pid) => false;
        public static void StartListening(Action onQuitRequested) { }
#endif
    }
}
