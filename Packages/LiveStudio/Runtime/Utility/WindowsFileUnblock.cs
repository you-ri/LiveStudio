// Copyright (c) You-Ri, 2026
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Windows の Mark of the Web (MOTW) を除去するユーティリティ。
    /// インターネットからダウンロードしたファイルに付与される Zone.Identifier
    /// 代替データストリームを削除し、SmartScreen によるブロックを解除する。
    /// </summary>
    public static class WindowsFileUnblock
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool DeleteFile(string lpFileName);
#endif

        /// <summary>
        /// 指定ファイルの Zone.Identifier 代替データストリームを削除し、MOTW を除去する。
        /// </summary>
        /// <param name="filePath">対象ファイルのフルパス</param>
        /// <returns>除去に成功、または元々存在しなかった場合は true</returns>
        public static bool UnblockFile(string filePath)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return false;

            // NTFS 代替データストリーム Zone.Identifier を削除
            // ストリームが存在しない場合 DeleteFile は false を返すが問題ない
            DeleteFile(filePath + ":Zone.Identifier");
            return true;
#else
            return true;
#endif
        }

        /// <summary>
        /// 指定ディレクトリ内の実行可能ファイル (.exe, .dll) を一括でアンブロックする。
        /// </summary>
        /// <param name="directoryPath">対象ディレクトリのフルパス</param>
        /// <param name="recursive">サブディレクトリも対象にするか</param>
        public static void UnblockDirectory(string directoryPath, bool recursive = true)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            if (string.IsNullOrEmpty(directoryPath) || !Directory.Exists(directoryPath))
                return;

            var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

            foreach (var file in Directory.GetFiles(directoryPath, "*.exe", searchOption))
                UnblockFile(file);

            foreach (var file in Directory.GetFiles(directoryPath, "*.dll", searchOption))
                UnblockFile(file);
#endif
        }
    }
}
