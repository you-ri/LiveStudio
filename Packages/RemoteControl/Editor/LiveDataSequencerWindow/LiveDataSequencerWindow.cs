// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Lilium.RemoteControl.Frames;
using Lilium.RemoteControl.Frames.Recording;

namespace Lilium.RemoteControl.Editor.LiveDataSequencer
{
    /// <summary>
    /// Drives a take: records live data, plays one back, and moves through it.
    ///
    /// The counterpart of the LiveData Viewer, and built to look like it on purpose. The viewer
    /// answers "what is going through the gate"; this one answers "record that, and put it back".
    /// Keeping them apart is what keeps either readable -- a window that both watches a lane and
    /// drives the transport is one where the thing that moved and the thing that moved it cannot be
    /// told apart.
    ///
    /// Everything here reaches through to the <see cref="FrameRecorderController"/> in the scene.
    /// This window owns no recorder of its own: a take is recorded by the running application, not by
    /// the editor watching it, and a second recorder would fight the first over the gate's one sink
    /// -- which is also why a page in the remote app and this window can be open at once.
    ///
    /// Rows are built once and then written into. Rebuilding them each redraw is the obvious way and
    /// the wrong one: the row under the pointer is destroyed and remade ten times a second, so it
    /// flickers and never settles into its hover state.
    /// </summary>
    public sealed class LiveDataSequencerWindow : EditorWindow
    {
        private const string kStyleSheet = "Editor/LiveDataSequencerWindow/LiveDataSequencerWindow.uss";

        // Frames arrive at sixty a second and nothing here is worth drawing that often.
        private const double kRedrawInterval = 0.1;

        // How often the folder is looked at again. Separate from the redraw because it touches the
        // disk: a take appears when one is finished, which is not something to check ten times a
        // second.
        private const double kFolderPollInterval = 1.0;

        private const string kTimecodePref = "Lilium.LiveStudio.LiveDataSequencer.showTimecode";

        /// <summary>One recording on disk, as the list shows it.</summary>
        private sealed class Take
        {
            public string path;
            public string name;
            public long bytes;
            public DateTime modified;

            /// <summary>Frames the tail index knows about, or 0 when there is no index to ask.</summary>
            public int frames;

            public double seconds;

            /// <summary>True when the file carries a tail, so it was closed rather than cut short.</summary>
            public bool complete;

            /// <summary>Why the file could not be read, or null. Shown rather than hidden.</summary>
            public string problem;
        }

        private sealed class TakeRowView
        {
            public VisualElement root;
            public Label name;
            public Label frames;
            public Label size;
            public Button delete;
            public string path;
        }

        private Label _positionLabel;
        private Label _rateLabel;
        private Label _recordPill;
        private Label _replayPill;
        private Label _takeLabel;
        private Label _detailLabel;

        private VisualElement _banners;

        private Button _recordButton;
        private Button _playButton;
        private Button _holdButton;
        private Button _stepBackButton;
        private Button _stepForwardButton;
        private SliderInt _slider;
        private Label _sliderPosition;

        private Label _takeCount;
        private Button _revealButton;
        private VisualElement _takeList;

        private readonly List<Take> _takes = new List<Take>();
        private readonly List<TakeRowView> _takeRows = new List<TakeRowView>();

        /// <summary>
        /// What has been read off each file, kept between refreshes.
        ///
        /// A probe opens the file and reads its tail, which is cheap but not free, and the folder is
        /// looked at every second. An entry is trusted while the length and the write time it was
        /// taken at still hold.
        /// </summary>
        private readonly Dictionary<string, Take> _probed = new Dictionary<string, Take>();

        private readonly List<string> _bannerText = new List<string>();
        private readonly StringBuilder _shape = new StringBuilder();

        private string _selectedPath;
        private string _listShape;
        private string _bannerShape;

        private bool _showTimecode;
        private double _nextRedraw;
        private double _nextFolderPoll;
        private DateTime _folderStamp;
        private string _folder = string.Empty;

