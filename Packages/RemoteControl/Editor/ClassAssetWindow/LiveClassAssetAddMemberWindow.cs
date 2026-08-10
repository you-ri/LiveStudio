// Copyright (c) You-Ri, 2026
using System;
using UnityEditor;
using UnityEngine;

using Lilium.RemoteControl.LiveScene;

namespace Lilium.RemoteControl.Editor
{
    /// <summary>
    /// Checkbox list of every exposable property/field/method on one class, toggled directly
    /// against the preset. Kept open (unlike the single-pick searchable dropdown it replaces) so
    /// several members can be added or removed in one sitting.
    /// </summary>
    internal class LiveClassAssetAddMemberWindow : EditorWindow
    {
        private static readonly Vector2 kWindowSize = new Vector2(320f, 400f);

        private Func<LiveClassAsset> _getPreset;
        private Func<RemoteControlContainer> _getContainer;
        private Type _type;
        private Action _onChanged;

        private string _memberFilter = "";
        private Vector2 _scroll;

        // Preset mutations are deferred to the next Layout event so no draw pass ever runs on a
        // half-modified list, same reasoning as LiveClassAssetWindow.
        private Action _pendingAction;

        public static void Open(Func<LiveClassAsset> getPreset, Func<RemoteControlContainer> getContainer, Type type, Action onChanged, Rect screenRect)
        {
            var window = CreateInstance<LiveClassAssetAddMemberWindow>();
            window.titleContent = new GUIContent("Add Member");
            window.minSize = kWindowSize;
            window._getPreset = getPreset;
            window._getContainer = getContainer;
            window._type = type;
            window._onChanged = onChanged;
            window.position = new Rect(screenRect.x, screenRect.yMax, kWindowSize.x, kWindowSize.y);
            window.ShowUtility();
        }

        private void OnEnable()
        {
            Undo.undoRedoPerformed += _OnUndoRedo;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= _OnUndoRedo;
        }

        // The checkbox states are read straight off the asset, so an undo elsewhere has to
        // repaint this list — and rebuild the container's lookup table with it.
        private void _OnUndoRedo()
        {
            _Applied(_getContainer?.Invoke());
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
            if (preset == null || _type == null)
            {
                EditorGUILayout.HelpBox("No class selected.", MessageType.Info);
                return;
            }
            var container = _getContainer?.Invoke();

            EditorGUILayout.LabelField(new GUIContent(_type.Name, _type.FullName), LiveClassAssetStyles.paneHeader);
            _memberFilter = EditorGUILayout.TextField("Filter", _memberFilter);

            var definition = preset.FindTypeDefinition(_type);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (var candidate in LiveClassAssetMemberExposure.EnumerateCandidates(_type))
            {
                if (!string.IsNullOrEmpty(_memberFilter)
                    && candidate.path.IndexOf(_memberFilter, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                bool exposed = definition != null
                    && LiveClassAssetMemberExposure.FindMember(definition, candidate.path, candidate.isFunction) != null;

                EditorGUILayout.BeginHorizontal();
                bool next = EditorGUILayout.ToggleLeft(candidate.isFunction ? $"{candidate.path} ()" : candidate.path, exposed);
                GUILayout.Label(candidate.typeLabel, LiveClassAssetStyles.rowMeta, GUILayout.MinWidth(60));
                EditorGUILayout.EndHorizontal();

                if (next == exposed) continue;
                var picked = candidate;
                if (next) _Defer(() => { LiveClassAssetMemberExposure.ExposeTypeMember(preset, container, _type, picked); _Applied(container); });
                else _Defer(() => { LiveClassAssetMemberExposure.UnexposeTypeMember(preset, container, _type, picked); _Applied(container); });
            }
            EditorGUILayout.EndScrollView();
        }

        private void _Defer(Action action)
        {
            _pendingAction = action;
            Repaint();
        }

        private void _Applied(RemoteControlContainer container)
        {
            if (container != null) container.Reload();
            _onChanged?.Invoke();
            Repaint();
        }
    }
}
