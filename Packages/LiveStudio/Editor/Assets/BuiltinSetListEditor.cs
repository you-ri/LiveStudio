// Copyright (c) You-Ri, 2026

using System.Collections.Generic;
using System.IO;

using UnityEditor;
using UnityEngine;

namespace Lilium.LiveStudio.Editor
{
    /// <summary>
    /// Draws one <see cref="BuiltinSetList.Entry"/> as the scene it declares. The stored identity is the
    /// scene's GUID, which is not something to type, so the row shows a scene object field and writes the
    /// GUID and the current build path from whatever is dropped in. The path is kept alongside so runtime
    /// can load the scene without <c>AssetDatabase</c>; it is refreshed here and by
    /// <see cref="BuiltinSetSceneWatcher"/> when the scene moves.
    /// </summary>
    [CustomPropertyDrawer(typeof(BuiltinSetList.Entry))]
    public class BuiltinSetListEntryDrawer : PropertyDrawer
    {
        // Per-element state must never be cached on the drawer: one drawer instance serves every element
        // of the array, so anything remembered here would leak between rows.
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float line = EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            return line * 3 + EditorGUIUtility.standardVerticalSpacing;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var guid = property.FindPropertyRelative(nameof(BuiltinSetList.Entry.guid));
            var scenePath = property.FindPropertyRelative(nameof(BuiltinSetList.Entry.scenePath));
            var displayName = property.FindPropertyRelative(nameof(BuiltinSetList.Entry.displayName));
            var thumbnail = property.FindPropertyRelative(nameof(BuiltinSetList.Entry.thumbnail));

            float line = EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            var row = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);

            var currentPath = AssetDatabase.GUIDToAssetPath(guid.stringValue);
            var scene = string.IsNullOrEmpty(currentPath)
                ? null
                : AssetDatabase.LoadAssetAtPath<SceneAsset>(currentPath);

            EditorGUI.BeginChangeCheck();
            var picked = (SceneAsset)EditorGUI.ObjectField(row, "Scene", scene, typeof(SceneAsset), false);
            if (EditorGUI.EndChangeCheck())
            {
                var pickedPath = picked != null ? AssetDatabase.GetAssetPath(picked) : string.Empty;
                guid.stringValue = string.IsNullOrEmpty(pickedPath)
                    ? string.Empty
                    : AssetDatabase.AssetPathToGUID(pickedPath);
                scenePath.stringValue = pickedPath;
            }
            else if (!string.IsNullOrEmpty(currentPath) && currentPath != scenePath.stringValue)
            {
                // The scene was moved or renamed while this list was open: the GUID still points at it, so
                // heal the cached path rather than leaving a path that no longer loads.
                scenePath.stringValue = currentPath;
            }

            row.y += line;
            EditorGUI.PropertyField(row, displayName, new GUIContent("Display Name"));

            row.y += line;
            EditorGUI.PropertyField(row, thumbnail, new GUIContent("Thumbnail"));
        }
    }

    /// <summary>
    /// Inspector for <see cref="BuiltinSetList"/>: the declared scenes plus the two things that make a
    /// declaration actually work — every scene must be in the build (runtime loads it by path, and a scene
    /// outside the build cannot be loaded at all) and no two sets may share a display name (the stage's
    /// saved and recorded state refers to a set by name). Both are reported here, where they can be fixed,
    /// rather than only at runtime.
    /// </summary>
    [CustomEditor(typeof(BuiltinSetList))]
    public class BuiltinSetListEditor : UnityEditor.Editor
    {
        const string kAssetPath = "Assets/Resources/" + BuiltinSetList.kResourcesName + ".asset";

        /// <summary>
        /// Creates the declaration asset at the fixed Resources path runtime loads it from, or selects the
        /// existing one. A scene cannot live under <c>Resources</c>, so this asset is what carries the
        /// declaration into the build.
        /// </summary>
        [MenuItem("Assets/Lilium Live Studio/Create Built-in Set List")]
        public static void CreateOrSelect()
        {
            var existing = AssetDatabase.LoadAssetAtPath<BuiltinSetList>(kAssetPath);
            if (existing == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(kAssetPath));
                existing = CreateInstance<BuiltinSetList>();
                AssetDatabase.CreateAsset(existing, kAssetPath);
                AssetDatabase.SaveAssets();
            }
            Selection.activeObject = existing;
            EditorGUIUtility.PingObject(existing);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_entries"), new GUIContent("Sets"), true);
            serializedObject.ApplyModifiedProperties();

            var list = (BuiltinSetList)target;
            _DrawValidation(list);
        }

        void _DrawValidation(BuiltinSetList list)
        {
            var entries = list.entries;
            var missing = new List<string>();
            var names = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            var duplicated = new List<string>();
            var buildScenes = _BuildScenePaths();

            for (int i = 0; i < entries.Length; i++)
            {
                var path = entries[i].scenePath;
                if (string.IsNullOrEmpty(path)) continue;

                if (!buildScenes.Contains(path)) missing.Add(path);
                if (!names.Add(BuiltinSetSource.ResolveName(entries[i]))) duplicated.Add(path);
            }

            if (duplicated.Count > 0)
            {
                EditorGUILayout.HelpBox(
                    "Two sets share a display name, so a saved scene or a take cannot tell them apart:\n" +
                    string.Join("\n", duplicated),
                    MessageType.Error);
            }

            if (missing.Count == 0)
            {
                if (entries.Length > 0)
                    EditorGUILayout.HelpBox("Every declared set is in the build's scene list.", MessageType.Info);
                return;
            }

            EditorGUILayout.HelpBox(
                "These scenes are declared as sets but are not in the build's scene list, so they cannot be " +
                "loaded at runtime:\n" + string.Join("\n", missing),
                MessageType.Warning);

            if (GUILayout.Button("Add Missing Scenes To Build"))
            {
                _AddToBuild(missing);
            }
        }

        static HashSet<string> _BuildScenePaths()
        {
            var result = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            var scenes = EditorBuildSettings.scenes;
            for (int i = 0; i < scenes.Length; i++)
            {
                // A disabled entry is not in the build, so it is as good as absent here.
                if (scenes[i] != null && scenes[i].enabled && !string.IsNullOrEmpty(scenes[i].path))
                    result.Add(scenes[i].path);
            }
            return result;
        }

        static void _AddToBuild(List<string> paths)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            for (int i = 0; i < paths.Count; i++)
            {
                var path = paths[i];
                int existing = scenes.FindIndex(s => s != null && string.Equals(s.path, path, System.StringComparison.OrdinalIgnoreCase));
                if (existing >= 0)
                {
                    // Present but disabled: enabling it is what "add to build" means here.
                    scenes[existing] = new EditorBuildSettingsScene(path, true);
                    continue;
                }
                scenes.Add(new EditorBuildSettingsScene(path, true));
            }
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
