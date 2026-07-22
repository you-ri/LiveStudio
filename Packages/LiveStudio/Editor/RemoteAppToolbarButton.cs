// Copyright (c) You-Ri, 2026

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
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
        static SynchronizationContext _mainThreadContext;

        /// <summary>Raised on the main thread whenever <see cref="IsRunning"/> changes.</summary>
        public static event Action stateChanged;

        /// <summary>True while a Remote app started from the toolbar is still alive.</summary>
        public static bool IsRunning => _process != null;

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
            var process = ChildProcessHost.Start(fullPath, settings.remoteArguments, hideWindow: false);
            if (process == null) return;

            // Report a Remote app that the user quits on its own instead of having the toolbar poll
            // for it. Exited is raised on a thread pool thread, so remember the main thread to hop
            // back to; this runs from a button click, so the current context is the editor's.
            _mainThreadContext = SynchronizationContext.Current;
            process.EnableRaisingEvents = true;
            process.Exited += _OnProcessExited;

            _process = process;
            stateChanged?.Invoke();
        }

        static void _Close()
        {
            if (_process == null) return;

            _process.Exited -= _OnProcessExited;
            ChildProcessHost.RequestCloseAndRelease(ref _process);
            stateChanged?.Invoke();
        }

        static void _OnProcessExited(object sender, EventArgs args)
        {
            var exited = sender as Process;
            _mainThreadContext?.Post(_ => _Release(exited), null);
        }

        static void _Release(Process process)
        {
            if (process == null) return;

            process.Exited -= _OnProcessExited;
            process.Dispose();

            // A later launch may already own the field; leave the newer process's state alone.
            if (!ReferenceEquals(_process, process)) return;

            _process = null;
            stateChanged?.Invoke();
        }
    }

    /// <summary>
    /// The toolbar icon and the tints that convey the Remote app's launch state, shared by every
    /// editor-version branch below.
    /// </summary>
    static class RemoteAppToolbarIcon
    {
        const string kIconPath = "Packages/jp.lilium.livestudio/Editor/Icons/cards.png";

        /// <summary>Idle tint matches the other main-toolbar icons (#e3e3e3).</summary>
        public static readonly Color idleTint = new Color(0.89f, 0.89f, 0.89f);

        /// <summary>The icon turns green while a Remote app started from the toolbar is running.</summary>
        public static readonly Color runningTint = new Color(0.30f, 0.85f, 0.30f);

        public static Texture2D Load()
        {
            return AssetDatabase.LoadAssetAtPath<Texture2D>(kIconPath);
        }

        /// <summary>
        /// Builds a tinted copy of the icon for hosts that cannot tint the rendered image. The source
        /// icon is white, so multiplying it by the tint yields the tint itself while keeping the alpha
        /// silhouette. The PNG is decoded from disk because the imported asset is not readable.
        /// </summary>
        public static Texture2D CreateTinted(Color tint)
        {
            var fullPath = Path.GetFullPath(kIconPath);
            if (!File.Exists(fullPath))
            {
                UnityEngine.Debug.LogError($"[Studio] Toolbar icon not found: {fullPath}");
                return null;
            }

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            if (!texture.LoadImage(File.ReadAllBytes(fullPath)))
            {
                UnityEngine.Debug.LogError($"[Studio] Failed to decode toolbar icon: {fullPath}");
                UnityEngine.Object.DestroyImmediate(texture);
                return null;
            }

            var pixels = texture.GetPixels32();
            for (int i = 0; i < pixels.Length; i++)
            {
                var pixel = pixels[i];
                pixel.r = (byte)(pixel.r * tint.r);
                pixel.g = (byte)(pixel.g * tint.g);
                pixel.b = (byte)(pixel.b * tint.b);
                pixels[i] = pixel;
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }
    }

#if UNITY_6000_3_OR_NEWER
    // Unity 6.3+ exposes an official main-toolbar element API.
    [InitializeOnLoad]
    static class RemoteAppToolbarButton
    {
        const string kToolbarButtonId = "Lilium.LiveStudio/RemoteAppToolbarButton";

        static MainToolbarButton _button;
        static Texture2D _runningIcon;

        static RemoteAppToolbarButton()
        {
            RemoteAppLauncher.stateChanged -= _OnStateChanged;
            RemoteAppLauncher.stateChanged += _OnStateChanged;
        }

        [MainToolbarElement(kToolbarButtonId,
            defaultDockPosition = MainToolbarDockPosition.Left,
            defaultDockIndex = 10)]
        static MainToolbarButton CreateButton()
        {
            _button = new MainToolbarButton(
                _BuildContent(RemoteAppLauncher.IsRunning),
                RemoteAppLauncher.Toggle);
            return _button;
        }

        /// <summary>
        /// A <see cref="MainToolbarElement"/> is a content descriptor rather than a VisualElement, so
        /// the running state cannot be shown by tinting a child Image the way the older path does.
        /// The tint is baked into a second icon and swapped into the element's content instead.
        /// </summary>
        static MainToolbarContent _BuildContent(bool running)
        {
            if (running && _runningIcon == null)
            {
                _runningIcon = RemoteAppToolbarIcon.CreateTinted(RemoteAppToolbarIcon.runningTint);
            }

            var icon = running && _runningIcon != null ? _runningIcon : RemoteAppToolbarIcon.Load();
            return new MainToolbarContent(
                null,
                icon,
                running ? "Close the Remote app" : "Launch the Remote app");
        }

        static void _OnStateChanged()
        {
            if (_button == null) return;

            _button.content = _BuildContent(RemoteAppLauncher.IsRunning);
            MainToolbar.Refresh(kToolbarButtonId);
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
        static EditorToolbarButton _button;
        static Image _icon;

        static RemoteAppToolbarButton()
        {
            EditorApplication.delayCall += _Register;
        }

        static void _Register()
        {
            // Own Image child (instead of EditorToolbarButton.icon) so the tint is fully controllable.
            _icon = new Image { image = RemoteAppToolbarIcon.Load(), scaleMode = ScaleMode.ScaleToFit };
            _icon.style.width = 16;
            _icon.style.height = 16;
            _icon.style.alignSelf = Align.Center;

            _button = new EditorToolbarButton(string.Empty, RemoteAppLauncher.Toggle);
            // Center the icon on both axes; the empty text element otherwise leaves it top-aligned.
            _button.style.alignItems = Align.Center;
            _button.style.justifyContent = Justify.Center;
            _button.Add(_icon);

            _UpdateState();
            RemoteAppLauncher.stateChanged -= _UpdateState;
            RemoteAppLauncher.stateChanged += _UpdateState;

            MainToolbarHook.AddLeftElement(_button);
        }

        static void _UpdateState()
        {
            bool running = RemoteAppLauncher.IsRunning;
            _icon.tintColor = running ? RemoteAppToolbarIcon.runningTint : RemoteAppToolbarIcon.idleTint;
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
