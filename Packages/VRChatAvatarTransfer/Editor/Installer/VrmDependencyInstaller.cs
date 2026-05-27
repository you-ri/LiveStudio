// Copyright (c) You-Ri, 2026

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace Lilium.VRChatAvatarTransfer.Installer
{
    /// <summary>
    /// Prompts the user to install UniVRM (com.vrmc.gltf / com.vrmc.vrm) via UPM Git URL
    /// when the package is first imported. UniVRM 1.0 is not published to any public
    /// VPM/registry, so VCC cannot resolve it automatically — this installer resolves
    /// it from Editor code that intentionally has no UniVRM references, so it keeps
    /// compiling even while the rest of the package fails to compile because UniVRM
    /// is missing.
    /// </summary>
    [InitializeOnLoad]
    internal static class VrmDependencyInstaller
    {
        const string kSessionFlag = "Lilium.VRChatAvatarTransfer.InstallerChecked";

        struct PackageSpec
        {
            public string Id;
            public string GitUrl;
            public string DisplayName;
        }

        // Versions are kept in lockstep with the corresponding entries that used to
        // live in VRChatAvatarTransfer/package.json. Bump them together when the
        // referenced UniVRM API changes.
        // VRChat Avatar Transfer only consumes VRM 1.0 APIs (UniVRM10 namespace from
        // com.vrmc.vrm). com.vrmc.gltf is its hard dependency and ships UniGLTF +
        // SpringBoneJobs. We do not pull in com.vrmc.univrm (VRM 0.x); jp.lilium.livestudio
        // gates its VRM 0.x branch on a separate VRMC_UNIVRM define so its compile no
        // longer requires the 0.x package.
        static readonly PackageSpec[] kRequired =
        {
            new PackageSpec
            {
                Id = "com.vrmc.gltf",
                GitUrl = "https://github.com/vrm-c/UniVRM.git?path=/Packages/UniGLTF#v0.131.0",
                DisplayName = "UniGLTF (com.vrmc.gltf)",
            },
            new PackageSpec
            {
                Id = "com.vrmc.vrm",
                GitUrl = "https://github.com/vrm-c/UniVRM.git?path=/Packages/VRM10#v0.131.0",
                DisplayName = "VRM 1.0 (com.vrmc.vrm)",
            },
        };

        static ListRequest _listRequest;

        static VrmDependencyInstaller()
        {
            // delayCall lets the domain reload settle and the package manager finish its
            // initial resolve pass before we read the installed list.
            EditorApplication.delayCall += _Check;
        }

        static void _Check()
        {
            if (SessionState.GetBool(kSessionFlag, false)) return;
            SessionState.SetBool(kSessionFlag, true);

            _listRequest = Client.List(offlineMode: true, includeIndirectDependencies: false);
            EditorApplication.update += _WaitForList;
        }

        static void _WaitForList()
        {
            if (!_listRequest.IsCompleted) return;
            EditorApplication.update -= _WaitForList;

            if (_listRequest.Status != StatusCode.Success) return;

            var installed = new HashSet<string>(_listRequest.Result.Select(p => p.name));
            var missing = kRequired.Where(p => !installed.Contains(p.Id)).ToArray();
            if (missing.Length == 0) return;

            var ids = missing.Select(p => p.Id + "@" + p.GitUrl).ToArray();

            // In batch mode (CI / smoke test) install without prompting — EditorUtility.DisplayDialog
            // returns the cancel value in -batchmode, which would otherwise silently skip the install.
            if (Application.isBatchMode)
            {
                Debug.Log("[VRChatAvatarTransfer.Installer] Batch mode detected; installing missing UniVRM packages: " +
                          string.Join(", ", missing.Select(p => p.Id)));
                Client.AddAndRemove(packagesToAdd: ids);
                return;
            }

            var packageList = string.Join("\n", missing.Select(p => "  - " + p.DisplayName));
            var ok = EditorUtility.DisplayDialog(
                "VRChat Avatar Transfer requires UniVRM",
                "The following packages are required but not installed in this project:\n\n" +
                packageList +
                "\n\nInstall them now from the vrm-c/UniVRM Git repository?",
                "Install",
                "Skip");
            if (!ok) return;

            Client.AddAndRemove(packagesToAdd: ids);
        }

        [MenuItem("Tools/VRChat Avatar Transfer/Install UniVRM Dependencies", priority = 9000)]
        static void _ReinstallMenu()
        {
            SessionState.SetBool(kSessionFlag, false);
            _Check();
        }
    }
}
