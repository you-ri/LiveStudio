// Copyright (c) You-Ri, 2026
using System;
using UnityEditor;
using UnityEngine;

namespace Lilium.RemoteControl.Editor
{
    /// <summary>
    /// Class picker sourced from the currently selected GameObject's components, instead of the
    /// global type search "Add Class" uses. Adds a (still empty) type definition for the picked
    /// component's type; member exposure itself happens afterward through "Add Member".
    /// </summary>
    internal class LiveBindingFromSelectedWindow : EditorWindow
    {
        private static readonly Vector2 kWindowSize = new Vector2(280f, 320f);

        private Func<LiveBindingPreset> _getPreset;
        private Action<string> _onAdded;
        private Vector2 _scroll;

        // Preset mutations are deferred to the next Layout event so no draw pass ever runs on a
        // half-modified list, same reasoning as LiveBindingWindow.
        private Action _pendingAction;

        public static void Open(Func<LiveBindingPreset> getPreset, Action<string> onAdded, Rect screenRect)
        {
            var window = CreateInstance<LiveBindingFromSelectedWindow>();
            window.titleContent = new GUIContent("From Selected");
            window.minSize = kWindowSize;
            window._getPreset = getPreset;
            window._onAdded = onAdded;
            window.position = new Rect(screenRect.x, screenRect.yMax, kWindowSize.x, kWindowSize.y);
            window.ShowUtility();
        }

        private void OnEnable()
        {
            Selection.selectionChanged += Repaint;
        }

        private void OnDisable()
        {
            Selection.selectionChanged -= Repaint;
        }

        private void OnGUI()
        {
            if (_pendingAction != null && Event.current.type == EventType.Layout)
            {
                var action = _pendingAction;
                _pendingAction = null;
                action();
            }

            var preset = _getPreset?.Invoke();
            if (preset == null)
            {
                EditorGUILayout.HelpBox("Assign a preset in the Live Binding window first.", MessageType.Info);
                return;
            }

            var go = Selection.activeGameObject;
            if (go == null)
            {
                EditorGUILayout.HelpBox("Select a GameObject in the scene.", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField(go.name, EditorStyles.boldLabel);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (var component in go.GetComponents<Component>())
            {
                if (component == null) continue; // missing script
                _DrawRow(preset, component.GetType());
            }
            EditorGUILayout.EndScrollView();
        }

        private void _DrawRow(LiveBindingPreset preset, Type type)
        {
            bool added = preset.FindTypeDefinition(type) != null;

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(type.Name, added ? EditorStyles.boldLabel : EditorStyles.label);
            GUILayout.FlexibleSpace();
            using (new EditorGUI.DisabledScope(added))
            {
                if (GUILayout.Button(added ? "Added" : "Add", GUILayout.Width(60)))
                {
                    _Defer(() =>
                    {
                        Undo.RecordObject(preset, "Add Class");
                        var definition = preset.GetOrAddTypeDefinition(type);
                        EditorUtility.SetDirty(preset);
                        _onAdded?.Invoke(definition.typeName);
                        Repaint();
                    });
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        private void _Defer(Action action)
        {
            _pendingAction = action;
            Repaint();
        }
    }
}
