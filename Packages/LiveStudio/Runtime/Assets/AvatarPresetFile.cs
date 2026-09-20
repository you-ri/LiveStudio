// Copyright (c) You-Ri, 2026

using System;
using System.IO;

using UnityEngine;

using Lilium.RemoteControl.LiveScene;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Where an avatar's own settings are kept: a preset file (<c>*.preset.json</c>) that belongs to one
    /// avatar and is written by the app rather than by the operator (see <see cref="AvatarPresetStore"/>).
    /// <para>
    /// For an avatar that is a file, the settings sit beside it under the same base name —
    /// <c>Avatars/hoge.vrm</c> is answered by <c>Avatars/hoge.preset.json</c> — so copying the avatar
    /// somewhere else and taking its settings along is moving two files that already look like a pair.
    /// An avatar with no file of its own (built in, or the scene's own avatar) is answered from
    /// <c>{project}/Settings/AvatarPresets/</c>, keyed by the catalog GUID the app knows it by.
    /// </para>
    /// <para>
    /// Such a file is NOT an entry of its own in the project: it is the avatar beside it, and listing both
    /// would show one avatar twice. <see cref="IsAutoPreset"/> is what says so, and it decides from the
    /// path alone — the project crawl reads no file contents (see <c>ProjectManager._Crawl</c>).
    /// </para>
    /// </summary>
    internal static class AvatarPresetFile
    {
        /// <summary>Folder under the project holding the settings of avatars that have no file of their own.</summary>
        public const string SettingsSubDir = "AvatarPresets";

        /// <summary>Key of the avatar the controller shows when no avatar asset is selected.</summary>
        public const string DefaultAvatarName = "default";

        // The avatar file kinds a sibling preset can belong to. Same set the avatar asset kind registers
        // (AssetTypeRegistry), listed here because this asks about a path, not about an entry.
        private static readonly string[] kAvatarSuffixes =
        {
            LiveStudioBundle.AvatarExtension,
            LiveStudioBundle.LegacyAvatarExtension,
            ".vrm",
        };

        /// <summary>
        /// The settings file of the avatar that is out, or null when no project is open to keep it in.
        /// <paramref name="source"/> is how the file names its avatar (relative to the file where it can
        /// be, so an avatar and its settings survive being copied together), and
        /// <paramref name="displayName"/> is what to call it when someone opens the file.
        /// <para>
        /// Which avatar is out is the asset layer's answer, not the controller's: the controller drives
        /// whichever model was handed to it and does not know where it came from.
        /// </para>
        /// </summary>
        public static string Resolve(string projectPath, out string source, out string displayName)
        {
            source = string.Empty;
            displayName = string.Empty;
            if (string.IsNullOrEmpty(projectPath)) return null;

            switch (ExternalAssetManager.current?.selectedExclusive)
            {
                case BuiltinAvatarAsset builtin when !string.IsNullOrEmpty(builtin.guid):
                    // Built into the app: no file of its own to sit beside, and the GUID is the identity
                    // the catalog and the live scene already use for it.
                    displayName = builtin.name ?? string.Empty;
                    return ForBuiltin(projectPath, builtin.guid);

                case AvatarAsset file:
                    var avatarPath = ResolveAvatarFilePath(file);
                    if (string.IsNullOrEmpty(avatarPath)) break;
                    displayName = AssetTypeRegistry.DeriveName(Path.GetFileName(avatarPath));
                    if (ExternalAssetManager.IsInsideProject(avatarPath, projectPath))
                    {
                        source = Path.GetFileName(avatarPath);
                        return ForAvatarFile(avatarPath);
                    }
                    source = avatarPath.Replace('\\', '/');
                    return ForOutsideProject(projectPath, avatarPath);
            }

            displayName = DefaultAvatarName;
            return ForDefaultAvatar(projectPath);
        }

        /// <summary>
        /// The avatar file an entry stands for: the referenced avatar for a preset entry, the entry's own
        /// file otherwise. Read from the preset file rather than from the entry's resolved source, because
        /// that is only filled in once the preset has been loaded — and the first avatar of a session is
        /// resolved while its load is still on the way.
        /// </summary>
        public static string ResolveAvatarFilePath(AvatarAsset asset)
        {
            var path = asset?.filePath;
            if (string.IsNullOrEmpty(path) || !PropPreset.IsPresetFile(path)) return path;

            if (!string.IsNullOrEmpty(asset.sourceFilePath) && !PropPreset.IsPresetFile(asset.sourceFilePath))
            {
                return asset.sourceFilePath;
            }

            try
            {
                if (PropPreset.TryParse(File.ReadAllText(path), out var data) && !string.IsNullOrEmpty(data.source))
                {
                    return PropPreset.ResolveSource(data.source, Path.GetDirectoryName(path));
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogError($"[LiveStudio] Failed to read the preset '{path}': {e.Message}");
            }
            return null;
        }

        /// <summary>The settings file of the avatar file <paramref name="avatarFilePath"/>: beside it, same base name.</summary>
        public static string ForAvatarFile(string avatarFilePath)
        {
            if (string.IsNullOrEmpty(avatarFilePath)) return null;
            var dir = Path.GetDirectoryName(avatarFilePath);
            var baseName = AssetTypeRegistry.DeriveName(Path.GetFileName(avatarFilePath));
            if (string.IsNullOrEmpty(baseName)) return null;
            return string.IsNullOrEmpty(dir)
                ? baseName + PropPreset.Extension
                : Path.Combine(dir, baseName + PropPreset.Extension);
        }

        /// <summary>The settings file of a built-in avatar, keyed by its catalog GUID.</summary>
        public static string ForBuiltin(string projectPath, string guid)
            => _InSettings(projectPath, PropPreset.SanitizeFileName(guid));

        /// <summary>The settings file of the avatar the scene itself carries (no avatar asset selected).</summary>
        public static string ForDefaultAvatar(string projectPath)
            => _InSettings(projectPath, DefaultAvatarName);

        /// <summary>
        /// The settings file of an avatar file outside the project folder. Kept in the project rather than
        /// beside the avatar, because that folder is somebody else's to write into. The path is hashed into
        /// the name so two avatars of the same name in different folders stay apart.
        /// </summary>
        public static string ForOutsideProject(string projectPath, string avatarFilePath)
        {
            var baseName = AssetTypeRegistry.DeriveName(Path.GetFileName(avatarFilePath ?? string.Empty));
            if (string.IsNullOrEmpty(baseName)) baseName = "avatar";
            return _InSettings(projectPath, PropPreset.SanitizeFileName($"{baseName}-{_PathHash(avatarFilePath)}"));
        }

        /// <summary>
        /// Whether this preset file is an avatar's own settings rather than an entry of its own: it sits
        /// beside an avatar file of the same base name, or in the project's <see cref="SettingsSubDir"/>.
        /// Decided from the path alone, so the project crawl can skip it without reading it.
        /// </summary>
        public static bool IsAutoPreset(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !PropPreset.IsPresetFile(filePath)) return false;

            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) &&
                string.Equals(Path.GetFileName(dir), SettingsSubDir, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var baseName = AssetTypeRegistry.DeriveName(Path.GetFileName(filePath));
            if (string.IsNullOrEmpty(baseName)) return false;

            for (int i = 0; i < kAvatarSuffixes.Length; i++)
            {
                var sibling = string.IsNullOrEmpty(dir)
                    ? baseName + kAvatarSuffixes[i]
                    : Path.Combine(dir, baseName + kAvatarSuffixes[i]);
                if (File.Exists(sibling)) return true;
            }
            return false;
        }

        private static string _InSettings(string projectPath, string baseName)
        {
            if (string.IsNullOrEmpty(projectPath)) return null;
            return Path.Combine(
                StartupStateStore.GetStateDir(projectPath), SettingsSubDir, baseName + PropPreset.Extension);
        }

        // Short, stable, case-insensitive hash of a full path. Only has to keep two avatars apart, so a
        // plain FNV-1a over the normalized path is enough; it is never parsed back.
        private static string _PathHash(string path)
        {
            var normalized = (path ?? string.Empty).Replace('\\', '/').ToLowerInvariant();
            uint hash = 2166136261;
            for (int i = 0; i < normalized.Length; i++)
            {
                hash = (hash ^ normalized[i]) * 16777619;
            }
            return hash.ToString("x8");
        }
    }
}