        [MenuItem("Window/Lilium Remote Control/LiveData Sequencer")]
        public static void ShowWindow()
        {
            var window = GetWindow<LiveDataSequencerWindow>();
            window.titleContent = new GUIContent("LiveData Sequencer");
            window.minSize = new Vector2(620, 320);
        }

        private void OnEnable()
        {
            _showTimecode = EditorPrefs.GetBool(kTimecodePref, false);
            EditorApplication.update += _OnUpdate;
        }

        private void OnDisable() => EditorApplication.update -= _OnUpdate;

        /// <summary>Takes appear while another window has focus, so this is where the list catches up.</summary>
        private void OnFocus() => _RefreshTakes(force: true);

        private void _OnUpdate()
        {
            var now = EditorApplication.timeSinceStartup;

            if (now >= _nextFolderPoll)
            {
                _nextFolderPoll = now + kFolderPollInterval;
                _RefreshTakes(force: false);
            }

            if (now < _nextRedraw) return;
            _nextRedraw = now + kRedrawInterval;

            // Everything drawn here moves without anything in this window happening -- the take
            // grows while it records, the position walks while it replays -- so the whole chrome is
            // refreshed on the clock rather than on a change.
            _DrawStatus();
            _DrawBanners();
            _DrawTransport();
            _DrawTakes();
        }

        private void CreateGUI()
        {
            var root = rootVisualElement;

            RemoteControlEditorStyles.Apply(root, kStyleSheet);

            root.AddToClassList("lds-root");

            root.Add(_BuildStatusBar());

            _banners = new VisualElement();
            root.Add(_banners);

            root.Add(_BuildTransport());
            root.Add(_BuildTakeLane());

            _RefreshTakes(force: true);

            _listShape = null;
            _bannerShape = null;
            _DrawStatus();
            _DrawBanners();
            _DrawTransport();
            _DrawTakes();
        }

        // --- chrome -------------------------------------------------------

        private VisualElement _BuildStatusBar()
        {
            var bar = new VisualElement();
            bar.AddToClassList("lds-status");

            _positionLabel = _AddStatus(bar, "lds-num");
            _positionLabel.tooltip = "クリックでフレーム番号とタイムコードを切り替えます";
            RemoteControlEditorFonts.ApplyMonospace(_positionLabel);
            _positionLabel.RegisterCallback<MouseDownEvent>(_ => _TogglePosition());

            _rateLabel = _AddStatus(bar, "lds-status-item");

            _recordPill = _AddStatus(bar, "lds-pill");
            _replayPill = _AddStatus(bar, "lds-pill");

            var spacer = new VisualElement();
            spacer.AddToClassList(RemoteControlEditorStyles.kSpacer);
            bar.Add(spacer);

            _detailLabel = _AddStatus(bar, "lds-status-item");
            _detailLabel.AddToClassList(RemoteControlEditorStyles.kSubtle);

            _takeLabel = _AddStatus(bar, "lds-status-item");
            _takeLabel.AddToClassList(RemoteControlEditorStyles.kSubtle);

            return bar;
        }

        private static Label _AddStatus(VisualElement parent, string className)
        {
            var label = new Label();
            label.AddToClassList(className);
            parent.Add(label);
            return label;
        }

        private void _TogglePosition()
        {
            _showTimecode = !_showTimecode;
            EditorPrefs.SetBool(kTimecodePref, _showTimecode);
            _DrawStatus();
        }

