// Copyright (c) You-Ri, 2026

using System;
using System.IO;
using UnityEngine;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// LiveStudio アプリのユーザー保存ファイル (プロジェクトフォルダ / シーン JSON 等) の
    /// ベースディレクトリを一元管理する静的 API。デフォルトは
    /// MyDocuments/&lt;LiveStudioProjectSettings.savedBaseSubDir&gt; (例: "Virgo Motion")、
    /// 設定が空ならパッケージ既定値 <see cref="kBaseSubDir"/> (LiveStudio/Saved) を使う。
    /// 個々のプロジェクトはこのベース直下のフォルダ (例: "Virgo Motion/Default") として配置する。
    /// </summary>
    public static class SavedPaths
    {
        /// <summary>設定が未指定のときに使うパッケージ既定のサブディレクトリ。</summary>
        public const string kBaseSubDir = "LiveStudio/Saved";

        public static string baseDirectory
        {
            get
            {
                if (string.IsNullOrEmpty(_baseDirectory))
                {
                    _baseDirectory = _ResolveDefaultBaseDirectory();
                }
                return _baseDirectory;
            }
        }

        private static string _baseDirectory;

        /// <summary>
        /// ベースディレクトリを明示的に上書きする (主にテスト用)。通常はアプリ側の
        /// <see cref="LiveStudioProjectSettings.savedBaseSubDir"/> から自動解決されるため呼ぶ必要はない。
        /// </summary>
        public static void SetBaseDirectory(string absolutePath)
        {
            _baseDirectory = absolutePath;
        }

        /// <summary>ベース直下のプロジェクトフォルダパスを返す (生成はしない)。</summary>
        public static string ProjectDirectory(string projectName) => Path.Combine(baseDirectory, projectName);

        /// <summary>ベース直下のプロジェクトフォルダを生成して返す。</summary>
        public static string EnsureProjectDirectory(string projectName) => _EnsureDirectory(ProjectDirectory(projectName));

        private static string _EnsureDirectory(string path)
        {
            try
            {
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Studio] Failed to create directory '{path}': {ex.Message}");
                return baseDirectory;
            }
            return path;
        }

        private static string _ResolveDefaultBaseDirectory()
        {
            string root;
            try
            {
                root = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            }
            catch (Exception)
            {
                root = null;
            }
            if (string.IsNullOrEmpty(root))
            {
                root = Application.persistentDataPath;
            }

            // アプリのブランド名 (例: "Virgo Motion") を設定から取得。空ならパッケージ既定値。
            var settings = LiveStudioProjectSettings.Instance;
            var subDir = settings != null && !string.IsNullOrEmpty(settings.savedBaseSubDir)
                ? settings.savedBaseSubDir
                : kBaseSubDir;
            return Path.Combine(root, subDir);
        }

#if UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void _ResetStatics()
        {
            _baseDirectory = null;
        }
#endif
    }
}
