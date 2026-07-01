// Copyright (c) You-Ri, 2026

using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

#if UNITY_2021_2_OR_NEWER
using UnityEditor.Toolbars;
#endif

namespace Lilium.LiveStudio.Editor
{
    /// <summary>
    /// Shared launch/close logic for the Remote app, driven from the editor toolbar button.
    /// Path/arguments come from <see cref="LiveStudioProjectSettings"/> (the same source as the
    /// player-side <see cref="RemoteAppHost"/>) and the process is started/stopped through the
    /// shared <see cref="ChildProcessHost"/>, so editor behaviour matches the runtime launcher.
    /// </summary>
    static class RemoteAppLauncher
    {
        static Process _process;

        /// <summary>True while a Remote app started from the toolbar is still alive.</summary>
        public static bool IsRunning
        {
            get
            {
                if (_process == null) return false;
                try
                {
                    if (_process.HasExited)
                    {
                        _process.Dispose();
                        _process = null;
                        return false;
                    }
                    return true;
                }
                catch (System.InvalidOperationException)
                {
                    // The process was disposed elsewhere; treat it as stopped.
                    _process = null;
                    return false;
                }
            }
        }

        public static void Toggle()
        {
            if (IsRunning) _Close();
            else _Launch();
        }

        static void _Launch()
        {
            var settings = LiveStudioProjectSettings.Instance;
            if (settings == null)
            {
                UnityEngine.Debug.LogWarning("[Studio] LiveStudioProjectSettings is not assigned. Remote app cannot be launched.");
                return;
            }

            var fullPath = ToolAppLauncher.ResolveToolApplicationPath(
                settings.remotePathType,
                settings.remoteApplicationPath,
                settings.remotePackageName);

            if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath))
            {
                UnityEngine.Debug.LogError($"[Studio] Remote app not found: {fullPath}");
                return;
            }

            // Always show the window when launched by hand from the editor; hiding it (the packaged
            // default in settings.remoteHideWindow) would defeat the purpose of opening the UI here.
            _process = ChildProcessHost.Start(fullPath, settings.remoteArguments, hideWindow: false);
        }

        static void _Close()
        {
            ChildProcessHost.RequestCloseAndRelease(ref _process);
        }
    }

#if UNITY_6000_3_OR_NEWER
    // Unity 6.3+ exposes an official main-toolbar element API.
    [InitializeOnLoad]
    static class RemoteAppToolbarButton
    {
        const string kToolbarButtonId = "Lilium.LiveStudio/RemoteAppToolbarButton";

        [MainToolbarElement(kToolbarButtonId,
            defaultDockPosition = MainToolbarDockPosition.Left,
            defaultDockIndex = 10)]
        static MainToolbarButton CreateButton()
        {
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(
                "Packages/jp.lilium.livestudio/Editor/Icons/cards.png");
            return new MainToolbarButton(
                new MainToolbarContent(null, icon, "Launch / close the Remote app"),
                RemoteAppLauncher.Toggle);
        }
    }
#elif UNITY_2021_2_OR_NEWER
    // Unity 2021.2 - 6.2 have no public toolbar-extension API, so the button is hosted through the
    // reflection-based MainToolbarHook below (also the active path on the Unity 6.0 Studio project).
    // A native EditorToolbarButton is injected so its background/hover exactly match the other
    // main-toolbar buttons (an IMGUI button drawn with EditorStyles.toolbarButton does not).
    [InitializeOnLoad]
    static class RemoteAppToolbarButton
    {
        // Idle tint matches the other main-toolbar icons (#e3e3e3); running turns the icon green.
        static readonly Color kIdleTint = new Color(0.89f, 0.89f, 0.89f);
        static readonly Color kRunningTint = new Color(0.30f, 0.85f, 0.30f);

        static EditorToolbarButton _button;
        static Image _icon;

        static RemoteAppToolbarButton()
        {
            EditorApplication.delayCall += _Register;
        }

        static void _Register()
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(
                "Packages/jp.lilium.livestudio/Editor/Icons/cards.png");

            // Own Image child (instead of EditorToolbarButton.icon) so the tint is fully controllable.
            _icon = new Image { image = texture, scaleMode = ScaleMode.ScaleToFit };
            _icon.style.width = 16;
            _icon.style.height = 16;
            _icon.style.alignSelf = Align.Center;

            _button = new EditorToolbarButton(string.Empty, RemoteAppLauncher.Toggle);
            // Center the icon on both axes; the empty text element otherwise leaves it top-aligned.
            _button.style.alignItems = Align.Center;
            _button.style.justifyContent = Justify.Center;
            _button.Add(_icon);

            // Poll the launch state so the icon reflects a Remote app that was closed on its own.
            _button.schedule.Execute(_UpdateState).Every(500);
            _UpdateState();

            MainToolbarHook.AddLeftElement(_button);
        }

        static void _UpdateState()
        {
            bool running = RemoteAppLauncher.IsRunning;
            _icon.tintColor = running ? kRunningTint : kIdleTint;
            _button.tooltip = running ? "Close the Remote app" : "Launch the Remote app";
        }
    }

    /// <summary>
    /// Injects a native <see cref="VisualElement"/> into the main toolbar's left-aligned zone. Unity
    /// 2021.2 - 6.2 expose no public toolbar-extension API, so the toolbar <see cref="VisualElement"/>
    /// is reached through reflection over the internal <c>UnityEditor.Toolbar</c> view.
    /// </summary>
    [InitializeOnLoad]
    static class MainToolbarHook
    {
        static readonly System.Collections.Generic.List<VisualElement> _pending =
            new System.Collections.Generic.List<VisualElement>();

        static readonly System.Type _toolbarType =
            typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.Toolbar");
        static readonly System.Type _guiViewType =
            typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GUIView");
        static readonly System.Type _windowBackendType =
            typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.IWindowBackend");

        static readonly System.Reflection.PropertyInfo _windowBackendProperty =
            _guiViewType?.GetProperty("windowBackend",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        static readonly System.Reflection.PropertyInfo _visualTreeProperty =
            _windowBackendType?.GetProperty("visualTree",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        static VisualElement _zone;

        static MainToolbarHook()
        {
            EditorApplication.update -= _OnUpdate;
            EditorApplication.update += _OnUpdate;
        }

        /// <summary>Add an element to the toolbar's left zone, deferring until the zone is available.</summary>
        public static void AddLeftElement(VisualElement element)
        {
            if (_zone != null)
            {
                _zone.Add(element);
                return;
            }
            _pending.Add(element);
        }

        static void _OnUpdate()
        {
            if (_zone != null) return;
            if (_toolbarType == null || _windowBackendProperty == null || _visualTreeProperty == null) return;

            var toolbars = Resources.FindObjectsOfTypeAll(_toolbarType);
            if (toolbars.Length == 0) return;

            var toolbar = (ScriptableObject)toolbars[0];
            var backend = _windowBackendProperty.GetValue(toolbar);
            if (backend == null) return;
            var visualTree = _visualTreeProperty.GetValue(backend, null) as VisualElement;
            var zone = visualTree?.Q("ToolbarZoneLeftAlign");
            if (zone == null) return;

            _zone = zone;
            foreach (var element in _pending)
            {
                _zone.Add(element);
            }
            _pending.Clear();
        }
    }
#endif
}
