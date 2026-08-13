// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

using Lilium.RemoteControl.LiveScene;

namespace Lilium.RemoteControl.Editor
{
    /// <summary>
    /// Panel that builds the exposure class-first. The class list's "+"
    /// (searchable dropdown over every type) or "From Selected"
    /// (<see cref="LiveClassAssetFromSelectedWindow"/>, scoped to the selected GameObject's own
    /// components) adds an empty type definition to the class list on the left; the detail
    /// pane's "+" (<see cref="LiveClassAssetAddMemberWindow"/>, a checkbox list kept open for
    /// multi-select) fills it with members and methods — then edit the metadata (label, control,
    /// persistence) of the exposed members in the detail pane on the right.
    ///
    /// Layout: the header holds the class asset and the container, the body is a two-pane class
    /// list / class detail split, and the footer lists the instance bindings — hidden entirely
    /// until a container is assigned, since there is nothing to bind into without one.
    ///
    /// Exposure settings are stored in a <see cref="LiveClassAsset"/> asset (shared across
    /// scenes); the scene-object references live in a <see cref="RemoteControlContainer"/> in the
    /// scene, using the standard IExposedPropertyTable mechanism.
    /// </summary>
    public class LiveClassAssetWindow : EditorWindow
    {
        [MenuItem("Window/Lilium Remote Control/Live Class Asset")]
        public static void Open()
        {
            GetWindow<LiveClassAssetWindow>("Live Class Asset");
        }

        private const float kHeaderLabelWidth = 58f;
        private const float kAddButtonWidth = 24f;
        private const float kRemoveButtonWidth = 24f;
        private const float kSplitterThickness = 4f;
        private const float kMinPaneWidth = 160f;
        private const float kMinFooterHeight = 60f;

        // The add / remove buttons are icon-sized, so the tooltip carries what they do.
        // "Toolbar Plus" is Unity's built-in "+" — the same one ReorderableList draws.
        private static GUIContent _addClassContentCache;
        private static GUIContent _addMemberContentCache;
        private static readonly GUIContent kRemoveContent = new GUIContent("✕", "Remove");

        private static GUIContent _AddClassContent => _addClassContentCache ??= _MakeAddIcon("Add a class");
        private static GUIContent _AddMemberContent => _addMemberContentCache ??= _MakeAddIcon("Add a member");

        // IconContent hands back a shared instance, so copy it before setting the tooltip.
        private static GUIContent _MakeAddIcon(string tooltip)
        {
            return new GUIContent(EditorGUIUtility.IconContent("Toolbar Plus")) { tooltip = tooltip };
        }

        private RemoteControlContainer _container;
        private LiveClassAsset _preset;

        // Two-pane body + footer geometry (persisted for the window's lifetime only).
        [SerializeField] private float _classPaneWidth = 240f;
        [SerializeField] private float _footerHeight = 220f;
        [SerializeField] private string _selectedTypeName;
        [SerializeField] private bool _bindingsFoldout = true;

        private Vector2 _classListScroll;
        private Vector2 _detailScroll;
        private Vector2 _footerScroll;

        // Preset mutations are deferred to the next Layout event so no draw pass ever runs on a
        // half-modified list (which is what GUIUtility.ExitGUI used to paper over).
        private Action _pendingAction;

        // Searchable class dropdown behind the class list's "+".
        private readonly UnityEditor.IMGUI.Controls.AdvancedDropdownState _classDropdownState = new UnityEditor.IMGUI.Controls.AdvancedDropdownState();

        private void OnEnable()
        {
            Undo.undoRedoPerformed += _OnUndoRedo;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= _OnUndoRedo;
        }

        // An undo restores the serialized state only; the container's runtime lookup table has to
        // be rebuilt from it, or the bindings keep resolving to the pre-undo objects.
        private void _OnUndoRedo()
        {
            if (_container != null) _container.Reload();
            Repaint();
        }

        private void OnGUI()
        {
            if (_pendingAction != null && Event.current.type == EventType.Layout)
            {
                var action = _pendingAction;
                _pendingAction = null;
                action();
            }

            _AcquireContainerAndPreset();

            _DrawHeader();
            _DrawBody();
            // No container means nothing to bind into, so the instance-bindings footer has
            // nothing to show; hide the whole pane rather than leave an empty box.
            if (_container != null) _DrawFooter();
        }

