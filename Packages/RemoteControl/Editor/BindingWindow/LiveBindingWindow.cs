// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

using Lilium.RemoteControl.LiveScene;

namespace Lilium.RemoteControl.Editor
{
    /// <summary>
    /// UE Remote Control-style panel: build the exposure class-first. "Add Class" (searchable
    /// dropdown over every type) or "From Selected" (<see cref="LiveBindingFromSelectedWindow"/>,
    /// scoped to the selected GameObject's own components) adds an empty type definition to the
    /// class list on the left; "Add Member" (<see cref="LiveBindingAddMemberWindow"/>, a checkbox
    /// list kept open for multi-select) fills it with members and methods — then edit the
    /// metadata (label, control, persistence) of the exposed members in the detail pane on the
    /// right.
    ///
    /// Layout: the header holds the preset asset, the body is a two-pane class list / class
    /// detail split, and the footer collects everything instance-related (the resolver and the
    /// instance bindings).
    ///
    /// Exposure settings are stored in a <see cref="LiveBindingPreset"/> asset (shared across
    /// scenes); the scene-object references live in a <see cref="LiveBindingResolver"/> in the
    /// scene, using the standard IExposedPropertyTable mechanism.
    /// </summary>
    public class LiveBindingWindow : EditorWindow
    {
        [MenuItem("Window/Lilium Remote Control/Live Binding")]
        public static void Open()
        {
            GetWindow<LiveBindingWindow>("Live Binding");
        }

        private const float kSplitterThickness = 4f;
        private const float kMinPaneWidth = 160f;
        private const float kMinFooterHeight = 60f;

        private LiveBindingResolver _resolver;
        private LiveBindingPreset _preset;

        // Two-pane body + footer geometry (persisted for the window's lifetime only).
        [SerializeField] private float _classPaneWidth = 240f;
        [SerializeField] private float _footerHeight = 220f;
        [SerializeField] private string _selectedTypeName;
        [SerializeField] private bool _resolverFoldout = true;
        [SerializeField] private bool _bindingsFoldout = true;

        private Vector2 _classListScroll;
        private Vector2 _detailScroll;
        private Vector2 _footerScroll;

        // Preset mutations are deferred to the next Layout event so no draw pass ever runs on a
        // half-modified list (which is what GUIUtility.ExitGUI used to paper over).
        private Action _pendingAction;

        // "Add Class" searchable dropdown.
        private readonly UnityEditor.IMGUI.Controls.AdvancedDropdownState _classDropdownState = new UnityEditor.IMGUI.Controls.AdvancedDropdownState();

        private void OnGUI()
        {
            if (_pendingAction != null && Event.current.type == EventType.Layout)
            {
                var action = _pendingAction;
                _pendingAction = null;
                action();
            }

            _AcquireResolverAndPreset();

            _DrawHeader();
            _DrawBody();
            _DrawFooter();
        }

        // Queues a preset/resolver mutation for the next Layout event.
        private void _Defer(Action action)
        {
            _pendingAction = action;
            Repaint();
        }

        // --- Header: preset asset ---

        private void _AcquireResolverAndPreset()
        {
            if (_resolver == null)
            {
                var resolvers = FindObjectsByType<LiveBindingResolver>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID);
                if (resolvers.Length > 0) _resolver = resolvers[0];
            }
            if (_preset == null && _resolver != null && _resolver.presets.Count > 0)
            {
                _preset = _resolver.presets[0];
            }
        }

