// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Lilium.RemoteControl.Frames;
using Lilium.RemoteControl.Frames.Recording;
using Lilium.RemoteControl.Replay;

namespace Lilium.RemoteControl
{
    /// <summary>
    /// Records and replays live data.
    ///
    /// This is the machinery: it holds the open file, the recorder and the replayer, and it needs a
    /// Unity lifecycle to close them when the object goes away. How it is presented -- sections,
    /// labels, help, the file picker -- belongs to whatever drives it: a page in the remote app, the
    /// LiveData Sequencer window, or both at once.
    ///
    /// It lives here rather than with an application because everything it does is this package's:
    /// the gate, the recorder, the replayer, retaining the state and structure systems for the length
    /// of a take, and putting the inventory back on a supplied frame. Getting that sequence right is
    /// not something each application should have to rediscover. The one thing it does not know is
    /// where an application files its work -- see <see cref="recordingFolderProvider"/>.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    [LiveClass(kLiveClassName, Icon = "fiber_manual_record", Category = "Recorder")]
    public class FrameRecorderController : MonoBehaviour
    {
        /// <summary>
        /// The exposed name of this component, which is also how a page addresses it. A constant
        /// because a page has to name it literally: it binds its fields while the class registry may
        /// still be empty, and a lookup by type would fall back to the C# type name and quietly
        /// resolve to nothing.
        /// </summary>
        public const string kLiveClassName = "FrameRecorder";

        /// <summary>Extension of a live data recording. One take, one file.</summary>
        public const string kExtension = ".livedata";

        // ---- Recording ----

        // The page reaches these by property path (LivePropertyRef), so they stay exposed here --
        // that keeps the value, the dirty flag and the persistence in one place. Only how they are
        // presented moved to the page.
        //
        // All of them are off the live data (lane = None). They are the recorder's own settings, and
        // <see cref="excludeObjectIds"/> is not enough on its own: that excludes by this component's
        // registry id, while any client writing through the owning GameObject addresses the same
        // members as `{gameObject}/components/{n}/_take`. The two
        // never meet, so a take was carrying the take number and the compression setting it was
        // written with, and a replay of it wrote them back over the operator's own.
        [SerializeField]
        [LiveField(persistable = false, lane = FrameLane.None)]
        private int _take = 1;

        [SerializeField]
        [LiveField(lane = FrameLane.None)]
        private int _keyframeInterval = FrameRecorder.kDefaultKeyframeInterval;

        [SerializeField]
        [LiveField(lane = FrameLane.None)]
        private bool _compress = true;

        // ---- Replay ----

        [SerializeField]
        [LiveField(persistable = false, lane = FrameLane.None)]
        private string _replayFilename = string.Empty;

        private readonly FrameRecorder _recorder = new FrameRecorder();
        private FrameReplayer _replayer;
        private bool _holdsStateSystems;

        // Rebuilt when the set of exclusions changes and reused otherwise. Filled in Record().
        private string[] _excludeIds = Array.Empty<string>();

        private static readonly List<string> _excludedControlObjectIds = new List<string>();

        private static FrameRecorderController _instance;

        /// <summary>
        /// The recorder in the scene, or null when none is placed. Whatever drives it goes through
        /// this for everything it cannot reach by property path.
        ///
        /// Found lazily rather than assigned in OnEnable, because the remote control server serves
        /// its page while the editor is not playing too, when no Unity callback has run.
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
        /// Says where takes are written, or null to keep the default.
        ///
        /// A take replays by rebuilding the world it was recorded against, so it only means anything
        /// where it came from -- an application that files its work per project has to file its takes
        /// there too, or a picker offers takes that cannot be played. Which project, and how that is
        /// decided, is the application's business and not this package's, so whoever knows installs
        /// the answer here.
        ///
        /// Asked on every read rather than cached, so switching projects switches the picker with it.
        /// Install it from an initializer that runs on both a domain reload and a play, since the
        /// recorder is reachable from a stopped editor too.
        /// </summary>
        public static Func<string> recordingFolderProvider { get; set; }

        /// <summary>
        /// Where takes are written. <see cref="recordingFolderProvider"/>'s answer, or a folder
        /// beside the player's own data when nothing installed one.
        /// </summary>
        public static string recordingFolder
        {
            get
            {
                var folder = recordingFolderProvider?.Invoke();

                return string.IsNullOrEmpty(folder)
                    ? Path.Combine(Application.persistentDataPath, kFolderName)
                    : folder;
            }
        }

