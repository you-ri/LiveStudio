// Copyright (c) You-Ri, 2026

using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Lilium.RemoteControl.Frames;
using Lilium.RemoteControl.Frames.Recording;

namespace Lilium.RemoteControl.Editor.LiveDataViewer
{
    /// <summary>
    /// Shows what is going through the gate: the two lanes down the left, whatever is selected on
    /// the right, and what the gate itself says about the run along the top.
    ///
    /// Built for one job -- telling "nothing is flowing" apart from "nothing is here to flow". Twice
    /// in one day a producer wrote to nobody and a recording played into nowhere, and neither said
    /// anything: the lane was simply empty, which is also what an idle run looks like. So the empty
    /// cases are the ones this draws loudest.
    ///
    /// The rows are built once and then written into. Rebuilding them each redraw is the obvious way
    /// and the wrong one: the row under the pointer is destroyed and remade ten times a second, so it
    /// flickers and never settles into its hover state. What changes every frame is text, not shape,
    /// and only the shape is worth rebuilding.
    /// </summary>
    public sealed class LiveDataViewerWindow : EditorWindow
    {
        private const string kStyleSheet = "Editor/LiveDataViewerWindow/LiveDataViewerWindow.uss";

        // Frames arrive at sixty a second and nothing here is worth drawing that often.
        private const double kRedrawInterval = 0.1;

        private enum DetailKind { None, StateElement, Input }

        /// <summary>A state row, kept so the parts that move can be written without rebuilding it.</summary>
        private sealed class StateRowView
        {
            public VisualElement root;
            public Label owner;
            public Label source;
            public Label age;
            public string typeName;
            public int ownerId;
        }

        private sealed class DetailRowView
        {
            public VisualElement root;
            public Label name;
            public Label value;
        }

        private Label _frameLabel;
        private Label _timecodeLabel;
        private Label _rateLabel;
        private Label _sourcePill;
        private Label _sinkPill;
        private Label _suppliedPill;
        private Label _gateLabel;
        private Label _observerLabel;

        private VisualElement _banners;
        private VisualElement _stateList;
        private VisualElement _inputList;
        private VisualElement _detailList;
        private Label _stateCount;
        private Label _inputCount;
        private Label _detailTitle;

        private DetailKind _detailKind;
        private string _selectedType;
        private int _selectedOwnerId = InputSymbolTable.kNone;
        private long _selectedInputSequence = -1;

        private double _nextRedraw;
        private long _drawnVersion = -1;

        private string _stateShape;
        private string _inputShape;
        private string _bannerShape;

        private readonly List<string> _bannerText = new List<string>();
        private readonly List<LiveDataValueRow> _rows = new List<LiveDataValueRow>();
        private readonly List<StateRowView> _stateRows = new List<StateRowView>();
        private readonly List<DetailRowView> _detailRows = new List<DetailRowView>();
        private readonly StringBuilder _shape = new StringBuilder();

        private static Font _monoFont;

        [MenuItem("Window/Lilium Remote Control/LiveData Viewer")]
        public static void ShowWindow()
        {
            var window = GetWindow<LiveDataViewerWindow>();
            window.titleContent = new GUIContent("LiveData Viewer");
            window.minSize = new Vector2(820, 420);
        }

        private void OnEnable()
        {
            LiveDataTap.Retain();
            EditorApplication.update += _OnUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.update -= _OnUpdate;
            LiveDataTap.Release();
        }

        private void _OnUpdate()
        {
            var now = EditorApplication.timeSinceStartup;
            if (now < _nextRedraw) return;
            _nextRedraw = now + kRedrawInterval;

            // The gate's own counters move without a frame going by (a bypassed write, a detached
            // observer), so the header is refreshed on the clock rather than on the version.
            _DrawStatus();

            if (_drawnVersion == LiveDataTap.version) return;
            _drawnVersion = LiveDataTap.version;

            _DrawBanners();
            _DrawState();
            _DrawInputs();
            _DrawDetail();
        }

