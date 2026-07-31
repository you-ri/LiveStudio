// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

using Lilium.RemoteControl.LiveScene;

namespace Lilium.RemoteControl.Editor
{
    /// <summary>One exposable member (property/field/method) of a candidate type.</summary>
    internal struct MemberCandidate
    {
        public string path;
        public bool isFunction;
        public Type valueType; // null for functions
        public string typeLabel;
    }

    /// <summary>
    /// UE Remote Control-style panel: pick any component in the scene and toggle which of its
    /// properties/fields/methods are exposed to RemoteApp, or build the exposure class-first —
    /// "Add Class" adds a type definition to the Exposed list and "Add Member" on each
    /// definition picks members through a searchable dropdown — then edit the metadata (label,
    /// control, persistence) of the exposed members.
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

        // --- Supported member types (mirrors the wire serializer's Unity-primitive set) ---

        private static readonly HashSet<Type> kSupportedValueTypes = new HashSet<Type>
        {
            typeof(bool), typeof(int), typeof(uint), typeof(long), typeof(ulong),
            typeof(short), typeof(ushort), typeof(byte), typeof(sbyte),
            typeof(float), typeof(double), typeof(decimal), typeof(string),
            typeof(Vector2), typeof(Vector3), typeof(Vector4), typeof(Quaternion),
            typeof(Color), typeof(Color32), typeof(Rect), typeof(Bounds),
            typeof(Vector2Int), typeof(Vector3Int), typeof(RectInt),
        };

        private LiveBindingResolver _resolver;
        private LiveBindingPreset _preset;
        private string _memberFilter = "";
        private Vector2 _selectionScroll;
        private Vector2 _exposedScroll;
        private readonly Dictionary<int, bool> _componentFoldouts = new Dictionary<int, bool>();
        private readonly Dictionary<string, bool> _typeFoldouts = new Dictionary<string, bool>();

        // Class-first flow: "Add Class" / "Add Member" searchable dropdowns in the Exposed section.
        private readonly UnityEditor.IMGUI.Controls.AdvancedDropdownState _classDropdownState = new UnityEditor.IMGUI.Controls.AdvancedDropdownState();
        private readonly UnityEditor.IMGUI.Controls.AdvancedDropdownState _memberDropdownState = new UnityEditor.IMGUI.Controls.AdvancedDropdownState();

        private void OnEnable()
        {
            Selection.selectionChanged += Repaint;
            EditorApplication.hierarchyChanged += Repaint;
        }

        private void OnDisable()
        {
            Selection.selectionChanged -= Repaint;
            EditorApplication.hierarchyChanged -= Repaint;
        }

        private void OnGUI()
        {
            _DrawSetupSection();
            if (_resolver == null || _preset == null) return;

            EditorGUILayout.Space();
            _DrawSelectionSection();

            EditorGUILayout.Space();
            _DrawExposedSection();
        }

        // --- Setup: resolver in scene + preset asset ---