        // Queues a preset/container mutation for the next Layout event.
        private void _Defer(Action action)
        {
            _pendingAction = action;
            Repaint();
        }

        // --- Header: preset asset ---

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

        private void _DrawHeader()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("Preset", LiveClassAssetStyles.rowTitle, GUILayout.Width(kHeaderLabelWidth));
            _preset = (LiveClassAsset)EditorGUILayout.ObjectField(
                _preset, typeof(LiveClassAsset), allowSceneObjects: false, GUILayout.Height(EditorGUIUtility.singleLineHeight));
            if (GUILayout.Button("New", EditorStyles.toolbarButton, GUILayout.Width(40)))
            {
                var path = EditorUtility.SaveFilePanelInProject("Create Live Class Asset", "LiveClassAsset", "asset", "");
                if (!string.IsNullOrEmpty(path))
                {
                    var created = ScriptableObject.CreateInstance<LiveClassAsset>();
                    AssetDatabase.CreateAsset(created, path);
                    AssetDatabase.SaveAssets();
                    _preset = created;
                    _selectedTypeName = null;
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("Container", LiveClassAssetStyles.rowTitle, GUILayout.Width(kHeaderLabelWidth));
            _container = (RemoteControlContainer)EditorGUILayout.ObjectField(
                _container, typeof(RemoteControlContainer), allowSceneObjects: true, GUILayout.Height(EditorGUIUtility.singleLineHeight));
            if (_container == null && GUILayout.Button("Create", EditorStyles.toolbarButton, GUILayout.Width(48)))
            {
                var go = new GameObject("Remote Control Container");
                Undo.RegisterCreatedObjectUndo(go, "Create Remote Control Container");
                _container = Undo.AddComponent<RemoteControlContainer>(go);
            }
            EditorGUILayout.EndHorizontal();
        }

        // --- Body: class list | class detail ---

        private void _DrawBody()
        {
            EditorGUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));
            _DrawClassList();
            _classPaneWidth = _Splitter(_classPaneWidth, kMinPaneWidth,
                Mathf.Max(kMinPaneWidth, position.width - kMinPaneWidth), horizontal: true, invert: false);
            _DrawClassDetail();
            EditorGUILayout.EndHorizontal();
        }