        private void CreateGUI()
        {
            var root = rootVisualElement;
            RemoteControlEditorStyles.Apply(root, kStyleSheet);
            root.AddToClassList("ldv-root");

            root.Add(_BuildStatusBar());

            _banners = new VisualElement();
            root.Add(_banners);

            // The two lanes stack, so a long list of one does not squeeze the other off the screen,
            // and whatever is selected gets a column of its own rather than a tooltip.
            var lanes = new TwoPaneSplitView(0, 240, TwoPaneSplitViewOrientation.Vertical);
            lanes.style.flexGrow = 1;
            lanes.Add(_BuildLane("State", out _stateList, out _stateCount, false));
            lanes.Add(_BuildLane("Input", out _inputList, out _inputCount, true));

            var body = new TwoPaneSplitView(1, 360, TwoPaneSplitViewOrientation.Horizontal);
            body.style.flexGrow = 1;
            body.Add(lanes);
            body.Add(_BuildDetailPane());
            root.Add(body);

            _Invalidate();
            _DrawStatus();
            _DrawBanners();
            _DrawState();
            _DrawInputs();
            _DrawDetail();
        }

        /// <summary>Forces the next redraw to rebuild rather than write into what is already there.</summary>
        private void _Invalidate()
        {
            _drawnVersion = -1;
            _stateShape = null;
            _inputShape = null;
            _bannerShape = null;
        }

        // --- chrome -------------------------------------------------------

        private VisualElement _BuildStatusBar()
        {
            var bar = new VisualElement();
            bar.AddToClassList("ldv-status");

            // Both count up every frame, so they are set in a fixed-width face and a fixed width of
            // digits -- otherwise the whole row shuffles sideways as the numbers grow.
            _frameLabel = _AddStatus(bar, "ldv-num");
            _timecodeLabel = _AddStatus(bar, "ldv-num");
            _ApplyMonospace(_frameLabel);
            _ApplyMonospace(_timecodeLabel);

            _rateLabel = _AddStatus(bar, "ldv-status-item");

            _suppliedPill = _AddStatus(bar, "ldv-pill");
            _sourcePill = _AddStatus(bar, "ldv-pill");
            _sinkPill = _AddStatus(bar, "ldv-pill");

            var spacer = new VisualElement();
            spacer.AddToClassList(RemoteControlEditorStyles.kSpacer);
            bar.Add(spacer);

            _gateLabel = _AddStatus(bar, "ldv-status-item");
            _gateLabel.AddToClassList(RemoteControlEditorStyles.kSubtle);
            _observerLabel = _AddStatus(bar, "ldv-status-item");
            _observerLabel.AddToClassList(RemoteControlEditorStyles.kSubtle);

            return bar;
        }

        private static Label _AddStatus(VisualElement parent, string className)
        {
            var label = new Label();
            label.AddToClassList(className);
            parent.Add(label);
            return label;
        }

        /// <summary>
        /// Puts a label in a fixed-width face, taking the first one the machine actually has.
        ///
        /// Nothing happens when the machine has none of them -- the label keeps the inherited font,
        /// and the columns still line up because the numbers are padded to a fixed width and the
        /// column is sized for it. An empty font definition must never be assigned: that is not
        /// "inherit", it is "no font", and the label draws nothing at all.
        /// </summary>
        private static void _ApplyMonospace(Label label)
        {
            var font = _MonoFont();
            if (font == null) return;

            label.style.unityFontDefinition = FontDefinition.FromFont(font);
        }

        private static Font _MonoFont()
        {
            // Checked rather than cached blindly: a font built this way belongs to nobody, so an
            // asset unload takes it away and leaves a reference that is not null in C# but is dead in
            // Unity. Labels holding it then render blank, which is how this was found.
            if (_monoFont != null) return _monoFont;

            string[] candidates = { "Consolas", "Courier New", "Menlo", "DejaVu Sans Mono", "monospace" };
            for (int i = 0; i < candidates.Length && _monoFont == null; i++)
            {
                _monoFont = Font.CreateDynamicFontFromOSFont(candidates[i], 12);
            }

            // Kept out of the unload sweep, so the labels that took it keep drawing.
            if (_monoFont != null) _monoFont.hideFlags = HideFlags.HideAndDontSave;

            return _monoFont;
        }

