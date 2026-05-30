// Copyright (c) You-Ri, 2026

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace Lilium.VRChatAvatarTransfer.Editor
{
    /// <summary>
    /// Tracks the install state of the optional packages surfaced in the readme and installs
    /// them on demand. The package list is data-driven from <see cref="Readme.optionalPackages"/>.
    ///
    /// VRChat Avatar Transfer depends on UniVRM (com.vrmc.gltf / com.vrmc.vrm), which is not
    /// published to any public VPM/registry, so the packages are added by Git URL. This lives in
    /// the un-gated Installer assembly (no UniVRM references) so the readme and its install button
    /// keep compiling and rendering even while UniVRM is missing — which is exactly when the user
    /// needs the button. <see cref="VrmDependencyInstaller"/> covers the on-import auto-prompt; this
    /// is the manual, discoverable path from the readme.
    /// </summary>
    internal static class OptionalPackageInstaller
    {
        // null until the first List request completes. Holds the names of installed packages.
        static HashSet<string> _installed;
        static bool _busy;

        /// <summary>True while a list or install operation is in flight; used to disable buttons.</summary>
        public static bool IsBusy => _busy;

        /// <summary>True once the installed set has been resolved at least once.</summary>
        public static bool IsInstalledKnown => _installed != null;

        public static bool IsInstalled(string id) => _installed != null && _installed.Contains(id);

        /// <summary>
        /// Refreshes the installed package set. <paramref name="onDone"/> is invoked on the editor
        /// main thread once the list resolves (typically used to Repaint the inspector).
        /// </summary>
        public static void RefreshInstalled(Action onDone = null)
        {
            if (_busy)
            {
                onDone?.Invoke();
                return;
            }

            var list = Client.List(offlineMode: true, includeIndirectDependencies: false);
            WaitFor(list, () =>
            {
                if (list.Status == StatusCode.Success)
                    _installed = new HashSet<string>(list.Result.Select(p => p.name));
                else
                    Debug.LogError("[VRChatAvatarTransfer.Installer] Failed to list installed packages: " + list.Error?.message);
                onDone?.Invoke();
            });
        }

        /// <summary>
        /// Installs the given packages. Packages already installed are skipped. <paramref name="onDone"/>
        /// is invoked on the editor main thread once the operation resolves.
        /// </summary>
        public static void Install(IReadOnlyList<Readme.OptionalPackage> packages, Action onDone = null)
        {
            if (_busy) return;

            var targets = packages.Where(p => p != null && !string.IsNullOrEmpty(p.id) && !IsInstalled(p.id)).ToList();
            if (targets.Count == 0)
            {
                onDone?.Invoke();
                return;
            }

            _busy = true;
            var ids = targets.Select(ToPackageId).ToArray();
            var add = Client.AddAndRemove(packagesToAdd: ids);
            WaitFor(add, () =>
            {
                if (add.Status != StatusCode.Success)
                    Debug.LogError("[VRChatAvatarTransfer.Installer] Failed to install optional packages: " + add.Error?.message);
                _busy = false;
                // Invalidate so the next refresh reflects the new state.
                _installed = null;
                RefreshInstalled(onDone);
            });
        }

        // Builds the UPM identifier added via Client.AddAndRemove. A Git URL wins (UniVRM ships
        // that way); otherwise fall back to id@version, or a bare id when no version is pinned.
        static string ToPackageId(Readme.OptionalPackage p)
        {
            if (!string.IsNullOrEmpty(p.gitUrl))
                return p.id + "@" + p.gitUrl;
            if (!string.IsNullOrEmpty(p.version))
                return p.id + "@" + p.version;
            return p.id;
        }

        // Polls a package manager request on the editor update loop and invokes onDone when it completes.
        static void WaitFor(Request request, Action onDone)
        {
            void Poll()
            {
                if (!request.IsCompleted) return;
                EditorApplication.update -= Poll;
                onDone();
            }
            EditorApplication.update += Poll;
        }
    }
}
