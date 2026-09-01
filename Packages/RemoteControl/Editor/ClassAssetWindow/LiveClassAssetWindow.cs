// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

using Lilium.RemoteControl.LiveScene;

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
    /// Layout: the header holds the class asset and the container, the body is a two-pane class
    /// list / class detail split, and the footer lists the instance bindings - hidden entirely
    /// until a container is assigned, since there is nothing to bind into without one.
    ///
    /// Declaring a class is the whole job for the ordinary case. A component of a GameObject the
    /// container exposes is listed, saved and carried in the frame from the declaration alone, so
    /// the footer is expected to stay empty. A binding is the opt-in for giving one object an id of
    /// its own - an object that is not a component of an exposed GameObject, a second component of
    /// a type already on the same GameObject (the composed address is keyed by type name and would
    /// collide), or one whose id has to survive being used from another scene.
    ///
    /// Exposure settings are stored in a <see cref="LiveClassAsset"/> asset (shared across
    /// scenes); the scene-object references live in a <see cref="RemoteControlContainer"/> in the
    /// scene, using the standard IExposedPropertyTable mechanism.
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
        private const float kDefaultFooterHeight = 220f;
        private const float kMinBodyHeight = 140f;

        // A bound PropertyField also raises its change callback once while the binding pushes the
        // stored value into the field. Treating those as edits would re-register every live type
        // (and notify every client) each time a class is selected, so container reloads are held
        // back until the initial bind pass over the freshly built detail pane has run.
        private const long kBindSettleMs = 50;

        private static readonly List<LiveClassAsset.TypeDefinition> kNoDefinitions = new List<LiveClassAsset.TypeDefinition>();

        private RemoteControlContainer _container;

        // Rows of the class list: this preset's own definitions, then the ones other assets
        // declared. Held rather than reading _preset.typeDefinitions directly, because the list is
        // no longer just that.
        private readonly List<LiveClassAsset.TypeDefinition> _classRows
            = new List<LiveClassAsset.TypeDefinition>();

        // Which asset declared a row, for the ones this preset did not. An instance binding names
        // its type and nothing else, so a type declared elsewhere can be bound here -- but its
        // members belong to whoever declared them and are not edited through this window.
        private readonly Dictionary<LiveClassAsset.TypeDefinition, LiveClassAsset> _classRowOwner
            = new Dictionary<LiveClassAsset.TypeDefinition, LiveClassAsset>();
        private LiveClassAsset _preset;

        // Two-pane body + footer geometry (persisted for the window's lifetime only).
        [SerializeField] private float _classPaneWidth = kDefaultClassPaneWidth;
        [SerializeField] private float _footerHeight = kDefaultFooterHeight;
        [SerializeField] private string _selectedTypeName;
        [SerializeField] private bool _bindingsFoldout = true;

        // Searchable class dropdown behind the class list's "+".
        private readonly AdvancedDropdownState _classDropdownState = new AdvancedDropdownState();

        private ObjectField _presetField;
        private ObjectField _containerField;
        private Button _createContainerButton;
        private VisualElement _bodyHost;

        private ListView _classList;
        private HelpBox _classHelp;
        private Label _classEmpty;
        private ToolbarButton _addClassButton;
        private ToolbarButton _fromSelectedButton;

        private VisualElement _detailTitle;
        private ToolbarButton _addMemberButton;
        private ToolbarButton _addBindingButton;
        private VisualElement _detailContent;
        private bool _settlingBind;

        private Foldout _bindingsFoldoutElement;

        private void OnEnable()
        {
            Undo.undoRedoPerformed += _OnUndoRedo;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= _OnUndoRedo;
        }

        // The container may have been created, deleted or replaced while another window had
        // focus; pick that up on the way back in rather than polling for it.
        private void OnFocus()
        {
            if (_presetField == null) return;
            var previousContainer = _container;
            var previousPreset = _preset;
            _AcquireContainerAndPreset();
            if (!ReferenceEquals(previousContainer, _container) || !ReferenceEquals(previousPreset, _preset))
            {
                _RefreshAll();
            }
        }

        // An undo restores the serialized state only; the container's runtime lookup table has to
        // be rebuilt from it, or the bindings keep resolving to the pre-undo objects.
        private void _OnUndoRedo()
        {
            if (_container != null) _container.Reload();
            if (_presetField != null) _RefreshAll();
        }

        private void CreateGUI()
        {
            var root = rootVisualElement;
            LiveClassAssetStyles.Apply(root);
            root.style.flexDirection = FlexDirection.Column;

            _BuildHeader(root);

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

            _containerField = new ObjectField("Container") { objectType = typeof(RemoteControlContainer), allowSceneObjects = true };
            _containerField.RegisterValueChangedCallback(evt =>
            {
                _container = evt.newValue as RemoteControlContainer;
                _RefreshAll();
            });
            _createContainerButton = new Button(_CreateContainer) { text = "Create" };
            header.Add(_MakeHeaderRow(_containerField, _createContainerButton));

            root.Add(header);
        }

        /// <summary>
        /// One header row: a labelled field that grows, then a fixed-width action column. The
        /// column keeps its width when its button is hidden, so both fields end at the same x
        /// whether or not a container still has to be created.
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

        private void _CreateContainer()
        {
            var go = new GameObject("Remote Control Container");
            Undo.RegisterCreatedObjectUndo(go, "Create Remote Control Container");
            _container = Undo.AddComponent<RemoteControlContainer>(go);
            _RefreshAll();
        }

        private void _AcquireContainerAndPreset()
        {
            if (_container == null)
            {
                var containers = FindObjectsByType<RemoteControlContainer>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID);
                if (containers.Length > 0) _container = containers[0];
            }
            if (_preset == null && _container != null && _container.assets.Count > 0)
            {
                _preset = _container.assets[0];
            }
        }

        // --- Body: (class list | class detail) over the instance-bindings footer ---

        /// <summary>
        /// Refreshes the header state and rebuilds the whole body. The panes are recreated rather
        /// than patched because the footer's presence changes the split hierarchy itself, and a
        /// <see cref="TwoPaneSplitView"/> does not survive having its children swapped out.
        /// </summary>
        private void _RefreshAll()
        {
            _AcquireContainerAndPreset();

            _presetField.SetValueWithoutNotify(_preset);
            _containerField.SetValueWithoutNotify(_container);
            _createContainerButton.style.display = _container == null ? DisplayStyle.Flex : DisplayStyle.None;

            _RebuildBody();
        }

        private void _RebuildBody()
        {
            _detailContent?.Unbind();
            _bindingsFoldoutElement = null;
            _bodyHost.Clear();

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

            // No container means nothing to bind into, so the instance-bindings footer has
            // nothing to show; leave the whole pane out rather than show an empty box.
            if (_container == null)
            {
                _bodyHost.Add(bodySplit);
            }
            else
            {
                var footer = _BuildFooter();
                var footerSplit = new TwoPaneSplitView(1, Mathf.Max(1f, _footerHeight), TwoPaneSplitViewOrientation.Vertical);
                footerSplit.Add(bodySplit);
                footerSplit.Add(footer);
                footer.RegisterCallback<GeometryChangedEvent>(evt =>
                {
                    if (evt.newRect.height > 0f) _footerHeight = evt.newRect.height;
                });
                _bodyHost.Add(footerSplit);
            }

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
                LiveClassAssetAddMemberWindow.Open(() => _preset, () => _container, type, _OnStructureChanged,
                    GUIUtility.GUIToScreenRect(_addMemberButton.worldBound));
            });
            bar.Add(_addMemberButton);

            // Creates an unbound instance entry; the object itself is assigned in Instance
            // Bindings in the footer (or another scene's object through its container). Needs a
            // container to bind into, so it stays hidden without one.
            //
            // Not the ordinary route any more. A component of a GameObject the container exposes is
            // listed, saved and carried in the frame without one -- a binding is for giving an
            // object an identity of its own, which the tooltip says and the footer repeats.
            _addBindingButton = new ToolbarButton(() => _AddBinding(_FindSelectedDefinition())) { text = "Add Binding" };
            _addBindingButton.tooltip =
                "Give one object an id of its own.\n\n"
                + "Usually unnecessary: add the GameObject to the container's object list and its "
                + "exposed components are listed, saved and recorded already.\n\n"
                + "Bind when the object is not a component of an exposed GameObject (an asset, a "
                + "ScriptableObject), when two components of this type sit on one GameObject, or "
                + "when the id has to stay the same across scenes.";
            bar.Add(_addBindingButton);
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

        private VisualElement _BuildFooter()
        {
            var footer = new VisualElement();
            footer.AddToClassList(LiveClassAssetStyles.kFooter);

            var scroll = new ScrollView();
            scroll.AddToClassList(LiveClassAssetStyles.kScroll);

            _bindingsFoldoutElement = new Foldout { value = _bindingsFoldout };
            _bindingsFoldoutElement.RegisterValueChangedCallback(evt =>
            {
                // Toggles inside the foldout raise bool change events of their own that bubble
                // through here; only the foldout's own event carries the fold state.
                if (evt.target != _bindingsFoldoutElement) return;
                _bindingsFoldout = evt.newValue;
            });
            scroll.Add(_bindingsFoldoutElement);
            footer.Add(scroll);

            return footer;
        }

        // --- Refresh ---

        private void _RefreshStructure()
        {
            _RefreshClassList();
            _RefreshDetail();
            _RefreshBindings();
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
        /// The second group is what makes a binding independent of where the type was declared. A
        /// binding carries a key and a type name, and the runtime resolves the live class by type
        /// from whoever registered it -- so a scene's own asset can bind an instance of a type a
        /// shared package declared, without copying the declaration and without writing the scene's
        /// object into the package.
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

        private void _RefreshBindings()
        {
            if (_bindingsFoldoutElement == null) return;

            _bindingsFoldoutElement.Clear();
            int count = _preset != null ? _preset.bindings.Count : 0;
            _bindingsFoldoutElement.text = $"Instance Bindings ({count})";
            // The foldout builds its text label lazily, so the title style can only be applied
            // once a text has been assigned.
            _bindingsFoldoutElement.Q<Label>(className: Foldout.textUssClassName)
                ?.EnableInClassList(LiveClassAssetStyles.kFooterTitle, true);

            if (_preset == null)
            {
                _bindingsFoldoutElement.Add(_MakeEmpty("Assign a preset to bind instances."));
                return;
            }
            if (count == 0)
            {
                // Deliberately not "use Add Binding": empty is the ordinary state. Declaring the
                // class is enough for every component of a GameObject the container exposes, and
                // saying otherwise here sent people to bind one object at a time.
                _bindingsFoldoutElement.Add(_MakeEmpty(
                    "None, which is usually right: components of a GameObject in the container's "
                    + "object list are exposed by the declaration alone.\n"
                    + "Bind an object here to give it an id of its own."));
                return;
            }
            foreach (var entry in _preset.bindings)
            {
                if (entry == null) continue;
                _bindingsFoldoutElement.Add(_MakeBindingRow(entry));
            }
        }

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
            _addBindingButton.style.display = definition != null && _container != null ? DisplayStyle.Flex : DisplayStyle.None;

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
                // The polymorphic controller goes through a PropertyField so the
                // [SerializeReference, Select] drawer provides the type dropdown and the
                // per-control fields.
                body.Add(_MakeBoundField(memberProperty.FindPropertyRelative("control"), "Control"));
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
            sectionDetail.AddToClassList(LiveClassAssetStyles.kMemberSectionDetail);
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

            if (refusal != LiveClassAsset.TypeDefinition.LaneRefusal.None)
            {
                text += " ⚠";
                tooltip = $"Asks for the state lane, but {member.ResolveValueType(ownerType)?.Name ?? "this member"} "
                    + "cannot be moved as bytes. Carried on the event lane instead.";
                badge.AddToClassList(LiveClassAssetStyles.kWarning);
            }
            else if (isAuto)
            {
                // Dimmed and marked: nothing was said about this member, and the answer would move
                // on its own if the member it points at changed from a field to a property.
                text += " (auto)";
                tooltip += " Auto, from the member being "
                    + (carriedBy == FrameLane.State ? "a field." : "a property.")
                    + " Set Lane to say otherwise.";
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
            _ApplyChanges();
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

        // --- Instance binding rows ---

        // Only reached with a container present - the footer itself is left out without one.
        private VisualElement _MakeBindingRow(LiveClassAsset.InstanceBinding entry)
        {
            var expectedType = entry.ResolveType() ?? typeof(UnityEngine.Object);
            var current = _container.ResolveKey(entry.key);

            var row = new VisualElement();
            row.AddToClassList(LiveClassAssetStyles.kBindingRow);

            var state = new Label("(unbound)");
            state.AddToClassList(LiveClassAssetStyles.kBindingRowState);
            state.style.display = current == null ? DisplayStyle.Flex : DisplayStyle.None;

            var field = new ObjectField(expectedType.Name) { objectType = expectedType, allowSceneObjects = true };
            field.AddToClassList(LiveClassAssetStyles.kBindingRowField);
            field.SetValueWithoutNotify(current);
            field.RegisterValueChangedCallback(evt =>
            {
                _BeginEdit("Rebind Instance");
                _container.SetReferenceValue(new PropertyName(entry.key), evt.newValue);
                if (evt.newValue != null) entry.typeName = evt.newValue.GetType().AssemblyQualifiedName;
                _ApplyChanges();
                // Patched in place instead of rebuilt, so the row the user is still interacting
                // with survives its own edit.
                field.label = (entry.ResolveType() ?? typeof(UnityEngine.Object)).Name;
                state.style.display = evt.newValue == null ? DisplayStyle.Flex : DisplayStyle.None;
            });
            row.Add(field);
            row.Add(state);

            row.Add(_MakeTextButton("✕", "Remove", () =>
            {
                _BeginEdit("Remove Binding");
                _container.ClearReferenceValue(new PropertyName(entry.key));
                _preset.bindings.Remove(entry);
                _OnStructureChanged();
            }));
            return row;
        }

        // --- Preset mutations ---

        private void _ApplyChanges()
        {
            if (_preset != null) EditorUtility.SetDirty(_preset);
            if (_container != null)
            {
                EditorUtility.SetDirty(_container);
                _container.Reload();
            }
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

        // Opens one undo step over the asset + container; see LiveClassAssetMemberExposure.BeginEdit.
        private void _BeginEdit(string name)
        {
            LiveClassAssetMemberExposure.BeginEdit(_preset, _container, name);
        }

        // Ensures the edited preset is registered on the container (so its bindings resolve at runtime).
        private void _EnsurePresetOnContainer()
        {
            LiveClassAssetMemberExposure.EnsurePresetOnContainer(_preset, _container);
        }

        private void _AddClass(Type type)
        {
            if (_preset == null) return;
            _BeginEdit("Add Class");
            _EnsurePresetOnContainer();
            var added = _preset.GetOrAddTypeDefinition(type);
            _selectedTypeName = added.typeName;
            _OnStructureChanged();
        }

        private void _AddBinding(LiveClassAsset.TypeDefinition definition)
        {
            if (_preset == null || definition == null) return;
            _BeginEdit("Add Binding");
            _EnsurePresetOnContainer();
            _preset.bindings.Add(new LiveClassAsset.InstanceBinding
            {
                key = Guid.NewGuid().ToString(),
                typeName = definition.typeName,
            });
            _bindingsFoldout = true;
            if (_bindingsFoldoutElement != null) _bindingsFoldoutElement.value = true;
            _OnStructureChanged();
        }

        private void _RemoveTypeDefinition(LiveClassAsset.TypeDefinition definition)
        {
            if (_preset == null || definition == null) return;

            // Declared by another asset. Removing it here would take this preset's bindings for the
            // type with it while leaving the declaration standing -- the opposite of what the button
            // says. The row hides the button; this is the guard behind it.
            if (_OwnerOf(definition) != null) return;
            var type = definition.ResolveType();

            _BeginEdit("Remove Class");
            _preset.typeDefinitions.Remove(definition);
            LiveClassAssetMemberExposure.RemoveBindingsOfType(_preset, _container, type);
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
