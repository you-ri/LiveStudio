// Copyright (c) You-Ri, 2026

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Ties child processes to this process's lifetime with a Windows Job Object.
    ///
    /// Every graceful stop path in <see cref="ChildProcessHost"/> runs from managed cleanup
    /// (OnDisable / OnDestroy / Application.quitting). None of them run when the host dies
    /// without unwinding — a crash, "End task", an external taskkill, or this app's own
    /// <see cref="QuitTerminationGuard"/> force-terminating a wedged shutdown. The children
    /// (Fusion, the Remote app) then survive as orphans: invisible to the user, still holding
    /// their ports, still holding file locks on the very build output a rebuild wants to
    /// overwrite.
    ///
    /// A job object closes that hole at the OS level. Children are assigned to a job created
    /// with JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE, so when the last handle to the job goes away —
    /// which the kernel guarantees when this process exits, by any means — every process still
    /// in the job is terminated. The handle is therefore deliberately never closed.
    ///
    /// Assignment is best-effort: failures are logged once and the child simply keeps the old
    /// (graceful-only) behaviour rather than failing to start.
    /// </summary>
    internal static class ChildProcessJob
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        private const int kJobObjectExtendedLimitInformation = 9;
        private const uint kLimitKillOnJobClose = 0x2000;

        // Handle to the job every child is assigned to. Intentionally never closed: the kernel
        // closes it at process exit, which is exactly what triggers the kill. A domain reload
        // clears this field and the next child creates a fresh job; the previous job stays alive
        // through its own children's handles and still kills them at process exit.
        private static IntPtr _job = IntPtr.Zero;
        private static bool _unavailable;

        [StructLayout(LayoutKind.Sequential)]
        private struct IO_COUNTERS
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetInformationJobObject(
            IntPtr hJob, int JobObjectInfoClass, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);
#endif

        /// <summary>
        /// Assigns <paramref name="process"/> to the kill-on-close job so it cannot outlive this
        /// process. Safe to call with a null or already-exited process. Never throws.
        /// </summary>
        public static void Assign(Process process)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            if (process == null || _unavailable) return;

            try
            {
                if (process.HasExited) return;

                if (_job == IntPtr.Zero && !_TryCreateJob()) return;

                if (!AssignProcessToJobObject(_job, process.Handle))
                {
                    // Not fatal: the child still runs and the graceful stop paths still apply.
                    UnityEngine.Debug.LogWarning(
                        "[Studio] Could not tie the child application to this process's lifetime " +
                        $"(AssignProcessToJobObject failed, error {Marshal.GetLastWin32Error()}). " +
                        "It may survive as an orphan if this process is force-terminated.");
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[Studio] Child process job assignment skipped: {ex.Message}");
            }
#endif
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        // Creates the unnamed job and applies KILL_ON_JOB_CLOSE. Latches _unavailable on failure so
        // the P/Invoke is not retried for every subsequent child.
        private static bool _TryCreateJob()
        {
            var job = CreateJobObject(IntPtr.Zero, null);
            if (job == IntPtr.Zero)
            {
                _unavailable = true;
                UnityEngine.Debug.LogWarning(
                    $"[Studio] CreateJobObject failed (error {Marshal.GetLastWin32Error()}); " +
                    "child applications will not be tied to this process's lifetime.");
                return false;
            }

            var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
            info.BasicLimitInformation.LimitFlags = kLimitKillOnJobClose;

            var length = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
            var buffer = Marshal.AllocHGlobal(length);
            try
            {
                Marshal.StructureToPtr(info, buffer, false);
                if (!SetInformationJobObject(job, kJobObjectExtendedLimitInformation, buffer, (uint)length))
                {
                    _unavailable = true;
                    UnityEngine.Debug.LogWarning(
                        $"[Studio] SetInformationJobObject failed (error {Marshal.GetLastWin32Error()}); " +
                        "child applications will not be tied to this process's lifetime.");
                    return false;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }

            _job = job;
            return true;
        }
#endif
    }
}
