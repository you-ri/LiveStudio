// Copyright (c) You-Ri, 2026

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace Lilium.LiveStudio.Virgo.Editor
{
    /// <summary>
    /// Tracks the install state of optional LiveStudio + VirgoMotion packages and installs them on
    /// demand. The package list is data-driven from <see cref="Readme.optionalPackages"/>.
    ///
    /// These are not hard dependencies: when missing, the relevant features are compiled out via the
    /// versionDefines declared in Lilium.LiveStudio.asmdef (KEIJIRO_KLAK_SPOUT / VRMC_VRM10 /
    /// HECOMI_UOSC). Packages are pulled from a scoped registry (OpenUPM by default); the registry
    /// scope is ensured in the project manifest before the package is added.
    ///
    /// The scoped-registry APIs on UnityEditor.PackageManager.Client are internal, so the registry
    /// is registered by editing Packages/manifest.json directly. Only the "scopedRegistries" array
    /// is rewritten; the "dependencies" map and everything else are preserved byte-for-byte.
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
                    Debug.LogError("[Studio] Failed to list installed packages: " + list.Error?.message);
                onDone?.Invoke();
            });
        }

        /// <summary>
        /// Ensures the required scoped registries exist in the manifest and installs the given
        /// packages. Packages already installed are skipped. <paramref name="onDone"/> is invoked on
        /// the editor main thread once the operation resolves.
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
            EnsureRegistries(targets);
            AddPackages(targets, () =>
            {
                _busy = false;
                // Invalidate so the next refresh reflects the new state.
                _installed = null;
                RefreshInstalled(onDone);
            });
        }

        static void AddPackages(List<Readme.OptionalPackage> targets, Action onDone)
        {
            var ids = targets
                .Select(p => string.IsNullOrEmpty(p.version) ? p.id : p.id + "@" + p.version)
                .ToArray();
            var add = Client.AddAndRemove(packagesToAdd: ids);
            WaitFor(add, () =>
            {
                if (add.Status != StatusCode.Success)
                    Debug.LogError("[Studio] Failed to install optional packages: " + add.Error?.message);
                onDone();
            });
        }

        [Serializable]
        class ScopedRegistry
        {
            public string name;
            public string url;
            public string[] scopes;
        }

        [Serializable]
        class RegistryList
        {
            public ScopedRegistry[] registries;
        }

        static string ManifestPath =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "../Packages/manifest.json"));

        // Adds the scoped registries required by the targets to manifest.json. A scope already
        // declared by any registry is left untouched (re-declaring it elsewhere breaks resolution).
        // Best-effort: failures are logged and the install proceeds.
        static void EnsureRegistries(List<Readme.OptionalPackage> targets)
        {
            var groups = targets
                .Where(p => !string.IsNullOrEmpty(p.registryUrl) && !string.IsNullOrEmpty(p.scope))
                .GroupBy(p => p.registryUrl)
                .ToList();
            if (groups.Count == 0) return;

            string text;
            try
            {
                text = File.ReadAllText(ManifestPath);
            }
            catch (Exception e)
            {
                Debug.LogError("[Studio] Failed to read manifest.json: " + e.Message);
                return;
            }

            var hasArray = TryGetScopedRegistriesArray(text, out int arrStart, out int arrEnd, out var registries);
            // The key exists but could not be parsed: abort rather than risk writing a duplicate key.
            if (arrStart >= 0 && registries == null)
                return;

            var list = registries ?? new List<ScopedRegistry>();

            // A scope already declared by ANY registry resolves those packages already. Adding the
            // same scope to a second registry makes Unity reject the manifest with a "defined by more
            // than one registry" error, so we must never re-add an existing scope. (e.g. jp.keijiro /
            // com.hecomi are commonly provided by a npmjs scoped registry already.)
            var existingScopes = new HashSet<string>(
                list.Where(r => r.scopes != null).SelectMany(r => r.scopes));

            bool changed = false;
            foreach (var group in groups)
            {
                var url = group.Key;
                var name = group.First().registryName;
                var newScopes = group.Select(p => p.scope).Distinct()
                    .Where(s => existingScopes.Add(s)) // skip scopes any registry already declares
                    .ToList();
                if (newScopes.Count == 0)
                    continue;

                var reg = list.FirstOrDefault(r => r.url == url);
                if (reg == null)
                {
                    list.Add(new ScopedRegistry
                    {
                        name = string.IsNullOrEmpty(name) ? "Scoped Registry" : name,
                        url = url,
                        scopes = newScopes.ToArray(),
                    });
                }
                else
                {
                    reg.scopes = (reg.scopes ?? Array.Empty<string>()).Concat(newScopes).ToArray();
                }
                changed = true;
            }
            if (!changed) return;

            var arrayJson = SerializeRegistries(list);
            string newText;
            if (hasArray)
            {
                newText = text.Substring(0, arrStart) + arrayJson + text.Substring(arrEnd + 1);
            }
            else
            {
                var brace = text.IndexOf('{');
                if (brace < 0)
                {
                    Debug.LogError("[Studio] manifest.json is not a JSON object; cannot add scoped registry.");
                    return;
                }
                newText = text.Substring(0, brace + 1) +
                          "\n  \"scopedRegistries\": " + arrayJson + "," +
                          text.Substring(brace + 1);
            }

            try
            {
                File.WriteAllText(ManifestPath, newText);
            }
            catch (Exception e)
            {
                Debug.LogError("[Studio] Failed to write manifest.json: " + e.Message);
            }
        }

        // Locates the "scopedRegistries" array in the manifest text and parses it. Returns true when
        // the array exists; arrStart/arrEnd are the indices of the enclosing '[' and ']' (inclusive),
        // or -1 when absent. registries is null when the key is present but parsing failed.
        static bool TryGetScopedRegistriesArray(string text, out int arrStart, out int arrEnd, out List<ScopedRegistry> registries)
        {
            arrStart = -1;
            arrEnd = -1;
            registries = null;

            var key = text.IndexOf("\"scopedRegistries\"", StringComparison.Ordinal);
            if (key < 0)
                return false;

            var open = text.IndexOf('[', key);
            if (open < 0)
                return false;

            int depth = 0;
            bool inString = false, escaped = false;
            for (int j = open; j < text.Length; j++)
            {
                char c = text[j];
                if (inString)
                {
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == '"') inString = false;
                    continue;
                }
                if (c == '"') inString = true;
                else if (c == '[') depth++;
                else if (c == ']')
                {
                    depth--;
                    if (depth == 0)
                    {
                        arrStart = open;
                        arrEnd = j;
                        break;
                    }
                }
            }
            if (arrStart < 0)
                return false; // unterminated array; treat as absent so the caller aborts safely

            var sub = text.Substring(arrStart, arrEnd - arrStart + 1);
            try
            {
                var parsed = JsonUtility.FromJson<RegistryList>("{\"registries\":" + sub + "}");
                registries = parsed?.registries != null
                    ? new List<ScopedRegistry>(parsed.registries)
                    : new List<ScopedRegistry>();
            }
            catch (Exception e)
            {
                Debug.LogError("[Studio] Failed to parse scopedRegistries in manifest.json: " + e.Message);
                registries = null;
            }
            return true;
        }

        static string SerializeRegistries(List<ScopedRegistry> list)
        {
            var json = JsonUtility.ToJson(new RegistryList { registries = list.ToArray() });
            // Extract the array payload from {"registries":[...]}.
            var open = json.IndexOf('[');
            var close = json.LastIndexOf(']');
            return json.Substring(open, close - open + 1);
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