        private void _DrawHeader()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("Preset", EditorStyles.miniLabel, GUILayout.Width(40));
            _preset = (LiveBindingPreset)EditorGUILayout.ObjectField(
                _preset, typeof(LiveBindingPreset), allowSceneObjects: false, GUILayout.Height(EditorGUIUtility.singleLineHeight));
            if (GUILayout.Button("New", EditorStyles.toolbarButton, GUILayout.Width(40)))
            {
                var path = EditorUtility.SaveFilePanelInProject("Create Live Binding Preset", "LiveBindingPreset", "asset", "");
                if (!string.IsNullOrEmpty(path))
                {
                    var created = ScriptableObject.CreateInstance<LiveBindingPreset>();
                    AssetDatabase.CreateAsset(created, path);
                    AssetDatabase.SaveAssets();
                    _preset = created;
                    _selectedTypeName = null;
                }
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
            GUILayout.Label("Classes", EditorStyles.miniBoldLabel);
            GUILayout.FlexibleSpace();
            using (new EditorGUI.DisabledScope(_preset == null))
            {
                // Class-first flow: pick a class through the searchable dropdown to add its
                // (initially empty) type definition, then fill it with "Add Member".
                var addClassRect = GUILayoutUtility.GetRect(new GUIContent("Add Class"), EditorStyles.toolbarButton, GUILayout.Width(72));
                if (GUI.Button(addClassRect, "Add Class", EditorStyles.toolbarButton))
                {
                    new LiveBindingTypeDropdown(_classDropdownState, new List<Type>(), _EnumerateCandidateTypes,
                        selected => _Defer(() => _AddClass(selected))).Show(addClassRect);
                }

                // Same as "Add Class", but the candidates are the selected GameObject's actual
                // components instead of a global type search.
                var fromSelectedRect = GUILayoutUtility.GetRect(new GUIContent("From Selected"), EditorStyles.toolbarButton, GUILayout.Width(90));
                if (GUI.Button(fromSelectedRect, "From Selected", EditorStyles.toolbarButton))
                {
                    LiveBindingFromSelectedWindow.Open(() => _preset,
                        typeName => { _selectedTypeName = typeName; _ApplyChanges(); },
                        GUIUtility.GUIToScreenRect(fromSelectedRect));
                }
            }
            EditorGUILayout.EndHorizontal();

            _classListScroll = EditorGUILayout.BeginScrollView(_classListScroll, GUILayout.ExpandHeight(true));
            if (_preset == null)
            {
                EditorGUILayout.HelpBox("Assign or create a LiveBindingPreset asset above. It stores which members are exposed, shared across scenes.", MessageType.Info);
            }
            else if (_preset.typeDefinitions.Count == 0)
            {
                EditorGUILayout.HelpBox("Nothing exposed yet. Add a class with \"Add Class\" or \"From Selected\", then expose its members with \"Add Member\".", MessageType.None);
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

        private void _DrawClassRow(LiveBindingPreset.TypeDefinition definition)
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

            var countRect = new Rect(rowRect.xMax - 66f, rowRect.y, 62f, rowRect.height);
            var labelRect = new Rect(rowRect.x + 4f, rowRect.y, Mathf.Max(0f, countRect.x - rowRect.x - 6f), rowRect.height);
            GUI.Label(labelRect, title, type != null ? EditorStyles.label : EditorStyles.centeredGreyMiniLabel);
            GUI.Label(countRect, $"{definition.members.Count} members", EditorStyles.miniLabel);

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
            GUILayout.Label(definition == null
                ? "Class Detail"
                : (type != null ? type.FullName : $"(unresolved: {definition.typeName})"), EditorStyles.miniBoldLabel);
            GUILayout.FlexibleSpace();
            if (definition != null)
            {
                // Checkbox list of this class's members/methods, kept open for multi-select.
                var addMemberRect = GUILayoutUtility.GetRect(new GUIContent("Add Member"), EditorStyles.toolbarButton, GUILayout.Width(82));
                using (new EditorGUI.DisabledScope(type == null))
                {
                    if (GUI.Button(addMemberRect, "Add Member", EditorStyles.toolbarButton))
                    {
                        LiveBindingAddMemberWindow.Open(() => _preset, () => _resolver, type, _ApplyChanges,
                            GUIUtility.GUIToScreenRect(addMemberRect));
                    }
                }
                // Create an unbound instance entry, then assign the object in Instance Bindings
                // in the footer (or bind another scene's object through its resolver).
                if (GUILayout.Button("Add Binding", EditorStyles.toolbarButton, GUILayout.Width(78)))
                {
                    _Defer(() => _AddBinding(definition));
                }
                if (GUILayout.Button("Remove Class", EditorStyles.toolbarButton, GUILayout.Width(88)))
                {
                    _Defer(() => _RemoveTypeDefinition(definition));
                }
            }
            EditorGUILayout.EndHorizontal();

            _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll, GUILayout.ExpandHeight(true));
            if (definition == null)
            {
                EditorGUILayout.HelpBox("Select a class on the left to edit its exposed members.", MessageType.None);
            }
            else if (definition.members.Count == 0)
            {
                EditorGUILayout.HelpBox("No member exposed on this class yet. Use \"Add Member\".", MessageType.None);
            }
            else
            {
                bool changed = false;
                var members = definition.members;
                for (int i = 0; i < members.Count; i++)
                {
                    changed |= _DrawMember(index, members, i);
                }
                if (changed) _ApplyChanges();
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.EndVertical();
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

        // --- Footer: resolver, instance bindings ---

        private void _DrawFooter()
        {
            _footerHeight = _Splitter(_footerHeight, kMinFooterHeight,
                Mathf.Max(kMinFooterHeight, position.height - 140f), horizontal: false, invert: true);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Height(_footerHeight));
            _footerScroll = EditorGUILayout.BeginScrollView(_footerScroll);

            _DrawResolverSection();
            EditorGUILayout.Space(2f);
            _DrawInstanceBindingsSection();

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void _DrawResolverSection()
        {
            _resolverFoldout = EditorGUILayout.Foldout(_resolverFoldout, "Resolver", toggleOnLabelClick: true);
            if (!_resolverFoldout) return;

            EditorGUI.indentLevel++;
            _resolver = (LiveBindingResolver)EditorGUILayout.ObjectField("Resolver", _resolver, typeof(LiveBindingResolver), allowSceneObjects: true);
            if (_resolver == null)
            {
                EditorGUILayout.HelpBox("No LiveBindingResolver in the open scenes. It holds the key → scene object reference table.", MessageType.Info);
                if (GUILayout.Button("Create Resolver In Scene"))
                {
                    var go = new GameObject("Live Binding Resolver");
                    Undo.RegisterCreatedObjectUndo(go, "Create Live Binding Resolver");
                    _resolver = Undo.AddComponent<LiveBindingResolver>(go);
                }
            }
            EditorGUI.indentLevel--;
        }

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
            else if (_resolver == null)
            {
                EditorGUILayout.HelpBox("A resolver is required to bind scene objects to the preset's keys.", MessageType.None);
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

        // Ensures the edited preset is registered on the resolver (so its bindings resolve at runtime).
        private void _EnsurePresetOnResolver()
        {
            LiveBindingMemberExposure.EnsurePresetOnResolver(_preset, _resolver);
        }

        private void _ApplyChanges()
        {
            if (_preset != null) EditorUtility.SetDirty(_preset);
            if (_resolver != null)
            {
                EditorUtility.SetDirty(_resolver);
                _resolver.Reload();
            }
            Repaint();
        }

        private void _AddClass(Type type)
        {
            if (_preset == null) return;
            _EnsurePresetOnResolver();
            Undo.RecordObject(_preset, "Add Class");
            var added = _preset.GetOrAddTypeDefinition(type);
            _selectedTypeName = added.typeName;
            _ApplyChanges();
        }

        private void _AddBinding(LiveBindingPreset.TypeDefinition definition)
        {
            if (_preset == null || definition == null) return;
            _EnsurePresetOnResolver();
            Undo.RecordObject(_preset, "Add Binding");
            _preset.bindings.Add(new LiveBindingPreset.InstanceBinding
            {
                key = Guid.NewGuid().ToString(),
                typeName = definition.typeName,
            });
            _bindingsFoldout = true;
            _ApplyChanges();
        }

        private void _RemoveTypeDefinition(LiveBindingPreset.TypeDefinition definition)
        {
            if (_preset == null || definition == null) return;
            var type = definition.ResolveType();

            Undo.RecordObject(_preset, "Remove Type Definition");
            if (_resolver != null) Undo.RecordObject(_resolver, "Remove Type Definition");

            _preset.typeDefinitions.Remove(definition);
            LiveBindingMemberExposure.RemoveBindingsOfType(_preset, _resolver, type);
            if (string.Equals(_selectedTypeName, definition.typeName, StringComparison.Ordinal)) _selectedTypeName = null;
            _ApplyChanges();
        }

        // --- Class-first flow: candidate types for the "Add Class" dropdown ---

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
            if (LiveBindingMemberExposure.IsObsolete(type)) return false;
            // Editor-only types are never resolvable in a player; keep them out of the picker.
            var ns = type.Namespace;
            if (ns != null && ns.StartsWith("UnityEditor", StringComparison.Ordinal)) return false;
            var assemblyName = type.Assembly.GetName().Name;
            if (assemblyName.IndexOf("Editor", StringComparison.Ordinal) >= 0) return false;
            return true;
        }

        // --- Member detail rows ---

        private bool _DrawMember(int definitionIndex, List<LiveBindingMember> members, int index)
        {
            var member = members[index];
            bool changed = false;

            EditorGUILayout.BeginHorizontal();
            // The label is derived from the member name at expose time and needs no editing;
            // show it as the row title with the wire name alongside.
            string rowTitle = string.IsNullOrEmpty(member.label) ? member.path : $"{member.label}  ({member.path})";
            EditorGUILayout.LabelField(member.isFunction ? $"{rowTitle} ()" : rowTitle, EditorStyles.miniBoldLabel);

            using (new EditorGUI.DisabledScope(index == 0))
            {
                if (GUILayout.Button("▲", GUILayout.Width(24)))
                {
                    Undo.RecordObject(_preset, "Reorder Member");
                    (members[index - 1], members[index]) = (members[index], members[index - 1]);
                    changed = true;
                }
            }
            using (new EditorGUI.DisabledScope(index == members.Count - 1))
            {
                if (GUILayout.Button("▼", GUILayout.Width(24)))
                {
                    Undo.RecordObject(_preset, "Reorder Member");
                    (members[index + 1], members[index]) = (members[index], members[index + 1]);
                    changed = true;
                }
            }
            if (GUILayout.Button("✕", GUILayout.Width(24)))
            {
                _Defer(() =>
                {
                    Undo.RecordObject(_preset, "Unexpose Member");
                    members.Remove(member);
                    _ApplyChanges();
                });
            }
            EditorGUILayout.EndHorizontal();

            EditorGUI.indentLevel++;
            EditorGUI.BeginChangeCheck();
            string help = EditorGUILayout.TextField("Help", member.help);
            bool persistable = member.persistable;
            if (!member.isFunction)
            {
                persistable = EditorGUILayout.Toggle("Persistable", member.persistable);
            }
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_preset, "Edit Member Metadata");
                member.help = help;
                member.persistable = persistable;
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

        private void _DrawInstanceBinding(LiveBindingPreset.InstanceBinding entry)
        {
            if (entry == null) return;
            var expectedType = entry.ResolveType() ?? typeof(UnityEngine.Object);
            var current = _resolver.ResolveKey(entry.key);

            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            var next = EditorGUILayout.ObjectField(expectedType.Name, current, expectedType, allowSceneObjects: true);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_resolver, "Rebind Instance");
                Undo.RecordObject(_preset, "Rebind Instance");
                _resolver.SetReferenceValue(new PropertyName(entry.key), next);
                if (next != null) entry.typeName = next.GetType().AssemblyQualifiedName;
                _ApplyChanges();
            }
            if (current == null)
            {
                GUILayout.Label("(unbound)", EditorStyles.miniLabel, GUILayout.Width(60));
            }
            if (GUILayout.Button("✕", GUILayout.Width(24)))
            {
                _Defer(() =>
                {
                    Undo.RecordObject(_preset, "Remove Binding");
                    Undo.RecordObject(_resolver, "Remove Binding");
                    _resolver.ClearReferenceValue(new PropertyName(entry.key));
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