        /// <summary>
        /// The transport: the take on the left, the replay on the right, and the position between
        /// them.
        ///
        /// Always present and never hidden, only disabled. A control that appears once you already
        /// know the feature exists is a control nobody finds -- and here, greyed out is itself part
        /// of the answer to "why can I not record", which the banner underneath then spells out.
        /// </summary>
        private VisualElement _BuildTransport()
        {
            var transport = new VisualElement();
            transport.AddToClassList("lds-transport");

            _recordButton = new Button(_ToggleRecord) { text = "● 収録" };
            _recordButton.AddToClassList("lds-record");
            transport.Add(_recordButton);

            var divider = new VisualElement();
            divider.AddToClassList("lds-divider");
            transport.Add(divider);

            _playButton = new Button(_TogglePlay) { text = "▶ 再生" };
            _playButton.AddToClassList("lds-play");
            transport.Add(_playButton);

            _holdButton = new Button(_ToggleHold) { text = "❙❙", tooltip = "一時停止" };
            transport.Add(_holdButton);

            _stepBackButton = new Button(() => _Step(-1)) { text = "◀", tooltip = "1 フレーム戻る" };
            transport.Add(_stepBackButton);

            _stepForwardButton = new Button(() => _Step(1)) { text = "▶", tooltip = "1 フレーム進む" };
            transport.Add(_stepForwardButton);

            _slider = new SliderInt(0, 0);
            _slider.AddToClassList("lds-slider");

            // Only a drag the user made moves the replay. Writing the slider's value while drawing
            // would otherwise feed straight back in as a seek and fight whatever is being dragged --
            // which is why every other write to it goes through SetValueWithoutNotify.
            _slider.RegisterValueChangedCallback(e => _Scrub(e.newValue));
            transport.Add(_slider);

            _sliderPosition = new Label();
            _sliderPosition.AddToClassList("lds-num");
            _sliderPosition.AddToClassList("lds-position");
            RemoteControlEditorFonts.ApplyMonospace(_sliderPosition);
            transport.Add(_sliderPosition);

            return transport;
        }

        private VisualElement _BuildTakeLane()
        {
            var lane = new VisualElement();
            lane.AddToClassList(RemoteControlEditorStyles.kPane);

            var header = new VisualElement();
            header.AddToClassList("lds-lane-header");

            var title = new Label("収録ファイル");
            title.AddToClassList(RemoteControlEditorStyles.kTitle);
            header.Add(title);

            _takeCount = new Label();
            _takeCount.AddToClassList(RemoteControlEditorStyles.kSubtle);
            _takeCount.style.marginLeft = 8;
            header.Add(_takeCount);

            var spacer = new VisualElement();
            spacer.AddToClassList(RemoteControlEditorStyles.kSpacer);
            header.Add(spacer);

            header.Add(new Button(() => _RefreshTakes(force: true)) { text = "更新" });

            // Named for what it opens rather than what it is. "フォルダ" beside a list of files reads
            // as a column header, and a button nobody reads as a button is a button nobody presses.
            _revealButton = new Button(_RevealFolder) { text = "フォルダを開く" };
            header.Add(_revealButton);

            lane.Add(header);

            var scroll = new ScrollView();
            scroll.AddToClassList("lds-page");
            _takeList = scroll.contentContainer;
            lane.Add(scroll);

            return lane;
        }

        // --- transport actions --------------------------------------------

        /// <summary>
        /// The recorder in the scene, or null.
        ///
        /// Only while playing: the gate is pumped from the player loop, so a recorder found in a
        /// stopped editor is an object that cannot record. Answering null there is what makes every
        /// control disable itself for the right reason.
        /// </summary>
        private static FrameRecorderController _Controller
            => EditorApplication.isPlaying ? FrameRecorderController.instance : null;

        private void _ToggleRecord()
        {
            var controller = _Controller;
            if (controller == null) return;

            if (controller.isRecording) controller.Stop();
            else controller.Record();

            // A take that just finished is a file that just appeared, and waiting a second for the
            // poll to notice reads as the recording having gone nowhere.
            _RefreshTakes(force: true);
        }

        private void _TogglePlay()
        {
            var controller = _Controller;
            if (controller == null) return;

            if (controller.isReplaying)
            {
                controller.StopReplay();
                return;
            }

            if (string.IsNullOrEmpty(_selectedPath))
            {
                EditorUtility.DisplayDialog("LiveData Sequencer", "再生する収録を選択してください。", "OK");
                return;
            }

            controller.replayFilename = Path.GetFileName(_selectedPath);
            controller.Replay();
        }