        /// <summary>
        /// Leaves an exposed object's events out of every take, on top of this component's own.
        ///
        /// Whatever drives the recording belongs here -- a remote page carrying the buttons, say. Its
        /// controls are not part of the world being recorded, and keeping them means the replay
        /// presses them again: a recorded Record starts a second recording, and a recorded Stop tears
        /// down the replay that is running it. This is the design's "内部起点は記録から除外する"
        /// applied to the recorder's own controls.
        ///
        /// Registering the same id twice does nothing, so an initializer that runs again after a
        /// domain reload -- or does not run again, because reloads are off -- ends up the same way.
        /// </summary>
        public static void ExcludeControlObject(string objectId)
        {
            if (string.IsNullOrEmpty(objectId)) return;
            if (_excludedControlObjectIds.Contains(objectId)) return;

            _excludedControlObjectIds.Add(objectId);
        }

        /// <summary>Take number, used to name the next recording.</summary>
        public int take { get => _take; set => _take = Mathf.Max(1, value); }

        /// <summary>
        /// Frames between keyframes. Only decides how far a seek walks back for the shape of the
        /// world -- the values are complete on every frame either way.
        /// </summary>
        public int keyframeInterval { get => _keyframeInterval; set => _keyframeInterval = Mathf.Max(0, value); }

        /// <summary>
        /// Compresses the recording -- measured about five times smaller over real takes.
        ///
        /// Turn it off to record something that will be read while it is still being written, or
        /// where losing the last second to a crash matters more than the size: entries only reach
        /// the file a chunk at a time.
        /// </summary>
        public bool compress { get => _compress; set => _compress = value; }

        /// <summary>Recording to play, chosen from <see cref="GetAvailableRecordings"/>.</summary>
        public string replayFilename { get => _replayFilename; set => _replayFilename = value; }

        [LiveProperty]
        public bool isRecording => _recorder.isRecording;

        [LiveProperty]
        public int recordedFrames => _recorder.frameCount;

        [LiveProperty]
        public float recordedMegabytes => _recorder.length / (1024f * 1024f);

        /// <summary>
        /// Where the take in progress is being written, or null when nothing is being recorded.
        ///
        /// For a picker listing the folder: the file being written is in it, but it has no tail yet
        /// and its length moves every frame, so it is the one entry that has to be told apart from
        /// the finished takes around it.
        /// </summary>
        public string recordingPath => _recorder.path;

        [LiveProperty]
        public bool isReplaying => _replayer != null;

        [LiveProperty]
        public long replayFrame => _replayer?.frameNumber ?? -1;

        /// <summary>
        /// Holds the replay on the frame it is showing. Scrubbing is a pause plus a
        /// <see cref="SeekReplay"/>, because a position that is being dragged and a recording that
        /// is walking on are two things fighting over the same world.
        ///
        /// Reads false when nothing is being replayed, so a control bound to it does not report a
        /// pause that has nothing to pause.
        ///
        /// Off the live data for the same reason as the settings above, and more sharply: this drives
        /// the replay itself, so a recorded pause would pause the replay that is playing it back.
        /// </summary>
        [LiveProperty(lane = FrameLane.None)]
        public bool replayPaused
        {
            get => _replayer != null && _replayer.isPaused;
            set { if (_replayer != null) _replayer.isPaused = value; }
        }

        /// <summary>
        /// Frames the recording being replayed holds, or zero when nothing is being replayed.
        ///
        /// Zero also for a recording that was cut short: without a tail index there is nothing to
        /// seek by, so it plays from the top and cannot be scrubbed.
        /// </summary>
        [LiveProperty]
        public int replayFrameCount => _replayer?.player.frameCount ?? 0;

        /// <summary>
        /// Position within <see cref="replayFrameCount"/>, or -1 before the first frame.
        ///
        /// A position rather than <see cref="replayFrame"/>, which is the gate's frame number from
        /// the run that was recorded: a scrubber wants 0..count-1, and the two are not the same
        /// number offset by a constant -- a run that drops below rate skips frame numbers, so only
        /// the recording can say where one of them sits.
        /// </summary>
        [LiveProperty]
        public int replayIndex
        {
            get
            {
                var replayer = _replayer;
                if (replayer == null || replayer.frameNumber < 0) return -1;

                return replayer.player.IndexOfFrame(replayer.frameNumber);
            }
        }

