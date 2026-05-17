// Copyright (c) You-Ri, 2026

using System;
using System.IO;
using UnityEngine;
using Lilium.RemoteControl;
using Lilium.RemoteControl.Scene;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// アプリ起動時に <see cref="LiveStudioProjectSettings.savedBaseSubDir"/> から
    /// <see cref="SavedPaths"/> のベースディレクトリを構築し、
    /// <see cref="SceneSaveSystem"/> の Save Scene As ダイアログ既定パスを揃える。
    /// </summary>
    public static class LiveStudioPathsInitializer
    {
        private static bool _initialized;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void _Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            var settings = LiveStudioProjectSettings.Instance;
            var subDir = settings != null ? settings.savedBaseSubDir : null;
            if (!string.IsNullOrEmpty(subDir))
            {
                SavedPaths.SetBaseDirectory(Path.Combine(_GetDocumentsRoot(), subDir));
            }

            SceneSaveSystem.SetSaveAsDefaultDirectory(SavedPaths.EnsureSceneDirectory());

            Debug.Log($"[Studio] SavedPaths base directory = {SavedPaths.baseDirectory}");
        }

        private static string _GetDocumentsRoot()
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
            return root;
        }

#if UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void _ResetStatics()
        {
            _initialized = false;
        }
#endif
    }
}