        private void _ToggleHold()
        {
            var controller = _Controller;
            if (controller == null || !controller.isReplaying) return;

            controller.replayPaused = !controller.replayPaused;
        }

        private void _Step(int delta)
        {
            var controller = _Controller;
            if (controller == null || !controller.isReplaying) return;

            // Stepping holds for the same reason a drag does: a step taken while the recording walks
            // on lands one frame from wherever it had already got to, not from what was on screen.
            controller.replayPaused = true;

            var index = controller.replayIndex;
            if (index < 0) index = 0;

            controller.SeekReplay(index + delta);
        }

        private void _Scrub(int index)
        {
            var controller = _Controller;
            if (controller == null || !controller.isReplaying) return;
            if (index == controller.replayIndex) return;

            controller.replayPaused = true;
            controller.SeekReplay(index);
        }

        /// <summary>
        /// Opens the folder takes are written to in the OS file browser.
        ///
        /// Created first, because the folder only comes into being when something is recorded --
        /// and "open" quietly doing nothing is the worst answer to "where are my takes".
        /// </summary>
        private void _RevealFolder()
        {
            var folder = FrameRecorderController.recordingFolder;

            Directory.CreateDirectory(folder);
            EditorUtility.RevealInFinder(folder);
        }

        /// <summary>
        /// Deletes one take from disk, after asking.
        ///
        /// Straight off the disk rather than into the recycle bin: these live outside the Unity
        /// project, so the editor's own trash does not reach them. Hence the confirmation, which
        /// names the file -- it is the only thing standing between a mis-click and a lost take.
        /// </summary>
        private void _DeleteTake(Take take)
        {
            var controller = _Controller;

            // The row's button is disabled while its take is being written, but only as of the last
            // redraw -- a take started within the last tenth of a second still has a live button.
            if (controller != null && controller.isRecording &&
                string.Equals(take.path, controller.recordingPath, StringComparison.OrdinalIgnoreCase))
            {
                EditorUtility.DisplayDialog("LiveData Sequencer",
                    "収録中のファイルは削除できません。先に収録を停止してください。", "OK");
                return;
            }

            var replayingThis = controller != null && controller.isReplaying &&
                                string.Equals(take.name, controller.replayFilename,
                                    StringComparison.OrdinalIgnoreCase);

            var message = replayingThis
                ? $"{take.name} を削除しますか?\n\n再生中の収録です。再生を停止してから削除します。\n元に戻せません。"
                : $"{take.name} を削除しますか?\n\n元に戻せません。";

            if (!EditorUtility.DisplayDialog("LiveData Sequencer", message, "削除", "キャンセル")) return;

            // The replayer holds the file open, and Windows refuses a delete outright while a handle
            // is on it rather than deferring one -- so it has to let go first.
            if (replayingThis) controller.StopReplay();

            try
            {
                File.Delete(take.path);
            }
            catch (Exception exception) when (exception is IOException ||
                                              exception is UnauthorizedAccessException)
            {
                // Shown rather than logged: the user asked for this file by name, so the answer
                // belongs where they asked.
                EditorUtility.DisplayDialog("LiveData Sequencer",
                    $"削除できませんでした。\n\n{_Reason(exception)}", "OK");
                return;
            }

            _probed.Remove(take.path);
            if (_selectedPath == take.path) _selectedPath = null;

            _RefreshTakes(force: true);

            // The rows are rebuilt on the next tick rather than here. This runs inside the deleted
            // row's own click, and clearing the list would destroy the element the event is still
            // being dispatched to.
            _listShape = null;
        }

        // --- status -------------------------------------------------------

