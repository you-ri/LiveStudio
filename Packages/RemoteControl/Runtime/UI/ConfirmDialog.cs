// Copyright (c) You-Ri, 2026
using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Lilium.RemoteControl.UI
{
    /// <summary>
    /// OS ネイティブの同期確認ダイアログ。OK/Cancel の 2 択。
    /// Windows / macOS 以外のプラットフォームでは何もせず既定値を返す。
    /// </summary>
    public static class ConfirmDialog
    {
        /// <summary>
        /// 同期で確認ダイアログを表示する。
        /// </summary>
        /// <param name="okLabel">OK ボタンのラベル。macOS のみ反映。Windows は OS 標準ラベルを使用する。</param>
        /// <param name="cancelLabel">Cancel ボタンのラベル。macOS のみ反映。Windows は OS 標準ラベルを使用する。</param>
        /// <returns>OK なら true、Cancel なら false。未対応プラットフォームでは <paramref name="defaultResult"/>。</returns>
        public static bool Show(string title, string message, string okLabel = "OK", string cancelLabel = "Cancel", bool defaultResult = false)
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            return _ShowWindowsMessageBox(title, message);
#elif UNITY_STANDALONE_OSX && !UNITY_EDITOR
            return _ShowMacOSDialog(title, message, okLabel, cancelLabel);
#else
            Debug.LogWarning("[RemoteControl] ConfirmDialog.Show: native dialog not supported on this platform. Returning default.");
            return defaultResult;
#endif
        }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        private const uint kMbOkCancel = 0x00000001;
        private const uint kMbIconQuestion = 0x00000020;
        private const uint kMbSystemModal = 0x00001000;
        private const uint kMbSetForeground = 0x00010000;
        private const int kIdOk = 1;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

        [DllImport("user32.dll")]
        private static extern IntPtr GetActiveWindow();

        private static bool _ShowWindowsMessageBox(string title, string message)
        {
            var hwnd = GetActiveWindow();
            var type = kMbOkCancel | kMbIconQuestion | kMbSystemModal | kMbSetForeground;
            var result = MessageBoxW(hwnd, message, title, type);
            return result == kIdOk;
        }
#endif

#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
        /// <summary>
        /// osascript の display dialog で確認ダイアログを表示。
        /// OK 押下で exit 0、Cancel 押下で AppleScript エラーにより exit 1。
        /// </summary>
        private static bool _ShowMacOSDialog(string title, string message, string okLabel, string cancelLabel)
        {
            var safeTitle = _EscapeForAppleScript(title);
            var safeMessage = _EscapeForAppleScript(message);
            var safeOk = _EscapeForAppleScript(okLabel);
            var safeCancel = _EscapeForAppleScript(cancelLabel);
            var script =
                $"display dialog \"{safeMessage}\" with title \"{safeTitle}\" " +
                $"buttons {{\"{safeCancel}\", \"{safeOk}\"}} default button \"{safeOk}\" cancel button \"{safeCancel}\" " +
                "with icon caution";

            try
            {
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
                    process.StandardInput.WriteLine(script);
                    process.StandardInput.Close();
                    process.WaitForExit();
                    return process.ExitCode == 0;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[RemoteControl] macOS dialog failed: {ex.Message}");
                return false;
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
