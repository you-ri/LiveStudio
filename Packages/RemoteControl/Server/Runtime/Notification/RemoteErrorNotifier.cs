// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace Lilium.RemoteControl.Notification
{
    /// <summary>
    /// Forwards Unity errors (LogType.Error / Exception / Assert) to every connected Remote App as an
    /// error notification via <see cref="RemoteNotificationSystem"/>, so an operator watching only the
    /// Remote App notices failures without the Unity console.
    ///
    /// Errors may be logged from background threads (via logMessageReceivedThreaded), but the broadcast
    /// enumerates the server registry which is only mutated on the main thread; delivery is therefore
    /// marshalled to the main thread through the captured <see cref="SynchronizationContext"/> (the same
    /// approach the request handlers use). Identical messages are de-duplicated and bursts are capped so
    /// a per-frame exception cannot flood the app — every error still reaches the log regardless.
    /// </summary>
    public static class RemoteErrorNotifier
    {
        /// <summary>Master switch. Enabled by default; set to false to stop forwarding errors.</summary>
        public static bool enabled = true;

        // Identical messages within this window are forwarded at most once.
        private const double kDedupWindowSeconds = 5.0;
        // At most this many notifications per rolling window; excess is dropped (still logged). Guards
        // against many *distinct* errors firing within a single frame.
        private const int kMaxPerWindow = 5;
        private const double kBurstWindowSeconds = 3.0;
        // Keep the dialog readable; the full text remains in the log.
        private const int kMaxMessageLength = 400;

        private static readonly object _lock = new object();
        private static readonly Dictionary<string, DateTime> _recent = new Dictionary<string, DateTime>();
        private static int _burstCount;
        private static DateTime _burstWindowStart;

        private static SynchronizationContext _mainThreadContext;

        // Reentrancy guard: the broadcast (or Show) may itself log, which re-enters this handler on the
        // same thread. [ThreadStatic] because the source handler is the threaded log variant.
        [ThreadStatic] private static bool _inHandler;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void _Initialize()
        {
            // Runs on the main thread — capture its context so broadcasts can be marshalled back here.
            _mainThreadContext = SynchronizationContext.Current;

            lock (_lock)
            {
                _recent.Clear();
                _burstCount = 0;
                _burstWindowStart = DateTime.UtcNow;
            }

            Application.logMessageReceivedThreaded -= _OnLog;
            Application.logMessageReceivedThreaded += _OnLog;
            Application.quitting -= _OnQuit;
            Application.quitting += _OnQuit;
        }

        private static void _OnLog(string condition, string stackTrace, LogType type)
        {
            if (!enabled) return;
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            if (_inHandler) return; // ignore logs produced while forwarding (avoid recursion)

            _inHandler = true;
            try
            {
                if (!_ShouldForward(condition)) return;

                var message = _Truncate(condition, kMaxMessageLength);
                var ctx = _mainThreadContext;
                if (ctx != null)
                {
                    // Marshal to the main thread; the server registry is only mutated there.
                    ctx.Post(_ =>
                    {
                        // Re-arm the guard on the main thread too: Show may log an error, which would
                        // otherwise re-enter and forward itself.
                        _inHandler = true;
                        try { RemoteNotificationSystem.Show(message, RemoteNotificationSystem.Type.Error); }
                        finally { _inHandler = false; }
                    }, null);
                }
                else
                {
                    // No context captured (not expected at runtime) — best-effort direct call.
                    RemoteNotificationSystem.Show(message, RemoteNotificationSystem.Type.Error);
                }
            }
            finally
            {
                _inHandler = false;
            }
        }

        // True when this error passes de-dup and the burst throttle. Thread-safe.
        private static bool _ShouldForward(string condition)
        {
            var now = DateTime.UtcNow;
            var key = condition ?? string.Empty;
            lock (_lock)
            {
                // De-dup identical messages within the window.
                if (_recent.TryGetValue(key, out var last) && (now - last).TotalSeconds < kDedupWindowSeconds)
                {
                    return false;
                }

                // Reset the burst window once it elapses.
                if ((now - _burstWindowStart).TotalSeconds >= kBurstWindowSeconds)
                {
                    _burstWindowStart = now;
                    _burstCount = 0;
                }
                if (_burstCount >= kMaxPerWindow)
                {
                    return false;
                }
                _burstCount++;

                _recent[key] = now;
                _PruneRecent(now);
                return true;
            }
        }

        // Bounds the de-dup map: drop entries past the window, then hard-cap the size. Caller holds _lock.
        private static void _PruneRecent(DateTime now)
        {
            if (_recent.Count < 64) return;

            List<string> stale = null;
            foreach (var kv in _recent)
            {
                if ((now - kv.Value).TotalSeconds >= kDedupWindowSeconds)
                {
                    (stale ??= new List<string>()).Add(kv.Key);
                }
            }
            if (stale != null)
            {
                foreach (var k in stale) _recent.Remove(k);
            }
            // Still large (many distinct recent messages) — clear outright to stay bounded.
            if (_recent.Count >= 256) _recent.Clear();
        }

        private static string _Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= max) return s;
            return s.Substring(0, max) + "…";
        }

        private static void _OnQuit()
        {
            Application.logMessageReceivedThreaded -= _OnLog;
        }
    }
}