        private void _DrawClassList()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(_classPaneWidth), GUILayout.ExpandHeight(true));

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("Classes", LiveClassAssetStyles.paneHeader);
            GUILayout.FlexibleSpace();
            using (new EditorGUI.DisabledScope(_preset == null))
            {
                // Class-first flow: pick a class through the searchable dropdown to add its
                // (initially empty) type definition, then fill it with the detail pane's "+".
                var addClassRect = GUILayoutUtility.GetRect(_AddClassContent, EditorStyles.toolbarButton, GUILayout.Width(kAddButtonWidth));
                if (GUI.Button(addClassRect, _AddClassContent, EditorStyles.toolbarButton))
                {
                    new LiveClassAssetTypeDropdown(_classDropdownState, new List<Type>(), _EnumerateCandidateTypes,
                        selected => _Defer(() => _AddClass(selected))).Show(addClassRect);
                }

                // Same as "+", but the candidates are the selected GameObject's actual
                // components instead of a global type search.
                var fromSelectedRect = GUILayoutUtility.GetRect(new GUIContent("From Selected"), EditorStyles.toolbarButton, GUILayout.Width(90));
                if (GUI.Button(fromSelectedRect, "From Selected", EditorStyles.toolbarButton))
                {
                    LiveClassAssetFromSelectedWindow.Open(() => _preset,
                        typeName => { _selectedTypeName = typeName; _ApplyChanges(); },
                        GUIUtility.GUIToScreenRect(fromSelectedRect));
                }
            }
            EditorGUILayout.EndHorizontal();

            _classListScroll = EditorGUILayout.BeginScrollView(_classListScroll, GUILayout.ExpandHeight(true));
            if (_preset == null)
            {
                EditorGUILayout.HelpBox("Assign or create a Live Class Asset above. It stores which members are exposed, shared across scenes.", MessageType.Info);
            }
            else if (_preset.typeDefinitions.Count == 0)
            {
                EditorGUILayout.HelpBox("Nothing exposed yet. Add a class with \"+\" or \"From Selected\", then expose its members with \"+\" in the detail pane.", MessageType.None);
            }
            else
            {
                for (int i = 0; i < _preset.typeDefinitions.Count; i++)
                {
                    _DrawClassRow(_preset.typeDefinitions[i]);
                }
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.EndVertical();
        }

        private void _DrawClassRow(LiveClassAsset.TypeDefinition definition)
        {
            if (definition == null) return;
            var type = definition.ResolveType();
            string title = type != null ? type.Name : $"(unresolved: {definition.typeName})";
            bool selected = string.Equals(_selectedTypeName, definition.typeName, StringComparison.Ordinal);

            var rowRect = GUILayoutUtility.GetRect(GUIContent.none, EditorStyles.label,
                GUILayout.ExpandWidth(true), GUILayout.Height(EditorGUIUtility.singleLineHeight + 2f));
            if (Event.current.type == EventType.Repaint && selected)
            {
                EditorGUI.DrawRect(rowRect, GUI.skin.settings.selectionColor);
            }

            int count = definition.members.Count;
            var countContent = new GUIContent(count == 1 ? "1 member" : $"{count} members");
            float countWidth = LiveClassAssetStyles.rowMeta.CalcSize(countContent).x;
            var removeRect = new Rect(rowRect.xMax - kRemoveButtonWidth - 2f, rowRect.y + 1f, kRemoveButtonWidth, rowRect.height - 2f);
            var countRect = new Rect(removeRect.x - countWidth - 6f, rowRect.y, countWidth, rowRect.height);
            var labelRect = new Rect(rowRect.x + 4f, rowRect.y, Mathf.Max(0f, countRect.x - rowRect.x - 8f), rowRect.height);
            GUI.Label(labelRect, new GUIContent(title, type?.FullName),
                type != null ? LiveClassAssetStyles.rowTitle : LiveClassAssetStyles.rowMeta);
            GUI.Label(countRect, countContent, LiveClassAssetStyles.rowMeta);

            // Drawn before the row's own click handling so hitting ✕ never also selects the row.
            if (GUI.Button(removeRect, kRemoveContent))
            {
                _Defer(() => _RemoveTypeDefinition(definition));
            }

            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && rowRect.Contains(Event.current.mousePosition))
            {
                _selectedTypeName = definition.typeName;
                GUI.FocusControl(null);
                Event.current.Use();
                Repaint();
            }
        }

        private void _DrawClassDetail()
        {
            EditorGUILayout.BeginVertical(GUILayout.ExpandHeight(true));

            int index = _FindSelectedDefinitionIndex();
            var definition = index >= 0 ? _preset.typeDefinitions[index] : null;
            var type = definition?.ResolveType();

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            _DrawDetailTitle(definition, type);
            GUILayout.FlexibleSpace();
            if (definition != null)
            {
                // Checkbox list of this class's members/methods, kept open for multi-select.
                var addMemberRect = GUILayoutUtility.GetRect(_AddMemberContent, EditorStyles.toolbarButton, GUILayout.Width(kAddButtonWidth));
                using (new EditorGUI.DisabledScope(type == null))
                {
                    if (GUI.Button(addMemberRect, _AddMemberContent, EditorStyles.toolbarButton))
                    {
                        LiveClassAssetAddMemberWindow.Open(() => _preset, () => _container, type, _ApplyChanges,
                            GUIUtility.GUIToScreenRect(addMemberRect));
                    }
                }
                // Create an unbound instance entry, then assign the object in Instance Bindings
                // in the footer (or bind another scene's object through its container). Needs a
                // container to bind into, so it's hidden without one.
                if (_container != null && GUILayout.Button("Add Binding", EditorStyles.toolbarButton, GUILayout.Width(78)))
                {
                    _Defer(() => _AddBinding(definition));
                }
            }
            EditorGUILayout.EndHorizontal();

            _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll, GUILayout.ExpandHeight(true));
            if (definition == null)
            {
                EditorGUILayout.HelpBox("Select a class on the left to edit its exposed members.", MessageType.None);
            }
            else
            {
                bool changed = _DrawClassMetadata(definition);

                if (definition.members.Count == 0)
                {
                    EditorGUILayout.HelpBox("No member exposed on this class yet. Use \"+\" above.", MessageType.None);
                }
                else
                {
                    var members = definition.members;
                    for (int i = 0; i < members.Count; i++)
                    {
                        changed |= _DrawMember(index, members, i);
                    }
                }
                if (changed) _ApplyChanges();
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.EndVertical();
        }

        // Type name in bold with the namespace trailing in dimmed text, so the part that
        // identifies the class stays readable when the pane is narrow.
        private static void _DrawDetailTitle(LiveClassAsset.TypeDefinition definition, Type type)
        {
            if (definition == null)
            {
                GUILayout.Label("Class Detail", LiveClassAssetStyles.paneHeader);
                return;
            }
            if (type == null)
            {
                GUILayout.Label(new GUIContent("(unresolved)", definition.typeName), LiveClassAssetStyles.paneHeaderDetail);
                return;
            }

            var nameContent = new GUIContent(type.Name, type.AssemblyQualifiedName);
            GUILayout.Label(nameContent, LiveClassAssetStyles.paneHeader,
                GUILayout.Width(LiveClassAssetStyles.paneHeader.CalcSize(nameContent).x));
            if (!string.IsNullOrEmpty(type.Namespace))
            {
                GUILayout.Label(type.Namespace, LiveClassAssetStyles.paneHeaderDetail);
            }
        }

        private int _FindSelectedDefinitionIndex()
        {
            if (_preset == null || string.IsNullOrEmpty(_selectedTypeName)) return -1;
            for (int i = 0; i < _preset.typeDefinitions.Count; i++)
            {
                var definition = _preset.typeDefinitions[i];
                if (definition != null && string.Equals(definition.typeName, _selectedTypeName, StringComparison.Ordinal))
                {
                    return i;
                }
            }
            return -1;
        }

        // --- Footer: instance bindings ---

        private void _DrawFooter()
        {
            _footerHeight = _Splitter(_footerHeight, kMinFooterHeight,
                Mathf.Max(kMinFooterHeight, position.height - 140f), horizontal: false, invert: true);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Height(_footerHeight));
            _footerScroll = EditorGUILayout.BeginScrollView(_footerScroll);

            _DrawInstanceBindingsSection();

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        // Only called with a container present (see OnGUI) — the footer pane itself is hidden without one.
        private void _DrawInstanceBindingsSection()
        {
            int count = _preset != null ? _preset.bindings.Count : 0;
            _bindingsFoldout = EditorGUILayout.Foldout(_bindingsFoldout, $"Instance Bindings ({count})", toggleOnLabelClick: true);
            if (!_bindingsFoldout) return;

            EditorGUI.indentLevel++;
            if (_preset == null)
            {
                EditorGUILayout.HelpBox("Assign a preset to bind instances.", MessageType.None);
            }
            else if (count == 0)
            {
                EditorGUILayout.HelpBox("No instance bound. Use \"Add Binding\" on a class.", MessageType.None);
            }
            else
            {
                for (int i = 0; i < _preset.bindings.Count; i++)
                {
                    _DrawInstanceBinding(_preset.bindings[i]);
                }
            }
            EditorGUI.indentLevel--;
        }

        // --- Preset mutations (all run from the deferred queue) ---

        // Ensures the edited preset is registered on the container (so its bindings resolve at runtime).
        private void _EnsurePresetOnContainer()
        {
            LiveClassAssetMemberExposure.EnsurePresetOnContainer(_preset, _container);
        }

        private void _ApplyChanges()
        {
            if (_preset != null) EditorUtility.SetDirty(_preset);
            if (_container != null)
            {
                EditorUtility.SetDirty(_container);
                _container.Reload();
            }
            Repaint();
        }

        // Opens one undo step over the asset + container; see LiveClassAssetMemberExposure.BeginEdit.
        private void _BeginEdit(string name)
        {
            LiveClassAssetMemberExposure.BeginEdit(_preset, _container, name);
        }

        private void _AddClass(Type type)
        {
            if (_preset == null) return;
            _BeginEdit("Add Class");
            _EnsurePresetOnContainer();
            var added = _preset.GetOrAddTypeDefinition(type);
            _selectedTypeName = added.typeName;
            _ApplyChanges();
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
            _ApplyChanges();
        }

        private void _RemoveTypeDefinition(LiveClassAsset.TypeDefinition definition)
        {
            if (_preset == null || definition == null) return;
            var type = definition.ResolveType();

            _BeginEdit("Remove Class");

            _preset.typeDefinitions.Remove(definition);
            LiveClassAssetMemberExposure.RemoveBindingsOfType(_preset, _container, type);
            if (string.Equals(_selectedTypeName, definition.typeName, StringComparison.Ordinal)) _selectedTypeName = null;
            _ApplyChanges();
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

        // --- Class-level metadata ---

        // Category and icon describe the type itself rather than any member, so they sit above the
        // member list. Both are optional: empty category falls back to "Binding" (the value assets
        // registered before these fields existed were given), empty icon to the type's default.
        private bool _DrawClassMetadata(LiveClassAsset.TypeDefinition definition)
        {
            EditorGUI.BeginChangeCheck();
            string category = EditorGUILayout.TextField(
                new GUIContent("Category", "Type category shown in RemoteApp. Empty falls back to \"Binding\""),
                definition.category);
            string icon = EditorGUILayout.TextField(
                new GUIContent("Icon", "Material Icons name. Empty uses the type's default icon"),
                definition.icon);
            if (!EditorGUI.EndChangeCheck()) return false;

            Undo.RecordObject(_preset, "Edit Class Metadata");
            definition.category = category;
            definition.icon = icon;
            return true;
        }

        // --- Member detail rows ---

        private bool _DrawMember(int definitionIndex, List<LiveClassAssetMember> members, int index)
        {
            var member = members[index];
            bool changed = false;

            EditorGUILayout.BeginHorizontal();
            // The label is derived from the member name at expose time and needs no editing;
            // show it as the row title with the wire name alongside. GUILayout.Label (not
            // LabelField) so the title starts at the pane edge instead of the field column.
            string rowTitle = string.IsNullOrEmpty(member.label) ? member.path : $"{member.label}  ({member.path})";
            if (member.isFunction) rowTitle += " ()";
            GUILayout.Label(new GUIContent(rowTitle, member.path), LiveClassAssetStyles.memberTitle);

            using (new EditorGUI.DisabledScope(index == 0))
            {
                if (GUILayout.Button("▲", GUILayout.Width(kRemoveButtonWidth)))
                {
                    _BeginEdit("Reorder Member");
                    (members[index - 1], members[index]) = (members[index], members[index - 1]);
                    changed = true;
                }
            }
            using (new EditorGUI.DisabledScope(index == members.Count - 1))
            {
                if (GUILayout.Button("▼", GUILayout.Width(kRemoveButtonWidth)))
                {
                    _BeginEdit("Reorder Member");
                    (members[index + 1], members[index]) = (members[index], members[index + 1]);
                    changed = true;
                }
            }
            if (GUILayout.Button(kRemoveContent, GUILayout.Width(kRemoveButtonWidth)))
            {
                _Defer(() =>
                {
                    _BeginEdit("Unexpose Member");
                    members.Remove(member);
                    _ApplyChanges();
                });
            }
            EditorGUILayout.EndHorizontal();

            EditorGUI.indentLevel++;
            EditorGUI.BeginChangeCheck();
            string help = EditorGUILayout.TextField("Help", member.help);

            bool persistable = member.persistable;
            bool readOnly = member.readOnly;
            string icon = member.icon;
            if (member.isFunction)
            {
                icon = EditorGUILayout.TextField(
                    new GUIContent("Icon", "Material Icons name shown on the button"), member.icon);
            }
            else
            {
                persistable = EditorGUILayout.Toggle("Persistable", member.persistable);
                readOnly = EditorGUILayout.Toggle(
                    new GUIContent("Read Only", "Forbid writes through the API and show as display-only"),
                    member.readOnly);
            }

            // The section starts at this member and runs until the next one that declares a title,
            // so an empty title is the normal state — do not treat it as unset metadata.
            string sectionTitle = EditorGUILayout.TextField(
                new GUIContent("Section Title", "Leave empty to keep the member in the current section"),
                member.section?.title);
            string sectionSubtitle = member.section?.subtitle;
            string sectionIcon = member.section?.icon;
            if (!string.IsNullOrEmpty(sectionTitle))
            {
                EditorGUI.indentLevel++;
                sectionSubtitle = EditorGUILayout.TextField("Subtitle", sectionSubtitle);
                sectionIcon = EditorGUILayout.TextField("Icon", sectionIcon);
                EditorGUI.indentLevel--;
            }

            if (EditorGUI.EndChangeCheck())
            {
                // A scalar field edit, so the incremental diff is enough here — the full snapshot
                // of _BeginEdit is only needed where the list shape changes.
                Undo.RecordObject(_preset, "Edit Member Metadata");
                member.help = help;
                member.persistable = persistable;
                member.readOnly = readOnly;
                member.icon = icon;
                member.section ??= new LiveClassAssetSection();
                member.section.title = sectionTitle;
                member.section.subtitle = sectionSubtitle;
                member.section.icon = sectionIcon;
                changed = true;
            }

            if (!member.isFunction)
            {
                changed |= _DrawControlField(definitionIndex, index);
            }
            EditorGUI.indentLevel--;

            return changed;
        }

        // Draws the polymorphic controller through the SerializedProperty path so the
        // [SerializeReference, Select] drawer provides the type dropdown and per-control fields.
        private bool _DrawControlField(int definitionIndex, int memberIndex)
        {
            var serialized = new SerializedObject(_preset);
            var property = serialized.FindProperty(
                $"typeDefinitions.Array.data[{definitionIndex}].members.Array.data[{memberIndex}].control");
            if (property == null) return false;

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(property, new GUIContent("Control"), includeChildren: true);
            if (EditorGUI.EndChangeCheck())
            {
                serialized.ApplyModifiedProperties();
                return true;
            }
            return false;
        }

        private void _DrawInstanceBinding(LiveClassAsset.InstanceBinding entry)
        {
            if (entry == null) return;
            var expectedType = entry.ResolveType() ?? typeof(UnityEngine.Object);
            var current = _container.ResolveKey(entry.key);

            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            var next = EditorGUILayout.ObjectField(expectedType.Name, current, expectedType, allowSceneObjects: true);
            if (EditorGUI.EndChangeCheck())
            {
                _BeginEdit("Rebind Instance");
                _container.SetReferenceValue(new PropertyName(entry.key), next);
                if (next != null) entry.typeName = next.GetType().AssemblyQualifiedName;
                _ApplyChanges();
            }
            if (current == null)
            {
                GUILayout.Label("(unbound)", LiveClassAssetStyles.rowMeta, GUILayout.Width(64));
            }
            if (GUILayout.Button(kRemoveContent, GUILayout.Width(kRemoveButtonWidth)))
            {
                _Defer(() =>
                {
                    _BeginEdit("Remove Binding");
                    _container.ClearReferenceValue(new PropertyName(entry.key));
                    _preset.bindings.Remove(entry);
                    _ApplyChanges();
                });
            }
            EditorGUILayout.EndHorizontal();
        }

        // --- Draggable pane splitters ---

        /// <summary>
        /// Reserves a splitter bar and returns the dragged size. <paramref name="invert"/> is for
        /// the footer, which grows when dragged up (its bar sits above the resized area).
        /// </summary>
        private float _Splitter(float value, float min, float max, bool horizontal, bool invert)
        {
            var rect = horizontal
                ? GUILayoutUtility.GetRect(kSplitterThickness, kSplitterThickness, GUILayout.Width(kSplitterThickness), GUILayout.ExpandHeight(true))
                : GUILayoutUtility.GetRect(kSplitterThickness, kSplitterThickness, GUILayout.Height(kSplitterThickness), GUILayout.ExpandWidth(true));

            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(rect, EditorGUIUtility.isProSkin
                    ? new Color(0.15f, 0.15f, 0.15f)
                    : new Color(0.55f, 0.55f, 0.55f));
            }
            EditorGUIUtility.AddCursorRect(rect, horizontal ? MouseCursor.ResizeHorizontal : MouseCursor.ResizeVertical);

            int id = GUIUtility.GetControlID(FocusType.Passive);
            var e = Event.current;
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (e.button == 0 && rect.Contains(e.mousePosition))
                    {
                        GUIUtility.hotControl = id;
                        e.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == id)
                    {
                        float delta = horizontal ? e.delta.x : e.delta.y;
                        value = Mathf.Clamp(value + (invert ? -delta : delta), min, max);
                        e.Use();
                        Repaint();
                    }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id)
                    {
                        GUIUtility.hotControl = 0;
                        e.Use();
                    }
                    break;
            }
            return value;
        }
    }
}