        private VisualElement _BuildLane(string title, out VisualElement list, out Label count,
            bool clearable)
        {
            var lane = new VisualElement();
            lane.AddToClassList("ldv-lane");

            var header = new VisualElement();
            header.AddToClassList("ldv-lane-header");

            var titleLabel = new Label(title);
            titleLabel.AddToClassList(RemoteControlEditorStyles.kTitle);
            header.Add(titleLabel);

            count = new Label();
            count.AddToClassList(RemoteControlEditorStyles.kSubtle);
            count.style.marginLeft = 6;
            header.Add(count);

            var spacer = new VisualElement();
            spacer.AddToClassList(RemoteControlEditorStyles.kSpacer);
            header.Add(spacer);

            if (clearable)
            {
                header.Add(new Button(() =>
                {
                    LiveDataTap.ClearInputs();
                    _Invalidate();
                })
                { text = "Clear" });
            }

            lane.Add(header);

            var scroll = new ScrollView();
            scroll.AddToClassList(RemoteControlEditorStyles.kScroll);
            scroll.style.flexGrow = 1;
            lane.Add(scroll);

            list = scroll.contentContainer;
            return lane;
        }

        private VisualElement _BuildDetailPane()
        {
            var pane = new VisualElement();
            pane.AddToClassList("ldv-lane");

            var header = new VisualElement();
            header.AddToClassList("ldv-lane-header");

            var titleLabel = new Label("Detail");
            titleLabel.AddToClassList(RemoteControlEditorStyles.kTitle);
            header.Add(titleLabel);

            _detailTitle = new Label();
            _detailTitle.AddToClassList(RemoteControlEditorStyles.kSubtle);
            _detailTitle.AddToClassList(RemoteControlEditorStyles.kEllipsis);
            _detailTitle.style.marginLeft = 6;
            header.Add(_detailTitle);

            pane.Add(header);

            var scroll = new ScrollView();
            scroll.AddToClassList(RemoteControlEditorStyles.kScroll);
            scroll.style.flexGrow = 1;
            pane.Add(scroll);

            _detailList = scroll.contentContainer;
            return pane;
        }

        // --- status -------------------------------------------------------

        private void _DrawStatus()
        {
            if (_frameLabel == null) return;

            var snapshot = LiveDataTap.snapshot;

            // Nothing has come through yet: the rate is still zero, and a timecode wants to divide
            // by it.
            if (!LiveDataTap.hasFrame)
            {
                _frameLabel.text = "--------";
                _timecodeLabel.text = "--:--:--:--.---";
                _rateLabel.text = LiveDataTap.isAttached ? "待機中" : "未接続";
            }
            else
            {
                _ApplyMonospace(_frameLabel);
                _ApplyMonospace(_timecodeLabel);
                _frameLabel.text = snapshot.frameNumber.ToString("D8");
                _timecodeLabel.text = new Timecode(snapshot.frameNumber, snapshot.frameRate).ToString();
                _rateLabel.text = snapshot.frameRate.ToString();
            }

            _SetPill(_suppliedPill, "supplied", snapshot.isSupplied, "ldv-pill-supplied");
            _SetPill(_sourcePill, "source", snapshot.hasSource, "ldv-pill-on");
            _SetPill(_sinkPill, "recording", snapshot.hasSink, "ldv-pill-on");

            // Every one of these is a quiet failure that otherwise only shows up as "it does not
            // work": an input that skipped the queue, a payload cut short, a target written many
            // times a frame that belongs on the other lane.
            _gateLabel.text =
                $"bypassed {FrameGate.bypassedCount}   truncated {FrameGate.truncatedPayloadCount}   " +
                $"repeated {FrameGate.repeatedWriteCount}";

            var detached = FrameGate.detachedObserverCount;
            _observerLabel.text = detached == 0
                ? $"observers {FrameGate.observerCount}"
                : $"observers {FrameGate.observerCount} (detached {detached})";
            _observerLabel.EnableInClassList(RemoteControlEditorStyles.kDanger, detached != 0);
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

            // The failure that cost a day: a recording carries a type the playing side cannot hold,
            // so the whole lane is dropped and the replay simply shows nothing.
            if (FrameGate.source is FrameReplayer replayer)
            {
                var unknown = replayer.player.unknownStateTypes;
                if (unknown.Count > 0)
                {
                    _bannerText.Add(
                        "この収録が運んでいる状態のうち、受け皿が無いものがあります: " +
                        string.Join(", ", unknown) +
                        "  → その分は再生されません。");
                }
            }

            var shape = string.Join("|", _bannerText);
            if (shape == _bannerShape) return;
            _bannerShape = shape;

            _banners.Clear();
            for (int i = 0; i < _bannerText.Count; i++)
            {
                var label = new Label(_bannerText[i]);
                label.AddToClassList("ldv-banner");
                label.AddToClassList("ldv-banner-danger");
                _banners.Add(label);
            }
        }

