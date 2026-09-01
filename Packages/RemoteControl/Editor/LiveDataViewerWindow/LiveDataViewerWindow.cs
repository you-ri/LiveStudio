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

        private enum DetailKind { None, StateElement, Event, StructureEntry }

        /// <summary>
        /// Which of the two views shares the top pane.
        ///
        /// Tabbed rather than a third strip: the inventory changes when the world does, which is
        /// rarely, while the state lane moves every frame. Giving both a permanent slice would take
        /// room from the one that is actually being watched.
        /// </summary>
        private enum TopTab { State, Structure }

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

        /// <summary>One inventory row, kept so its parent can be written without rebuilding it.</summary>
        private sealed class StructureRowView
        {
            public VisualElement root;
            public Label parent;
            public int objectId;
        }

        private sealed class DetailRowView
        {
            public VisualElement root;
            public Label name;
            public Label value;
        }

        private Label _positionLabel;
        private Label _rateLabel;
        private Label _sourcePill;
        private Label _sinkPill;
        private Label _suppliedPill;
        private Label _gateLabel;
        private Label _observerLabel;

        private VisualElement _banners;
        private VisualElement _stateList;
        private VisualElement _structureList;
        private VisualElement _statePage;
        private VisualElement _structurePage;
        private Button _stateTab;
        private Button _structureTab;
        private VisualElement _eventList;
        private VisualElement _detailList;
        private Label _stateCount;
        private Button _emptyToggle;
        private Label _eventCount;
        private Label _detailTitle;

        private DetailKind _detailKind;
        private TopTab _topTab = TopTab.State;
        private string _selectedType;
        private int _selectedOwnerId = FrameSymbolTable.kNone;
        private long _selectedEventRowId = -1;
        private int _selectedObjectId = FrameSymbolTable.kNone;

        /// <summary>
        /// Whether the position reads as a timecode rather than a frame number.
        ///
        /// One or the other, not both: they are the same number twice, and the pair took the width
        /// that the rest of the bar wanted. Kept in EditorPrefs because a preference that resets on
        /// every recompile is worse than not having one.
        /// </summary>
        private bool _showTimecode;

        private const string kTimecodePref = "Lilium.RemoteControl.LiveDataViewer.showTimecode";

        /// <summary>
        /// Whether types carrying nothing are listed: a declared type with no block, and a block
        /// with no elements.
        ///
        /// On by default, and deliberately so -- an empty lane is the case this window exists to
        /// make visible, and a filter that hides it by default would put the silence back. But once
        /// a project declares more types than any one run uses, the empties outnumber the live rows
        /// and push them off the screen, so it has to be possible to fold them away while watching
        /// something specific. The choice is per-user rather than per-window: it is a way of
        /// looking, not a property of what is being looked at.
        /// </summary>
        private bool _showEmptyTypes = true;

        private const string kEmptyTypesPref = "Lilium.RemoteControl.LiveDataViewer.showEmptyTypes";

        private double _nextRedraw;
        private long _drawnVersion = -1;

        /// <summary>The text generation this window was built with. See <see cref="_ApplyLanguage"/>.</summary>
        private int _textGeneration = -1;

        private string _stateShape;
        private string _structureShape;
        private string _eventShape;
        private string _bannerShape;

        private readonly List<string> _bannerText = new List<string>();
        private readonly List<LiveDataValueRow> _rows = new List<LiveDataValueRow>();
        private readonly List<StateRowView> _stateRows = new List<StateRowView>();
        private readonly List<StructureRowView> _structureRows = new List<StructureRowView>();
        private readonly List<DetailRowView> _detailRows = new List<DetailRowView>();
        private readonly StringBuilder _shape = new StringBuilder();

        /// <summary>Which type names the snapshot carries a block for. Reused: this is walked on every redraw.</summary>
        private readonly HashSet<string> _blocked = new HashSet<string>();

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

            _ApplyLanguage();

            // The gate's own counters move without a frame going by (a bypassed write, a detached
            // observer), so the header is refreshed on the clock rather than on the version.
            _DrawStatus();

            if (_drawnVersion == LiveDataTap.version) return;
            _drawnVersion = LiveDataTap.version;

            _DrawBanners();
            _DrawState();
            _DrawStructure();
            _DrawEvents();
            _DrawDetail();
        }

        private void CreateGUI()
        {
            var root = rootVisualElement;

            _textGeneration = RemoteControlEditorLocalization.generation;

            RemoteControlEditorStyles.Apply(root, kStyleSheet);
            root.AddToClassList("ldv-root");

            root.Add(_BuildStatusBar());

            _banners = new VisualElement();
            root.Add(_banners);

            // The two lanes stack, so a long list of one does not squeeze the other off the screen,
            // and whatever is selected gets a column of its own rather than a tooltip.
            var lanes = new TwoPaneSplitView(0, 240, TwoPaneSplitViewOrientation.Vertical);
            lanes.style.flexGrow = 1;
            lanes.Add(_BuildTopLane());
            lanes.Add(_BuildLane("Event", out _eventList, out _eventCount, true));

            var body = new TwoPaneSplitView(1, 360, TwoPaneSplitViewOrientation.Horizontal);
            body.style.flexGrow = 1;
            body.Add(lanes);
            body.Add(_BuildDetailPane());
            root.Add(body);

            _Invalidate();
            _DrawStatus();
            _DrawBanners();
            _DrawState();
            _DrawStructure();
            _DrawEvents();
            _DrawDetail();
        }

        /// <summary>Forces the next redraw to rebuild rather than write into what is already there.</summary>
        private void _Invalidate()
        {
            _drawnVersion = -1;
            _stateShape = null;
            _structureShape = null;
            _eventShape = null;
            _bannerShape = null;
        }

        // --- words --------------------------------------------------------

        private static string _Tr(string key) => RemoteControlEditorLocalization.Tr(key);

        private static string _Tr(string key, params object[] args)
            => RemoteControlEditorLocalization.Tr(key, args);

        /// <summary>
        /// Builds the window again when the language changed under it.
        ///
        /// The whole window rather than the labels that moved: most of the chrome is written once,
        /// when it is built, and a language change is the one moment where every one of those has to
        /// be asked again. Naming them individually is a list that goes stale the first time a
        /// control is added -- and the flicker a rebuild costs is not worth avoiding for something
        /// that happens when a person changes a setting.
        /// </summary>
        private void _ApplyLanguage()
        {
            if (_textGeneration == RemoteControlEditorLocalization.generation) return;

            var root = rootVisualElement;
            if (root == null || root.childCount == 0) return;

            root.Clear();
            CreateGUI();
        }

        // --- chrome -------------------------------------------------------

        private VisualElement _BuildStatusBar()
        {
            var bar = new VisualElement();
            bar.AddToClassList("ldv-status");

            // Counts up every frame, so it is set in a fixed-width face at a fixed width -- the row
            // would otherwise shuffle sideways as the digits grow.
            _showTimecode = EditorPrefs.GetBool(kTimecodePref, false);

            _positionLabel = _AddStatus(bar, "ldv-num");
            _positionLabel.tooltip = _Tr("LD_POSITION_TOOLTIP");
            RemoteControlEditorFonts.ApplyMonospace(_positionLabel);
            _positionLabel.RegisterCallback<MouseDownEvent>(_ => _TogglePosition());

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

        private void _TogglePosition()
        {
            _showTimecode = !_showTimecode;
            EditorPrefs.SetBool(kTimecodePref, _showTimecode);
            _DrawStatus();
        }

        private void _ToggleEmptyTypes()
        {
            _showEmptyTypes = !_showEmptyTypes;
            EditorPrefs.SetBool(kEmptyTypesPref, _showEmptyTypes);

            // The set of rows changes, not their contents, so this is one of the few times the list
            // has to be built again rather than written into.
            _stateShape = null;
            _DrawState();
        }

        /// <summary>
        /// The top pane: the state lane and the inventory, sharing one slice through tabs.
        ///
        /// The counts sit on the tabs rather than only on the active one, so an empty inventory is
        /// visible without switching to it -- which is the case worth noticing, since "nothing is
        /// registered" and "nothing is being written" look the same from the state lane alone.
        /// </summary>
        private VisualElement _BuildTopLane()
        {
            var lane = new VisualElement();
            lane.AddToClassList("ldv-lane");

            var header = new VisualElement();
            header.AddToClassList("ldv-lane-header");

            _stateTab = _BuildTab("State", TopTab.State);
            _structureTab = _BuildTab("Structure", TopTab.Structure);
            header.Add(_stateTab);
            header.Add(_structureTab);

            _stateCount = new Label();
            _stateCount.AddToClassList(RemoteControlEditorStyles.kSubtle);
            _stateCount.style.marginLeft = 8;
            header.Add(_stateCount);

            var spacer = new VisualElement();
            spacer.AddToClassList(RemoteControlEditorStyles.kSpacer);
            header.Add(spacer);

            _showEmptyTypes = EditorPrefs.GetBool(kEmptyTypesPref, true);
            _emptyToggle = new Button(_ToggleEmptyTypes);
            _emptyToggle.AddToClassList("ldv-toggle");
            header.Add(_emptyToggle);

            lane.Add(header);

            _statePage = _BuildScrollPage(out _stateList);
            _structurePage = _BuildScrollPage(out _structureList);
            lane.Add(_statePage);
            lane.Add(_structurePage);

            _ShowTab(_topTab);
            return lane;
        }

        private Button _BuildTab(string title, TopTab tab)
        {
            var button = new Button(() => _ShowTab(tab)) { text = title };
            button.AddToClassList("ldv-tab");
            return button;
        }

        private void _ShowTab(TopTab tab)
        {
            _topTab = tab;

            _statePage.style.display = tab == TopTab.State ? DisplayStyle.Flex : DisplayStyle.None;
            _structurePage.style.display = tab == TopTab.Structure ? DisplayStyle.Flex : DisplayStyle.None;

            _stateTab?.EnableInClassList("ldv-tab-active", tab == TopTab.State);
            _structureTab?.EnableInClassList("ldv-tab-active", tab == TopTab.Structure);

            // The filter belongs to the state lane alone; leaving it up over the inventory would
            // read as applying to whatever is on screen.
            _Show(_emptyToggle, tab == TopTab.State);

            _DrawState();
            _DrawStructure();
        }

        private static VisualElement _BuildScrollPage(out VisualElement list)
        {
            var scroll = new ScrollView();
            scroll.AddToClassList(RemoteControlEditorStyles.kScroll);
            scroll.AddToClassList("ldv-page");

            list = scroll.contentContainer;
            return scroll;
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
                    LiveDataTap.ClearEvents();
                    _Invalidate();
                })
                { text = "Clear" });
            }

            lane.Add(header);

            var scroll = new ScrollView();
            scroll.AddToClassList(RemoteControlEditorStyles.kScroll);
            scroll.AddToClassList("ldv-page");
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
            scroll.AddToClassList("ldv-page");
            pane.Add(scroll);

            _detailList = scroll.contentContainer;
            return pane;
        }

        private static void _Show(VisualElement element, bool visible)
        {
            if (element == null) return;

            element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // --- status -------------------------------------------------------

        private void _DrawStatus()
        {
            if (_positionLabel == null) return;

            var snapshot = LiveDataTap.snapshot;

            // Nothing has come through yet: the rate is still zero, and a timecode wants to divide
            // by it.
            if (!LiveDataTap.hasFrame)
            {
                _positionLabel.text = _showTimecode ? "--:--:--:--" : "--------";
                _rateLabel.text = _Tr(LiveDataTap.isAttached ? "LD_WAITING" : "LDV_DISCONNECTED");
            }
            else
            {
                RemoteControlEditorFonts.ApplyMonospace(_positionLabel);
                _positionLabel.text = _showTimecode
                    ? new Timecode(snapshot.frameNumber, snapshot.frameRate).ToSmpteString()
                    : snapshot.frameNumber.ToString("D8");
                _rateLabel.text = snapshot.frameRate.ToString();
            }

            _SetPill(_suppliedPill, "supplied", snapshot.isSupplied, "ldv-pill-supplied");
            _SetPill(_sourcePill, "source", snapshot.hasSource, "ldv-pill-on");
            _SetPill(_sinkPill, "recording", snapshot.hasSink, "ldv-pill-on");

            // Every one of these is a quiet failure that otherwise only shows up as "it does not
            // work": an event that skipped the queue, a payload cut short, a target written many
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
                    _bannerText.Add(_Tr("LD_UNKNOWN_STATE_TYPES", string.Join(", ", unknown)));
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
            _blocked.Clear();
            var elementTotal = 0;
            var byteTotal = 0L;
            var emptyTotal = 0;
            for (int i = 0; i < snapshot.types.Count; i++)
            {
                var row = snapshot.types[i];
                _shape.Append(row.typeName).Append(':').Append(row.elements.Count).Append(';');
                elementTotal += row.elements.Count;
                _blocked.Add(row.typeName);
                if (row.elements.Count == 0) emptyTotal++;

                // What this lane costs every frame, which is the number to watch when deciding
                // whether a member belongs here: state is paid for whether it changes or not.
                byteTotal += (long)row.elements.Count * row.elementSize;
                for (int e = 0; e < row.elements.Count; e++)
                {
                    _shape.Append(row.elements[e].ownerId).Append(',');
                }
            }
            foreach (var name in StateTypeRegistry.knownTypeNames)
            {
                _shape.Append('!').Append(name);
                if (!_blocked.Contains(name)) emptyTotal++;
            }
            _shape.Append('#').Append(RemoteControlEditorFonts.generation).Append(_showEmptyTypes ? '+' : '-');

            var shape = _shape.ToString();
            if (shape != _stateShape)
            {
                _stateShape = shape;
                _RebuildState(snapshot);
            }

            if (_topTab == TopTab.State)
            {
                _stateCount.text = _Tr("LDV_STATE_SUMMARY",
                    snapshot.types.Count, elementTotal, _Bytes(byteTotal));
            }

            _stateTab.text = $"State  {snapshot.types.Count}";
            _UpdateEmptyToggle(emptyTotal);
            _RefreshStateRows(snapshot);
        }

        private void _RebuildState(LiveDataSnapshot snapshot)
        {
            _stateList.Clear();
            _stateRows.Clear();

            var hidden = 0;

            for (int i = 0; i < snapshot.types.Count; i++)
            {
                var row = snapshot.types[i];

                if (row.elements.Count == 0 && !_showEmptyTypes)
                {
                    hidden++;
                    continue;
                }

                _stateList.Add(_BuildTypeRow(row));
            }

            // A type that announced itself but has no block is the other half of the picture. Drawing
            // only what exists makes "the producer wrote to nobody" look exactly like "we are not
            // recording", which is how it went unnoticed twice.
            foreach (var name in StateTypeRegistry.knownTypeNames)
            {
                if (_blocked.Contains(name)) continue;

                if (!_showEmptyTypes)
                {
                    hidden++;
                    continue;
                }

                _stateList.Add(_BuildMissingTypeRow(name));
            }

            if (_stateList.childCount == 0)
            {
                // Filtered down to nothing is not the same as having nothing, and it must not be
                // reported as such -- that is the exact confusion this window was built against.
                _stateList.Add(_Empty(hidden > 0
                    ? _Tr("LDV_EMPTY_FILTERED", hidden)
                    : _Tr("LDV_EMPTY_STATE_LANE")));
            }
        }

        /// <summary>
        /// Keeps the button saying what it will do and how much it is holding back.
        ///
        /// The count is on the button whether the empties are shown or hidden: hidden with no
        /// number would be a filter that silently swallows an unknown amount, which is the one
        /// thing this window must never do.
        /// </summary>
        private void _UpdateEmptyToggle(int empty)
        {
            if (_emptyToggle == null) return;

            _emptyToggle.text = _Tr(_showEmptyTypes ? "LDV_HIDE_EMPTY" : "LDV_SHOW_EMPTY", empty);
            _emptyToggle.tooltip = _showEmptyTypes
                ? _Tr("LDV_HIDE_EMPTY_TOOLTIP")
                : _Tr("LDV_SHOW_EMPTY_TOOLTIP", empty);
            _emptyToggle.EnableInClassList("ldv-toggle-active", !_showEmptyTypes);
        }

        private void _RefreshStateRows(LiveDataSnapshot snapshot)
        {
            for (int i = 0; i < _stateRows.Count; i++)
            {
                var view = _stateRows[i];
                if (!_TryFindElement(snapshot, view.typeName, view.ownerId, out var element)) continue;

                view.owner.text = string.IsNullOrEmpty(element.owner)
                    ? _Tr("LDV_UNRESOLVED", element.ownerId)
                    : element.owner;
                view.owner.EnableInClassList(RemoteControlEditorStyles.kWarning,
                    string.IsNullOrEmpty(element.owner));

                view.source.text = string.IsNullOrEmpty(element.source) ? "-" : element.source;

                var fresh = element.lastChangedFrame == snapshot.frameNumber;
                view.age.text = fresh
                    ? "now"
                    : _Tr("LDV_AGE_FRAMES", snapshot.frameNumber - element.lastChangedFrame);
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

            // No line for an empty block: the count beside the name already says it, and the bar
            // says which state it is in.
            if (row.elements.Count == 0) return container;

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

            var note = new Label("  " + _Tr("LDV_DECLARED_NO_BLOCK"));
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

        private void _SelectEvent(long rowId)
        {
            _detailKind = DetailKind.Event;
            _selectedEventRowId = rowId;
            _Invalidate();
        }

        // --- structure lane -----------------------------------------------

        private void _DrawStructure()
        {
            if (_structureList == null) return;

            var snapshot = LiveDataTap.snapshot;

            _structureTab.text = $"Structure  {snapshot.structure.Count}";

            if (_topTab == TopTab.Structure)
            {
                _stateCount.text = $"{snapshot.structure.Count} objects / epoch {snapshot.structureEpoch}";
            }

            // Which objects exist and what they are. Reparenting is the one part that moves without
            // the set changing, so it is written into the rows rather than counted as a new shape.
            _shape.Clear();
            for (int i = 0; i < snapshot.structure.Count; i++)
            {
                var row = snapshot.structure[i];
                _shape.Append(row.objectId).Append(':').Append(row.typeId).Append(';');
            }

            var shape = _shape.ToString();
            if (shape != _structureShape)
            {
                _structureShape = shape;
                _RebuildStructure(snapshot);
            }

            _RefreshStructureRows(snapshot);
        }

        private void _RebuildStructure(LiveDataSnapshot snapshot)
        {
            _structureList.Clear();
            _structureRows.Clear();

            for (int i = 0; i < snapshot.structure.Count; i++)
            {
                _structureList.Add(_BuildStructureRow(snapshot.structure[i]));
            }

            if (_structureList.childCount == 0)
            {
                _structureList.Add(_Empty(_Tr("LDV_EMPTY_STRUCTURE_LANE")));
            }
        }

        private void _RefreshStructureRows(LiveDataSnapshot snapshot)
        {
            for (int i = 0; i < _structureRows.Count; i++)
            {
                var view = _structureRows[i];
                if (!_TryFindObject(snapshot, view.objectId, out var entry)) continue;

                view.parent.text = string.IsNullOrEmpty(entry.parentName)
                    ? (entry.parentId == FrameSymbolTable.kNone
                        ? "-"
                        : _Tr("LDV_UNRESOLVED", entry.parentId))
                    : entry.parentName;

                view.parent.EnableInClassList(RemoteControlEditorStyles.kWarning,
                    entry.parentId != FrameSymbolTable.kNone && string.IsNullOrEmpty(entry.parentName));

                view.root.EnableInClassList("ldv-element-selected",
                    _detailKind == DetailKind.StructureEntry && view.objectId == _selectedObjectId);
            }
        }

        private static bool _TryFindObject(LiveDataSnapshot snapshot, int objectId, out StructureRow entry)
        {
            for (int i = 0; i < snapshot.structure.Count; i++)
            {
                if (snapshot.structure[i].objectId != objectId) continue;

                entry = snapshot.structure[i];
                return true;
            }

            entry = default;
            return false;
        }

        private VisualElement _BuildStructureRow(StructureRow entry)
        {
            var line = new VisualElement();
            line.AddToClassList("ldv-evt");

            var name = new Label(string.IsNullOrEmpty(entry.objectName)
                ? $"#{entry.objectId}"
                : entry.objectName);
            name.AddToClassList(RemoteControlEditorStyles.kGrow);
            name.AddToClassList(RemoteControlEditorStyles.kEllipsis);
            name.tooltip = entry.objectName;
            line.Add(name);

            var type = new Label(_ShortTypeName(entry.typeName));
            type.AddToClassList("ldv-col-mid");
            type.AddToClassList(RemoteControlEditorStyles.kSubtle);
            type.tooltip = entry.typeName;
            line.Add(type);

            var parent = new Label();
            parent.AddToClassList("ldv-col-mid");
            parent.AddToClassList(RemoteControlEditorStyles.kSubtle);
            line.Add(parent);

            var objectId = entry.objectId;
            line.RegisterCallback<MouseDownEvent>(_ => _SelectObject(objectId));

            _structureRows.Add(new StructureRowView
            {
                root = line,
                parent = parent,
                objectId = objectId,
            });

            return line;
        }

        private void _SelectObject(int objectId)
        {
            _detailKind = DetailKind.StructureEntry;
            _selectedObjectId = objectId;
            _Invalidate();
        }

        private void _BuildStructureDetail()
        {
            var snapshot = LiveDataTap.snapshot;

            if (!_TryFindObject(snapshot, _selectedObjectId, out var entry))
            {
                _detailTitle.text = $"#{_selectedObjectId}";
                _rows.Add(new LiveDataValueRow(string.Empty, _Tr("LDV_OBJECT_GONE")));
                return;
            }

            _detailTitle.text = string.IsNullOrEmpty(entry.objectName)
                ? $"#{entry.objectId}"
                : entry.objectName;

            _rows.Add(new LiveDataValueRow("id", string.IsNullOrEmpty(entry.objectName)
                ? _Tr("LDV_NOT_IN_SYMBOLS", entry.objectId)
                : entry.objectName));
            _rows.Add(new LiveDataValueRow("type", string.IsNullOrEmpty(entry.typeName) ? "-" : entry.typeName));
            _rows.Add(new LiveDataValueRow("parent",
                entry.parentId == FrameSymbolTable.kNone ? _Tr("LD_NONE") : entry.parentName));

            // Whether a replay can stand this back up. Empty is common and not an error -- an object
            // that was in the scene from the start is listed so its values have an owner -- but it
            // also means scrubbing back past this object's spawn will not remove it, and that is not
            // visible anywhere else.
            _rows.Add(new LiveDataValueRow("recipe",
                string.IsNullOrEmpty(entry.recipe) ? _Tr("LDV_RECIPE_NONE") : entry.recipe));

            // What the inventory says exists, against what the state lane is actually carrying for
            // it. An object with no state is not an error -- most have none -- but an object that
            // should have some and does not is exactly what this view is for.
            _rows.Add(new LiveDataValueRow("state", string.Empty));

            var carried = 0;
            for (int i = 0; i < snapshot.types.Count; i++)
            {
                var type = snapshot.types[i];
                for (int e = 0; e < type.elements.Count; e++)
                {
                    if (type.elements[e].ownerId != entry.objectId) continue;

                    var element = type.elements[e];
                    var fresh = element.lastChangedFrame == snapshot.frameNumber;

                    _rows.Add(new LiveDataValueRow(
                        _ShortTypeName(type.typeName),
                        $"{(string.IsNullOrEmpty(element.source) ? "-" : element.source)}  " +
                        (fresh
                            ? "now"
                            : _Tr("LDV_AGE_FRAMES", snapshot.frameNumber - element.lastChangedFrame)),
                        depth: 1));

                    carried++;
                    break;
                }
            }

            if (carried == 0)
            {
                _rows.Add(new LiveDataValueRow(string.Empty, _Tr("LDV_NO_STATE_FOR_OBJECT"), depth: 1));
            }
        }

        // --- event lane ---------------------------------------------------

        private void _DrawEvents()
        {
            if (_eventList == null) return;

            var count = LiveDataTap.eventCount;
            var newest = count == 0 ? -1 : LiveDataTap.GetEvent(count - 1).sequence;

            // Events only arrive, so the list is rebuilt when one does and left alone otherwise.
            var shape = $"{count}:{newest}:{_selectedEventRowId}:{_detailKind}:{RemoteControlEditorFonts.generation}";

            if (shape == _eventShape) return;
            _eventShape = shape;

            _eventList.Clear();
            _eventCount.text = _Tr("LDV_EVENT_COUNT", count);

            if (count == 0)
            {
                _eventList.Add(_Empty(_Tr("LDV_NO_EVENTS")));
                return;
            }

            // Newest first: what just happened is what is being looked for.
            for (int i = count - 1; i >= 0; i--)
            {
                _eventList.Add(_BuildEventRow(LiveDataTap.GetEvent(i)));
            }
        }

        private VisualElement _BuildEventRow(EventRow evt)
        {
            var line = new VisualElement();
            line.AddToClassList("ldv-evt-row");
            line.EnableInClassList("ldv-evt-faulted", evt.faulted);
            line.EnableInClassList("ldv-element-selected",
                _detailKind == DetailKind.Event && evt.rowId == _selectedEventRowId);

            // Frame, source and verb share one line; the target takes the next one on its own,
            // since a target path is long enough to crowd out everything beside it.
            var head = new VisualElement();
            head.AddToClassList("ldv-evt");
            line.Add(head);

            var frame = new Label(evt.frameNumber.ToString("D8"));
            frame.AddToClassList("ldv-col-frame");
            frame.AddToClassList(RemoteControlEditorStyles.kSubtle);
            RemoteControlEditorFonts.ApplyMonospace(frame);
            head.Add(frame);

            var source = new Label(string.IsNullOrEmpty(evt.source) ? "-" : evt.source);
            source.AddToClassList("ldv-col-mid");
            source.AddToClassList(RemoteControlEditorStyles.kSubtle);
            head.Add(source);

            var verb = new Label(string.IsNullOrEmpty(evt.verb) ? evt.kind.ToString() : evt.verb);
            verb.AddToClassList(RemoteControlEditorStyles.kGrow);
            verb.AddToClassList(RemoteControlEditorStyles.kEllipsis);
            head.Add(verb);

            if (evt.truncated)
            {
                var cut = new Label(_Tr("LDV_TRUNCATED"));
                cut.AddToClassList(RemoteControlEditorStyles.kWarning);
                head.Add(cut);
            }

            var target = new Label(evt.target);
            target.AddToClassList("ldv-evt-target");
            target.AddToClassList(RemoteControlEditorStyles.kEllipsis);
            target.tooltip = evt.target;
            line.Add(target);

            var rowId = evt.rowId;
            line.RegisterCallback<MouseDownEvent>(_ => _SelectEvent(rowId));

            return line;
        }

        private bool _TryFindEvent(long rowId, out EventRow evt)
        {
            var count = LiveDataTap.eventCount;
            for (int i = count - 1; i >= 0; i--)
            {
                var candidate = LiveDataTap.GetEvent(i);
                if (candidate.rowId != rowId) continue;

                evt = candidate;
                return true;
            }

            evt = default;
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
                case DetailKind.Event:
                    _BuildEventDetail();
                    break;
                case DetailKind.StructureEntry:
                    _BuildStructureDetail();
                    break;
                default:
                    _detailTitle.text = string.Empty;
                    _rows.Add(new LiveDataValueRow(string.Empty, _Tr("LDV_DETAIL_HINT")));
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
                _rows.Add(new LiveDataValueRow(string.Empty, _Tr("LDV_ELEMENT_NOT_IN_FRAME")));
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

                _rows.Add(new LiveDataValueRow(field.label, text, field.depth));
            }
        }

        private void _BuildEventDetail()
        {
            if (!_TryFindEvent(_selectedEventRowId, out var evt))
            {
                _detailTitle.text = _Tr("LDV_SELECTED_EVENT");
                _rows.Add(new LiveDataValueRow(string.Empty, _Tr("LDV_EVENT_DROPPED")));
                return;
            }

            _detailTitle.text = $"#{evt.sequence}  {evt.kind}";

            _rows.Add(new LiveDataValueRow("frame", evt.frameNumber.ToString("D8")));
            _rows.Add(new LiveDataValueRow("sequence", evt.sequence.ToString()));
            _rows.Add(new LiveDataValueRow("kind", evt.kind.ToString()));
            _rows.Add(new LiveDataValueRow("source", string.IsNullOrEmpty(evt.source) ? "-" : evt.source));
            _rows.Add(new LiveDataValueRow("verb", string.IsNullOrEmpty(evt.verb) ? "-" : evt.verb));
            _rows.Add(new LiveDataValueRow("target", evt.target));

            if (evt.faulted) _rows.Add(new LiveDataValueRow(_Tr("LDV_ROW_STATUS"), _Tr("LDV_APPLY_FAILED")));
            if (evt.truncated)
            {
                _rows.Add(new LiveDataValueRow(_Tr("LDV_ROW_STATUS"), _Tr("LDV_PAYLOAD_TRUNCATED")));
            }

            _AddPayloadRows(in evt);
        }

        /// <summary>
        /// Shows the payload the way the state lane shows a value: by its type.
        ///
        /// The same walker, so the same declarations apply -- a payload struct carrying
        /// <c>[LiveArray]</c> reads here exactly as it would in the state lane, and there is no
        /// second place that has to be taught what a type looks like.
        /// </summary>
        private void _AddPayloadRows(in EventRow evt)
        {
            if (evt.payload == null || evt.payload.Length == 0)
            {
                _rows.Add(new LiveDataValueRow("payload", _Tr("LD_NONE")));
                return;
            }

            _rows.Add(new LiveDataValueRow("payload",
                $"{_ShortTypeName(evt.payloadTypeName)}  ({evt.payload.Length} B)"));

            if (EventPayload.IsTextual(evt.payloadTypeName))
            {
                _rows.Add(new LiveDataValueRow(string.Empty,
                    EventPayload.ReadString(evt.payload), depth: 1));
                return;
            }

            var type = EventPayload.Resolve(evt.payloadTypeName);
            if (type == null)
            {
                _rows.Add(new LiveDataValueRow(string.Empty,
                    _Tr("LDV_TYPE_MISSING_IN_BUILD", evt.payloadTypeName), depth: 1));
                _rows.Add(new LiveDataValueRow(string.Empty, _Hex(evt.payload), depth: 1));
                return;
            }

            var presenter = LiveDataValuePresenters.Find(type);
            if (presenter != null)
            {
                presenter(evt.payload, evt.payload.Length, _rows);
                return;
            }

            var layout = LiveDataValueLayout.For(type);
            if (layout == null || layout.Count == 0)
            {
                _rows.Add(new LiveDataValueRow(string.Empty, _Hex(evt.payload), depth: 1));
                return;
            }

            for (int i = 0; i < layout.Count; i++)
            {
                var field = layout[i];
                var text = LiveDataValueLayout.Read(evt.payload, evt.payload.Length, field);

                _rows.Add(new LiveDataValueRow(field.label, text, field.depth + 1));
            }
        }

        private static string _Hex(byte[] bytes)
        {
            var shown = System.Math.Min(bytes.Length, 24);
            var text = new System.Text.StringBuilder(shown * 3);

            for (int i = 0; i < shown; i++)
            {
                if (i > 0) text.Append(' ');
                text.Append(bytes[i].ToString("X2"));
            }

            if (shown < bytes.Length) text.Append(" …");
            return text.ToString();
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
            RemoteControlEditorFonts.ApplyMonospace(value);
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

        /// <summary>
        /// Bytes, in the unit that reads at a glance. Two decimals from a kilobyte up: the number
        /// worth watching is how it moves when a member joins the lane, and whole kilobytes hide
        /// that until it is already large.
        /// </summary>
        private static string _Bytes(long count)
        {
            if (count < 1024) return $"{count} B";
            if (count < 1024 * 1024) return $"{count / 1024.0:0.##} KB";

            return $"{count / (1024.0 * 1024.0):0.##} MB";
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