        private void _DrawSetupSection()
        {
            if (_resolver == null)
            {
                var resolvers = FindObjectsByType<LiveBindingResolver>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID);
                if (resolvers.Length > 0) _resolver = resolvers[0];
            }

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
                return;
            }

            if (_preset == null && _resolver.presets.Count > 0)
            {
                _preset = _resolver.presets[0];
            }

            EditorGUILayout.BeginHorizontal();
            _preset = (LiveBindingPreset)EditorGUILayout.ObjectField("Preset", _preset, typeof(LiveBindingPreset), allowSceneObjects: false);
            if (GUILayout.Button("New", GUILayout.Width(44)))
            {
                var path = EditorUtility.SaveFilePanelInProject("Create Live Binding Preset", "LiveBindingPreset", "asset", "");
                if (!string.IsNullOrEmpty(path))
                {
                    var created = ScriptableObject.CreateInstance<LiveBindingPreset>();
                    AssetDatabase.CreateAsset(created, path);
                    AssetDatabase.SaveAssets();
                    _preset = created;
                }
            }
            EditorGUILayout.EndHorizontal();

            if (_preset == null)
            {
                EditorGUILayout.HelpBox("Assign or create a LiveBindingPreset asset. It stores which members are exposed, shared across scenes.", MessageType.Info);
            }
        }

        // Ensures the edited preset is registered on the resolver (so its bindings resolve at runtime).
        private void _EnsurePresetOnResolver()
        {
            if (_resolver.presets.Contains(_preset)) return;
            Undo.RecordObject(_resolver, "Add Preset To Resolver");
            _resolver.presets.Add(_preset);
            EditorUtility.SetDirty(_resolver);
        }

        private void _ApplyChanges()
        {
            EditorUtility.SetDirty(_preset);
            EditorUtility.SetDirty(_resolver);
            _resolver.Reload();
            Repaint();
        }

        // --- Selected object: member toggles ---

        private void _DrawSelectionSection()
        {
            EditorGUILayout.LabelField("Selected Object", EditorStyles.boldLabel);

            var go = Selection.activeGameObject;
            if (go == null)
            {
                EditorGUILayout.HelpBox("Select a GameObject in the scene to expose its component members.", MessageType.None);
                return;
            }

            _memberFilter = EditorGUILayout.TextField("Filter", _memberFilter);

            _selectionScroll = EditorGUILayout.BeginScrollView(_selectionScroll, GUILayout.MinHeight(120));
            foreach (var component in go.GetComponents<Component>())
            {
                if (component == null) continue; // missing script
                _DrawComponentMembers(component);
            }
            EditorGUILayout.EndScrollView();
        }

        private void _DrawComponentMembers(Component component)
        {
            int key = component.GetInstanceID();
            _componentFoldouts.TryGetValue(key, out var open);
            var type = component.GetType();

            var definition = _preset.FindTypeDefinition(type);
            var boundEntry = _FindBindingEntry(component);
            int exposedCount = definition != null ? definition.members.Count : 0;
            string title = type.Name;
            if (exposedCount > 0) title += $"  ({exposedCount} exposed{(boundEntry == null ? ", instance unbound" : "")})";

            open = EditorGUILayout.Foldout(open, title, toggleOnLabelClick: true);
            _componentFoldouts[key] = open;
            if (!open) return;

            EditorGUI.indentLevel++;
            foreach (var candidate in _EnumerateCandidates(type))
            {
                if (!string.IsNullOrEmpty(_memberFilter)
                    && candidate.path.IndexOf(_memberFilter, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                bool exposed = boundEntry != null && definition != null
                    && _FindMember(definition, candidate.path, candidate.isFunction) != null;

                EditorGUILayout.BeginHorizontal();
                bool next = EditorGUILayout.ToggleLeft(candidate.path, exposed);
                GUILayout.Label(candidate.typeLabel, EditorStyles.miniLabel, GUILayout.MinWidth(60));
                EditorGUILayout.EndHorizontal();

                if (next == exposed) continue;
                if (next) _Expose(component, candidate);
                else _Unexpose(component, candidate);
                GUIUtility.ExitGUI();
            }
            EditorGUI.indentLevel--;
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
            if (_IsObsolete(type)) return false;
            // Editor-only types are never resolvable in a player; keep them out of the picker.
            var ns = type.Namespace;
            if (ns != null && ns.StartsWith("UnityEditor", StringComparison.Ordinal)) return false;
            var assemblyName = type.Assembly.GetName().Name;
            if (assemblyName.IndexOf("Editor", StringComparison.Ordinal) >= 0) return false;
            return true;
        }

        private void _ExposeTypeMember(Type type, in MemberCandidate candidate)
        {
            _EnsurePresetOnResolver();
            Undo.RecordObject(_preset, "Expose Member");

            var definition = _preset.GetOrAddTypeDefinition(type);
            if (_FindMember(definition, candidate.path, candidate.isFunction) == null)
            {
                definition.members.Add(new LiveBindingMember
                {
                    path = candidate.path,
                    isFunction = candidate.isFunction,
                    // Default the label to the display form of the member name
                    // ("_myValue" / "m_MyValue" → "My Value"), same as the Inspector does.
                    label = ObjectNames.NicifyVariableName(candidate.path),
                    persistable = !candidate.isFunction,
                });
            }

            _ApplyChanges();
        }

        private void _UnexposeTypeMember(Type type, in MemberCandidate candidate)
        {
            var definition = _preset.FindTypeDefinition(type);
            if (definition == null) return;
            var member = _FindMember(definition, candidate.path, candidate.isFunction);
            if (member == null) return;

            Undo.RecordObject(_preset, "Unexpose Member");
            Undo.RecordObject(_resolver, "Unexpose Member");

            definition.members.Remove(member);
            if (definition.members.Count == 0)
            {
                // Last member of the type gone: drop the definition and every binding of that type.
                _preset.typeDefinitions.Remove(definition);
                for (int i = _preset.bindings.Count - 1; i >= 0; i--)
                {
                    var entry = _preset.bindings[i];
                    if (entry != null && entry.ResolveType() == type)
                    {
                        _resolver.ClearReferenceValue(new PropertyName(entry.key));
                        _preset.bindings.RemoveAt(i);
                    }
                }
            }

            _ApplyChanges();
        }

        private IEnumerable<MemberCandidate> _EnumerateCandidates(Type type)
        {
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!prop.CanRead || prop.GetIndexParameters().Length > 0) continue;
                if (!_IsSupportedValueType(prop.PropertyType)) continue;
                if (_IsObsolete(prop)) continue;
                yield return new MemberCandidate
                {
                    path = prop.Name,
                    isFunction = false,
                    valueType = prop.PropertyType,
                    typeLabel = prop.CanWrite ? prop.PropertyType.Name : $"{prop.PropertyType.Name} (read only)",
                };
            }

            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!_IsSupportedValueType(field.FieldType)) continue;
                if (_IsObsolete(field)) continue;
                yield return new MemberCandidate
                {
                    path = field.Name,
                    isFunction = false,
                    valueType = field.FieldType,
                    typeLabel = field.FieldType.Name,
                };
            }

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (method.IsSpecialName || method.IsGenericMethod) continue;
                if (method.GetParameters().Length > 0) continue;
                // Skip the universal noise every component inherits (ToString, GetInstanceID, ...).
                if (method.DeclaringType == typeof(object) || method.DeclaringType == typeof(UnityEngine.Object)) continue;
                if (_IsObsolete(method)) continue;
                yield return new MemberCandidate
                {
                    path = method.Name,
                    isFunction = true,
                    valueType = null,
                    typeLabel = "method",
                };
            }
        }

        private static bool _IsSupportedValueType(Type type)
        {
            return type.IsEnum || kSupportedValueTypes.Contains(type);
        }

        private static bool _IsObsolete(MemberInfo member)
        {
            return member.GetCustomAttribute<ObsoleteAttribute>() != null;
        }

        // --- Expose / unexpose ---

        private LiveBindingPreset.InstanceBinding _FindBindingEntry(UnityEngine.Object target)
        {
            foreach (var entry in _preset.bindings)
            {
                if (entry != null && _resolver.ResolveKey(entry.key) == target) return entry;
            }
            return null;
        }

        private void _Expose(Component component, in MemberCandidate candidate)
        {
            _EnsurePresetOnResolver();
            Undo.RecordObject(_preset, "Expose Member");
            Undo.RecordObject(_resolver, "Expose Member");

            var type = component.GetType();
            var definition = _preset.GetOrAddTypeDefinition(type);
            if (_FindMember(definition, candidate.path, candidate.isFunction) == null)
            {
                definition.members.Add(new LiveBindingMember
                {
                    path = candidate.path,
                    isFunction = candidate.isFunction,
                    // Default the label to the display form of the member name
                    // ("_myValue" / "m_MyValue" → "My Value"), same as the Inspector does.
                    label = ObjectNames.NicifyVariableName(candidate.path),
                    persistable = !candidate.isFunction,
                });
            }

            if (_FindBindingEntry(component) == null)
            {
                var entry = new LiveBindingPreset.InstanceBinding
                {
                    key = Guid.NewGuid().ToString(),
                    typeName = type.AssemblyQualifiedName,
                };
                _preset.bindings.Add(entry);
                _resolver.SetReferenceValue(new PropertyName(entry.key), component);
            }

            _ApplyChanges();
        }

        private void _Unexpose(Component component, in MemberCandidate candidate)
        {
            // Member removal is type-level; the class-first flow shares the same behavior.
            _UnexposeTypeMember(component.GetType(), candidate);
        }

        private static LiveBindingMember _FindMember(LiveBindingPreset.TypeDefinition definition, string path, bool isFunction)
        {
            foreach (var m in definition.members)
            {
                if (m != null && m.isFunction == isFunction && string.Equals(m.path, path, StringComparison.Ordinal))
                {
                    return m;
                }
            }
            return null;
        }

        // --- Exposed: type definitions + instance bindings ---

        private void _DrawExposedSection()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Exposed", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            // Class-first flow: pick a class through the searchable dropdown to add its
            // (initially empty) type definition, then fill it with "Add Member".
            var addClassRect = GUILayoutUtility.GetRect(new GUIContent("Add Class"), GUI.skin.button, GUILayout.Width(84));
            if (GUI.Button(addClassRect, "Add Class"))
            {
                new LiveBindingTypeDropdown(_classDropdownState, new List<Type>(), _EnumerateCandidateTypes,
                    selected =>
                    {
                        _EnsurePresetOnResolver();
                        Undo.RecordObject(_preset, "Add Class");
                        var added = _preset.GetOrAddTypeDefinition(selected);
                        _typeFoldouts[added.typeName ?? ""] = true;
                        _ApplyChanges();
                    }).Show(addClassRect);
            }
            EditorGUILayout.EndHorizontal();

            _exposedScroll = EditorGUILayout.BeginScrollView(_exposedScroll);

            if (_preset.typeDefinitions.Count == 0 && _preset.bindings.Count == 0)
            {
                EditorGUILayout.HelpBox("Nothing exposed yet. Select a GameObject above and toggle members on, or add a class with \"Add Class\".", MessageType.None);
            }

            // Removal paths call GUIUtility.ExitGUI, so forward iteration is safe here.
            for (int i = 0; i < _preset.typeDefinitions.Count; i++)
            {
                _DrawTypeDefinition(_preset.typeDefinitions[i], i);
            }

            if (_preset.bindings.Count > 0)
            {
                EditorGUILayout.LabelField("Instance Bindings", EditorStyles.miniBoldLabel);
                for (int i = 0; i < _preset.bindings.Count; i++)
                {
                    _DrawInstanceBinding(_preset.bindings[i]);
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private void _DrawTypeDefinition(LiveBindingPreset.TypeDefinition definition, int definitionIndex)
        {
            if (definition == null) return;
            var type = definition.ResolveType();
            string title = type != null ? type.Name : $"(unresolved: {definition.typeName})";

            _typeFoldouts.TryGetValue(definition.typeName ?? "", out var open);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            open = EditorGUILayout.Foldout(open, $"{title}  ({definition.members.Count} members)", toggleOnLabelClick: true);
            _typeFoldouts[definition.typeName ?? ""] = open;
            // Searchable member dropdown; picking an entry exposes it on this type definition.
            var addMemberRect = GUILayoutUtility.GetRect(new GUIContent("Add Member"), GUI.skin.button, GUILayout.Width(88));
            using (new EditorGUI.DisabledScope(type == null))
            {
                if (GUI.Button(addMemberRect, "Add Member"))
                {
                    var candidates = new List<MemberCandidate>();
                    foreach (var candidate in _EnumerateCandidates(type))
                    {
                        if (_FindMember(definition, candidate.path, candidate.isFunction) != null) continue;
                        candidates.Add(candidate);
                    }
                    new LiveBindingMemberDropdown(_memberDropdownState, candidates,
                        selected => _ExposeTypeMember(type, selected)).Show(addMemberRect);
                }
            }
            // Create an unbound instance entry, then assign the object in Instance Bindings
            // below (or bind another scene's object through its resolver).
            if (GUILayout.Button("Add Binding", GUILayout.Width(84)))
            {
                _EnsurePresetOnResolver();
                Undo.RecordObject(_preset, "Add Binding");
                _preset.bindings.Add(new LiveBindingPreset.InstanceBinding
                {
                    key = Guid.NewGuid().ToString(),
                    typeName = definition.typeName,
                });
                _ApplyChanges();
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                GUIUtility.ExitGUI();
                return;
            }
            if (GUILayout.Button("✕", GUILayout.Width(24)))
            {
                Undo.RecordObject(_preset, "Remove Type Definition");
                Undo.RecordObject(_resolver, "Remove Type Definition");
                _preset.typeDefinitions.Remove(definition);
                for (int i = _preset.bindings.Count - 1; i >= 0; i--)
                {
                    var entry = _preset.bindings[i];
                    if (entry != null && entry.ResolveType() == type)
                    {
                        _resolver.ClearReferenceValue(new PropertyName(entry.key));
                        _preset.bindings.RemoveAt(i);
                    }
                }
                _ApplyChanges();
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                GUIUtility.ExitGUI();
                return;
            }
            EditorGUILayout.EndHorizontal();

            if (open)
            {
                EditorGUI.indentLevel++;
                bool changed = false;
                var members = definition.members;
                for (int i = 0; i < members.Count; i++)
                {
                    changed |= _DrawMember(definitionIndex, members, i);
                }
                EditorGUI.indentLevel--;

                if (changed) _ApplyChanges();
            }

            EditorGUILayout.EndVertical();
        }

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
                Undo.RecordObject(_preset, "Unexpose Member");
                members.RemoveAt(index);
                EditorGUILayout.EndHorizontal();
                return true;
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
                Undo.RecordObject(_preset, "Remove Binding");
                Undo.RecordObject(_resolver, "Remove Binding");
                _resolver.ClearReferenceValue(new PropertyName(entry.key));
                _preset.bindings.Remove(entry);
                _ApplyChanges();
                EditorGUILayout.EndHorizontal();
                GUIUtility.ExitGUI();
                return;
            }
            EditorGUILayout.EndHorizontal();
        }

    }
}