        // --- state lane ---------------------------------------------------

        private void _DrawState()
        {
            if (_stateList == null) return;

            var snapshot = LiveDataTap.snapshot;

            // The shape is which types and owners are present. It changes when the world does, which
            // is rarely; the numbers beside them change every frame, and writing those into rows that
            // already exist is what keeps the list still under the pointer.
            _shape.Clear();
            var elementTotal = 0;
            for (int i = 0; i < snapshot.types.Count; i++)
            {
                var row = snapshot.types[i];
                _shape.Append(row.typeName).Append(':').Append(row.elements.Count).Append(';');
                elementTotal += row.elements.Count;
                for (int e = 0; e < row.elements.Count; e++)
                {
                    _shape.Append(row.elements[e].ownerId).Append(',');
                }
            }
            foreach (var name in StateTypeRegistry.knownTypeNames) _shape.Append('!').Append(name);

            var shape = _shape.ToString();
            if (shape != _stateShape)
            {
                _stateShape = shape;
                _RebuildState(snapshot);
            }

            _stateCount.text = $"{snapshot.types.Count} types / {elementTotal} elements";
            _RefreshStateRows(snapshot);
        }

        private void _RebuildState(LiveDataSnapshot snapshot)
        {
            _stateList.Clear();
            _stateRows.Clear();

            var drawn = new HashSet<string>();

            for (int i = 0; i < snapshot.types.Count; i++)
            {
                var row = snapshot.types[i];
                drawn.Add(row.typeName);
                _stateList.Add(_BuildTypeRow(row));
            }

            // A type that announced itself but has no block is the other half of the picture. Drawing
            // only what exists makes "the producer wrote to nobody" look exactly like "we are not
            // recording", which is how it went unnoticed twice.
            foreach (var name in StateTypeRegistry.knownTypeNames)
            {
                if (drawn.Contains(name)) continue;
                _stateList.Add(_BuildMissingTypeRow(name));
            }

            if (_stateList.childCount == 0)
            {
                _stateList.Add(_Empty(
                    "状態レーンには何もありません。状態を運ぶ型がまだ 1 つも宣言されていない可能性があります。"));
            }
        }

        private void _RefreshStateRows(LiveDataSnapshot snapshot)
        {
            for (int i = 0; i < _stateRows.Count; i++)
            {
                var view = _stateRows[i];
                if (!_TryFindElement(snapshot, view.typeName, view.ownerId, out var element)) continue;

                view.owner.text = string.IsNullOrEmpty(element.owner)
                    ? $"#{element.ownerId} (未解決)"
                    : element.owner;
                view.owner.EnableInClassList(RemoteControlEditorStyles.kWarning,
                    string.IsNullOrEmpty(element.owner));

                view.source.text = string.IsNullOrEmpty(element.source) ? "-" : element.source;

                var fresh = element.lastChangedFrame == snapshot.frameNumber;
                view.age.text = fresh ? "now" : $"{snapshot.frameNumber - element.lastChangedFrame}f 前";
                view.age.EnableInClassList("ldv-fresh", fresh);
                view.age.EnableInClassList("ldv-stale", !fresh);
                view.age.tooltip = $"producer stamp: {element.time}";

                view.root.EnableInClassList("ldv-element-selected",
                    _detailKind == DetailKind.StateElement &&
                    view.typeName == _selectedType && view.ownerId == _selectedOwnerId);
            }
        }