        /// <summary>
        /// Moves the replay to a position within <see cref="replayFrameCount"/>. False when there is
        /// no replay, or when the recording carries no index to seek by.
        ///
        /// Does not pause by itself: a seek on a running replay lands and then carries on from
        /// there, which is what a jump means. Pause first for a scrub.
        /// </summary>
        public bool SeekReplay(int index)
        {
            var replayer = _replayer;
            if (replayer == null) return false;

            var count = replayer.player.frameCount;
            if (count <= 0) return false;

            index = Mathf.Clamp(index, 0, count - 1);

            var frame = replayer.player.FrameNumberAt(index);
            return frame >= 0 && replayer.TrySeek(frame);
        }

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
            _recorder.compress = _compress;

            // The recorder's own controls are not part of the take. Recording them means the replay
            // presses them again -- a recorded Record starts a second recording, and a recorded Stop
            // tears down the replay that is running it.
            //
            // This component always, plus whatever registered itself through ExcludeControlObject:
            // the controls and the machinery are separate exposed objects, since a page carries the
            // buttons and the settings while this carries the values they write.
            var count = 1 + _excludedControlObjectIds.Count;
            if (_excludeIds.Length != count) _excludeIds = new string[count];

            _excludeIds[0] = LiveObjectRegistry.FindByTarget(this)?.id;
            for (int i = 0; i < _excludedControlObjectIds.Count; i++)
            {
                _excludeIds[i + 1] = _excludedControlObjectIds[i];
            }

            _recorder.excludeObjectIds = _excludeIds;

            // Nothing carries the state lane on its own -- it costs a copy of every bridged object
            // every frame, and outside a take nothing reads it. Turn it on for the length of one.
            _StartStateSystem();

            _recorder.Start(path);
            FrameGate.sink = _recorder;

            Debug.Log($"[RemoteControl] Recording frames to {path}");
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

            Debug.Log($"[RemoteControl] Recorded {frames} frames ({megabytes:F1} MB) to {path}");
        }

        public void Replay()
        {
            if (_recorder.isRecording) Stop();
            StopReplay();

            if (string.IsNullOrEmpty(_replayFilename))
            {
                Debug.LogWarning("[RemoteControl] No recording selected to replay.");
                return;
            }

            var path = Path.Combine(recordingFolder, _replayFilename);
            if (!File.Exists(path))
            {
                Debug.LogError($"[RemoteControl] Recording not found: {path}");
                return;
            }

            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            _replayer = new FrameReplayer(stream, new LiveEventApplier());

            // Every registered type gets a block up front. Without it the recording reports each type
            // as unknown until something happens to write it live first, which on a machine that is
            // only replaying never happens.
            LiveStateSystem.PrepareBlocks(_replayer.player.state);

            // Running for the opposite reason than during a take: on a supplied frame the state
            // system writes the recorded values back onto the objects instead of reading them off.
            _StartStateSystem();

            // The same reversal for the inventory: it is acted on rather than taken. This is off by
            // default because a viewer watching a replay must not have the recording rearrange the
            // scene it is being watched in -- but a replay is exactly the case that wants it, and
            // without it scrubbing back past a spawn leaves the spawn standing.
            LiveStructureSystem.applyOnSuppliedFrames = true;

            // And the same for time: with this on, the engine's clock steps by the recorded tick, so
            // everything that advances with time -- including code that still reads Time.deltaTime --
            // follows the take rather than whatever this machine managed to render.
            FrameGate.driveEngineTimeOnSuppliedFrames = true;

            // The replay is where the frame comes from now, not something that runs during one. That
            // is what puts it ahead of the producers -- they read the frame it filled rather than
            // racing it in an order nobody declared.
            FrameGate.onSourceEnded += _OnReplayEnded;
            FrameGate.source = _replayer;

            Debug.Log($"[RemoteControl] Replaying {_replayFilename}");
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

            LiveStructureSystem.applyOnSuppliedFrames = false;
            FrameGate.driveEngineTimeOnSuppliedFrames = false;

            // What the replay stood up stays, but stops being the replay's to take away: the next
            // take must not be able to destroy these by not listing them.
            LiveStructureSystem.ForgetMade();

            var applied = replayer.appliedEventCount;
            var failed = replayer.failedEventCount;
            var skipped = replayer.skippedTruncatedCount;

            replayer.Dispose();

            _StopStateSystem();

            Debug.Log($"[RemoteControl] Replay stopped: {applied} events applied, {failed} failed, {skipped} skipped as truncated");
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
