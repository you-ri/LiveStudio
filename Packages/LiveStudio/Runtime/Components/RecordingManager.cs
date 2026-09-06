// Copyright (c) You-Ri, 2026

using System;
using System.Collections.Generic;
using System.IO;

using UnityEngine;

using Lilium.RemoteControl;
using Lilium.RemoteControl.Notification;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// One take on disk, as the recordings page lists it. Deliberately the same shape as
    /// <see cref="SnapshotInfo"/>: the page draws both from one list, so what a card needs to draw
    /// itself must not depend on which of the two it holds.
    /// </summary>
    [Serializable]
    [LiveClass]
    public struct RecordingInfo
    {
        /// <summary>File name without the recording extension. Also the key every call here takes.</summary>
        [LiveField]
        public string name;

        [LiveField]
        public string timestamp;

        [LiveField]
        public bool hasThumbnail;

        /// <summary>
        /// Project-relative reference to the take, which is also its key as a project asset — so a
        /// client fetches its picture from the generic <c>GET /live/asset/{key}/@image</c> like any
        /// other asset. Same reasoning as <see cref="SnapshotInfo.reference"/>, and the same route,
        /// which is what lets one page draw both kinds of card.
        /// </summary>
        [LiveField]
        public string reference;
    }

    /// <summary>
    /// Takes of live data: the app's side of <see cref="FrameRecorderController"/>.
    ///
    /// A take and a snapshot are the same thing at two lengths — a snapshot is live data that lasts
    /// one frame — so they are listed and operated together, and this is the half of that pair the
    /// recordings page drives for takes (<see cref="SnapshotManager"/> is the other). The controller
    /// holds the machinery and has no opinion about projects or listings; this knows where this
    /// application files its takes and what the page needs to draw them.
    ///
    /// Off the live data as a whole. Everything here starts, stops or picks a recording, so a
    /// recorded call would be pressed again by the replay running it: a recorded Record starts a
    /// second recording, a recorded Stop tears down the replay playing it back.
    /// </summary>
    [LiveClass(Icon = "movie", lane = FrameLane.None)]
    public static class RecordingManager
    {
        /// <summary>Extension of a take. One take, one file.</summary>
        public const string kFileExtension = FrameRecorderController.kExtension;

        /// <summary>Extension takes were written with before <see cref="kFileExtension"/>.</summary>
        public const string kLegacyFileExtension = FrameRecorderController.kLegacyExtension;

        /// <summary>Picture of what was on screen when the take began, written beside it.</summary>
        public const string kThumbnailFileExtension = ".live.png";

        /// <summary>
        /// Subfolder of the project that holds the takes — and the snapshots, which are the same
        /// thing one frame long (<see cref="SnapshotManager.kSnapshotDirName"/>). One folder, because
        /// one page lists both.
        ///
        /// The machinery has a default of its own (<see cref="FrameRecorderController.kFolderName"/>,
        /// used when nothing says where to file takes); this is the answer this application installs.
        /// </summary>
        public const string kFolderName = "Recordings";

        /// <summary>
        /// Takes in the open project's recording folder, newest first. Listed by path only (a take is
        /// never opened here — the file is the size of the run it recorded).
        /// </summary>
        [LiveProperty, Hide]
        public static RecordingInfo[] recordings
        {
            get
            {
                var dir = GetRecordingDirectory();
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                    return Array.Empty<RecordingInfo>();

                var files = new List<string>(Directory.GetFiles(dir, "*" + kFileExtension, SearchOption.TopDirectoryOnly));
                files.AddRange(Directory.GetFiles(dir, "*" + kLegacyFileExtension, SearchOption.TopDirectoryOnly));

                var list = new List<RecordingInfo>(files.Count);
                foreach (var file in files)
                {
                    var name = NameFromPath(file);
                    if (string.IsNullOrEmpty(name)) continue;

                    var thumbnailPath = ResolveThumbnailPath(file);
                    list.Add(new RecordingInfo
                    {
                        name = name,
                        timestamp = File.GetLastWriteTime(file).ToString("o"),
                        hasThumbnail = thumbnailPath != null && File.Exists(thumbnailPath),
                        // The folder is always "{project}/LiveData", so the project-relative reference
                        // is this one concat — see SnapshotManager.snapshots for why it is not
                        // relativized path-by-path (this getter is polled while the page is open).
                        reference = kFolderName + "/" + Path.GetFileName(file),
                    });
                }
                // Newest first. The timestamp is ISO 8601, so ordinal string order is time order.
                list.Sort((a, b) => string.CompareOrdinal(b.timestamp, a.timestamp));
                return list.ToArray();
            }
        }

        // ---- Transport ----
        //
        // Plain properties reaching through to the component rather than LivePropertyRef: a ref
        // delegates the value, the dirty flag and the persistence to the component, which a saved
        // setting needs -- and nothing here is saved. The encoding settings (keyframe interval,
        // compression) stay on the component, where they are project settings and appear with the
        // rest of them; they are not something an operator reaches for between takes.

        /// <summary>True while a take is being recorded.</summary>
        [LiveProperty, Hide]
        public static bool isRecording => FrameRecorderController.instance?.isRecording ?? false;

        /// <summary>True while a take is being played back.</summary>
        [LiveProperty, Hide]
        public static bool isReplaying => FrameRecorderController.instance?.isReplaying ?? false;

        /// <summary>
        /// The take being played back, by the same name <see cref="recordings"/> lists it under, or
        /// empty when nothing is playing. So the page can mark the card that is running rather than
        /// keeping its own idea of what it started.
        /// </summary>
        [LiveProperty, Hide]
        public static string playingName
        {
            get
            {
                var recorder = FrameRecorderController.instance;
                if (recorder == null || !recorder.isReplaying) return string.Empty;

                return NameFromPath(recorder.replayFilename) ?? string.Empty;
            }
        }

        /// <summary>
        /// Holds the replay on the frame it is showing.
        ///
        /// Reads false when nothing is being played back, so a control bound to it does not offer a
        /// pause that has nothing to pause.
        /// </summary>
        [LiveProperty, Hide]
        public static bool paused
        {
            get => FrameRecorderController.instance?.replayPaused ?? false;
            set
            {
                var recorder = FrameRecorderController.instance;
                if (recorder != null) recorder.replayPaused = value;
            }
        }

        /// <summary>Plays the take over and over instead of stopping at the end.</summary>
        [LiveProperty, Hide]
        public static bool loop
        {
            get => FrameRecorderController.instance?.loop ?? false;
            set
            {
                var recorder = FrameRecorderController.instance;
                if (recorder != null) recorder.loop = value;
            }
        }

        /// <summary>
        /// Position in the take being played back, 0 at the first frame and 1 at the last.
        ///
        /// Normalized rather than a frame index because a slider is what the operator gets: the
        /// remote app has no way to learn the length of a take before it draws the control.
        ///
        /// Reads 0 with nothing playing, and with a take that carries no index to seek by — writing
        /// it then does nothing rather than reporting a position that cannot be reached.
        ///
        /// Writing it pauses the replay. Dragging a position and a recording walking on are two
        /// things fighting over the same world, so a scrub holds the take where it was let go; the
        /// operator carries on with the play button. The machinery deliberately does not decide this
        /// (<see cref="FrameRecorderController.SeekReplay"/> lands and carries on, which is what a
        /// jump means) — pausing is what this product's transport does with a drag, so it is said here.
        /// </summary>
        [LiveProperty, Hide]
        public static float seekProgress
        {
            get
            {
                var recorder = FrameRecorderController.instance;
                if (recorder == null) return 0f;

                var count = recorder.replayFrameCount;
                if (count <= 1) return 0f;

                var index = recorder.replayIndex;
                if (index < 0) return 0f;

                return Mathf.Clamp01((float)index / (count - 1));
            }
            set
            {
                var recorder = FrameRecorderController.instance;
                if (recorder == null) return;

                var count = recorder.replayFrameCount;
                if (count <= 0) return;

                // Held before the jump, not after: the frames between the write arriving and the
                // pause landing would otherwise play at the old position.
                recorder.replayPaused = true;
                recorder.SeekReplay(Mathf.RoundToInt(Mathf.Clamp01(value) * (count - 1)));
            }
        }

        /// <summary>Starts recording a take. Named after the take number the component keeps.</summary>
        [LiveFunction(label = "RECORD", icon = "fiber_manual_record"), Hide]
        public static void Record()
        {
            var recorder = FrameRecorderController.instance;
            if (recorder == null)
            {
                Debug.LogError("[Studio] Record: no FrameRecorderController in the scene.");
                return;
            }

            // Already recording: the component ignores this, so saying a take started would be a lie.
            // A second press reaches here whenever two clients hold the page open at once.
            if (recorder.isRecording) return;

            recorder.Record();

            // The file exists from here on, so the list has to show it being written.
            _BroadcastRecordings();
            RemoteNotificationSystem.Show(
                LocalizationSystem.Translate("NOTIFY_RECORDING_STARTED"),
                RemoteNotificationSystem.Type.Success,
                icon: "fiber_manual_record");
        }

        /// <summary>
        /// Stops whichever of the two is running. One call rather than a stop each, because an
        /// operator stops what is happening and only this knows which that was — the component
        /// already tears down a replay before it stops a recording.
        /// </summary>
        [LiveFunction(label = "STOP", icon = "stop"), Hide]
        public static void Stop()
        {
            var recorder = FrameRecorderController.instance;
            if (recorder == null) return;

            var wasRecording = recorder.isRecording;

            recorder.Stop();

            if (!wasRecording) return;

            // The take is finished (and has its final length), and it is a project file now.
            _BroadcastRecordings();
            ProjectManager.RecrawlProject();
        }

        /// <summary>Plays the take back, by the name <see cref="recordings"/> lists it under.</summary>
        [LiveFunction(label = "REPLAY", icon = "play_arrow"), Hide]
        public static void Play(string name)
        {
            var filePath = _ResolveExistingFile(name);
            if (filePath == null) return;

            var recorder = FrameRecorderController.instance;
            if (recorder == null)
            {
                Debug.LogError("[Studio] Play: no FrameRecorderController in the scene.");
                return;
            }

            recorder.replayFilename = Path.GetFileName(filePath);
            recorder.Replay();
        }

        /// <summary>Deletes the take and the picture beside it.</summary>
        [LiveFunction(label = "RECORDING_DELETE", icon = "delete"), Hide]
        public static void DeleteRecording(string name)
        {
            var filePath = _ResolveExistingFile(name);
            if (filePath == null) return;

            var recorder = FrameRecorderController.instance;
            // Deleting the file being written, or the one being played, leaves the stream reading a
            // file that is no longer there. Stop first -- the operator asked for the take to go.
            if (recorder != null &&
                (string.Equals(recorder.recordingPath, filePath, StringComparison.OrdinalIgnoreCase) ||
                 (recorder.isReplaying &&
                  string.Equals(recorder.replayFilename, Path.GetFileName(filePath), StringComparison.OrdinalIgnoreCase))))
            {
                recorder.Stop();
            }

            File.Delete(filePath);
            var thumbnailPath = ResolveThumbnailPath(filePath);
            if (thumbnailPath != null && File.Exists(thumbnailPath)) File.Delete(thumbnailPath);

            Debug.Log($"[Studio] Recording deleted: '{filePath}'");
            _BroadcastRecordings();
            ProjectManager.RecrawlProject();
        }

        /// <summary>
        /// The picture belonging to the take at <paramref name="recordingFilePath"/> (same folder,
        /// <c>.live.png</c> in place of the recording extension), or null when the path is not a take.
        /// Derived from the path rather than the name so it holds for any take the project crawl
        /// found. The file may not exist — the caller decides what "no picture" means.
        /// </summary>
        public static string ResolveThumbnailPath(string recordingFilePath)
        {
            var extension = _MatchExtension(recordingFilePath);
            if (extension == null) return null;

            return recordingFilePath.Substring(0, recordingFilePath.Length - extension.Length)
                + kThumbnailFileExtension;
        }

        /// <summary>
        /// True when the path names a take. Path-only (the content is never read), so the project
        /// crawl can classify takes like every other asset kind.
        /// </summary>
        public static bool IsRecordingFile(string filePath) => _MatchExtension(filePath) != null;

        /// <summary>
        /// "&lt;name&gt;.live.bin" — or a legacy take's file name — as the page names it, or null when
        /// the file is not a take.
        /// </summary>
        public static string NameFromPath(string filePath)
        {
            var extension = _MatchExtension(filePath);
            if (extension == null) return null;

            var fileName = Path.GetFileName(filePath);
            return fileName.Substring(0, fileName.Length - extension.Length);
        }

        /// <summary>Where this application's takes are, or null when nothing has said.</summary>
        public static string GetRecordingDirectory() => FrameRecorderController.recordingFolder;

        // The recording extension the path ends with, or null for a file that is not a take.
        private static string _MatchExtension(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return null;
            if (filePath.EndsWith(kFileExtension, StringComparison.OrdinalIgnoreCase)) return kFileExtension;
            if (filePath.EndsWith(kLegacyFileExtension, StringComparison.OrdinalIgnoreCase)) return kLegacyFileExtension;
            return null;
        }

        /// <summary>
        /// Resolves a take's name to the file that holds it, or null when there is none.
        ///
        /// Names arrive from remote apps, so a name that is not a plain file name is rejected rather
        /// than resolved — it must never reach outside the recording folder. Both extensions are
        /// tried, so a legacy take answers to the same name the list showed.
        /// </summary>
        internal static string _ResolveExistingFile(string name)
        {
            var dir = GetRecordingDirectory();
            if (string.IsNullOrEmpty(dir))
            {
                Debug.LogError("[Studio] Recording: no recording folder.");
                return null;
            }
            if (string.IsNullOrEmpty(name) || name != Path.GetFileName(name) || name.Contains(".."))
            {
                Debug.LogError($"[Studio] Recording: invalid recording name: '{name}'");
                return null;
            }

            var path = Path.Combine(dir, name + kFileExtension);
            if (File.Exists(path)) return path;

            var legacyPath = Path.Combine(dir, name + kLegacyFileExtension);
            if (File.Exists(legacyPath)) return legacyPath;

            Debug.LogError($"[Studio] Recording: recording not found: '{path}'");
            return null;
        }

        private static void _BroadcastRecordings()
            => LivePropertyBroadcast.BroadcastStaticProperty(typeof(RecordingManager), nameof(recordings));
    }
}