        private static bool _TryFindElement(LiveDataSnapshot snapshot, string typeName, int ownerId,
            out ElementRow element)
        {
            for (int i = 0; i < snapshot.types.Count; i++)
            {
                var row = snapshot.types[i];
                if (row.typeName != typeName) continue;

                for (int e = 0; e < row.elements.Count; e++)
                {
                    if (row.elements[e].ownerId != ownerId) continue;

                    element = row.elements[e];
                    return true;
                }
            }

            element = default;
            return false;
        }

        private VisualElement _BuildTypeRow(TypeRow row)
        {
            var container = new VisualElement();
            container.AddToClassList("ldv-type");
            container.EnableInClassList("ldv-type-empty", row.elements.Count == 0);

            var head = new VisualElement();
            head.AddToClassList("ldv-type-head");

            var name = new Label(_ShortTypeName(row.typeName));
            name.AddToClassList(RemoteControlEditorStyles.kTitle);
            name.tooltip = row.typeName;
            head.Add(name);

            var detail = new Label($"  {row.elements.Count} × {row.elementSize} B");
            detail.AddToClassList(RemoteControlEditorStyles.kSubtle);
            head.Add(detail);

            container.Add(head);

            if (row.elements.Count == 0)
            {
                var note = new Label("要素なし — 誰もこの型を書いていません");
                note.AddToClassList(RemoteControlEditorStyles.kWarning);
                note.style.marginLeft = 14;
                container.Add(note);
                return container;
            }

            for (int i = 0; i < row.elements.Count; i++)
            {
                container.Add(_BuildElementRow(row, row.elements[i]));
            }

            return container;
        }

        private VisualElement _BuildElementRow(TypeRow type, ElementRow element)
        {
            var line = new VisualElement();
            line.AddToClassList("ldv-element");

            var owner = new Label();
            owner.AddToClassList(RemoteControlEditorStyles.kGrow);
            owner.AddToClassList(RemoteControlEditorStyles.kEllipsis);
            line.Add(owner);

            var source = new Label();
            source.AddToClassList("ldv-col-mid");
            source.AddToClassList(RemoteControlEditorStyles.kSubtle);
            line.Add(source);

            var age = new Label();
            age.AddToClassList("ldv-col-mid");
            line.Add(age);

            var typeName = type.typeName;
            var ownerId = element.ownerId;
            line.RegisterCallback<MouseDownEvent>(_ => _SelectState(typeName, ownerId));

            _stateRows.Add(new StateRowView
            {
                root = line,
                owner = owner,
                source = source,
                age = age,
                typeName = typeName,
                ownerId = ownerId,
            });

            return line;
        }

        private VisualElement _BuildMissingTypeRow(string typeName)
        {
            var container = new VisualElement();
            container.AddToClassList("ldv-type");
            container.AddToClassList("ldv-type-empty");

            var head = new VisualElement();
            head.AddToClassList("ldv-type-head");

            var name = new Label(_ShortTypeName(typeName));
            name.AddToClassList(RemoteControlEditorStyles.kTitle);
            name.tooltip = typeName;
            head.Add(name);

            var note = new Label("  登録済み・ブロックなし");
            note.AddToClassList(RemoteControlEditorStyles.kWarning);
            head.Add(note);

            container.Add(head);
            return container;
        }

        private void _SelectState(string typeName, int ownerId)
        {
            _detailKind = DetailKind.StateElement;
            _selectedType = typeName;
            _selectedOwnerId = ownerId;
            LiveDataTap.Select(typeName, ownerId);
            _Invalidate();
        }

        private void _SelectInput(long sequence)
        {
            _detailKind = DetailKind.Input;
            _selectedInputSequence = sequence;
            _Invalidate();
        }

        // --- input lane ---------------------------------------------------

