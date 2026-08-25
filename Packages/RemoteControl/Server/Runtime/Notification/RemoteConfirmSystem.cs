// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;

using UnityEngine;

using Lilium.RemoteControl.Dialogs;
using Lilium.RemoteControl.Server;

namespace Lilium.RemoteControl.Notification
{
    /// <summary>
    /// Raises a confirmation on every surface at once — the OS dialog on the machine running the app
    /// and a modal in every connected remote app — and resolves it from whichever one answers first,
    /// dismissing the rest. An operator working from a phone must not have to walk to the PC to
    /// answer a prompt they triggered remotely, and someone at the PC must not be locked out of one
    /// raised there.
    ///
    /// Prompts travel as localization keys with the app's own translation attached as a fallback, so
    /// each remote app renders them in ITS language rather than the app's.
    ///
    /// Answering is asynchronous by nature: the app has to keep running while the prompt is up, or it
    /// could not serve the REST call carrying the remote answer. Callers pass a callback, which runs
    /// on the main thread.
    /// </summary>
    public static class RemoteConfirmSystem
    {
        /// <summary>What the user picked. Mirrors <see cref="ConfirmDialog.ConfirmResult"/>.</summary>
        public enum Choice
        {
            Yes,
            No,
            Cancel,
        }

        /// <summary>
        /// The prompt to raise. Every field is a localization key; the labels default to the shared
        /// Save / Don't Save / Cancel set used by the unsaved-changes prompt.
        /// </summary>
        public struct Request
        {
            public string titleKey;
            public string messageKey;
            public string yesKey;
            public string noKey;
            public string cancelKey;
            /// <summary>Material Symbols name for the remote modal. Optional.</summary>
            public string icon;

            public static Request UnsavedChanges(string messageKey = "DIALOG_UNSAVED_CHANGES_MESSAGE")
            {
                return new Request
                {
                    titleKey = "DIALOG_UNSAVED_CHANGES_TITLE",
                    messageKey = messageKey,
                    yesKey = "DIALOG_SAVE",
                    noKey = "DIALOG_DONT_SAVE",
                    cancelKey = "DIALOG_CANCEL",
                    icon = "save",
                };
            }
        }

        // Event types the remote app dispatches on. Both are queued in each connected client's
        // inbox and collected on its next poll, so a prompt raised between two polls is not lost.
        private const string kRequestEvent = "confirm_request";
        private const string kDismissEvent = "confirm_dismiss";

        private sealed class Pending
        {
            public string id;
            public Action<Choice> onAnswered;
            public NativeConfirmDialog nativeDialog;
            // 0 until one surface answers; only the winner runs the callback.
            public int settled;
        }

        private static readonly Dictionary<string, Pending> kPending = new Dictionary<string, Pending>();
        private static int _nextId;
        private static SynchronizationContext _mainThreadContext;

        // Re-established at runtime start so a play session with Domain Reload disabled neither leaks
        // the previous session's requests nor keeps its (dead) synchronization context.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void _Initialize()
        {
            // Runs on the main thread, so this is the context an answer arriving on an HTTP worker
            // hands the callback back through.
            _mainThreadContext = SynchronizationContext.Current;
            lock (kPending) kPending.Clear();
            _nextId = 0;
        }

        /// <summary>
        /// Raises <paramref name="request"/> on every surface and returns immediately.
        /// <paramref name="onAnswered"/> runs once, on the main thread, with the first answer given.
        /// When no surface can show the prompt (no native dialog on this platform and no remote app
        /// connected) the request resolves as <see cref="Choice.Cancel"/> — the answer that never
        /// discards anything on the user's behalf.
        /// </summary>
        public static void Ask(Request request, Action<Choice> onAnswered)
        {
            var id = "confirm-" + Interlocked.Increment(ref _nextId).ToString(CultureInfo.InvariantCulture);
            var pending = new Pending { id = id, onAnswered = onAnswered };
            lock (kPending) kPending[id] = pending;

            // Start the native dialog BEFORE broadcasting, so its handle is in place by the time any
            // remote app can answer and need it dismissed.
            pending.nativeDialog = NativeConfirmDialog.ShowYesNoCancel(
                _Translate(request.titleKey),
                _Translate(request.messageKey),
                _Translate(request.yesKey),
                _Translate(request.noKey),
                _Translate(request.cancelKey),
                result => _Settle(id, _FromDialogResult(result)));

            int listeners = _BroadcastRequest(id, request);

            if (Volatile.Read(ref pending.settled) != 0)
            {
                // Answered natively before the request reached the remote apps; tell them to close the
                // modal they are only now showing.
                _BroadcastDismiss(id);
                return;
            }

            if (pending.nativeDialog == null && listeners == 0)
            {
                Debug.LogWarning($"[RemoteControl] Confirmation '{request.messageKey}' has nowhere to show " +
                                 "(no native dialog on this platform, no remote app connected). Cancelling.");
                _Settle(id, Choice.Cancel);
            }
        }

