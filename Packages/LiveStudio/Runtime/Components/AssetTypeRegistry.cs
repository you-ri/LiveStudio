// Copyright (c) You-Ri, 2026

using System;
using System.Collections.Generic;

using Lilium.RemoteControl.LiveScene;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Describes one asset kind the <see cref="AssetTypeRegistry"/> can produce from a file path:
    /// which file suffixes it owns, how to recognize a matching file, and how to create the concrete
    /// <see cref="AssetBase"/> for it.
    /// </summary>
    public sealed class AssetTypeDescriptor
    {
        /// <summary>
        /// Evaluation order. Higher priority descriptors are matched first, so kinds whose suffixes
        /// would otherwise collide (e.g. <c>*.set.lsb</c> before <c>*.avatar.lsb</c>, or
        /// <c>*.preset.json</c> before <c>*.scene.json</c>) win when they sit above the others.
        /// </summary>
        public int priority;

        /// <summary>
        /// File suffixes this kind owns (compound suffixes allowed, e.g. <c>.set.lsb</c>). Used to
        /// strip the display name and, for the default <see cref="matches"/>, to classify a file by
        /// path alone. Never read the file content here — classification must stay content-free so the
        /// project crawler can list files without opening them.
        /// </summary>
        public IReadOnlyList<string> suffixes;

        /// <summary>
        /// Optional custom matcher. When null, a file matches if it ends with any of
        /// <see cref="suffixes"/> (case-insensitive). Supply a custom predicate for kinds whose
        /// detection is not a plain suffix test (e.g. the compound-suffix bundle checks).
        /// </summary>
        public Func<string, bool> matches;

        /// <summary>
        /// Creates the concrete asset (an empty instance; the manager fills id/name/state). Receives the
        /// file path so a kind can branch on content when a single suffix maps to several types — e.g.
        /// a preset resolves to a prop or an avatar by peeking the file.
        /// </summary>
        public Func<string, AssetBase> create;

        /// <summary>
        /// Subfolder (relative to the project folder) that imported files of this kind are copied into,
        /// e.g. "Avatars". Null/empty copies into the project root. Content-free; classification is by
        /// path only, like the rest of this descriptor.
        /// </summary>
        public string importSubfolder;
    }

    /// <summary>
    /// Maps external files to the concrete <see cref="AssetBase"/> kind that loads them, replacing the
    /// hard-coded extension dispatch that used to live in <see cref="ExternalAssetManager"/>. Each kind
    /// registers an <see cref="AssetTypeDescriptor"/>; the manager only asks the registry to
    /// <see cref="Create"/> an asset, test <see cref="IsSupported"/>, or derive a display
    /// <see cref="DeriveName"/>. Adding a new asset kind therefore needs no change to the manager.
    ///
    /// Built-in kinds (prop / avatar / set bundle / live scene / preset) are registered here. External
    /// packages can add their own kinds by calling <see cref="Register"/> from their own initialization
    /// hook; do so after the built-ins are registered (e.g. a <c>RuntimeInitializeOnLoadMethod</c> phase
    /// later than <c>SubsystemRegistration</c>), since registration order is otherwise undefined.
    /// </summary>
    public static class AssetTypeRegistry
    {
        // Registered descriptors, kept sorted by descending priority so the first match wins. Built-in
        // descriptors and their delegates are created once in _RegisterBuiltIns (no per-call alloc).
        private static readonly List<AssetTypeDescriptor> _descriptors = new List<AssetTypeDescriptor>();

        // Re-register built-ins on every domain (re)load and on entering play mode, so the registry is
        // populated whether or not Domain Reload is enabled. InitializeOnLoadMethod also covers the
        // editor crawl, which calls IsSupported before play mode (see ProjectManager).
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#endif
        private static void _Initialize()
        {
            _descriptors.Clear();
            _RegisterBuiltIns();
        }

        /// <summary>
        /// Adds a descriptor and keeps the list sorted by descending priority. Ties keep insertion
        /// order (stable), so same-priority kinds with mutually exclusive matchers are order-independent.
        /// </summary>
        public static void Register(AssetTypeDescriptor descriptor)
        {
            if (descriptor == null || descriptor.create == null)
            {
                UnityEngine.Debug.LogError("[LiveStudio] AssetTypeRegistry.Register: descriptor and create must be non-null.");
                return;
            }

            // Insert before the first descriptor of strictly lower priority (stable for equal priority).
            int i = 0;
            while (i < _descriptors.Count && _descriptors[i].priority >= descriptor.priority) i++;
            _descriptors.Insert(i, descriptor);
        }

        /// <summary>
        /// Creates the concrete asset for the file, or null when no kind matches. The first matching
        /// descriptor (highest priority) wins.
        /// </summary>
        public static AssetBase Create(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return null;
            _EnsureInitialized();

            for (int i = 0; i < _descriptors.Count; i++)
            {
                if (_Matches(_descriptors[i], filePath)) return _descriptors[i].create(filePath);
            }
            return null;
        }

        /// <summary>
        /// True if any registered kind handles the file. Lets the project crawler classify files by path
        /// alone, without reading their contents.
        /// </summary>
        public static bool IsSupported(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return false;
            _EnsureInitialized();

            for (int i = 0; i < _descriptors.Count; i++)
            {
                if (_Matches(_descriptors[i], filePath)) return true;
            }
            return false;
        }

        /// <summary>
        /// Returns the import subfolder for the file's kind (the first matching descriptor's
        /// <see cref="AssetTypeDescriptor.importSubfolder"/>), or null when no kind matches or the kind
        /// imports into the project root. Used to organize imported files by kind under the project folder.
        /// </summary>
        public static string ResolveImportSubfolder(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return null;
            _EnsureInitialized();

            for (int i = 0; i < _descriptors.Count; i++)
            {
                if (_Matches(_descriptors[i], filePath)) return _descriptors[i].importSubfolder;
            }
            return null;
        }

        /// <summary>
        /// Derives a display name by stripping a known asset suffix from the file name, falling back to
        /// the file name without its last extension when none of the registered suffixes match.
        /// </summary>
        public static string DeriveName(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return string.Empty;
            _EnsureInitialized();

            var fileName = System.IO.Path.GetFileName(filePath);
            for (int i = 0; i < _descriptors.Count; i++)
            {
                var suffixes = _descriptors[i].suffixes;
                if (suffixes == null) continue;
                for (int s = 0; s < suffixes.Count; s++)
                {
                    var suffix = suffixes[s];
                    if (!string.IsNullOrEmpty(suffix) &&
                        fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    {
                        return fileName.Substring(0, fileName.Length - suffix.Length);
                    }
                }
            }
            return System.IO.Path.GetFileNameWithoutExtension(fileName);
        }

        // True when the descriptor's custom matcher accepts the file, or — when it has none — when the
        // file ends with one of its suffixes (case-insensitive). Never reads file content.
        private static bool _Matches(AssetTypeDescriptor descriptor, string filePath)
        {
            if (descriptor.matches != null) return descriptor.matches(filePath);

            var suffixes = descriptor.suffixes;
            if (suffixes == null) return false;
            for (int s = 0; s < suffixes.Count; s++)
            {
                var suffix = suffixes[s];
                if (!string.IsNullOrEmpty(suffix) &&
                    filePath.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        // Lazy guard: if the built-ins have not been registered yet (e.g. a static caller runs before the
        // init hooks), register them now. Idempotent because _Initialize clears first.
        private static void _EnsureInitialized()
        {
            if (_descriptors.Count == 0) _RegisterBuiltIns();
        }

        // Registers the built-in kinds. Priorities encode the original dispatch order: preset before the
        // live-scene ".json" files, and the set bundle before the other "*.lsb" bundles.
        private static void _RegisterBuiltIns()
        {
            // Presets (*.preset.json) reference a source asset and reapply state; the kind stored in the
            // file selects prop vs avatar. Highest priority so the ".json" suffix is not misread as a
            // live scene.
            Register(new AssetTypeDescriptor
            {
                priority = 40,
                suffixes = new[] { PropPreset.Extension },
                matches = PropPreset.IsPresetFile,
                create = filePath => PropPreset.PeekKind(filePath) == PropPreset.AssetKind.Avatar
                    ? (AssetBase)new AvatarAsset()
                    : new PropAsset(),
            });

            // Live scenes (*.live.json / *.scene.json) are launcher entries, not loadable resources.
            Register(new AssetTypeDescriptor
            {
                priority = 30,
                suffixes = new[] { ".live.json", ".scene.json" },
                matches = LiveSceneSaveSystem.IsLiveSceneFile,
                create = _ => new LiveSceneAsset(),
            });

            // Set bundles (*.set.lsb, plus the legacy *.scene.lsb accepted on input). Evaluated before
            // the avatar/prop bundles because the compound suffix shares the ".lsb" tail.
            Register(new AssetTypeDescriptor
            {
                priority = 20,
                suffixes = new[] { LiveStudioBundle.SetExtension, LiveStudioBundle.LegacySetExtension },
                matches = LiveStudioBundle.IsSetBundle,
                create = _ => new SetBundleAsset(),
                importSubfolder = "Sets",
            });

            // Avatars (*.avatar.lsb / legacy *.lsavatar / *.vrm).
            Register(new AssetTypeDescriptor
            {
                priority = 10,
                suffixes = new[] { LiveStudioBundle.AvatarExtension, LiveStudioBundle.LegacyAvatarExtension, ".vrm" },
                matches = filePath => LiveStudioBundle.IsAvatarBundle(filePath) ||
                    filePath.EndsWith(".vrm", StringComparison.OrdinalIgnoreCase),
                create = _ => new AvatarAsset(),
                importSubfolder = "Avatars",
            });

            // Props (*.prop.lsb / *.glb / *.gltf). Same priority as avatars; the matchers are mutually
            // exclusive so order between them does not matter.
            Register(new AssetTypeDescriptor
            {
                priority = 10,
                suffixes = new[] { LiveStudioBundle.PropExtension, ".glb", ".gltf" },
                matches = filePath => LiveStudioBundle.IsPropBundle(filePath) ||
                    filePath.EndsWith(".glb", StringComparison.OrdinalIgnoreCase) ||
                    filePath.EndsWith(".gltf", StringComparison.OrdinalIgnoreCase),
                create = _ => new PropAsset(),
                importSubfolder = "Props",
            });
        }
    }
}
