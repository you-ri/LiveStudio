// Copyright (c) You-Ri, 2026
using System;
using System.Runtime.InteropServices;
using System.Threading;

using UnityEngine;

namespace Lilium.RemoteControl.Dialogs
{
    /// <summary>
    /// A native Yes/No/Cancel dialog that does not block the caller and can be closed from another
    /// thread.
    ///
    /// <see cref="ConfirmDialog"/> blocks the calling thread for as long as the dialog is up. That is
    /// acceptable on a terminal path (quit), but a confirmation that is also mirrored to the remote
    /// apps cannot block: the app has to stay responsive to serve the REST call carrying the remote
    /// answer, and that answer has to be able to close this dialog. So the OS dialog runs on its own
    /// thread and <see cref="Dismiss"/> closes it from outside.
    ///
    /// Platforms with no native dialog (the Editor, Linux) show nothing and never answer — the
    /// request then resolves from whichever remote app answers it.
    /// </summary>
    public sealed class NativeConfirmDialog
    {
        /// <summary>
        /// Shows the dialog and returns immediately. <paramref name="onAnswered"/> runs on the dialog
        /// thread when the user picks a button, and never runs if <see cref="Dismiss"/> won the race.
        /// Returns null when the platform has no native dialog.
        /// </summary>
        public static NativeConfirmDialog ShowYesNoCancel(
            string title, string message, string yesLabel, string noLabel, string cancelLabel,
            Action<ConfirmDialog.ConfirmResult> onAnswered)
        {
#if (UNITY_STANDALONE_WIN || UNITY_STANDALONE_OSX) && !UNITY_EDITOR
            var dialog = new NativeConfirmDialog(onAnswered);
            dialog._Start(title, message, yesLabel, noLabel, cancelLabel);
            return dialog;
#else
            return null;
#endif
        }

        // Set once, by whichever of the dialog thread / Dismiss gets there first. Guards against both
        // answering the request and reporting a user choice for a dialog already closed remotely.
        private int _settled;

        private readonly Action<ConfirmDialog.ConfirmResult> _onAnswered;

        private NativeConfirmDialog(Action<ConfirmDialog.ConfirmResult> onAnswered)
        {
            _onAnswered = onAnswered;
        }

