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
    internal class LiveBindingAddMemberWindow : EditorWindow
    {
        private static readonly Vector2 kWindowSize = new Vector2(320f, 400f);

        private Func<LiveBindingPreset> _getPreset;
        private Func<LiveBindingResolver> _getResolver;
        private Type _type;
        private Action _onChanged;

        private string _memberFilter = "";
        private Vector2 _scroll;

        // Preset mutations are deferred to the next Layout event so no draw pass ever runs on a
        // half-modified list, same reasoning as LiveBindingWindow.
        private Action _pendingAction;

        public static void Open(Func<LiveBindingPreset> getPreset, Func<LiveBindingResolver> getResolver, Type type, Action onChanged, Rect screenRect)
        {
            var window = CreateInstance<LiveBindingAddMemberWindow>();
            window.titleContent = new GUIContent("Add Member");
            window.minSize = kWindowSize;
            window._getPreset = getPreset;
            window._getResolver = getResolver;
            window._type = type;
            window._onChanged = onChanged;
            window.position = new Rect(screenRect.x, screenRect.yMax, kWindowSize.x, kWindowSize.y);
            window.ShowUtility();
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
            var resolver = _getResolver?.Invoke();

            EditorGUILayout.LabelField(_type.FullName, EditorStyles.boldLabel);
            _memberFilter = EditorGUILayout.TextField("Filter", _memberFilter);

            var definition = preset.FindTypeDefinition(_type);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (var candidate in LiveBindingMemberExposure.EnumerateCandidates(_type))
            {
                if (!string.IsNullOrEmpty(_memberFilter)
                    && candidate.path.IndexOf(_memberFilter, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                bool exposed = definition != null
                    && LiveBindingMemberExposure.FindMember(definition, candidate.path, candidate.isFunction) != null;

                EditorGUILayout.BeginHorizontal();
                bool next = EditorGUILayout.ToggleLeft(candidate.isFunction ? $"{candidate.path} ()" : candidate.path, exposed);
                GUILayout.Label(candidate.typeLabel, EditorStyles.miniLabel, GUILayout.MinWidth(60));
                EditorGUILayout.EndHorizontal();

                if (next == exposed) continue;
                var picked = candidate;
                if (next) _Defer(() => { LiveBindingMemberExposure.ExposeTypeMember(preset, resolver, _type, picked); _Applied(resolver); });
                else _Defer(() => { LiveBindingMemberExposure.UnexposeTypeMember(preset, resolver, _type, picked); _Applied(resolver); });
            }
            EditorGUILayout.EndScrollView();
        }

        private void _Defer(Action action)
        {
            _pendingAction = action;
            Repaint();
        }

        private void _Applied(LiveBindingResolver resolver)
        {
            if (resolver != null) resolver.Reload();
            _onChanged?.Invoke();
            Repaint();
        }
    }
}
