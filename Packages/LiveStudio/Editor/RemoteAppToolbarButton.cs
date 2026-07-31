// Copyright (c) You-Ri, 2026

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

using Lilium.RemoteControl.Editor;

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
    /// The toolbar icon that conveys the Remote app's launch state, shared by every editor-version
    /// branch below. Loading and tinting are <see cref="ToolbarIcon"/>'s, so this button and the
    /// Remote Control server button next to it share one set of tints.
    /// </summary>
    static class RemoteAppToolbarIcon
    {
        public const string path = "Packages/jp.lilium.livestudio/Editor/Icons/cards.png";
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
            defaultDockIndex = 11)]
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
                _runningIcon = ToolbarIcon.CreateTinted(RemoteAppToolbarIcon.path, ToolbarIcon.runningTint);
            }

            var icon = running && _runningIcon != null ? _runningIcon : ToolbarIcon.Load(RemoteAppToolbarIcon.path);
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
    // reflection-based MainToolbarHook (also the active path on the Unity 6.0 Studio project).
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
            _icon = new Image { image = ToolbarIcon.Load(RemoteAppToolbarIcon.path), scaleMode = ScaleMode.ScaleToFit };
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

            // Right of the Remote Control server button (order 10).
            MainToolbarHook.AddLeftElement(_button, order: 11);
        }

        static void _UpdateState()
        {
            bool running = RemoteAppLauncher.IsRunning;
            _icon.tintColor = running ? ToolbarIcon.runningTint : ToolbarIcon.idleTint;
            _button.tooltip = running ? "Close the Remote app" : "Launch the Remote app";
        }
    }
#endif
}