        private bool _TrySettle() => Interlocked.CompareExchange(ref _settled, 1, 0) == 0;

#if (UNITY_STANDALONE_WIN || UNITY_STANDALONE_OSX) && !UNITY_EDITOR
        private void _Start(string title, string message, string yesLabel, string noLabel, string cancelLabel)
        {
            var thread = new Thread(() =>
            {
                ConfirmDialog.ConfirmResult result;
                try
                {
                    result = _ShowBlocking(title, message, yesLabel, noLabel, cancelLabel);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[RemoteControl] Native confirm dialog failed: {ex.Message}");
                    return;
                }

                // Lost the race to a remote answer: the dialog was closed by Dismiss, so the "result"
                // is the synthetic cancel that closing produced, not a user choice.
                if (!_TrySettle()) return;
                _onAnswered?.Invoke(result);
            })
            {
                IsBackground = true,
                Name = "RemoteControl.NativeConfirmDialog",
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }

        /// <summary>
        /// Closes the dialog if it is still up, so it cannot answer any more. Safe to call from any
        /// thread and more than once. Returns true if this call is the one that closed it.
        /// </summary>
        public bool Dismiss()
        {
            if (!_TrySettle()) return false;
            _CloseNative();
            return true;
        }
#else
        private void _Start(string title, string message, string yesLabel, string noLabel, string cancelLabel) { }

        /// <summary>No native dialog on this platform; nothing to close.</summary>
        public bool Dismiss() => _TrySettle();
#endif

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        private const uint kMbYesNoCancel = 0x00000003;
        private const uint kMbIconQuestion = 0x00000020;
        private const uint kMbDefButton3 = 0x00000200;
        private const uint kMbSystemModal = 0x00001000;
        private const uint kMbSetForeground = 0x00010000;
        private const int kIdYes = 6;
        private const int kIdNo = 7;
        private const uint kWmClose = 0x0010;

        private delegate bool EnumThreadWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

        [DllImport("user32.dll")]
        private static extern bool EnumThreadWindows(uint threadId, EnumThreadWindowsProc callback, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        // Win32 thread id of the thread running MessageBoxW, needed to find its window. 0 until the
        // dialog thread records it.
        private uint _dialogThreadId;

        // EnumThreadWindows hands its result back through a static because the callback must be a
        // plain static method under IL2CPP AOT. Dismissal is rare and this lock serializes it.
        private static readonly object kEnumLock = new object();
        private static IntPtr _enumFoundWindow;

        [AOT.MonoPInvokeCallback(typeof(EnumThreadWindowsProc))]
        private static bool _OnEnumThreadWindow(IntPtr hWnd, IntPtr lParam)
        {
            if (!IsWindowVisible(hWnd)) return true; // keep looking
            _enumFoundWindow = hWnd;
            return false; // stop
        }

        private ConfirmDialog.ConfirmResult _ShowBlocking(string title, string message, string yesLabel, string noLabel, string cancelLabel)
        {
            // Windows uses OS-standard button labels; the supplied ones only apply on macOS.
            _dialogThreadId = GetCurrentThreadId();

            // Owner is null: this runs off the main thread, which owns the game window. Default to the
            // Cancel button so hammering Enter cannot discard data.
            var type = kMbYesNoCancel | kMbIconQuestion | kMbDefButton3 | kMbSystemModal | kMbSetForeground;
            var result = MessageBoxW(IntPtr.Zero, message, title, type);
            switch (result)
            {
                case kIdYes: return ConfirmDialog.ConfirmResult.Yes;
                case kIdNo: return ConfirmDialog.ConfirmResult.No;
                default: return ConfirmDialog.ConfirmResult.Cancel;
            }
        }

        // Posts WM_CLOSE to the message box, which dismisses it as if Cancel was pressed. Polls
        // briefly because Dismiss can arrive before the window exists (a remote app answering an
        // instant after the request was raised).
        private void _CloseNative()
        {
            const int kMaxAttempts = 40; // ~2s at 50ms
            for (int attempt = 0; attempt < kMaxAttempts; attempt++)
            {
                var threadId = _dialogThreadId;
                if (threadId != 0)
                {
                    IntPtr hWnd;
                    lock (kEnumLock)
                    {
                        _enumFoundWindow = IntPtr.Zero;
                        EnumThreadWindows(threadId, _OnEnumThreadWindow, IntPtr.Zero);
                        hWnd = _enumFoundWindow;
                    }

                    if (hWnd != IntPtr.Zero)
                    {
                        PostMessage(hWnd, kWmClose, IntPtr.Zero, IntPtr.Zero);
                        return;
                    }
                }
                Thread.Sleep(50);
            }
            Debug.LogWarning("[RemoteControl] Native confirm dialog did not close: its window was never found.");
        }
#endif

#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
        // The osascript process showing the dialog, killed to dismiss it.
        private System.Diagnostics.Process _process;

        private ConfirmDialog.ConfirmResult _ShowBlocking(string title, string message, string yesLabel, string noLabel, string cancelLabel)
        {
            var script =
                $"display dialog \"{_EscapeForAppleScript(message)}\" with title \"{_EscapeForAppleScript(title)}\" " +
                $"buttons {{\"{_EscapeForAppleScript(cancelLabel)}\", \"{_EscapeForAppleScript(noLabel)}\", \"{_EscapeForAppleScript(yesLabel)}\"}} " +
                $"default button \"{_EscapeForAppleScript(yesLabel)}\" cancel button \"{_EscapeForAppleScript(cancelLabel)}\" " +
                "with icon caution";

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "/usr/bin/osascript",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            using (var process = System.Diagnostics.Process.Start(psi))
            {
                _process = process;
                process.StandardInput.WriteLine(script);
                process.StandardInput.Close();
                string stdout = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                _process = null;

                if (process.ExitCode != 0) return ConfirmDialog.ConfirmResult.Cancel;
                if (!string.IsNullOrEmpty(stdout) && stdout.Contains("button returned:" + noLabel))
                    return ConfirmDialog.ConfirmResult.No;
                return ConfirmDialog.ConfirmResult.Yes;
            }
        }

        private void _CloseNative()
        {
            try
            {
                var process = _process;
                if (process != null && !process.HasExited) process.Kill();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RemoteControl] Failed to dismiss the native confirm dialog: {ex.Message}");
            }
        }

        private static string _EscapeForAppleScript(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
#endif
    }
}