        private void _DrawInputs()
        {
            if (_inputList == null) return;

            var count = LiveDataTap.inputCount;
            var newest = count == 0 ? -1 : LiveDataTap.GetInput(count - 1).sequence;

            // Inputs only arrive, so the list is rebuilt when one does and left alone otherwise.
            var shape = $"{count}:{newest}:{_selectedInputSequence}:{_detailKind}";
            if (shape == _inputShape) return;
            _inputShape = shape;

            _inputList.Clear();
            _inputCount.text = $"{count} records";

            if (count == 0)
            {
                _inputList.Add(_Empty("入力はまだ通っていません。"));
                return;
            }

            // Newest first: what just happened is what is being looked for.
            for (int i = count - 1; i >= 0; i--)
            {
                _inputList.Add(_BuildInputRow(LiveDataTap.GetInput(i)));
            }
        }

        private VisualElement _BuildInputRow(InputRow input)
        {
            var line = new VisualElement();
            line.AddToClassList("ldv-input");
            line.EnableInClassList("ldv-input-faulted", input.faulted);
            line.EnableInClassList("ldv-element-selected",
                _detailKind == DetailKind.Input && input.sequence == _selectedInputSequence);

            var frame = new Label(input.frameNumber.ToString("D8"));
            frame.AddToClassList("ldv-col-frame");
            frame.AddToClassList(RemoteControlEditorStyles.kSubtle);
            _ApplyMonospace(frame);
            line.Add(frame);

            var source = new Label(string.IsNullOrEmpty(input.source) ? "-" : input.source);
            source.AddToClassList("ldv-col-mid");
            source.AddToClassList(RemoteControlEditorStyles.kSubtle);
            line.Add(source);

            var method = new Label(string.IsNullOrEmpty(input.method) ? input.kind.ToString() : input.method);
            method.AddToClassList("ldv-col-narrow");
            line.Add(method);

            var target = new Label(input.target);
            target.AddToClassList(RemoteControlEditorStyles.kGrow);
            target.AddToClassList(RemoteControlEditorStyles.kEllipsis);
            line.Add(target);

            if (input.truncated)
            {
                var cut = new Label("切り詰め");
                cut.AddToClassList(RemoteControlEditorStyles.kWarning);
                line.Add(cut);
            }

            var sequence = input.sequence;
            line.RegisterCallback<MouseDownEvent>(_ => _SelectInput(sequence));

            return line;
        }

        private static bool _TryFindInput(long sequence, out InputRow input)
        {
            var count = LiveDataTap.inputCount;
            for (int i = count - 1; i >= 0; i--)
            {
                var candidate = LiveDataTap.GetInput(i);
                if (candidate.sequence != sequence) continue;

                input = candidate;
                return true;
            }

            input = default;
            return false;
        }

        // --- detail -------------------------------------------------------

        private void _DrawDetail()
        {
            if (_detailList == null) return;

            _rows.Clear();

            switch (_detailKind)
            {
                case DetailKind.StateElement:
                    _BuildStateDetail();
                    break;
                case DetailKind.Input:
                    _BuildInputDetail();
                    break;
                default:
                    _detailTitle.text = string.Empty;
                    _rows.Add(new LiveDataValueRow(string.Empty, "左の行を選ぶと、その中身がここに出ます。"));
                    break;
            }

            _WriteDetailRows();
        }

        private void _BuildStateDetail()
        {
            var snapshot = LiveDataTap.snapshot;
            _detailTitle.text = $"{_ShortTypeName(_selectedType)}  ({snapshot.selectedValueLength} B)";

            if (snapshot.selectedType != _selectedType ||
                snapshot.selectedOwnerId != _selectedOwnerId ||
                snapshot.selectedValueLength == 0)
            {
                _rows.Add(new LiveDataValueRow(string.Empty, "選んだ要素はこのフレームにありません。"));
                return;
            }

            var row = _FindType(snapshot, _selectedType);
            var elementType = row?.elementType;

            // A reading provided by whoever owns the type comes first: it is the only thing that can
            // say a byte buffer is really a list of named bones.
            var presenter = LiveDataValuePresenters.Find(elementType);
            if (presenter != null)
            {
                presenter(snapshot.selectedValue, snapshot.selectedValueLength, _rows);
                return;
            }

            var layout = LiveDataValueLayout.For(elementType);
            if (layout == null) return;

            for (int i = 0; i < layout.Count; i++)
            {
                var field = layout[i];
                var text = LiveDataValueLayout.Read(snapshot.selectedValue, snapshot.selectedValueLength, field);

                // A fixed buffer of bytes is where reflection runs out: the type says 880 bytes and
                // nothing about what they mean. Say so rather than printing 880 numbers.
                if (field.bufferElementType == typeof(byte) && field.bufferLength > 64)
                {
                    text = $"{field.bufferLength} バイト — 読み方が登録されていません";
                }

                _rows.Add(new LiveDataValueRow(_LeafName(field.path), text, field.depth));
            }
        }

