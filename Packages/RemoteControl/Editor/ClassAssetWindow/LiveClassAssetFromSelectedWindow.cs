// Copyright (c) You-Ri, 2026
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Lilium.RemoteControl.Editor
{
    /// <summary>
    /// Class picker sourced from the currently selected GameObject's components, instead of the
    /// global type search the class list's "+" uses. Adds a (still empty) type definition for the
    /// picked component's type; member exposure itself happens afterward through the detail
    /// pane's "+".
    /// </summary>
    internal class LiveClassAssetFromSelectedWindow : EditorWindow
    {
        private static readonly Vector2 kWindowSize = new Vector2(280f, 320f);

        private Func<LiveClassAsset> _getPreset;
        private Action<string> _onAdded;

        private VisualElement _content;

        public static void Open(Func<LiveClassAsset> getPreset, Action<string> onAdded, Rect screenRect)
        {
            var window = CreateInstance<LiveClassAssetFromSelectedWindow>();
            window.titleContent = new GUIContent("From Selected");
            window.minSize = kWindowSize;
            window._getPreset = getPreset;
            window._onAdded = onAdded;
            window.position = new Rect(screenRect.x, screenRect.yMax, kWindowSize.x, kWindowSize.y);
            window.ShowUtility();
        }

        private void OnEnable()
        {
            Selection.selectionChanged += _Rebuild;
        }

        private void OnDisable()
        {
            Selection.selectionChanged -= _Rebuild;
        }

        private void CreateGUI()
        {
            var root = rootVisualElement;
            LiveClassAssetStyles.Apply(root);
            root.style.flexDirection = FlexDirection.Column;

            _content = new VisualElement();
            _content.style.flexGrow = 1;
            root.Add(_content);

            _Rebuild();
        }

        private void _Rebuild()
        {
            if (_content == null) return;
            _content.Clear();

            // The delegate does not survive a domain reload, so the window can come back with no
            // preset to add to.
            var preset = _getPreset?.Invoke();
            if (preset == null)
            {
                _content.Add(_MakeHelp("Assign a Live Class Asset in the Live Class Asset window first."));
                return;
            }

            var go = Selection.activeGameObject;
            if (go == null)
            {
                _content.Add(_MakeHelp("Select a GameObject in the scene."));
                return;
            }

            var header = new Label(go.name);
            header.AddToClassList(LiveClassAssetStyles.kPaneHeader);
            _content.Add(header);

            var scroll = new ScrollView();
            scroll.AddToClassList(LiveClassAssetStyles.kScroll);
            foreach (var component in go.GetComponents<Component>())
            {
                if (component == null) continue; // missing script
                scroll.Add(_MakeRow(preset, component.GetType()));
            }
            _content.Add(scroll);
        }

        private VisualElement _MakeRow(LiveClassAsset preset, Type type)
        {
            bool added = preset.FindTypeDefinition(type) != null;

            var row = new VisualElement();
            row.AddToClassList(LiveClassAssetStyles.kPopupRow);

            var title = new Label(type.Name) { tooltip = type.FullName };
            title.AddToClassList(LiveClassAssetStyles.kPopupRowTitle);
            title.EnableInClassList(LiveClassAssetStyles.kPopupRowTitleAdded, added);
            row.Add(title);

            var button = new Button(() => _AddClass(preset, type)) { text = added ? "Added" : "Add" };
            button.AddToClassList(LiveClassAssetStyles.kPopupRowButton);
            button.SetEnabled(!added);
            row.Add(button);

            return row;
        }

        private void _AddClass(LiveClassAsset preset, Type type)
        {
            LiveClassAssetMemberExposure.BeginEdit(preset, null, "Add Class");
            var definition = preset.GetOrAddTypeDefinition(type);
            EditorUtility.SetDirty(preset);
            _onAdded?.Invoke(definition.typeName);
            // Rebuilding here would destroy the button whose click is still being dispatched.
            rootVisualElement.schedule.Execute(_Rebuild);
        }

        private static HelpBox _MakeHelp(string text)
        {
            var help = new HelpBox(text, HelpBoxMessageType.Info);
            help.AddToClassList(LiveClassAssetStyles.kHelp);
            return help;
        }
    }
}