        private void _DrawStatus()
        {
            if (_positionLabel == null) return;

            var controller = _Controller;
            var recording = controller != null && controller.isRecording;
            var replaying = controller != null && controller.isReplaying;
            var held = replaying && controller.replayPaused;

            var rate = FrameGate.clock.frameRate;

            if (recording)
            {
                _SetPosition(controller.recordedFrames, rate);
                _detailLabel.text =
                    $"{controller.recordedFrames} フレーム / {controller.recordedMegabytes:0.0} MB";
            }
            else if (replaying)
            {
                var index = controller.replayIndex;
                _SetPosition(Math.Max(index, 0), rate);

                var count = controller.replayFrameCount;
                _detailLabel.text = count > 0
                    ? $"{Math.Max(index, 0) + 1} / {count} フレーム"
                    : "索引なし (先頭から再生)";
            }
            else
            {
                _positionLabel.text = _showTimecode ? "--:--:--:--" : "--------";
                _detailLabel.text = EditorApplication.isPlaying ? "待機中" : "停止中";
            }

            _rateLabel.text = $"{rate.AsDecimal():0.##} fps";

            _SetPill(_recordPill, "収録", recording, "lds-pill-rec");

            // Two classes on one pill, so whichever the state does not call for has to come off --
            // otherwise a replay that was paused once keeps the amber it was given.
            _replayPill.text = held ? "再生 (一時停止)" : "再生";
            _replayPill.EnableInClassList("lds-pill-play", replaying && !held);
            _replayPill.EnableInClassList("lds-pill-hold", held);
            _replayPill.EnableInClassList(RemoteControlEditorStyles.kSubtle, !replaying);

            _takeLabel.text = controller != null ? $"次のテイク {controller.take:D3}" : string.Empty;
        }

        /// <summary>
        /// Writes the position, counted from the start of the take rather than from the start of the
        /// run.
        ///
        /// The gate's own frame number is what a recording stores, and it is whatever the clock had
        /// reached when the take began -- a session running for a while reads as seventeen hours in,
        /// which is true and useless. What is being moved through here is the take.
        /// </summary>
        private void _SetPosition(long frames, FrameRate rate)
        {
            RemoteControlEditorFonts.ApplyMonospace(_positionLabel);
            _positionLabel.text = _showTimecode
                ? new Timecode(frames, rate).ToSmpteString()
                : frames.ToString("D8");
        }

        private static void _SetPill(Label pill, string text, bool on, string onClass)
        {
            pill.text = text;
            pill.EnableInClassList(onClass, on);
            pill.EnableInClassList(RemoteControlEditorStyles.kSubtle, !on);
        }

        // --- banners ------------------------------------------------------

        private void _DrawBanners()
        {
            if (_banners == null) return;

            _bannerText.Clear();

            var controller = _Controller;

            // The first character says which of the two kinds a line is, so the list stays a list of
            // strings and the shape comparison below keeps working on it unchanged.
            if (!EditorApplication.isPlaying)
            {
                _bannerText.Add("!収録と再生は再生モード中のみ動作します (ゲートはプレイヤーループから回ります)。");
            }
            else if (controller == null)
            {
                _bannerText.Add(
                    "#シーンに FrameRecorderController がありません。" +
                    "収録・再生するアプリの GameObject に追加してください。");
            }

            if (controller != null && controller.isReplaying && controller.replayFrameCount == 0)
            {
                _bannerText.Add(
                    "!この収録には索引がありません (収録が正常に終了していない可能性があります)。" +
                    "先頭から再生はできますが、スクラブはできません。");
            }

            // The failure that costs a day: a recording carries a type the playing side cannot hold,
            // so the whole lane is dropped and the replay simply shows nothing.
            if (FrameGate.source is FrameReplayer replayer)
            {
                var unknown = replayer.player.unknownStateTypes;
                if (unknown.Count > 0)
                {
                    _bannerText.Add(
                        "#この収録が運んでいる状態のうち、受け皿が無いものがあります: " +
                        string.Join(", ", unknown) + "  → その分は再生されません。");
                }
            }

            var shape = string.Join("|", _bannerText);
            if (shape == _bannerShape) return;
            _bannerShape = shape;

            _banners.Clear();
            for (int i = 0; i < _bannerText.Count; i++)
            {
                var text = _bannerText[i];
                var danger = text[0] == '#';

                var label = new Label(text.Substring(1));
                label.AddToClassList("lds-banner");
                label.AddToClassList(danger ? "lds-banner-danger" : "lds-banner-warning");
                _banners.Add(label);
            }
        }