        /// <summary>
        /// Records a remote app's answer. Called by the REST layer on a worker thread. Returns false
        /// when the id is unknown or another surface already answered — a normal race, not an error.
        /// </summary>
        public static bool Answer(string id, Choice choice)
        {
            return _Settle(id, choice);
        }

        /// <summary>Parses the wire value of a choice. Unknown values fall back to Cancel.</summary>
        public static Choice ParseChoice(string value)
        {
            if (string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase)) return Choice.Yes;
            if (string.Equals(value, "no", StringComparison.OrdinalIgnoreCase)) return Choice.No;
            return Choice.Cancel;
        }

        // Resolves the request if it is still open. The winner closes the other surfaces and runs the
        // callback on the main thread.
        private static bool _Settle(string id, Choice choice)
        {
            Pending pending;
            lock (kPending)
            {
                if (!kPending.TryGetValue(id, out pending)) return false;
            }

            if (Interlocked.CompareExchange(ref pending.settled, 1, 0) != 0) return false;

            lock (kPending) kPending.Remove(id);

            // Close whichever surface did not answer. Dismiss() is a no-op on the dialog that just
            // answered (it settles itself first), and the remote apps ignore an unknown id.
            pending.nativeDialog?.Dismiss();
            _BroadcastDismiss(id);

            var callback = pending.onAnswered;
            if (callback == null) return true;

            var context = _mainThreadContext;
            if (context == null)
            {
                // No captured context (edit mode / tests): the caller is on the main thread anyway.
                callback(choice);
                return true;
            }
            context.Post(_ =>
            {
                try
                {
                    callback(choice);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[RemoteControl] Confirmation callback failed: {ex}");
                }
            }, null);
            return true;
        }

        // Queues the prompt for every connected remote app. Returns how many clients it went to, so
        // the caller can tell "nobody is listening" apart from "waiting for an answer". Presence is
        // "polled us recently" rather than "holds a connection", so a remote that quit without
        // saying so still counts as present for a few seconds — the cost of that is a prompt that
        // waits a moment longer, which is far better than one cancelled out from under a user whose
        // phone was merely slow to answer.
        private static int _BroadcastRequest(string id, Request request)
        {
            var payload = new ConfirmRequestEvent
            {
                type = kRequestEvent,
                id = id,
                icon = request.icon,
                titleKey = request.titleKey,
                messageKey = request.messageKey,
                yesKey = request.yesKey,
                noKey = request.noKey,
                cancelKey = request.cancelKey,
                // Fallbacks for a remote app that does not know these keys; it prefers its own
                // translation so the prompt follows the reader's language, not the app's.
                title = _Translate(request.titleKey),
                message = _Translate(request.messageKey),
                yes = _Translate(request.yesKey),
                no = _Translate(request.noKey),
                cancel = _Translate(request.cancelKey),
            };

            int listeners = 0;
            foreach (var kv in RemoteControlServerManager.servers)
            {
                var server = kv.Value?.server;
                if (server == null) continue;
                listeners += server.GetConnectionCount();
                _ = server.BroadcastMessage(payload, kRequestEvent);
            }
            return listeners;
        }

        private static void _BroadcastDismiss(string id)
        {
            var payload = new ConfirmDismissEvent { type = kDismissEvent, id = id };
            foreach (var kv in RemoteControlServerManager.servers)
            {
                var server = kv.Value?.server;
                if (server == null) continue;
                _ = server.BroadcastMessage(payload, kDismissEvent);
            }
        }

        private static string _Translate(string key)
        {
            // Translate falls back to the key itself, which is still better than an empty label.
            return string.IsNullOrEmpty(key) ? string.Empty : LocalizationSystem.Translate(key);
        }

        private static Choice _FromDialogResult(ConfirmDialog.ConfirmResult result)
        {
            switch (result)
            {
                case ConfirmDialog.ConfirmResult.Yes: return Choice.Yes;
                case ConfirmDialog.ConfirmResult.No: return Choice.No;
                default: return Choice.Cancel;
            }
        }

        // Wire shapes. Field names are the JSON keys the remote app reads.
        [Serializable]
        private class ConfirmRequestEvent
        {
            public string type;
            public string id;
            public string icon;
            public string titleKey;
            public string messageKey;
            public string yesKey;
            public string noKey;
            public string cancelKey;
            public string title;
            public string message;
            public string yes;
            public string no;
            public string cancel;
        }

        [Serializable]
        private class ConfirmDismissEvent
        {
            public string type;
            public string id;
        }
    }
}
