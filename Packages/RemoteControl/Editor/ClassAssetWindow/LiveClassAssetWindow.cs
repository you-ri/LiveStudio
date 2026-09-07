// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Lilium.RemoteControl.Editor
{
    /// <summary>
    /// Panel that builds the exposure class-first. The class list's "+"
    /// (searchable dropdown over every type) or "From Selected"
    /// (<see cref="LiveClassAssetFromSelectedWindow"/>, scoped to the selected GameObject's own
    /// components) adds an empty type definition to the class list on the left; the detail
    /// pane's "+" (<see cref="LiveClassAssetAddMemberWindow"/>, a checkbox list kept open for
    /// multi-select) fills it with members and methods - then edit the metadata (label, control,
    /// persistence) of the exposed members in the detail pane on the right.
    ///
    /// Layout: the header holds the class asset, the tabs pick what is edited, and the class tab's
    /// body is a two-pane class list / class detail split.
    ///
    /// Declaring a class is the whole job. A component of a GameObject the container exposes is
    /// listed, saved and carried in the frame from the declaration alone. Giving some other object
    /// an id of its own is said in the scene rather than here: add a LiveComponent / LiveAsset
    /// entry to the container's object list and point it at the object.
    ///
    /// What a class asset holds is type-level and scene-independent, which is why the same asset
    /// can apply to every scene at once. Who applies it is said elsewhere -- the project settings
    /// for the app's own declarations, a Remote Control Container for the ones that travel with a
    /// scene or a set bundle -- and this window only reports when nothing does.
    ///
    /// The member metadata fields are bound to a <see cref="SerializedObject"/> over the asset, so
    /// their edits get Unity's undo and dirtying for free; everything that changes the shape of a
    /// list (add / remove / reorder) goes through the explicit
    /// <see cref="LiveClassAssetMemberExposure"/> path instead and rebuilds the affected panes.
    /// </summary>
    public class LiveClassAssetWindow : EditorWindow
    {
        [MenuItem("Window/Lilium Remote Control/Live Class Asset")]
        public static void Open()
        {
            GetWindow<LiveClassAssetWindow>("Live Class Asset");
        }

        /// <summary>
        /// Opens the window on a specific asset. Switching assets clears the class selection,
        /// since the selected type name belongs to the previous asset.
        /// </summary>
        public static void Open(LiveClassAsset asset)
        {
            var window = GetWindow<LiveClassAssetWindow>("Live Class Asset");
            if (asset != null && window._preset != asset)
            {
                window._preset = asset;
                window._selectedTypeName = null;
                // Null until CreateGUI has run, which picks the fields up on its own.
                if (window._presetField != null) window._RefreshAll();
            }
            window.Focus();
        }

        // Double-clicking a LiveClassAsset in the Project view opens it here rather than doing
        // nothing; returning true tells Unity the open was handled. The callback signature is
        // fixed at int, so the id goes through LiveObjectUtility to survive the EntityId change.
        [UnityEditor.Callbacks.OnOpenAsset]
        private static bool _OnOpenAsset(int instanceId, int line)
        {
            var asset = LiveObjectUtility.InstanceIDToObject(instanceId) as LiveClassAsset;
            if (asset == null) return false;
            Open(asset);
            return true;
        }

        private const float kClassRowHeight = 20f;
        private const float kDefaultClassPaneWidth = 240f;
        private const float kMinBodyHeight = 140f;

        // A bound PropertyField also raises its change callback once while the binding pushes the
        // stored value into the field. Treating those as edits would re-register every live type
        // (and notify every client) each time a class is selected, so container reloads are held
        // back until the initial bind pass over the freshly built detail pane has run.
        private const long kBindSettleMs = 50;

        private static readonly List<LiveClassAsset.TypeDefinition> kNoDefinitions = new List<LiveClassAsset.TypeDefinition>();

        // Rows of the class list: this preset's own definitions, then the ones other assets
        // declared. Held rather than reading _preset.typeDefinitions directly, because the list is
        // no longer just that.
        private readonly List<LiveClassAsset.TypeDefinition> _classRows
            = new List<LiveClassAsset.TypeDefinition>();

        // Which asset declared a row, for the ones this preset did not. Such a row is shown so the
        // full set of declared types stays visible from one window, but its members belong to
        // whoever declared them and are not edited here.
        private readonly Dictionary<LiveClassAsset.TypeDefinition, LiveClassAsset> _classRowOwner
            = new Dictionary<LiveClassAsset.TypeDefinition, LiveClassAsset>();
        private LiveClassAsset _preset;

        // Two-pane body geometry (persisted for the window's lifetime only).
        [SerializeField] private float _classPaneWidth = kDefaultClassPaneWidth;
        [SerializeField] private string _selectedTypeName;

        /// <summary>Which of the asset's two declaration lists the body edits.</summary>
        private enum Tab
        {
            /// <summary>The types this asset declares, and the members exposed on them.</summary>
            Classes = 0,

            /// <summary>The prefabs this asset lets the remote stand up in the live scene.</summary>
            Prefabs = 1,
        }

        [SerializeField] private Tab _tab = Tab.Classes;

        // Searchable class dropdown behind the class list's "+".
        private readonly AdvancedDropdownState _classDropdownState = new AdvancedDropdownState();

        private ObjectField _presetField;
        private HelpBox _applyHelp;
        private VisualElement _bodyHost;

        private ListView _classList;
        private HelpBox _classHelp;
        private Label _classEmpty;
        private ToolbarButton _addClassButton;
        private ToolbarButton _fromSelectedButton;

        private VisualElement _detailTitle;
        private ToolbarButton _addMemberButton;
        private VisualElement _detailContent;
        private bool _settlingBind;

        private ToolbarButton[] _tabButtons;

        private ToolbarButton _addPrefabButton;
        private VisualElement _prefabContent;

        // Header text of the prefab cards, refreshed in place after an edit rather than rebuilt:
        // it is derived from the prefab the entry points at, and rebuilding the card to update it
        // would take the field being typed into with it.
        private readonly List<PrefabHeader> _prefabHeaders = new List<PrefabHeader>();

        private void OnEnable()
        {
            Undo.undoRedoPerformed += _OnUndoRedo;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= _OnUndoRedo;
        }

        // The asset may have been deleted or replaced while another window had focus; pick that
        // up on the way back in rather than polling for it.
        private void OnFocus()
        {
            if (_presetField == null) return;

            var previousPreset = _preset;
            _AcquirePreset();
            if (!ReferenceEquals(previousPreset, _preset))
            {
                _RefreshAll();
                return;
            }

            // Whether anything applies the asset is decided in another window (the project
            // settings) or in the scene, so re-read it here rather than leaving a stale notice.
            _RefreshApplyNotice();
        }

        // An undo restores the serialized state only; the declarations registered from it have to
        // be applied again, or the live classes keep the pre-undo member set.
        private void _OnUndoRedo()
        {
            LiveClassAssetMemberExposure.Reapply(_preset);
            if (_presetField != null) _RefreshAll();
        }

        private void CreateGUI()
        {
            var root = rootVisualElement;
            LiveClassAssetStyles.Apply(root);
            root.style.flexDirection = FlexDirection.Column;

            _BuildHeader(root);
            _BuildTabs(root);

            _bodyHost = new VisualElement();
            _bodyHost.style.flexGrow = 1;
            root.Add(_bodyHost);

            _RefreshAll();
        }

        // --- Header: preset asset and container ---

        private void _BuildHeader(VisualElement root)
        {
            var header = new VisualElement();
            header.AddToClassList(LiveClassAssetStyles.kHeader);

            _presetField = new ObjectField("Preset") { objectType = typeof(LiveClassAsset), allowSceneObjects = false };
            _presetField.RegisterValueChangedCallback(evt =>
            {
                _preset = evt.newValue as LiveClassAsset;
                _selectedTypeName = null;
                _RefreshAll();
            });
            header.Add(_MakeHeaderRow(_presetField, new Button(_CreatePresetAsset) { text = "New" }));

            // That nothing applies the asset, when nothing does. The window edits a declaration,
            // and a declaration nobody applies is not an error - but nothing it says reaches the
            // remote either, and there is no other place that would say so.
            _applyHelp = new HelpBox(
                "Nothing applies this asset, so none of it reaches the remote. Add it to Project"
                + " Settings > Lilium Remote Control for the whole project, or to a Remote Control"
                + " Container for declarations that travel with a scene or a set bundle.",
                HelpBoxMessageType.Warning);
            _applyHelp.AddToClassList(LiveClassAssetStyles.kHelp);
            header.Add(_applyHelp);

            root.Add(header);
        }

        /// <summary>
        /// One header row: a labelled field that grows, then a fixed-width action column.
        /// </summary>
        private static VisualElement _MakeHeaderRow(ObjectField field, Button action)
        {
            var row = new VisualElement();
            row.AddToClassList(LiveClassAssetStyles.kHeaderRow);

            field.AddToClassList(LiveClassAssetStyles.kHeaderRowField);
            row.Add(field);

            var actionColumn = new VisualElement();
            actionColumn.AddToClassList(LiveClassAssetStyles.kHeaderRowAction);
            actionColumn.Add(action);
            row.Add(actionColumn);

            return row;
        }

        private void _CreatePresetAsset()
        {
            var path = EditorUtility.SaveFilePanelInProject("Create Live Class Asset", "LiveClassAsset", "asset", "");
            if (string.IsNullOrEmpty(path)) return;

            var created = CreateInstance<LiveClassAsset>();
            AssetDatabase.CreateAsset(created, path);
            AssetDatabase.SaveAssets();
            _preset = created;
            _selectedTypeName = null;
            _RefreshAll();
        }

        /// <summary>
        /// The asset to open on when the window has none: the first one the project settings
        /// apply, which is the one the app itself declares through.
        ///
        /// A container's list is not consulted. An asset a set bundle carries belongs to that
        /// bundle, and opening this window is no reason to start editing it.
        /// </summary>
        private void _AcquirePreset()
        {
            if (_preset != null) return;

            var settings = RemoteControlProjectSettings.Instance;
            if (settings == null) return;

            var assets = settings.liveClassAssets;
            for (int i = 0; i < assets.Count; i++)
            {
                if (assets[i] == null) continue;
                _preset = assets[i];
                return;
            }
        }

        // --- Tabs ---

        /// <summary>
        /// The strip that picks what the body edits.
        ///
        /// The asset declares two independent things - what of a type is exposed, and what the
        /// remote can stand up - and neither is edited while looking at the other. Tabs rather
        /// than one long page: the class side is a two-pane split that wants the whole window.
        /// </summary>
        private void _BuildTabs(VisualElement root)
        {
            var bar = new Toolbar();
            bar.AddToClassList(LiveClassAssetStyles.kTabs);

            _tabButtons = new ToolbarButton[2];
            _tabButtons[(int)Tab.Classes] = _MakeTab(bar, Tab.Classes, "Classes",
                "Types this asset declares, and the members exposed on them");
            _tabButtons[(int)Tab.Prefabs] = _MakeTab(bar, Tab.Prefabs, "Prefabs",
                "Prefabs this asset lets the remote add to the live scene");

            root.Add(bar);
        }

        private ToolbarButton _MakeTab(Toolbar bar, Tab tab, string text, string tooltip)
        {
            var button = new ToolbarButton(() => _SelectTab(tab)) { text = text, tooltip = tooltip };
            button.AddToClassList(LiveClassAssetStyles.kTab);
            button.EnableInClassList(LiveClassAssetStyles.kTabActive, _tab == tab);
            bar.Add(button);
            return button;
        }

        private void _SelectTab(Tab tab)
        {
            // Clicking the open tab is not an edit: rebuilding the body for it would only take
            // the field being typed into with it.
            if (_tab == tab) return;

            _tab = tab;
            for (int i = 0; i < _tabButtons.Length; i++)
            {
                _tabButtons[i].EnableInClassList(LiveClassAssetStyles.kTabActive, i == (int)tab);
            }
            _RebuildBody();
        }

        // --- Body: class list | class detail ---

        /// <summary>
        /// Refreshes the header state and rebuilds the whole body. The panes are recreated rather
        /// than patched because a <see cref="TwoPaneSplitView"/> does not survive having its
        /// children swapped out.
        /// </summary>
        private void _RefreshAll()
        {
            _AcquirePreset();

            _presetField.SetValueWithoutNotify(_preset);
            _RefreshApplyNotice();

            _RebuildBody();
        }

        private void _RefreshApplyNotice()
        {
            bool unapplied = _preset != null && !LiveClassAssetMemberExposure.IsApplied(_preset);
            _applyHelp.style.display = unapplied ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void _RebuildBody()
        {
            _detailContent?.Unbind();
            _prefabContent?.Unbind();
            _bodyHost.Clear();

            // Only the open tab's pane exists; the fields of the other one point at elements that
            // were just cleared, which is why every refresh below checks the tab first.
            _detailContent = null;
            _prefabContent = null;
            _prefabHeaders.Clear();

            if (_tab == Tab.Prefabs)
            {
                _bodyHost.Add(_BuildPrefabPane());
                _RefreshPrefabs();
                return;
            }

            var classPane = _BuildClassPane();
            var detailPane = _BuildDetailPane();

            var bodySplit = new TwoPaneSplitView(0, Mathf.Max(1f, _classPaneWidth), TwoPaneSplitViewOrientation.Horizontal);
            bodySplit.Add(classPane);
            bodySplit.Add(detailPane);
            bodySplit.style.minHeight = kMinBodyHeight;
            classPane.RegisterCallback<GeometryChangedEvent>(evt =>
            {
                if (evt.newRect.width > 0f) _classPaneWidth = evt.newRect.width;
            });

            _bodyHost.Add(bodySplit);

            _RefreshStructure();
        }

        private VisualElement _BuildClassPane()
        {
            var pane = new VisualElement();
            pane.AddToClassList(LiveClassAssetStyles.kPane);
            pane.AddToClassList(LiveClassAssetStyles.kPaneDivided);

            var bar = new Toolbar();
            bar.Add(_MakePaneHeader("Classes"));
            bar.Add(_MakeSpacer());

            // Class-first flow: pick a class through the searchable dropdown to add its
            // (initially empty) type definition, then fill it with the detail pane's "+".
            _addClassButton = _MakeAddButton("Add a class", () =>
            {
                new LiveClassAssetTypeDropdown(_classDropdownState, new List<Type>(), _EnumerateCandidateTypes, _AddClass)
                    .Show(_addClassButton.worldBound);
            });
            bar.Add(_addClassButton);

            // Same as "+", but the candidates are the selected GameObject's actual components
            // instead of a global type search.
            _fromSelectedButton = new ToolbarButton(() =>
            {
                LiveClassAssetFromSelectedWindow.Open(() => _preset,
                    typeName => { _selectedTypeName = typeName; _OnStructureChanged(); },
                    GUIUtility.GUIToScreenRect(_fromSelectedButton.worldBound));
            })
            { text = "From Selected" };
            bar.Add(_fromSelectedButton);
            pane.Add(bar);

            _classList = new ListView
            {
                fixedItemHeight = kClassRowHeight,
                selectionType = SelectionType.Single,
                showBorder = false,
                makeItem = _MakeClassRow,
                bindItem = _BindClassRow,
            };
            _classList.AddToClassList(LiveClassAssetStyles.kScroll);
            _classList.selectionChanged += _OnClassSelectionChanged;
            pane.Add(_classList);

            _classHelp = new HelpBox(string.Empty, HelpBoxMessageType.Info);
            _classHelp.AddToClassList(LiveClassAssetStyles.kHelp);
            pane.Add(_classHelp);

            _classEmpty = _MakeEmpty(string.Empty);
            pane.Add(_classEmpty);

            return pane;
        }

        private VisualElement _BuildDetailPane()
        {
            var pane = new VisualElement();
            pane.AddToClassList(LiveClassAssetStyles.kPane);

            var bar = new Toolbar();
            _detailTitle = new VisualElement();
            _detailTitle.style.flexDirection = FlexDirection.Row;
            _detailTitle.style.alignItems = Align.Center;
            _detailTitle.style.flexShrink = 1;
            _detailTitle.style.overflow = Overflow.Hidden;
            bar.Add(_detailTitle);
            bar.Add(_MakeSpacer());

            // Checkbox list of this class's members/methods, kept open for multi-select.
            _addMemberButton = _MakeAddButton("Add a member", () =>
            {
                var type = _FindSelectedDefinition()?.ResolveType();
                if (type == null) return;
                LiveClassAssetAddMemberWindow.Open(() => _preset, type, _OnStructureChanged,
                    GUIUtility.GUIToScreenRect(_addMemberButton.worldBound));
            });
            bar.Add(_addMemberButton);

            pane.Add(bar);

            var scroll = new ScrollView();
            scroll.AddToClassList(LiveClassAssetStyles.kScroll);
            // The padding goes on the scroll view, not on its content container: padding there
            // adds to the content width and makes the pane scroll sideways over its own inset.
            scroll.AddToClassList(LiveClassAssetStyles.kDetail);
            _detailContent = scroll.contentContainer;
            pane.Add(scroll);

            return pane;
        }

        // --- Refresh ---

        private void _RefreshStructure()
        {
            // A structural edit lands one frame late (see _OnStructureChanged), by which time the
            // tab may have changed: the class panes are gone and there is nothing to refresh.
            if (_tab != Tab.Classes || _classList == null) return;

            _RefreshClassList();
            _RefreshDetail();
        }

        private void _RefreshClassList()
        {
            bool hasPreset = _preset != null;
            _addClassButton.SetEnabled(hasPreset);
            _fromSelectedButton.SetEnabled(hasPreset);

            _RebuildClassRows();

            _classList.itemsSource = _classRows;
            _classList.Rebuild();

            int selected = _FindSelectedDefinitionIndex();
            _classList.SetSelectionWithoutNotify(selected >= 0 ? new[] { selected } : Array.Empty<int>());

            bool hasClasses = _classRows.Count > 0;
            _classList.style.display = hasClasses ? DisplayStyle.Flex : DisplayStyle.None;

            // A missing prerequisite is advice and gets an icon; a list that is merely still
            // empty is not a problem, so it reads as the pane's dimmed empty state instead.
            _classHelp.style.display = hasPreset ? DisplayStyle.None : DisplayStyle.Flex;
            _classEmpty.style.display = hasPreset && !hasClasses ? DisplayStyle.Flex : DisplayStyle.None;
            if (!hasPreset)
            {
                _classHelp.text = "Assign or create a Live Class Asset above. It stores which members are exposed, shared across scenes.";
            }
            else if (!hasClasses)
            {
                _classEmpty.text = "Nothing exposed yet. Add a class with \"+\" or \"From Selected\", then expose its members with \"+\" in the detail pane."
                    + "\n\nThat is the whole of it for a component: add its GameObject to the container's object list and the declaration reaches every instance.";
            }
        }

        /// <summary>
        /// Fills the class list: this preset's definitions, then every type another asset declared.
        ///
        /// The second group is there because exposure is resolved by type, not by which asset
        /// declared it: a scene can expose an instance of a type a shared package declared, without
        /// copying the declaration into an asset of its own.
        /// </summary>
        private void _RebuildClassRows()
        {
            _classRows.Clear();
            _classRowOwner.Clear();

            if (_preset == null) return;

            var declaredHere = new HashSet<string>(StringComparer.Ordinal);
            foreach (var definition in _preset.typeDefinitions)
            {
                if (definition == null) continue;

                _classRows.Add(definition);
                if (!string.IsNullOrEmpty(definition.typeName)) declaredHere.Add(definition.typeName);
            }

            foreach (var guid in AssetDatabase.FindAssets("t:" + nameof(LiveClassAsset)))
            {
                var other = AssetDatabase.LoadAssetAtPath<LiveClassAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (other == null || other == _preset) continue;

                foreach (var definition in other.typeDefinitions)
                {
                    if (definition == null || string.IsNullOrEmpty(definition.typeName)) continue;

                    // Declared in both places. The one shown is this preset's, because that is the
                    // one an edit here would reach -- and which of the two the runtime ends up with
                    // is decided by registration order, not by this window.
                    if (!declaredHere.Add(definition.typeName)) continue;

                    _classRows.Add(definition);
                    _classRowOwner[definition] = other;
                }
            }
        }

        /// <summary>The asset a row was declared in, or null when this preset declared it.</summary>
        private LiveClassAsset _OwnerOf(LiveClassAsset.TypeDefinition definition)
            => definition != null && _classRowOwner.TryGetValue(definition, out var owner) ? owner : null;

        // --- Class list rows ---

        private VisualElement _MakeClassRow()
        {
            var row = new VisualElement();
            row.AddToClassList(LiveClassAssetStyles.kClassRow);

            var title = new Label();
            title.AddToClassList(LiveClassAssetStyles.kClassRowTitle);
            row.Add(title);

            var count = new Label();
            count.AddToClassList(LiveClassAssetStyles.kClassRowCount);
            row.Add(count);

            row.Add(_MakeTextButton("✕", "Remove", () =>
            {
                if (row.userData is LiveClassAsset.TypeDefinition definition) _RemoveTypeDefinition(definition);
            }));
            return row;
        }

        private void _BindClassRow(VisualElement row, int index)
        {
            var definition = index >= 0 && index < _classRows.Count ? _classRows[index] : null;
            row.userData = definition;

            var title = (Label)row[0];
            var count = (Label)row[1];
            if (definition == null)
            {
                title.text = string.Empty;
                count.text = string.Empty;
                return;
            }

            var type = definition.ResolveType();
            var owner = _OwnerOf(definition);

            title.text = type != null ? type.Name : $"(unresolved: {definition.typeName})";
            title.tooltip = owner != null
                ? $"Declared in {AssetDatabase.GetAssetPath(owner)}"
                : (type != null ? type.FullName : definition.typeName);
            title.EnableInClassList(LiveClassAssetStyles.kClassRowTitleUnresolved, type == null || owner != null);
            count.text = owner != null
                ? owner.name
                : (definition.members.Count == 1 ? "1 member" : $"{definition.members.Count} members");

            // Nothing to remove on a row this asset does not own.
            row[2].style.display = owner == null ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void _OnClassSelectionChanged(IEnumerable<object> selection)
        {
            LiveClassAsset.TypeDefinition picked = null;
            foreach (var item in selection)
            {
                picked = item as LiveClassAsset.TypeDefinition;
                break;
            }
            _selectedTypeName = picked?.typeName;
            _RefreshDetail();
        }

        // --- Class detail ---

        private void _RefreshDetail()
        {
            _detailContent.Unbind();
            _detailContent.Clear();

            int index = _FindSelectedDefinitionIndex();
            var definition = index >= 0 ? _classRows[index] : null;
            var type = definition?.ResolveType();
            var owner = _OwnerOf(definition);

            _RefreshDetailTitle(definition, type);

            // A type declared elsewhere can be bound here but not edited here: its members belong
            // to the asset that declared them, and editing them through this window would write a
            // scene's opinion into whatever shared asset happens to hold the declaration.
            _addMemberButton.style.display = definition != null && owner == null ? DisplayStyle.Flex : DisplayStyle.None;
            _addMemberButton.SetEnabled(type != null);

            if (definition == null)
            {
                _detailContent.Add(_MakeEmpty("Select a class on the left to edit its exposed members."));
                return;
            }

            if (owner != null)
            {
                _detailContent.Add(_MakeForeignDetail(definition, type, owner));
                return;
            }

            var serialized = new SerializedObject(_preset);
            var definitionProperty = serialized.FindProperty("typeDefinitions").GetArrayElementAtIndex(index);

            // Category and icon describe the type itself rather than any member, so they sit
            // above the member list. Both are optional: an empty category falls back to
            // "Binding" (the value assets registered before these fields existed were given),
            // an empty icon to the type's default.
            _detailContent.Add(_MakeBoundField(definitionProperty.FindPropertyRelative("category"), null));
            _detailContent.Add(_MakeBoundField(definitionProperty.FindPropertyRelative("icon"), null));

            var frameCost = _MakeFrameCost(definition, type);
            if (frameCost != null) _detailContent.Add(frameCost);

            _detailContent.Add(_MakeSeparator());

            var members = definition.members;
            if (members.Count == 0)
            {
                _detailContent.Add(_MakeEmpty("No member exposed on this class yet. Use \"+\" above."));
            }
            else
            {
                var membersProperty = definitionProperty.FindPropertyRelative("members");
                for (int i = 0; i < members.Count; i++)
                {
                    _detailContent.Add(_MakeMemberCard(members, i, membersProperty.GetArrayElementAtIndex(i), definition, type));
                }
            }

            _settlingBind = true;
            _detailContent.Bind(serialized);
            _detailContent.schedule.Execute(() => _settlingBind = false).ExecuteLater(kBindSettleMs);
        }

        // Type name in bold with the namespace trailing in dimmed text, so the part that
        // identifies the class stays readable when the pane is narrow.
        private void _RefreshDetailTitle(LiveClassAsset.TypeDefinition definition, Type type)
        {
            _detailTitle.Clear();
            if (definition == null)
            {
                _detailTitle.Add(_MakePaneHeader("Class Detail"));
                return;
            }
            if (type == null)
            {
                var unresolved = new Label("(unresolved)") { tooltip = definition.typeName };
                unresolved.AddToClassList(LiveClassAssetStyles.kPaneHeaderDetail);
                _detailTitle.Add(unresolved);
                return;
            }

            var name = _MakePaneHeader(type.Name);
            name.tooltip = type.AssemblyQualifiedName;
            _detailTitle.Add(name);
            if (!string.IsNullOrEmpty(type.Namespace))
            {
                var ns = new Label(type.Namespace);
                ns.AddToClassList(LiveClassAssetStyles.kPaneHeaderDetail);
                _detailTitle.Add(ns);
            }
        }

        /// <summary>
        /// What one object of this type costs in every frame, or null when none of its members are
        /// on the state lane.
        ///
        /// The state lane's one real cost is that it is paid whether or not the value changed, and
        /// nothing else in this window says what that costs. A number here is what someone deciding
        /// whether to put one more member on the lane actually needs.
        /// </summary>
        private VisualElement _MakeFrameCost(LiveClassAsset.TypeDefinition definition, Type type)
        {
            if (definition == null || type == null) return null;

            var perFrame = definition.MeasureFrameCost(type);
            if (perFrame == 0) return null;

            var label = new Label($"State lane: {perFrame} bytes per object, every frame"
                + $"  ({perFrame * 60 / 1024f:0.0} KB/s at 60 fps)");
            label.AddToClassList(LiveClassAssetStyles.kStateBudget);
            label.AddToClassList(LiveClassAssetStyles.kSubtle);
            label.tooltip = "Paid for every object of this type in every frame of a recording, "
                + "whether or not the values changed. Members carried as events cost nothing until "
                + "they are written.";
            return label;
        }

        /// <summary>
        /// The read-only view of a type another asset declared: what it exposes, and where to go to
        /// change it. Enough to decide whether this is the type whose instances are wanted here.
        /// </summary>
        private VisualElement _MakeForeignDetail(LiveClassAsset.TypeDefinition definition, Type type,
            LiveClassAsset owner)
        {
            var host = new VisualElement();

            var origin = new Label($"Declared in {owner.name}. Its instances are exposed here; "
                + "edit the members in that asset.");
            origin.AddToClassList(LiveClassAssetStyles.kHelp);
            origin.RegisterCallback<ClickEvent>(_ => Selection.activeObject = owner);
            origin.tooltip = AssetDatabase.GetAssetPath(owner);
            host.Add(origin);

            var frameCost = _MakeFrameCost(definition, type);
            if (frameCost != null) host.Add(frameCost);

            host.Add(_MakeSeparator());

            foreach (var member in definition.OrderedMembers())
            {
                if (member == null) continue;

                var row = new VisualElement();
                row.AddToClassList(LiveClassAssetStyles.kMemberHeader);

                var title = new Label(member.isFunction ? member.path + " ()" : member.path);
                title.AddToClassList(LiveClassAssetStyles.kMemberTitle);
                title.AddToClassList(LiveClassAssetStyles.kSubtle);
                row.Add(title);

                if (!member.isFunction && type != null)
                {
                    var badge = new Label();
                    badge.AddToClassList(LiveClassAssetStyles.kMemberLane);
                    _RefreshLaneBadge(badge, definition, member, type);
                    row.Add(badge);
                }

                host.Add(row);
            }

            return host;
        }

        private VisualElement _MakeMemberCard(List<LiveClassAssetMember> members, int index,
            SerializedProperty memberProperty, LiveClassAsset.TypeDefinition definition, Type ownerType)
        {
            var member = members[index];

            var card = new VisualElement();
            card.AddToClassList(LiveClassAssetStyles.kMember);

            // The label is derived from the member name at expose time and needs no editing;
            // show it as the row title with the wire name alongside.
            string rowTitle = string.IsNullOrEmpty(member.label) ? member.path : $"{member.label}  ({member.path})";
            if (member.isFunction) rowTitle += " ()";

            var header = new VisualElement();
            header.AddToClassList(LiveClassAssetStyles.kMemberHeader);
            var title = new Label(rowTitle) { tooltip = member.path };
            title.AddToClassList(LiveClassAssetStyles.kMemberTitle);
            header.Add(title);

            // Which lane carries it, on the row itself: the lane is the difference between a value
            // a recording holds every frame and one it only hears about when it changes, and reading
            // that off a list means not having to open every member to find out.
            if (!member.isFunction)
            {
                var laneProperty = memberProperty.FindPropertyRelative("lane");
                var laneBadge = new Label();
                laneBadge.AddToClassList(LiveClassAssetStyles.kMemberLane);
                _RefreshLaneBadge(laneBadge, definition, member, ownerType);
                card.TrackPropertyValue(laneProperty,
                    _ => _RefreshLaneBadge(laneBadge, definition, member, ownerType));
                header.Add(laneBadge);
            }

            var moveUp = _MakeTextButton("▲", "Move up", () => _MoveMember(members, index, -1));
            moveUp.SetEnabled(index > 0);
            header.Add(moveUp);
            var moveDown = _MakeTextButton("▼", "Move down", () => _MoveMember(members, index, 1));
            moveDown.SetEnabled(index < members.Count - 1);
            header.Add(moveDown);
            header.Add(_MakeTextButton("✕", "Unexpose", () => _RemoveMember(members, member)));
            card.Add(header);

            var body = new VisualElement();
            if (member.isFunction)
            {
                body.Add(_MakeBoundField(memberProperty.FindPropertyRelative("icon"), null));
            }
            else
            {
                body.Add(_MakeBoundField(memberProperty.FindPropertyRelative("persistable"), null));
                body.Add(_MakeBoundField(memberProperty.FindPropertyRelative("readOnly"), null));
                body.Add(_MakeBoundField(memberProperty.FindPropertyRelative("lane"), "Lane"));
            }

            if (!member.isFunction)
            {
                body.Add(_MakeSelectField(memberProperty.FindPropertyRelative("control"),
                    typeof(LiveBindingControl), "Control"));
            }

            // Help text is documentation rather than a setting, so it trails the fields that
            // shape the member itself.
            body.Add(_MakeBoundField(memberProperty.FindPropertyRelative("help"), null));

            // The section starts at this member and runs until the next one that declares a
            // title, so an empty title is the normal state - not unset metadata. It describes
            // where the member lands in the remote UI rather than the member, which is why it
            // sits last.
            var sectionProperty = memberProperty.FindPropertyRelative("section");
            var sectionTitleProperty = sectionProperty.FindPropertyRelative("title");
            body.Add(_MakeBoundField(sectionTitleProperty, "Section Title"));

            var sectionDetail = new VisualElement();
            sectionDetail.AddToClassList(LiveClassAssetStyles.kMemberNested);
            sectionDetail.Add(_MakeBoundField(sectionProperty.FindPropertyRelative("subtitle"), "Subtitle"));
            sectionDetail.Add(_MakeBoundField(sectionProperty.FindPropertyRelative("icon"), "Icon"));
            sectionDetail.style.display = _SectionDetailDisplay(sectionTitleProperty);
            card.TrackPropertyValue(sectionTitleProperty,
                property => sectionDetail.style.display = _SectionDetailDisplay(property));
            body.Add(sectionDetail);

            card.Add(body);

            return card;
        }

        private static DisplayStyle _SectionDetailDisplay(SerializedProperty sectionTitleProperty)
        {
            return string.IsNullOrEmpty(sectionTitleProperty.stringValue) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        /// <summary>
        /// A [SerializeReference, Select] field: a dropdown over the concrete types the reference
        /// can hold, and the chosen one's own fields stepped in under it. Used for a member's
        /// controller and for a prefab entry's factory, which are the same choice made twice.
        ///
        /// Built here rather than through a PropertyField over the [SerializeReference, Select]
        /// drawer. That drawer is IMGUI, and outside an InspectorElement a PropertyField leaves
        /// IMGUI's label column at its own default width, which put the dropdown a column to the
        /// right of every other field on the card. Giving the drawer a UI Toolkit side is not open
        /// to this package either: on 2022.3, PropertyField dereferences a field it only sets for
        /// its default drawing when a managed reference comes with a custom CreatePropertyGUI.
        /// </summary>
        /// <param name="derivedChildName">
        /// Serialized name of a child the reference computes for itself (a factory's prefab guid).
        /// Shown, because an empty one is why a saved object cannot find its prefab again, but not
        /// offered for editing. Null when every child is the author's to set.
        /// </param>
        private VisualElement _MakeSelectField(SerializedProperty selectProperty, Type baseType, string label,
            string derivedChildName = null)
        {
            var host = new VisualElement();

            var choices = SelectPropertyDrawer.GetChoices(baseType);
            var names = new List<string>(choices.names);
            var dropdown = new DropdownField(label, names, _SelectChoiceIndex(selectProperty, choices));
            dropdown.AddToClassList(BaseField<string>.alignedFieldUssClassName);
            dropdown.tooltip = selectProperty.tooltip;
            dropdown.RegisterValueChangedCallback(evt =>
            {
                int typeIndex = names.IndexOf(evt.newValue) - 1;
                selectProperty.managedReferenceValue = typeIndex >= 0 && typeIndex < choices.types.Length
                    ? Activator.CreateInstance(choices.types[typeIndex])
                    : null;
                selectProperty.serializedObject.ApplyModifiedProperties();
                _OnEditApplied();
            });
            host.Add(dropdown);

            var detail = new VisualElement();
            detail.AddToClassList(LiveClassAssetStyles.kMemberNested);
            _FillSelectDetail(detail, selectProperty, derivedChildName);
            host.Add(detail);

            // Rebuilt only when the reference changes type, whichever side changed it. The fields
            // under it are bound, so a value edit reaches them on its own -- and tearing them down
            // for one would take the field being typed into with it.
            var typeName = selectProperty.managedReferenceFullTypename;
            host.TrackPropertyValue(selectProperty, property =>
            {
                if (string.Equals(property.managedReferenceFullTypename, typeName, StringComparison.Ordinal)) return;
                typeName = property.managedReferenceFullTypename;
                dropdown.SetValueWithoutNotify(names[_SelectChoiceIndex(property, choices)]);
                _FillSelectDetail(detail, property, derivedChildName);
                detail.Bind(property.serializedObject);
            });

            return host;
        }

        private static int _SelectChoiceIndex(SerializedProperty selectProperty, SelectPropertyDrawer.TypeChoices choices)
        {
            var type = selectProperty.managedReferenceValue?.GetType();
            return type != null ? Array.IndexOf(choices.types, type) + 1 : 0;
        }

        // One field per visible member of the reference, or nothing for None. The fields carry
        // their binding path only; whoever rebuilds them binds them.
        private void _FillSelectDetail(VisualElement detail, SerializedProperty selectProperty, string derivedChildName)
        {
            detail.Clear();
            if (selectProperty.managedReferenceValue == null) return;

            var child = selectProperty.Copy();
            var end = selectProperty.GetEndProperty();
            bool enterChildren = true;
            while (child.NextVisible(enterChildren) && !SerializedProperty.EqualContents(child, end))
            {
                enterChildren = false;

                var field = _MakeBoundField(child.Copy(), null);
                // Derived from another field of the same reference: what is typed into it would be
                // overwritten by the next edit, which re-resolves it.
                if (derivedChildName != null && string.Equals(child.name, derivedChildName, StringComparison.Ordinal))
                {
                    field.SetEnabled(false);
                }
                detail.Add(field);
            }
        }

        /// <summary>
        /// Writes the member's lane onto its row.
        ///
        /// The lane a member is on and the lane it asked for are not always the same, and where
        /// they differ the row says the one that is true -- a badge reading "State" on a member the
        /// block has no room for would be the one place a person goes to check being the place that
        /// misleads them. The answer comes from the type definition rather than being worked out
        /// here, so the row and the registration cannot drift apart.
        /// </summary>
        private static void _RefreshLaneBadge(Label badge, LiveClassAsset.TypeDefinition definition,
            LiveClassAssetMember member, Type ownerType)
        {
            var carriedBy = definition.EffectiveLaneOf(member, ownerType, out var refusal);
            var isAuto = member.lane == LiveClassAssetLane.Auto;

            badge.RemoveFromClassList(LiveClassAssetStyles.kSubtle);
            badge.RemoveFromClassList(LiveClassAssetStyles.kWarning);
            badge.RemoveFromClassList(LiveClassAssetStyles.kAccent);

            string text;
            string tooltip;
            switch (carriedBy)
            {
                case FrameLane.State:
                    text = "State";
                    tooltip = "State lane: copied into every frame at a fixed size.";
                    badge.AddToClassList(LiveClassAssetStyles.kAccent);
                    break;
                case FrameLane.None:
                    text = "None";
                    tooltip = "Not carried by the live data at all.";
                    break;
                default:
                    text = "Event";
                    tooltip = "Event lane: recorded when it changes, one entry at a time.";
                    break;
            }

            if (refusal == LiveClassAsset.TypeDefinition.LaneRefusal.NoneRefused)
            {
                text += " ⚠";
                tooltip = "Asks to be off the frame, but the live scene saves this member, and what the "
                    + "scene saves a recording carries. Untick Persistable to take it off the frame.";
                badge.AddToClassList(LiveClassAssetStyles.kWarning);
            }
            else if (refusal != LiveClassAsset.TypeDefinition.LaneRefusal.None)
            {
                text += " ⚠";
                tooltip = $"Asks for the state lane, but {member.ResolveValueType(ownerType)?.Name ?? "this member"} "
                    + "cannot be moved as bytes. Carried on the event lane instead.";
                badge.AddToClassList(LiveClassAssetStyles.kWarning);
            }
            else if (isAuto)
            {
                // Dimmed and marked: nothing was said about this member, and the answer would move
                // on its own if the persistence did -- or, for the fallback below, if the value's
                // type did.
                text += " (auto)";
                tooltip += carriedBy == FrameLane.State
                    ? " Auto, because the live scene saves this member. Set Lane to say otherwise."
                    : carriedBy == FrameLane.Event && member.persistable
                        ? " Auto: the live scene saves this member, but its value cannot be moved as "
                          + "bytes, so it is carried as events. Set Lane to say otherwise."
                        : " Auto, because the live scene does not save this member. Tick Persistable "
                          + "to carry it, or set Lane to carry it anyway.";
                badge.AddToClassList(LiveClassAssetStyles.kSubtle);
            }

            badge.text = text;
            badge.tooltip = tooltip;
        }

        private PropertyField _MakeBoundField(SerializedProperty property, string label)
        {
            var field = label == null ? new PropertyField(property) : new PropertyField(property, label);
            field.RegisterValueChangeCallback(_ => _OnBoundValueChanged());
            return field;
        }

        // Bound fields apply and dirty the asset themselves; only the container's live
        // registration has to be told that the metadata behind it moved.
        private void _OnBoundValueChanged()
        {
            if (_settlingBind) return;
            _OnEditApplied();
        }

        // Everything an edit has to do beyond the binding's own apply. The card headers follow
        // here rather than at each field, because what a prefab card is called comes from a field
        // two levels down inside its factory.
        private void _OnEditApplied()
        {
            _ApplyChanges();
            _RefreshPrefabHeaders();
        }

        private LiveClassAsset.TypeDefinition _FindSelectedDefinition()
        {
            int index = _FindSelectedDefinitionIndex();
            return index >= 0 ? _classRows[index] : null;
        }

        private int _FindSelectedDefinitionIndex()
        {
            if (_preset == null || string.IsNullOrEmpty(_selectedTypeName)) return -1;

            for (int i = 0; i < _classRows.Count; i++)
            {
                var definition = _classRows[i];
                if (definition != null
                    && string.Equals(definition.typeName, _selectedTypeName, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        // --- Preset mutations ---

        private void _ApplyChanges()
        {
            if (_preset == null) return;

            // The guid a saved object names its prefab by is derived from the prefab reference,
            // so it is re-resolved before anything registers the entries again.
            _preset.RefreshPrefabKeys();
            EditorUtility.SetDirty(_preset);

            // Both reach the asset through whoever applied it - a container that lists it, or the
            // session-long registration the project settings made - and do nothing at all for an
            // asset nothing applies.
            LivePrefabCatalog.Refresh(_preset);
            LiveClassAssetMemberExposure.Reapply(_preset);
        }

        /// <summary>
        /// Applies a mutation that changed the shape of a list. The rebuild itself waits for the
        /// next panel update: these run from click handlers on rows the rebuild destroys, and
        /// tearing an element down while its own event is still being dispatched is not safe.
        /// </summary>
        private void _OnStructureChanged()
        {
            _ApplyChanges();
            rootVisualElement.schedule.Execute(_RefreshStructure);
        }

        // Opens one undo step over the asset; see LiveClassAssetMemberExposure.BeginEdit.
        private void _BeginEdit(string name)
        {
            LiveClassAssetMemberExposure.BeginEdit(_preset, name);
        }

        private void _AddClass(Type type)
        {
            if (_preset == null) return;
            _BeginEdit("Add Class");
            var added = _preset.GetOrAddTypeDefinition(type);
            _selectedTypeName = added.typeName;
            _OnStructureChanged();
        }

        private void _RemoveTypeDefinition(LiveClassAsset.TypeDefinition definition)
        {
            if (_preset == null || definition == null) return;

            // Declared by another asset: removing it here would leave that declaration standing --
            // the opposite of what the button says. The row hides the button; this is the guard
            // behind it.
            if (_OwnerOf(definition) != null) return;

            _BeginEdit("Remove Class");
            _preset.typeDefinitions.Remove(definition);
            if (string.Equals(_selectedTypeName, definition.typeName, StringComparison.Ordinal)) _selectedTypeName = null;
            _OnStructureChanged();
        }

        private void _MoveMember(List<LiveClassAssetMember> members, int index, int delta)
        {
            int target = index + delta;
            if (target < 0 || target >= members.Count) return;
            _BeginEdit("Reorder Member");
            (members[target], members[index]) = (members[index], members[target]);
            _OnStructureChanged();
        }

        private void _RemoveMember(List<LiveClassAssetMember> members, LiveClassAssetMember member)
        {
            _BeginEdit("Unexpose Member");
            members.Remove(member);
            _OnStructureChanged();
        }

        // --- Prefabs ---

        /// <summary>
        /// The prefab pane: one card per entry the asset offers, in the order the remote lists
        /// them.
        ///
        /// One pane rather than the class tab's list / detail split. An entry is a factory, the
        /// prefab it holds and the page that offers it - which fits on the card that names it,
        /// with nothing left over for a second pane to show.
        /// </summary>
        private VisualElement _BuildPrefabPane()
        {
            var pane = new VisualElement();
            pane.AddToClassList(LiveClassAssetStyles.kPane);

            var bar = new Toolbar();
            bar.Add(_MakePaneHeader("Prefabs"));
            bar.Add(_MakeSpacer());
            _addPrefabButton = _MakeAddButton("Add a prefab", _AddPrefab);
            bar.Add(_addPrefabButton);
            pane.Add(bar);

            var scroll = new ScrollView();
            scroll.AddToClassList(LiveClassAssetStyles.kScroll);
            // Same reason as the class detail pane: padding on the content container adds to the
            // content width and makes the pane scroll sideways over its own inset.
            scroll.AddToClassList(LiveClassAssetStyles.kDetail);
            _prefabContent = scroll.contentContainer;
            pane.Add(scroll);

            return pane;
        }

        private void _RefreshPrefabs()
        {
            _prefabContent.Unbind();
            _prefabContent.Clear();
            _prefabHeaders.Clear();

            bool hasPreset = _preset != null;
            _addPrefabButton.SetEnabled(hasPreset);

            if (!hasPreset)
            {
                var help = new HelpBox(
                    "Assign or create a Live Class Asset above. Its prefab list is what a page's \"+\" offers in RemoteApp.",
                    HelpBoxMessageType.Info);
                help.AddToClassList(LiveClassAssetStyles.kHelp);
                _prefabContent.Add(help);
                return;
            }

            var prefabs = _preset.prefabs;
            if (prefabs == null || prefabs.Count == 0)
            {
                _prefabContent.Add(_MakeEmpty(
                    "Nothing offered yet. Add an entry with \"+\" and point it at a prefab."
                    + "\n\nEvery entry is offered on the scene page; naming a category also puts it on that page's \"+\"."));
                return;
            }

            var serialized = new SerializedObject(_preset);
            var prefabsProperty = serialized.FindProperty("prefabs");
            for (int i = 0; i < prefabs.Count; i++)
            {
                if (prefabs[i] == null) continue;
                _prefabContent.Add(_MakePrefabCard(prefabs, i, prefabsProperty.GetArrayElementAtIndex(i)));
            }

            _settlingBind = true;
            _prefabContent.Bind(serialized);
            _prefabContent.schedule.Execute(() => _settlingBind = false).ExecuteLater(kBindSettleMs);
        }

        private VisualElement _MakePrefabCard(List<LiveClassAsset.PrefabDefinition> prefabs, int index,
            SerializedProperty prefabProperty)
        {
            var definition = prefabs[index];

            var card = new VisualElement();
            card.AddToClassList(LiveClassAssetStyles.kMember);

            var header = new VisualElement();
            header.AddToClassList(LiveClassAssetStyles.kMemberHeader);

            var title = new Label();
            title.AddToClassList(LiveClassAssetStyles.kMemberTitle);
            header.Add(title);

            var moveUp = _MakeTextButton("\u25b2", "Move up", () => _MovePrefab(prefabs, index, -1));
            moveUp.SetEnabled(index > 0);
            header.Add(moveUp);
            var moveDown = _MakeTextButton("\u25bc", "Move down", () => _MovePrefab(prefabs, index, 1));
            moveDown.SetEnabled(index < prefabs.Count - 1);
            header.Add(moveDown);
            header.Add(_MakeTextButton("\u2715", "Remove", () => _RemovePrefab(prefabs, definition)));
            card.Add(header);

            // What the entry is missing, on the card that is missing it. An entry with no factory
            // or no prefab is skipped when the catalogue is built (LivePrefabCatalog.Register), so
            // nothing downstream ever complains about it.
            var status = new Label();
            status.AddToClassList(LiveClassAssetStyles.kStateBudget);
            status.AddToClassList(LiveClassAssetStyles.kWarning);
            card.Add(status);

            var headerRefs = new PrefabHeader(definition, title, status);
            _prefabHeaders.Add(headerRefs);
            _RefreshPrefabHeader(headerRefs);

            var body = new VisualElement();

            // The factory decides what the instance is exposed as (a plain object, one with a
            // transform, a camera) and holds the prefab itself, which is why the prefab field
            // appears stepped in under it rather than beside it.
            body.Add(_MakeSelectField(prefabProperty.FindPropertyRelative("factory"),
                typeof(ILiveObjectFactory), "Factory", kPrefabGuidField));

            body.Add(_MakeBoundField(prefabProperty.FindPropertyRelative("category"), "Category"));
            card.Add(body);

            return card;
        }

        /// <summary>Card header of one prefab entry: what it is called, and what it lacks.</summary>
        private readonly struct PrefabHeader
        {
            public readonly LiveClassAsset.PrefabDefinition definition;
            public readonly Label title;
            public readonly Label status;

            public PrefabHeader(LiveClassAsset.PrefabDefinition definition, Label title, Label status)
            {
                this.definition = definition;
                this.title = title;
                this.status = status;
            }
        }

        private void _RefreshPrefabHeaders()
        {
            for (int i = 0; i < _prefabHeaders.Count; i++)
            {
                _RefreshPrefabHeader(_prefabHeaders[i]);
            }
        }

        private static void _RefreshPrefabHeader(PrefabHeader header)
        {
            var factory = header.definition?.factory;

            // Every factory names itself after the prefab it holds, so an empty name is the one
            // reading of "nothing is pointed at yet" that works for all of them.
            var name = factory != null ? factory.name : null;
            bool unset = string.IsNullOrEmpty(name);

            header.title.text = unset ? "(no prefab)" : name;
            header.title.EnableInClassList(LiveClassAssetStyles.kSubtle, unset);

            string status = factory == null
                ? "No factory: this entry offers nothing until one is picked."
                : unset ? "No prefab: this entry offers nothing until one is assigned."
                : null;
            header.status.text = status ?? string.Empty;
            header.status.style.display = status == null ? DisplayStyle.None : DisplayStyle.Flex;
        }

        // --- Prefab mutations ---

        // The guid the factories derive from their prefab: shown on the card, not editable there.
        // See _MakeSelectField.
        private const string kPrefabGuidField = "_prefabGuid";

        /// <summary>
        /// Adds an entry holding the ordinary factory: a prefab you place and move. The other
        /// kinds are one dropdown away on the card, which is a better start than an entry that
        /// offers nothing until a type is picked.
        /// </summary>
        private void _AddPrefab()
        {
            if (_preset == null) return;

            _BeginEdit("Add Prefab");
            _preset.prefabs ??= new List<LiveClassAsset.PrefabDefinition>();
            _preset.prefabs.Add(new LiveClassAsset.PrefabDefinition
            {
                factory = new LiveGameObjectWithTransformFactory(),
            });
            _OnPrefabStructureChanged();
        }

        private void _RemovePrefab(List<LiveClassAsset.PrefabDefinition> prefabs,
            LiveClassAsset.PrefabDefinition definition)
        {
            if (_preset == null || definition == null) return;

            _BeginEdit("Remove Prefab");
            prefabs.Remove(definition);
            _OnPrefabStructureChanged();
        }

        private void _MovePrefab(List<LiveClassAsset.PrefabDefinition> prefabs, int index, int delta)
        {
            int target = index + delta;
            if (target < 0 || target >= prefabs.Count) return;

            _BeginEdit("Reorder Prefab");
            (prefabs[target], prefabs[index]) = (prefabs[index], prefabs[target]);
            _OnPrefabStructureChanged();
        }

        // The same deferral as _OnStructureChanged, for the same reason: these run from click
        // handlers on the cards the rebuild destroys.
        private void _OnPrefabStructureChanged()
        {
            _ApplyChanges();
            rootVisualElement.schedule.Execute(() =>
            {
                if (_tab == Tab.Prefabs && _prefabContent != null) _RefreshPrefabs();
            });
        }

        // --- Class-first flow: candidate types for the class list's "+" dropdown ---

        private static IEnumerable<Type> _EnumerateCandidateTypes()
        {
            foreach (var type in TypeCache.GetTypesDerivedFrom<Component>())
            {
                if (_IsPickableType(type)) yield return type;
            }
            foreach (var type in TypeCache.GetTypesDerivedFrom<ScriptableObject>())
            {
                if (_IsPickableType(type)) yield return type;
            }
        }

        private static bool _IsPickableType(Type type)
        {
            if (type.IsAbstract || type.IsGenericType) return false;
            if (LiveClassAssetMemberExposure.IsObsolete(type)) return false;
            // Editor-only types are never resolvable in a player; keep them out of the picker.
            var ns = type.Namespace;
            if (ns != null && ns.StartsWith("UnityEditor", StringComparison.Ordinal)) return false;
            var assemblyName = type.Assembly.GetName().Name;
            if (assemblyName.IndexOf("Editor", StringComparison.Ordinal) >= 0) return false;
            return true;
        }

        // --- Small element factories ---

        private static Label _MakePaneHeader(string text)
        {
            var label = new Label(text);
            label.AddToClassList(LiveClassAssetStyles.kPaneHeader);
            return label;
        }

        // Rule between the fields that describe the class and the cards that describe its members.
        private static VisualElement _MakeSeparator()
        {
            var separator = new VisualElement();
            separator.AddToClassList(LiveClassAssetStyles.kSeparator);
            separator.style.marginTop = 4f;
            separator.style.marginBottom = 4f;
            return separator;
        }

        private static VisualElement _MakeSpacer()
        {
            var spacer = new VisualElement();
            spacer.AddToClassList(LiveClassAssetStyles.kSpacer);
            return spacer;
        }

        // What a pane shows in place of a list that has no entries yet.
        private static Label _MakeEmpty(string text)
        {
            var label = new Label(text);
            label.AddToClassList(LiveClassAssetStyles.kEmpty);
            return label;
        }

        // Borderless glyph button for the affordances that sit inside a list row; a framed
        // button per row reads as heavier than the row it acts on.
        private static Button _MakeTextButton(string text, string tooltip, Action onClick)
        {
            var button = new Button(onClick) { text = text, tooltip = tooltip };
            button.AddToClassList(LiveClassAssetStyles.kRowButton);
            return button;
        }

        // "Toolbar Plus" is Unity's built-in "+" - the same one ReorderableList draws.
        private static ToolbarButton _MakeAddButton(string tooltip, Action onClick)
        {
            var button = new ToolbarButton(onClick) { tooltip = tooltip };
            button.AddToClassList(LiveClassAssetStyles.kIconButton);
            button.Add(new Image
            {
                image = EditorGUIUtility.IconContent("Toolbar Plus").image,
                scaleMode = ScaleMode.ScaleToFit,
            });
            return button;
        }
    }
}
