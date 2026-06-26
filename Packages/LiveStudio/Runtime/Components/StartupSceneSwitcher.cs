// Copyright (c) You-Ri, 2026

using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using Lilium.RemoteControl.LiveScene;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Before the first scene is loaded, redirects to the base Unity scene recorded in the open
    /// project's startup state (<c>{projectPath}/Settings/startup.json</c>). Switching up-front
    /// avoids changing scenes after the HTTP server has started, which would race with in-flight
    /// requests and produce ObjectDisposedException noise.
    ///
    /// This lives in the LiveStudio layer (not the generic RemoteControl layer) because locating the
    /// state directory needs the project-path knowledge that <see cref="ProjectManager"/> owns. The
    /// persisted project path is read straight from PlayerPrefs rather than from
    /// <see cref="ProjectManager"/> state, so the hook never depends on static-init ordering between
    /// the two BeforeSceneLoad callbacks.
    /// </summary>
    internal static class StartupSceneSwitcher
    {
        /// <summary>
        /// Runs before the first scene is loaded. All error paths are silent: fall through to the
        /// normal startup scene.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void _SwitchBaseSceneOnStartup()
        {
            if (!Application.isPlaying) return;

            // Read the persisted project path directly (no dependency on ProjectManager state and no
            // reliance on static-init ordering). Fall back to persistentDataPath when no project is set.
            var projectPath = PlayerPrefs.GetString(ProjectManager.kProjectPathKey, "");
            var stateDir = string.IsNullOrEmpty(projectPath) ? Application.persistentDataPath : projectPath;

            var baseSceneName = ReadBaseSceneName(stateDir);
            if (string.IsNullOrEmpty(baseSceneName)) return;

            int count = SceneManager.sceneCountInBuildSettings;
            var buildSceneNames = new string[count];
            for (int i = 0; i < count; i++)
                buildSceneNames[i] = Path.GetFileNameWithoutExtension(SceneUtility.GetScenePathByBuildIndex(i));

            // Build index 0 (among enabled scenes) is the one Unity is about to load.
            var initialName = count > 0 ? buildSceneNames[0] : null;

            int target = ResolveSwitchTargetIndex(baseSceneName, initialName, buildSceneNames);
            if (target >= 0) SceneManager.LoadScene(target);
        }

        /// <summary>
        /// Reads the live scene recorded in <paramref name="stateDir"/>'s startup state and returns
        /// its <c>baseSceneName</c>, or <c>null</c> when nothing is recorded, the file is missing, or
        /// it cannot be parsed.
        /// </summary>
        internal static string ReadBaseSceneName(string stateDir)
        {
            var fullPath = StartupStateStore.Read(stateDir);
            if (string.IsNullOrEmpty(fullPath)) return null;
            if (!File.Exists(fullPath)) return null;

            try
            {
                var json = File.ReadAllText(fullPath);
                return LiveSceneSerializer.ExtractBaseSceneName(json);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Resolves which build scene index to switch to, or <c>-1</c> for no switch. Returns
        /// <c>-1</c> when the recorded base scene is empty, no scene is about to load matches it, the
        /// scene Unity is already about to load (<paramref name="initialSceneName"/>) is the target,
        /// or the target is not registered in build settings.
        /// </summary>
        internal static int ResolveSwitchTargetIndex(string baseSceneName, string initialSceneName, IReadOnlyList<string> buildSceneNames)
        {
            if (string.IsNullOrEmpty(baseSceneName)) return -1;
            if (buildSceneNames == null || buildSceneNames.Count == 0) return -1;

            // Unity is already about to load the recorded scene; nothing to redirect.
            if (initialSceneName == baseSceneName) return -1;

            for (int i = 0; i < buildSceneNames.Count; i++)
            {
                if (buildSceneNames[i] == baseSceneName) return i;
            }
            return -1;
        }
    }
}
