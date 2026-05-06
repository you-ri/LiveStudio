// Copyright (c) You-Ri, 2026

using System;
using System.IO;
using UnityEngine;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// LiveStudio アプリのユーザー保存ファイル (シーン JSON 等) のベースディレクトリを
    /// 一元管理する静的 API。デフォルトは MyDocuments/LiveStudio/Saved。
    /// アプリ側で <see cref="SetBaseDirectory(string)"/> を起動時に呼んでブランド名を上書きできる。
    /// </summary>
    public static class SavedPaths
    {
        public const string kBaseSubDir = "LiveStudio/Saved";
        public const string kSceneSubDir = "Scene";

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

        public static string sceneDirectory => Path.Combine(baseDirectory, kSceneSubDir);

        private static string _baseDirectory;

        public static void SetBaseDirectory(string absolutePath)
        {
            _baseDirectory = absolutePath;
        }

        public static string EnsureSceneDirectory() => _EnsureDirectory(sceneDirectory);

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
            return Path.Combine(root, kBaseSubDir);
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
