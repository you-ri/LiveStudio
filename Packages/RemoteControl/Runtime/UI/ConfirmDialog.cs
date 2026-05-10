// Copyright (c) You-Ri, 2026
using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Lilium.RemoteControl.UI
{
    /// <summary>
    /// OS ネイティブの同期確認ダイアログ。OK/Cancel の 2 択、または Yes/No/Cancel の 3 択。
    /// Windows / macOS 以外のプラットフォームでは何もせず既定値を返す。
    /// </summary>
    public static class ConfirmDialog
    {
        /// <summary>
        /// 3 ボタンダイアログの結果。
        /// </summary>
        public enum ConfirmResult
        {
            Yes,
            No,
            Cancel,
        }

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

        /// <summary>
        /// 同期で Yes / No / Cancel の 3 ボタンダイアログを表示する。
        /// 未保存変更の確認のように「保存して終了 / 保存せず終了 / 終了をやめる」の 3 択を扱う場合に使う。
        /// </summary>
        /// <param name="yesLabel">Yes (保存) ボタンのラベル。macOS のみ反映。Windows は OS 標準ラベルを使用する。</param>
        /// <param name="noLabel">No (保存せず) ボタンのラベル。macOS のみ反映。Windows は OS 標準ラベルを使用する。</param>
        /// <param name="cancelLabel">Cancel ボタンのラベル。macOS のみ反映。Windows は OS 標準ラベルを使用する。</param>
        /// <returns>選択結果。未対応プラットフォームでは <paramref name="defaultResult"/>。</returns>
        public static ConfirmResult ShowYesNoCancel(
            string title,
            string message,
            string yesLabel = "Yes",
            string noLabel = "No",
            string cancelLabel = "Cancel",
            ConfirmResult defaultResult = ConfirmResult.Cancel)
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            return _ShowWindowsYesNoCancel(title, message);
#elif UNITY_STANDALONE_OSX && !UNITY_EDITOR
            return _ShowMacOSYesNoCancel(title, message, yesLabel, noLabel, cancelLabel);
#else
            Debug.LogWarning("[RemoteControl] ConfirmDialog.ShowYesNoCancel: native dialog not supported on this platform. Returning default.");
            return defaultResult;
#endif
        }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        private const uint kMbOkCancel = 0x00000001;
        private const uint kMbYesNoCancel = 0x00000003;
        private const uint kMbIconQuestion = 0x00000020;
        private const uint kMbDefButton3 = 0x00000200;
        private const uint kMbSystemModal = 0x00001000;
        private const uint kMbSetForeground = 0x00010000;
        private const int kIdOk = 1;
        private const int kIdCancel = 2;
        private const int kIdYes = 6;
        private const int kIdNo = 7;

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

        private static ConfirmResult _ShowWindowsYesNoCancel(string title, string message)
        {
            var hwnd = GetActiveWindow();
            // 既定ボタンを Cancel にして、Enter 連打でデータ消失しないようにする。
            var type = kMbYesNoCancel | kMbIconQuestion | kMbDefButton3 | kMbSystemModal | kMbSetForeground;
            var result = MessageBoxW(hwnd, message, title, type);
            switch (result)
            {
                case kIdYes: return ConfirmResult.Yes;
                case kIdNo: return ConfirmResult.No;
                default: return ConfirmResult.Cancel; // IDCANCEL or window-close
            }
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

        /// <summary>
        /// osascript の display dialog で 3 ボタンの確認ダイアログを表示。
        /// Cancel 押下時は cancel button 指定により AppleScript エラー (exit code != 0) で抜ける。
        /// Yes/No は stdout の "button returned:&lt;label&gt;" を見て判別する。
        /// </summary>
        private static ConfirmResult _ShowMacOSYesNoCancel(string title, string message, string yesLabel, string noLabel, string cancelLabel)
        {
            var safeTitle = _EscapeForAppleScript(title);
            var safeMessage = _EscapeForAppleScript(message);
            var safeYes = _EscapeForAppleScript(yesLabel);
            var safeNo = _EscapeForAppleScript(noLabel);
            var safeCancel = _EscapeForAppleScript(cancelLabel);
            // ボタンは左から Cancel / No / Yes の順で並べ、default button を Yes、cancel button を Cancel に割り当てる。
            var script =
                $"display dialog \"{safeMessage}\" with title \"{safeTitle}\" " +
                $"buttons {{\"{safeCancel}\", \"{safeNo}\", \"{safeYes}\"}} default button \"{safeYes}\" cancel button \"{safeCancel}\" " +
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
                    string stdout = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();
                    if (process.ExitCode != 0)
                        return ConfirmResult.Cancel;

                    // 例: "button returned:Save, gave up:false"
                    if (!string.IsNullOrEmpty(stdout) && stdout.Contains("button returned:" + noLabel))
                        return ConfirmResult.No;
                    return ConfirmResult.Yes;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[RemoteControl] macOS yes/no/cancel dialog failed: {ex.Message}");
                return ConfirmResult.Cancel;
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
