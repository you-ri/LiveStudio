// Copyright (c) You-Ri, 2026
using System;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using System.Runtime.InteropServices;
#endif

namespace Lilium.RemoteControl
{
    /// <summary>
    /// Reads the current process memory footprint.
    ///
    /// Unity's Mono returns 0 from <c>Process.WorkingSet64</c>/<c>PrivateMemorySize64</c>, and
    /// <c>Profiler.GetTotalAllocatedMemoryLong</c> reports 0 in non-development player builds, so on
    /// Windows we query the OS directly via GetProcessMemoryInfo, which is reliable in release builds.
    /// </summary>
    internal static class ProcessMemoryReader
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessMemoryCounters
        {
            public uint cb;
            public uint PageFaultCount;
            public UIntPtr PeakWorkingSetSize;
            public UIntPtr WorkingSetSize;
            public UIntPtr QuotaPeakPagedPoolUsage;
            public UIntPtr QuotaPagedPoolUsage;
            public UIntPtr QuotaPeakNonPagedPoolUsage;
            public UIntPtr QuotaNonPagedPoolUsage;
            public UIntPtr PagefileUsage;
            public UIntPtr PeakPagefileUsage;
        }

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("psapi.dll", SetLastError = true)]
        private static extern bool GetProcessMemoryInfo(
            IntPtr hProcess, out ProcessMemoryCounters counters, uint size);

        /// <summary>Working set (physical RAM currently used by the process) in bytes, 0 if unavailable.</summary>
        public static long GetWorkingSetBytes()
        {
            uint size = (uint)Marshal.SizeOf(typeof(ProcessMemoryCounters));
            if (GetProcessMemoryInfo(GetCurrentProcess(), out var counters, size))
            {
                return (long)counters.WorkingSetSize.ToUInt64();
            }
            return 0;
        }
#else
        /// <summary>
        /// Non-Windows fallback. The Unity profiler memory counter works in the Editor and in
        /// development builds; it returns 0 in release players where it is unavailable.
        /// </summary>
        public static long GetWorkingSetBytes()
        {
            return UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
        }
#endif
    }
}
