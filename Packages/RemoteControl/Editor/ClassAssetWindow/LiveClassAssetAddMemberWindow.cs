// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

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

        // One row per exposable member, kept so the filter can hide rows and an external change
        // (undo, the owning window) can re-read every checkbox without rebuilding the list.
        private readonly List<MemberRow> _rows = new List<MemberRow>();
        private string _memberFilter = "";

        private struct MemberRow
        {
            public MemberCandidate candidate;
            public VisualElement root;
            public Toggle toggle;
        }

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
        // re-read this list - and rebuild the container's lookup table with it.
        private void _OnUndoRedo()
        {
            _Applied(_getContainer?.Invoke());
        }

        private void CreateGUI()
        {
            var root = rootVisualElement;
            LiveClassAssetStyles.Apply(root);
            root.style.flexDirection = FlexDirection.Column;

            // The delegates and the target type do not survive a domain reload, so the window can
            // come back with nothing to edit.
            if (_getPreset?.Invoke() == null || _type == null)
            {
                var help = new HelpBox("No class selected.", HelpBoxMessageType.Info);
                help.AddToClassList(LiveClassAssetStyles.kHelp);
                root.Add(help);
                return;
            }

            var header = new Label(_type.Name) { tooltip = _type.FullName };
            header.AddToClassList(LiveClassAssetStyles.kPaneHeader);
            root.Add(header);

            var bar = new Toolbar();
            var filter = new ToolbarSearchField();
            filter.AddToClassList(LiveClassAssetStyles.kToolbarField);
            filter.RegisterValueChangedCallback(evt =>
            {
                _memberFilter = evt.newValue ?? "";
                _ApplyFilter();
            });
            bar.Add(filter);
            root.Add(bar);

            var scroll = new ScrollView();
            scroll.AddToClassList(LiveClassAssetStyles.kScroll);
            foreach (var candidate in LiveClassAssetMemberExposure.EnumerateCandidates(_type))
            {
                var row = _MakeRow(candidate);
                _rows.Add(row);
                scroll.Add(row.root);
            }
            root.Add(scroll);

            _RefreshToggles();
        }

        private MemberRow _MakeRow(MemberCandidate candidate)
        {
            var row = new VisualElement();
            row.AddToClassList(LiveClassAssetStyles.kPopupRow);

            var toggle = new Toggle { text = candidate.isFunction ? $"{candidate.path} ()" : candidate.path };
            toggle.AddToClassList(LiveClassAssetStyles.kPopupRowToggle);
            toggle.RegisterValueChangedCallback(evt => _SetExposed(candidate, evt.newValue));
            row.Add(toggle);

            var meta = new Label(candidate.typeLabel);
            meta.AddToClassList(LiveClassAssetStyles.kPopupRowMeta);
            row.Add(meta);

            return new MemberRow { candidate = candidate, root = row, toggle = toggle };
        }

        private void _SetExposed(MemberCandidate candidate, bool exposed)
        {
            var preset = _getPreset?.Invoke();
            if (preset == null) return;
            var container = _getContainer?.Invoke();

            if (exposed) LiveClassAssetMemberExposure.ExposeTypeMember(preset, container, _type, candidate);
            else LiveClassAssetMemberExposure.UnexposeTypeMember(preset, container, _type, candidate);

            _Applied(container);
        }

        private void _ApplyFilter()
        {
            foreach (var row in _rows)
            {
                bool visible = string.IsNullOrEmpty(_memberFilter)
                    || row.candidate.path.IndexOf(_memberFilter, StringComparison.OrdinalIgnoreCase) >= 0;
                row.root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        // Unexposing the last member drops the whole type definition, so a single toggle can
        // change the state of every other row - re-read them all rather than just the one.
        private void _RefreshToggles()
        {
            var preset = _getPreset?.Invoke();
            var definition = preset != null && _type != null ? preset.FindTypeDefinition(_type) : null;
            foreach (var row in _rows)
            {
                bool exposed = definition != null
                    && LiveClassAssetMemberExposure.FindMember(definition, row.candidate.path, row.candidate.isFunction) != null;
                row.toggle.SetValueWithoutNotify(exposed);
            }
        }

        private void _Applied(RemoteControlContainer container)
        {
            if (container != null) container.Reload();
            _RefreshToggles();
            _onChanged?.Invoke();
        }
    }
}
