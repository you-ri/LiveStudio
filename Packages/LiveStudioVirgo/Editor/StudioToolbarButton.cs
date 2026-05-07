using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

#if UNITY_6000_3_OR_NEWER
using UnityEditor.Toolbars;
#endif

using Lilium.LiveStudio;
using Lilium.LiveStudio.Editor;
namespace Lilium.LiveStudio.Virgo.Editor
{
#if UNITY_6000_3_OR_NEWER
    // Unity 6.3+ implementation using official Toolbar API
    [InitializeOnLoad]
    static class StudioToolbarButton
    {
        private const string kToolbarButtonId = "Lilium.LiveStudio/StudioToolbarButton";

        [MainToolbarElement(kToolbarButtonId,
            defaultDockPosition = MainToolbarDockPosition.Left,
            defaultDockIndex = 10)]
        static MainToolbarDropdown CreateDropdown()
        {
            return new MainToolbarDropdown(
                new MainToolbarContent("Studio", null, "Studio Menu"),
                OnDropdownClicked);
        }

        static void OnDropdownClicked(Rect rect)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Studio Home"), false, () => StudioHomeWindow.ShowWindow());
            menu.AddItem(new GUIContent("Launch Remote App"), false, () => ToolMenu.LaunchRemoteApp());
            menu.AddItem(new GUIContent("Clear Current Data"), false, () => { foreach (var p in Object.FindObjectsOfType<Lilium.RemoteControl.Server.RemoteControlBehaviour>()) p.ClearCurrentData(); });
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Build/Release"), false, () => BuildStudioApp.BuildFromEditor());
            menu.AddItem(new GUIContent("Build/Development"), false, () => BuildStudioApp.BuildDevelopmentFromEditor());
            menu.DropDown(rect);
        }
    }
#elif UNITY_2021_1_OR_NEWER
    // Unity 2021 - 6.2 implementation using custom ToolbarExtender
    [InitializeOnLoad]
    public static class StudioToolbarButton
    {
        static StudioToolbarButton()
        {
            EditorApplication.delayCall += () =>
            {
                ToolbarExtender.LeftToolbarGUI.Add(OnToolbarGUI);
            };
        }

        private static void OnToolbarGUI()
        {
            if (GUILayout.Button(new GUIContent("Studio", "Studio Menu"), EditorStyles.toolbarDropDown, GUILayout.Width(60)))
            {
                var buttonRect = GUILayoutUtility.GetLastRect();
                buttonRect.y += 20;
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent("Studio Home"), false, () => StudioHomeWindow.ShowWindow());
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("Build/Release"), false, () => BuildStudioApp.BuildFromEditor());
                menu.AddItem(new GUIContent("Build/Development"), false, () => BuildStudioApp.BuildDevelopmentFromEditor());
                menu.DropDown(buttonRect);
            }
        }
    }

    // Toolbar Extender for Unity 2021 - 6.2
    [InitializeOnLoad]
    public static class ToolbarExtender
    {
        static int _toolCount;

        public static readonly System.Collections.Generic.List<System.Action> LeftToolbarGUI = new System.Collections.Generic.List<System.Action>();

        static ToolbarExtender()
        {
            System.Type toolbarType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.Toolbar");

            string fieldName = "k_ToolCount";

            var toolIcons = toolbarType.GetField(fieldName,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

            _toolCount = toolIcons != null ? ((int)toolIcons.GetValue(null)) : 8;

            ToolbarCallback.OnToolbarGUILeft = GUILeft;
        }

        public static void GUILeft()
        {
            GUILayout.BeginHorizontal();
            foreach (var handler in LeftToolbarGUI)
            {
                handler();
            }
            GUILayout.EndHorizontal();
        }
    }

    public static class ToolbarCallback
    {
        static System.Type _toolbarType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.Toolbar");
        static System.Type _guiViewType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GUIView");

        static System.Type _iWindowBackendType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.IWindowBackend");
        static System.Reflection.PropertyInfo _windowBackend = _guiViewType.GetProperty("windowBackend",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        static System.Reflection.PropertyInfo _viewVisualTree = _iWindowBackendType.GetProperty("visualTree",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        static System.Reflection.FieldInfo _imguiContainerOnGui = typeof(IMGUIContainer).GetField("m_OnGUIHandler",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        static ScriptableObject _currentToolbar;

        public static System.Action OnToolbarGUILeft;

        static ToolbarCallback()
        {
            EditorApplication.update -= OnUpdate;
            EditorApplication.update += OnUpdate;
        }

        static void OnUpdate()
        {
            if (_currentToolbar == null)
            {
                var toolbars = UnityEngine.Resources.FindObjectsOfTypeAll(_toolbarType);
                _currentToolbar = toolbars.Length > 0 ? (ScriptableObject)toolbars[0] : null;
                if (_currentToolbar != null)
                {
                    var windowBackend = _windowBackend.GetValue(_currentToolbar);
                    var visualTree = (VisualElement)_viewVisualTree.GetValue(windowBackend, null);

                    var toolbarZone = visualTree.Q("ToolbarZoneLeftAlign");
                    var parent = new VisualElement()
                    {
                        style = {
                            flexGrow = 1,
                            flexDirection = FlexDirection.Row,
                        }
                    };
                    var container = new IMGUIContainer();
                    container.style.flexGrow = 0;
                    container.onGUIHandler += () =>
                    {
                        OnToolbarGUILeft?.Invoke();
                    };
                    parent.Add(container);
                    toolbarZone.Add(parent);
                }
            }
        }
    }
#endif
}
