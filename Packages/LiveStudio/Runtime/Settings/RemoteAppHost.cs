// Copyright (c) You-Ri, 2026

using System.Diagnostics;
using UnityEngine;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Owns the Remote app child process for the lifetime of a player build.
    /// Reads launch options from <see cref="LiveStudioProjectSettings"/> and ties
    /// Start/Stop to <c>RuntimeInitializeOnLoadMethod</c> + <c>Application.quitting</c>
    /// instead of any in-scene component lifecycle.
    /// In the Unity Editor this host is intentionally a no-op so the developer
    /// can launch the Remote app manually (e.g. via the UIDesignerWindow button)
    /// without scene reloads or Play Mode toggles churning the process.
    /// </summary>
    public static class RemoteAppHost
    {
        static Process _process;
        static bool _initialized;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void _Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            // The Editor leaves Remote app lifecycle to the developer.
            if (Application.isEditor) return;

            var settings = LiveStudioProjectSettings.Instance;
            if (settings == null)
            {
                UnityEngine.Debug.LogWarning("[Studio] LiveStudioProjectSettings is not assigned. Remote app will not be launched.");
                return;
            }
            if (!settings.launchRemoteOnStartup)
            {
                return;
            }

            var fullPath = ToolAppLauncher.ResolveToolApplicationPath(
                settings.remotePathType,
                settings.remoteApplicationPath,
                settings.remotePackageName);

            _process = ChildProcessHost.Start(fullPath, settings.remoteArguments, settings.remoteHideWindow);
            if (_process == null) return;

            Application.quitting += _Stop;
        }

        static void _Stop()
        {
            ChildProcessHost.Stop(ref _process);
        }
    }
}
