// Copyright (c) You-Ri, 2026
using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Lilium.RemoteControl.UI
{
    /// <summary>
    /// OS ネイティブの「名前を付けて保存」ダイアログ。
    /// Windows / macOS スタンドアロン以外のプラットフォームでは null を返す。
    /// </summary>
    public static class SaveFileDialog
    {
        /// <summary>
        /// 同期で「名前を付けて保存」ダイアログを表示する。
        /// </summary>
        /// <param name="title">ダイアログタイトル。</param>
        /// <param name="initialDirectory">初期ディレクトリ。null/存在しない場合は OS 標準。</param>
        /// <param name="defaultFileName">既定のファイル名（拡張子含む）。null可。</param>
        /// <param name="extension">ユーザーが拡張子を付けなかった場合に補う拡張子（例: ".scene.json"）。null可。</param>
        /// <returns>選択されたフルパス。キャンセルされた場合は null。</returns>
        public static string Show(string title, string initialDirectory = null, string defaultFileName = null, string extension = null)
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            return _ShowWindows(title, initialDirectory, defaultFileName, extension);
#elif UNITY_STANDALONE_OSX && !UNITY_EDITOR
            return _ShowMacOS(title, initialDirectory, defaultFileName, extension);
#else
            Debug.LogWarning("[RemoteControl] SaveFileDialog.Show: native dialog not supported on this platform. Returning null.");
            return null;
#endif
        }

        private static string _NormalizeExtension(string ext)
        {
            if (string.IsNullOrEmpty(ext)) return null;
            return ext.StartsWith(".") ? ext : "." + ext;
        }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        private const int kOfnOverwritePrompt = 0x00000002;
        private const int kOfnHideReadonly    = 0x00000004;
        private const int kOfnNoChangeDir     = 0x00000008;
        private const int kOfnPathMustExist   = 0x00000800;
        private const int kOfnExplorer        = 0x00080000;
        private const int kPathBufferLength   = 1024;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private class OpenFileNameW
        {
            public int structSize;
            public IntPtr hwndOwner = IntPtr.Zero;
            public IntPtr hInstance = IntPtr.Zero;
            public string filter;
            public string customFilter;
            public int maxCustFilter;
            public int filterIndex;
            public string file;
            public int maxFile;
            public string fileTitle;
            public int maxFileTitle;
            public string initialDir;
            public string title;
            public int flags;
            public short fileOffset;
            public short fileExtension;
            public string defExt;
            public IntPtr custData = IntPtr.Zero;
            public IntPtr hook = IntPtr.Zero;
            public string templateName;
            public IntPtr reservedPtr = IntPtr.Zero;
            public int reservedInt;
            public int flagsEx;
        }

        [DllImport("Comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool GetSaveFileNameW([In, Out] OpenFileNameW ofn);

        [DllImport("user32.dll")]
        private static extern IntPtr GetActiveWindow();

        private static string _ShowWindows(string title, string initialDirectory, string defaultFileName, string extension)
        {
            var ofn = new OpenFileNameW();
            ofn.structSize = Marshal.SizeOf(ofn);
            ofn.hwndOwner = GetActiveWindow();

            string normalizedExt = _NormalizeExtension(extension);
            ofn.filter = _BuildFilter(normalizedExt);
            ofn.filterIndex = string.IsNullOrEmpty(normalizedExt) ? 1 : 2;

            // 入出力バッファ。kPathBufferLength 文字確保し、先頭にデフォルトファイル名を入れる。
            var chars = new char[kPathBufferLength];
            if (!string.IsNullOrEmpty(defaultFileName))
            {
                int copyLen = Math.Min(defaultFileName.Length, chars.Length - 1);
                defaultFileName.CopyTo(0, chars, 0, copyLen);
            }
            ofn.file = new string(chars);
            ofn.maxFile = chars.Length;
            ofn.fileTitle = new string('\0', 256);
            ofn.maxFileTitle = ofn.fileTitle.Length;
            ofn.initialDir = initialDirectory;
            ofn.title = title;
            // GetSaveFileName の defExt は単一拡張子のみ。複合拡張子は後段で補う。
            ofn.defExt = string.IsNullOrEmpty(normalizedExt) ? null : normalizedExt.TrimStart('.');
            ofn.flags = kOfnExplorer | kOfnPathMustExist | kOfnOverwritePrompt | kOfnHideReadonly | kOfnNoChangeDir;

            if (!GetSaveFileNameW(ofn))
            {
                return null;
            }

            string path = ofn.file;
            if (path != null)
            {
                int nul = path.IndexOf('\0');
                if (nul >= 0) path = path.Substring(0, nul);
            }
            if (string.IsNullOrEmpty(path)) return null;

            if (!string.IsNullOrEmpty(normalizedExt) && !path.EndsWith(normalizedExt, StringComparison.OrdinalIgnoreCase))
            {
                path += normalizedExt;
            }
            return path;
        }

        private static string _BuildFilter(string normalizedExt)
        {
            // 各エントリは '\0' 区切り、末尾は '\0' で終端する必要がある（C# 文字列リテラルでは "\0" で表現）
            if (string.IsNullOrEmpty(normalizedExt))
            {
                return "All Files\0*.*\0\0";
            }
            string label = normalizedExt.TrimStart('.').ToUpperInvariant();
            return $"All Files\0*.*\0{label} (*{normalizedExt})\0*{normalizedExt}\0\0";
        }
#endif

#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
        private static string _ShowMacOS(string title, string initialDirectory, string defaultFileName, string extension)
        {
            string normalizedExt = _NormalizeExtension(extension);
            var safeTitle = _EscapeForAppleScript(title);
            var safeDefaultName = _EscapeForAppleScript(defaultFileName ?? string.Empty);

            string defaultLocationClause = string.Empty;
            if (!string.IsNullOrEmpty(initialDirectory) && Directory.Exists(initialDirectory))
            {
                defaultLocationClause = $" default location (POSIX file \"{_EscapeForAppleScript(initialDirectory)}\")";
            }

            var script =
                $"set f to choose file name with prompt \"{safeTitle}\" default name \"{safeDefaultName}\"{defaultLocationClause}\n" +
                "return POSIX path of f";

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
                    if (process.ExitCode != 0) return null;

                    string path = stdout?.Trim();
                    if (string.IsNullOrEmpty(path)) return null;

                    if (!string.IsNullOrEmpty(normalizedExt) && !path.EndsWith(normalizedExt, StringComparison.OrdinalIgnoreCase))
                    {
                        path += normalizedExt;
                    }
                    return path;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[RemoteControl] macOS save dialog failed: {ex.Message}");
                return null;
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