        private void _BuildInputDetail()
        {
            if (!_TryFindInput(_selectedInputSequence, out var input))
            {
                _detailTitle.text = $"#{_selectedInputSequence}";
                _rows.Add(new LiveDataValueRow(string.Empty, "この入力はもう保持していません。"));
                return;
            }

            _detailTitle.text = $"#{input.sequence}  {input.kind}";

            _rows.Add(new LiveDataValueRow("frame", input.frameNumber.ToString("D8")));
            _rows.Add(new LiveDataValueRow("sequence", input.sequence.ToString()));
            _rows.Add(new LiveDataValueRow("kind", input.kind.ToString()));
            _rows.Add(new LiveDataValueRow("source", string.IsNullOrEmpty(input.source) ? "-" : input.source));
            _rows.Add(new LiveDataValueRow("method", string.IsNullOrEmpty(input.method) ? "-" : input.method));
            _rows.Add(new LiveDataValueRow("target", input.target));

            if (input.faulted) _rows.Add(new LiveDataValueRow("状態", "適用に失敗しました"));
            if (input.truncated) _rows.Add(new LiveDataValueRow("状態", "記録時に payload が切り詰められました"));

            _rows.Add(new LiveDataValueRow("payload",
                string.IsNullOrEmpty(input.payload) ? "(なし)" : input.payload));
        }

        /// <summary>
        /// Writes the rows into the pane, growing or shrinking it only when the count changes. The
        /// values move every frame; the rows they sit in do not.
        /// </summary>
        private void _WriteDetailRows()
        {
            while (_detailRows.Count > _rows.Count)
            {
                var last = _detailRows.Count - 1;
                _detailList.Remove(_detailRows[last].root);
                _detailRows.RemoveAt(last);
            }

            while (_detailRows.Count < _rows.Count)
            {
                var view = _BuildDetailRow();
                _detailList.Add(view.root);
                _detailRows.Add(view);
            }

            for (int i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                var view = _detailRows[i];

                view.name.text = row.label;
                view.name.style.marginLeft = 4 + row.depth * 12;
                view.value.text = row.text;
                view.value.tooltip = row.text;
            }
        }

        private static DetailRowView _BuildDetailRow()
        {
            var line = new VisualElement();
            line.AddToClassList("ldv-detail-row");

            var name = new Label();
            name.AddToClassList("ldv-detail-name");
            name.AddToClassList(RemoteControlEditorStyles.kEllipsis);
            line.Add(name);

            var value = new Label();
            value.AddToClassList(RemoteControlEditorStyles.kGrow);
            value.AddToClassList("ldv-detail-value");
            _ApplyMonospace(value);
            line.Add(value);

            return new DetailRowView { root = line, name = name, value = value };
        }

        private static TypeRow _FindType(LiveDataSnapshot snapshot, string typeName)
        {
            for (int i = 0; i < snapshot.types.Count; i++)
            {
                if (snapshot.types[i].typeName == typeName) return snapshot.types[i];
            }
            return null;
        }

        // --- helpers ------------------------------------------------------

        private static Label _Empty(string text)
        {
            var label = new Label(text);
            label.AddToClassList("ldv-empty");
            label.AddToClassList(RemoteControlEditorStyles.kSubtle);
            return label;
        }

        private static string _ShortTypeName(string fullName)
        {
            if (string.IsNullOrEmpty(fullName)) return "?";

            var dot = fullName.LastIndexOf('.');
            return dot < 0 ? fullName : fullName.Substring(dot + 1);
        }

        private static string _LeafName(string path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;

            var dot = path.LastIndexOf('.');
            return dot < 0 ? path : path.Substring(dot + 1);
        }
    }
}