        // --- transport ----------------------------------------------------

        private void _DrawTransport()
        {
            if (_recordButton == null) return;

            var controller = _Controller;
            var live = controller != null;
            var recording = live && controller.isRecording;
            var replaying = live && controller.isReplaying;
            var held = replaying && controller.replayPaused;
            var count = replaying ? controller.replayFrameCount : 0;

            _recordButton.SetEnabled(live);
            _recordButton.text = recording ? "■ 停止" : "● 収録";
            _recordButton.EnableInClassList("lds-record-on", recording);

            _playButton.SetEnabled(live && (replaying || !string.IsNullOrEmpty(_selectedPath)));
            _playButton.text = replaying ? "■ 停止" : "▶ 再生";
            _playButton.EnableInClassList("lds-play-on", replaying);

            _holdButton.SetEnabled(replaying);
            _holdButton.EnableInClassList("lds-hold-on", held);

            // Seeking needs the tail index, so a take that was cut short plays but does not scrub.
            var seekable = replaying && count > 0;
            _stepBackButton.SetEnabled(seekable);
            _stepForwardButton.SetEnabled(seekable);
            _slider.SetEnabled(seekable);

            var high = Mathf.Max(count - 1, 0);
            if (_slider.highValue != high) _slider.highValue = high;

            var index = replaying ? controller.replayIndex : -1;

            if (!seekable) _slider.SetValueWithoutNotify(0);
            else if (index >= 0 && _slider.value != index) _slider.SetValueWithoutNotify(index);

            _sliderPosition.text = seekable ? $"{Math.Max(index, 0) + 1} / {count}" : "- / -";
        }

        // --- take list ----------------------------------------------------

        /// <summary>
        /// Reads the folder again.
        ///
        /// Cheap unless something changed: the folder's own write time moves when a take is added or
        /// removed, so the usual poll costs one stat call. <paramref name="force"/> is for the times
        /// that is not enough -- a take rewritten in place, or the folder itself changing because
        /// the open project did.
        /// </summary>
        private void _RefreshTakes(bool force)
        {
            var folder = FrameRecorderController.recordingFolder;

            if (!Directory.Exists(folder))
            {
                if (_takes.Count == 0 && _folder == folder) return;

                _folder = folder;
                _takes.Clear();
                _folderStamp = default;
                return;
            }

            var stamp = Directory.GetLastWriteTimeUtc(folder);
            if (!force && folder == _folder && stamp == _folderStamp) return;

            _folder = folder;
            _folderStamp = stamp;

            var controller = _Controller;
            var recordingPath = controller != null && controller.isRecording ? controller.recordingPath : null;

            _takes.Clear();
            var files = Directory.GetFiles(folder, "*" + FrameRecorderController.kExtension);
            for (int i = 0; i < files.Length; i++) _takes.Add(_Describe(files[i], recordingPath));

            _takes.Sort((a, b) => b.modified.CompareTo(a.modified));

            // A take that is gone is not a selection. Left standing, the play button stays lit for a
            // file that no longer exists and the replay fails with a path nobody chose.
            if (!string.IsNullOrEmpty(_selectedPath) && !File.Exists(_selectedPath)) _selectedPath = null;
        }

        private Take _Describe(string path, string recordingPath)
        {
            var info = new FileInfo(path);

            if (_probed.TryGetValue(path, out var cached) &&
                cached.bytes == info.Length &&
                cached.modified == info.LastWriteTime)
            {
                return cached;
            }

            var take = new Take
            {
                path = path,
                name = Path.GetFileName(path),
                bytes = info.Length,
                modified = info.LastWriteTime,
            };

            // The take being written is not probed and not cached. It has no tail yet, so there is
            // nothing to read, and every frame changes its length -- which would re-probe it for an
            // answer that is always the same.
            if (string.Equals(path, recordingPath, StringComparison.OrdinalIgnoreCase))
            {
                take.problem = "収録中";
                return take;
            }

            _Probe(take);
            _probed[path] = take;
            return take;
        }

