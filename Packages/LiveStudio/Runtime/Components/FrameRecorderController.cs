// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Lilium.RemoteControl;
using Lilium.RemoteControl.Frames;
using Lilium.RemoteControl.Frames.Recording;
using Lilium.RemoteControl.Replay;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Records and replays live data.
    ///
    /// This is the machinery: it holds the open file, the recorder and the replayer, and it needs a
    /// Unity lifecycle to close them when the object goes away. How it is presented -- sections,
    /// labels, help, the file picker -- belongs to <see cref="FrameRecorderPage"/>, which the studio
    /// UI definition points its menu entry at. Same split as Fusion's page and its providers.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    [LiveClass(kLiveClassName, Icon = "fiber_manual_record", Category = "Recorder")]
    public class FrameRecorderController : MonoBehaviour
    {
        /// <summary>
        /// The exposed name of this component, which is also how <see cref="FrameRecorderPage"/>
        /// addresses it. A constant because the page has to name it literally: it binds its fields
        /// while the class registry may still be empty, and a lookup by type would fall back to the
        /// C# type name and quietly resolve to nothing.
        /// </summary>
        public const string kLiveClassName = "FrameRecorder";

        /// <summary>Extension of a live data recording. One take, one file.</summary>
        public const string kExtension = ".livedata";

        // ---- Recording ----

        // The page reaches these by property path (LivePropertyRef), so they stay exposed here --
        // that keeps the value, the dirty flag and the persistence in one place. Only how they are
        // presented moved to the page.
        [SerializeField]
        [LiveField(persistable = false)]
        private int _take = 1;

        [SerializeField]
        [LiveField]
        private int _keyframeInterval = FrameRecorder.kDefaultKeyframeInterval;

        // ---- Replay ----

        [SerializeField]
        [LiveField(persistable = false)]
        private string _replayFilename = string.Empty;

        private readonly FrameRecorder _recorder = new FrameRecorder();
        private FrameReplayer _replayer;
        private bool _holdsStateSystems;

        // Reused so starting a recording does not allocate. Filled in Record().
        private readonly string[] _excludeIds = new string[2];

        private static FrameRecorderController _instance;

        /// <summary>
        /// The recorder in the scene, or null when none is placed. <see cref="FrameRecorderPage"/>
        /// goes through this for everything it cannot reach by property path.
        ///
        /// Found lazily rather than assigned in OnEnable, because the remote control server serves
        /// the page while the editor is not playing too, when no Unity callback has run.
        /// </summary>
        public static FrameRecorderController instance
        {
            get
            {
                if (_instance != null) return _instance;
#if UNITY_2022_3_OR_NEWER
                _instance = FindFirstObjectByType<FrameRecorderController>();
#else
                _instance = FindObjectOfType<FrameRecorderController>();
#endif
                return _instance;
            }
        }

        /// <summary>Subfolder of the open project that holds the takes.</summary>
        public const string kFolderName = "LiveData";

        /// <summary>
        /// Where takes are written: <c>&lt;open project&gt;/LiveData</c>.
        ///
        /// A take replays by rebuilding the world it was recorded against, so it only means anything
        /// in the project it came from -- it belongs to the project the same way scenes, decks and
        /// avatars do. Recomputed on every read so switching projects switches the picker with it.
        ///
        /// The persisted path is read as a fallback, because ProjectManager only fills its own field
        /// once playing and the page is served in the editor too. Failing both, the project that
        /// would be opened next -- so a take is never written outside a project, and the picker in a
        /// stopped editor lists the same folder the next run records into.
        /// </summary>
        public static string recordingFolder => ResolveRecordingFolder(
            ProjectManager.projectPath,
            PlayerPrefs.GetString(ProjectManager.kProjectPathKey, ""),
            SavedPaths.ProjectDirectory(ProjectManager.projectName));

        /// <summary>
        /// Picks the folder from the paths on offer, most specific first. Split out from
        /// <see cref="recordingFolder"/> so the precedence can be tested without a project open.
        ///
        /// <paramref name="openProjectPath"/> is empty until something is playing, and
        /// <paramref name="persistedProjectPath"/> is empty on a first launch that has not saved a
        /// project yet -- so both are needed, and neither alone covers both cases.
        /// <paramref name="fallbackPath"/> catches what is left.
        /// </summary>
        internal static string ResolveRecordingFolder(string openProjectPath,
            string persistedProjectPath, string fallbackPath)
        {
            var projectPath = openProjectPath;
            if (string.IsNullOrEmpty(projectPath)) projectPath = persistedProjectPath;
            if (string.IsNullOrEmpty(projectPath)) projectPath = fallbackPath;

            return Path.Combine(projectPath, kFolderName);
        }

        /// <summary>Take number, used to name the next recording.</summary>
        public int take { get => _take; set => _take = Mathf.Max(1, value); }

        /// <summary>
        /// Frames between keyframes. Only decides how far a seek walks back for the shape of the
        /// world -- the values are complete on every frame either way.
        /// </summary>
        public int keyframeInterval { get => _keyframeInterval; set => _keyframeInterval = Mathf.Max(0, value); }

        /// <summary>Recording to play, chosen from <see cref="GetAvailableRecordings"/>.</summary>
        public string replayFilename { get => _replayFilename; set => _replayFilename = value; }

        [LiveProperty]
        public bool isRecording => _recorder.isRecording;

        [LiveProperty]
        public int recordedFrames => _recorder.frameCount;

        [LiveProperty]
        public float recordedMegabytes => _recorder.length / (1024f * 1024f);

        [LiveProperty]
        public bool isReplaying => _replayer != null;

        [LiveProperty]
        public long replayFrame => _replayer?.frameNumber ?? -1;

        /// <summary>
        /// Takes on disk, newest first. Source for the page's file picker.
        ///
        /// Static because the picker has to offer something before anything is placed in the scene:
        /// the folder is the same whether or not a recorder exists.
        /// </summary>
        public static string[] GetAvailableRecordings()
        {
            if (!Directory.Exists(recordingFolder)) return Array.Empty<string>();

            var files = new List<string>(Directory.GetFiles(recordingFolder, "*" + kExtension));
            files.Sort((a, b) => File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a)));

            var names = new string[files.Count];
            for (int i = 0; i < files.Count; i++) names[i] = Path.GetFileName(files[i]);

            return names;
        }

        public void Record()
        {
            if (_recorder.isRecording) return;

            StopReplay();

            var path = Path.Combine(recordingFolder, $"take{_take:D3}_{DateTime.Now:yyyyMMdd_HHmmss}{kExtension}");

            _recorder.keyframeInterval = _keyframeInterval;

            // The recorder's own controls are not part of the take. Recording them means the replay
            // presses them again -- a recorded Record starts a second recording, and a recorded Stop
            // tears down the replay that is running it.
            //
            // Two ids, because the controls and the machinery are separate exposed objects: the page
            // carries the buttons and the settings, this component carries the values they write.
            _excludeIds[0] = nameof(FrameRecorderPage);
            _excludeIds[1] = LiveObjectRegistry.FindByTarget(this)?.id;
            _recorder.excludeObjectIds = _excludeIds;

            // Nothing carries the state lane on its own -- it costs a copy of every bridged object
            // every frame, and outside a take nothing reads it. Turn it on for the length of one.
            _StartStateSystem();

            _recorder.Start(path);
            FrameGate.sink = _recorder;

            Debug.Log($"[Studio] Recording frames to {path}");
        }

        public void Stop()
        {
            StopReplay();

            if (!_recorder.isRecording) return;

            var path = _recorder.path;
            var frames = _recorder.frameCount;
            var megabytes = _recorder.length / (1024f * 1024f);

            FrameGate.sink = null;
            _recorder.Stop();
            _StopStateSystem();
            _take++;

            Debug.Log($"[Studio] Recorded {frames} frames ({megabytes:F1} MB) to {path}");
        }

        public void Replay()
        {
            if (_recorder.isRecording) Stop();
            StopReplay();

            if (string.IsNullOrEmpty(_replayFilename))
            {
                Debug.LogWarning("[Studio] No recording selected to replay.");
                return;
            }

            var path = Path.Combine(recordingFolder, _replayFilename);
            if (!File.Exists(path))
            {
                Debug.LogError($"[Studio] Recording not found: {path}");
                return;
            }

            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            _replayer = new FrameReplayer(stream, new LiveInputApplier());

            // Every registered type gets a block up front. Without it the recording reports each type
            // as unknown until something happens to write it live first, which on a machine that is
            // only replaying never happens.
            LiveStateSystem.PrepareBlocks(_replayer.player.state);

            // Running for the opposite reason than during a take: on a supplied frame the state
            // system writes the recorded values back onto the objects instead of reading them off.
            _StartStateSystem();

            // The replay is where the frame comes from now, not something that runs during one. That
            // is what puts it ahead of the producers -- they read the frame it filled rather than
            // racing it in an order nobody declared.
            FrameGate.onSourceEnded += _OnReplayEnded;
            FrameGate.source = _replayer;

            Debug.Log($"[Studio] Replaying {_replayFilename}");
        }

        public void StopReplay()
        {
            var replayer = _replayer;
            if (replayer == null) return;

            // Cleared before the teardown, so a re-entrant call (an input reaching back into this
            // object) finds nothing left to do rather than disposing it twice.
            _replayer = null;

            FrameGate.onSourceEnded -= _OnReplayEnded;
            if (ReferenceEquals(FrameGate.source, replayer)) FrameGate.source = null;

            var applied = replayer.appliedInputCount;
            var failed = replayer.failedInputCount;
            var skipped = replayer.skippedTruncatedCount;

            replayer.Dispose();

            _StopStateSystem();

            Debug.Log($"[Studio] Replay stopped: {applied} inputs applied, {failed} failed, {skipped} skipped as truncated");
        }

        private void OnDisable()
        {
            if (_instance == this) _instance = null;

            // Both hold a file open, and neither survives the object going away.
            StopReplay();

            if (!_recorder.isRecording) return;

            FrameGate.sink = null;
            _recorder.Stop();
            _StopStateSystem();
        }

        // The gate detached the recording because it ran out. Tear down what was set up for it.
        private void _OnReplayEnded() => StopReplay();

        private void _StartStateSystem()
        {
            if (_holdsStateSystems) return;
            _holdsStateSystems = true;

            LiveStateSystem.Retain();

            // The inventory goes with the values. A recording of state addressed to objects it
            // never lists cannot say whether the world it is being replayed into is the right one.
            LiveStructureSystem.Retain();
        }

        // Undoes what this object turned on, and only that: a system someone else installed stays.
        private void _StopStateSystem()
        {
            if (!_holdsStateSystems) return;
            _holdsStateSystems = false;

            LiveStructureSystem.Release();
            LiveStateSystem.Release();
        }
    }
}
