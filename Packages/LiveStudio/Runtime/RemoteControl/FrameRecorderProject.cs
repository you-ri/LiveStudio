// Copyright (c) You-Ri, 2026
using System.IO;
using UnityEngine;
using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Files this application's takes with the rest of its project, and keeps the page that drives
    /// them out of what they record.
    ///
    /// Both are answers only the application has. <see cref="FrameRecorderController"/> holds the
    /// machinery and deliberately has no opinion about where a project lives -- a package that
    /// records live data should not have to know that this one keeps scenes, decks and avatars under
    /// a per-project folder.
    /// </summary>
    public static class FrameRecorderProject
    {
        // Runs on every domain (re)load and on entering play mode, so the answer is installed whether
        // or not Domain Reload is enabled. InitializeOnLoadMethod also covers the stopped editor,
        // where the remote control server serves the recorder page and its file picker.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#endif
        private static void _Initialize()
        {
            FrameRecorderController.recordingFolderProvider = RecordingFolder;

            // The page carries the buttons and the settings; the component carries the values they
            // write. Both are exposed objects, and neither is part of the world being recorded.
            FrameRecorderController.ExcludeControlObject(nameof(FrameRecorderPage));
        }

        /// <summary>
        /// Where this application's takes go: <c>&lt;open project&gt;/LiveData</c>.
        ///
        /// A take replays by rebuilding the world it was recorded against, so it only means anything
        /// in the project it came from -- it belongs to the project the same way scenes, decks and
        /// avatars do.
        ///
        /// The persisted path is read as a fallback, because <see cref="ProjectManager"/> only fills
        /// its own field once playing and the page is served in the editor too. Failing both, the
        /// project that would be opened next -- so a take is never written outside a project, and the
        /// picker in a stopped editor lists the same folder the next run records into.
        /// </summary>
        public static string RecordingFolder() => ResolveRecordingFolder(
            ProjectManager.projectPath,
            PlayerPrefs.GetString(ProjectManager.kProjectPathKey, ""),
            SavedPaths.ProjectDirectory(ProjectManager.projectName));

        /// <summary>
        /// Picks the folder from the paths on offer, most specific first. Split out from
        /// <see cref="RecordingFolder"/> so the precedence can be tested without a project open.
        ///
        /// <paramref name="openProjectPath"/> is empty until something is playing, and
        /// <paramref name="persistedProjectPath"/> is empty on a first launch that has not saved a
        /// project yet -- so both are needed, and neither alone covers both cases.
        /// <paramref name="fallbackPath"/> catches what is left.
        /// </summary>
        public static string ResolveRecordingFolder(string openProjectPath,
            string persistedProjectPath, string fallbackPath)
        {
            var projectPath = openProjectPath;
            if (string.IsNullOrEmpty(projectPath)) projectPath = persistedProjectPath;
            if (string.IsNullOrEmpty(projectPath)) projectPath = fallbackPath;

            return Path.Combine(projectPath, FrameRecorderController.kFolderName);
        }
    }
}
