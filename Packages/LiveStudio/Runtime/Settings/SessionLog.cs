// Copyright (c) You-Ri, 2026

using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Mirrors the Unity log into the open project's hidden ".livestudio/log" folder as a per-session
    /// file ("session_yyyyMMdd_HHmmss.log"). This runs *in addition to* the native Player.log (which
    /// stays in its fixed platform location): the native log's destination is locked at process start,
    /// whereas the project folder — and therefore ".livestudio" — is only resolved at runtime and can
    /// change while running (<see cref="ProjectManager.OpenProject"/>). So we cannot relocate the native
    /// log there; we tail the log stream instead and write our own copy under the active project.
    ///
    /// Messages produced before the project path is known are buffered (bounded) and flushed once the
    /// file opens; the earliest lines are always in the native Player.log regardless. When the project
    /// switches, a fresh session file is opened under the new project's ".livestudio/log".
    /// </summary>
    public static class SessionLog
    {
        // Keep at most this many session files per project; older ones are pruned on open.
        private const int kMaxSessionFiles = 20;
        // Cap the pre-project startup buffer so a project that never opens cannot grow it without bound.
        private const int kMaxPendingLines = 4096;

        private static readonly object _lock = new object();
        private static StreamWriter _writer;
        // Project path the current writer is bound to; used to detect project switches cheaply per line.
        private static string _boundProjectPath;
        private static string _pending; // startup buffer (concatenated lines), flushed on first open

        // Guards against recursion: any Debug.* emitted from inside this handler (e.g. a directory
        // failure) re-enters synchronously on the same thread. [ThreadStatic] because the handler is the
        // threaded variant and can fire from worker threads.
        [ThreadStatic] private static bool _inHandler;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void _Initialize()
        {
            // Re-establish a clean state with Domain Reload disabled (statics survive between plays).
            lock (_lock)
            {
                _CloseWriter();
                _boundProjectPath = null;
                _pending = null;
            }

            Application.logMessageReceivedThreaded -= _OnLog;
            Application.logMessageReceivedThreaded += _OnLog;
            Application.quitting -= _OnQuit;
            Application.quitting += _OnQuit;
        }

        private static void _OnLog(string message, string stackTrace, LogType type)
        {
            if (_inHandler) return; // ignore logs emitted while handling a log (avoid re-entrancy)
            _inHandler = true;
            try
            {
                var line = _FormatLine(message, stackTrace, type);
                lock (_lock)
                {
                    _EnsureWriterForCurrentProject();
                    if (_writer == null)
                    {
                        // Project not resolved (or unwritable) yet — buffer, but never without bound.
                        if (_pending == null) _pending = line;
                        else if (_pending.Length < kMaxPendingLines * 128) _pending += line;
                        return;
                    }
                    _writer.Write(line);
                }
            }
            finally
            {
                _inHandler = false;
            }
        }

        // Opens (or reopens on project switch) the session file for the currently open project. Caller
        // must hold _lock. Leaves _writer null while no project is open so messages keep buffering.
        private static void _EnsureWriterForCurrentProject()
        {
            var projectPath = ProjectManager.projectPath;
            if (string.IsNullOrEmpty(projectPath)) return;

            // Fast path: already writing for this project.
            if (_writer != null && projectPath == _boundProjectPath) return;

            var dir = ProjectPaths.EnsureLogDir();
            if (string.IsNullOrEmpty(dir)) return; // creation failed; keep buffering, retry next line

            _CloseWriter();
            _PruneOldSessions(dir);

            var path = Path.Combine(dir, "session_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".log");
            try
            {
                var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
                _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
                _boundProjectPath = projectPath;

                _WriteHeader(projectPath);
                if (!string.IsNullOrEmpty(_pending))
                {
                    _writer.Write(_pending);
                    _pending = null;
                }
            }
            catch (Exception)
            {
                // Never call Debug.* here — it would re-enter the handler. The native Player.log still
                // captures everything, so silently give up and retry when the next line arrives.
                _CloseWriter();
                _boundProjectPath = null;
            }
        }

        private static void _WriteHeader(string projectPath)
        {
            var nl = Environment.NewLine;
            _writer.Write("==== Session log ====" + nl);
            _writer.Write("Started : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + nl);
            _writer.Write("Product : " + Application.productName + " " + Application.version + nl);
            _writer.Write("Unity   : " + Application.unityVersion + nl);
            _writer.Write("Platform: " + Application.platform + nl);
            _writer.Write("Project : " + projectPath + nl);
            _writer.Write("=====================" + nl);
        }

        private static string _FormatLine(string message, string stackTrace, LogType type)
        {
            var nl = Environment.NewLine;
            var sb = new StringBuilder(message == null ? 32 : message.Length + 32);
            sb.Append(DateTime.Now.ToString("HH:mm:ss.fff"));
            sb.Append(' ').Append(type).Append(": ").Append(message).Append(nl);
            // Include the stack only for the noteworthy types, so ordinary logs stay readable.
            if (!string.IsNullOrEmpty(stackTrace) &&
                (type == LogType.Error || type == LogType.Exception || type == LogType.Assert || type == LogType.Warning))
            {
                sb.Append(stackTrace);
                if (!stackTrace.EndsWith("\n")) sb.Append(nl);
            }
            return sb.ToString();
        }

        // Keeps the newest (kMaxSessionFiles - 1) files so the one about to open stays within the cap.
        private static void _PruneOldSessions(string dir)
        {
            try
            {
                var files = Directory.GetFiles(dir, "session_*.log");
                // Fixed-width timestamps make the file name sort chronologically.
                Array.Sort(files, StringComparer.Ordinal);
                var removeCount = files.Length - (kMaxSessionFiles - 1);
                for (int i = 0; i < removeCount; i++)
                {
                    try { File.Delete(files[i]); } catch { /* leave a locked/removed file as-is */ }
                }
            }
            catch { /* pruning is best-effort */ }
        }

        private static void _CloseWriter()
        {
            if (_writer == null) return;
            try
            {
                _writer.Flush();
                _writer.Dispose();
            }
            catch { /* closing is best-effort */ }
            _writer = null;
        }

        private static void _OnQuit()
        {
            Application.logMessageReceivedThreaded -= _OnLog;
            lock (_lock)
            {
                _CloseWriter();
            }
        }
    }
}