        /// <summary>
        /// Reads what the file says about itself: how many frames it holds and at what rate.
        ///
        /// Shared read access, because a take may still be open elsewhere. A file that cannot be read
        /// is reported on its own row rather than dropped from the list -- a take that is there but
        /// unreadable and a take that was never written look identical once it is missing, and those
        /// are very different problems.
        /// </summary>
        private static void _Probe(Take take)
        {
            try
            {
                using (var stream = new FileStream(take.path, FileMode.Open, FileAccess.Read,
                           FileShare.ReadWrite))
                using (var reader = new FrameRecordReader(stream))
                {
                    take.complete = reader.hasIndex;
                    take.frames = reader.indexedFrameCount;
                    take.seconds = reader.header.frameRate.AsSecounds(take.frames);
                }
            }
            catch (InvalidDataException exception)
            {
                // Not a recording, or one from a format this build cannot read. Expected input rather
                // than a fault: the folder is a folder, and anything can be put in it.
                take.problem = _Reason(exception);
            }
            catch (IOException exception)
            {
                take.problem = _Reason(exception);
            }
        }

        /// <summary>
        /// The message without the package tag it was logged with.
        ///
        /// The tag says which package is speaking, which is worth having in a console shared by
        /// everything -- and is pure noise on a row inside that package's own window, where it costs
        /// the width the reason itself needs.
        /// </summary>
        private static string _Reason(Exception exception)
        {
            const string kTag = "[RemoteControl] ";
            var message = exception.Message;

            return message.StartsWith(kTag, StringComparison.Ordinal)
                ? message.Substring(kTag.Length)
                : message;
        }

        private void _DrawTakes()
        {
            if (_takeList == null) return;

            var controller = _Controller;

            var activePath = controller != null && controller.isReplaying
                ? Path.Combine(FrameRecorderController.recordingFolder, controller.replayFilename)
                : null;
            var recordingPath = controller != null && controller.isRecording ? controller.recordingPath : null;

            _takeCount.text = $"{_takes.Count} 件";

            // The folder moves with the open project, so the tooltip is the one place the path is
            // actually readable before pressing anything.
            if (_revealButton != null) _revealButton.tooltip = FrameRecorderController.recordingFolder;

            var shape = _Shape();
            if (shape != _listShape)
            {
                _listShape = shape;
                _RebuildTakes();
            }

            for (int i = 0; i < _takeRows.Count; i++)
            {
                var view = _takeRows[i];

                var recordingThis = string.Equals(view.path, recordingPath, StringComparison.OrdinalIgnoreCase);

                view.root.EnableInClassList("lds-take-selected", view.path == _selectedPath);
                view.root.EnableInClassList("lds-take-active",
                    !recordingThis && string.Equals(view.path, activePath, StringComparison.OrdinalIgnoreCase));
                view.root.EnableInClassList("lds-take-recording", recordingThis);

                // A file being written cannot go anywhere, and offering it is offering a failure.
                view.delete.SetEnabled(!recordingThis);

                // Only the take being written moves while the list stands, so it is the one row whose
                // numbers are worth rewriting on every redraw.
                if (!recordingThis) continue;

                view.frames.text = controller.recordedFrames.ToString();
                view.size.text = _Bytes(_Length(view.path));
            }
        }

        /// <summary>What the list is made of. Its rows are rebuilt when this moves, and written into otherwise.</summary>
        private string _Shape()
        {
            _shape.Clear();
            _shape.Append(RemoteControlEditorFonts.generation);

            for (int i = 0; i < _takes.Count; i++)
            {
                var take = _takes[i];
                _shape.Append('|').Append(take.path).Append(':').Append(take.frames).Append(take.problem);
            }

            return _shape.ToString();
        }

        private void _RebuildTakes()
        {
            _takeList.Clear();
            _takeRows.Clear();

            if (_takes.Count == 0)
            {
                var empty = new Label($"収録がまだありません。\n{FrameRecorderController.recordingFolder}");
                empty.AddToClassList("lds-empty");
                empty.AddToClassList(RemoteControlEditorStyles.kSubtle);
                _takeList.Add(empty);
                return;
            }

            for (int i = 0; i < _takes.Count; i++)
            {
                var view = _BuildTakeRow(_takes[i]);
                _takeRows.Add(view);
                _takeList.Add(view.root);
            }
        }

        private TakeRowView _BuildTakeRow(Take take)
        {
            var row = new VisualElement();
            row.AddToClassList("lds-take");

            var view = new TakeRowView { root = row, path = take.path };

            view.name = new Label(take.name);
            view.name.AddToClassList("lds-take-name");
            view.name.tooltip = take.problem == null
                ? take.path
                : take.path + Environment.NewLine + take.problem;
            row.Add(view.name);

            // A take that could not be read says so where its numbers would have been, and one that
            // was cut short has no tail to count from -- both are unknown rather than zero, which is
            // a distinction the columns are the only place on the row to make. A take that closed
            // properly with nothing in it is a different thing and says 0.
            var unreadable = take.problem != null;
            var counted = !unreadable && take.complete;

            view.frames = _Column(row, "lds-col-frames", counted ? take.frames.ToString() : "—");

            _Column(row, "lds-col-duration", counted ? _Duration(take.seconds) : "—");

            view.size = _Column(row, "lds-col-size", _Bytes(take.bytes));
            _Column(row, "lds-col-date", take.modified.ToString("MM/dd HH:mm:ss"));

            view.delete = new Button(() => _DeleteTake(take)) { text = "✕", tooltip = "削除" };
            view.delete.AddToClassList("lds-delete");

            // The row underneath selects on mouse down, and a double click on it plays. Neither is
            // wanted from the button that removes the file, so the press stops here.
            view.delete.RegisterCallback<MouseDownEvent>(e => e.StopPropagation());
            row.Add(view.delete);

            if (unreadable)
            {
                view.name.text += $"  ({take.problem})";
                view.name.AddToClassList(RemoteControlEditorStyles.kWarning);
            }
            else if (!take.complete)
            {
                // Readable, but cut short. Worth saying on the row rather than only once it is played
                // and the scrubber turns out to be dead.
                view.name.text += "  (未完了)";
                view.name.AddToClassList(RemoteControlEditorStyles.kWarning);
            }

            row.RegisterCallback<MouseDownEvent>(e =>
            {
                _selectedPath = take.path;

                // Double click plays it. The single click that came first already selected it, so
                // this is only the second half of the same gesture.
                if (e.clickCount >= 2) _TogglePlay();

                _DrawTransport();
                _DrawTakes();
            });

            return view;
        }

        private static Label _Column(VisualElement row, string className, string text)
        {
            var label = new Label(text);
            label.AddToClassList(className);
            label.AddToClassList(RemoteControlEditorStyles.kFixed);
            RemoteControlEditorFonts.ApplyMonospace(label);
            row.Add(label);
            return label;
        }

        // --- helpers ------------------------------------------------------

        private static long _Length(string path)
        {
            var info = new FileInfo(path);
            return info.Exists ? info.Length : 0;
        }

        private static string _Duration(double seconds)
        {
            var span = TimeSpan.FromSeconds(seconds);

            return span.TotalHours >= 1
                ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}"
                : $"{span.Minutes:00}:{span.Seconds:00}.{span.Milliseconds:000}";
        }

        /// <summary>Bytes in the unit that reads at a glance.</summary>
        private static string _Bytes(long count)
        {
            if (count < 1024) return $"{count} B";
            if (count < 1024 * 1024) return $"{count / 1024.0:0.##} KB";

            return $"{count / (1024.0 * 1024.0):0.##} MB";
        }
    }
}
