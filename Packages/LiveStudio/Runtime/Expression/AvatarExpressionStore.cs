// Copyright (c) You-Ri, 2026

using System;
using System.IO;

using UnityEngine;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Lilium.RemoteControl;
using Lilium.RemoteControl.LiveScene;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Keeps each avatar's expression settings in the project rather than in the live scene:
    /// <c>{projectPath}/Settings/AvatarExpressions.json</c>, one entry per avatar.
    /// <para>
    /// The settings are a tuning of one avatar (how its blend shapes answer the tracked face), so they
    /// follow the avatar across scenes and stay out of takes. <see cref="AvatarExpressionConfig"/>
    /// declares its members <see cref="PersistScope.Custom"/>, which keeps the live scene from writing
    /// them; this is the owner that does.
    /// </para>
    /// <para>
    /// The entry is keyed by the avatar, not by the config: every avatar shares the one config instance
    /// its <see cref="AvatarController"/> cloned, and the shipped default config asset is shared by every
    /// avatar that has not been given its own, so neither the instance nor the asset tells avatars apart.
    /// See <see cref="ResolveAvatarKey"/>.
    /// </para>
    /// <para>
    /// Like a deck file there is no save button: an edit lands in the file shortly after it is made, and
    /// whatever the config holds is written for the outgoing avatar when the avatar is switched and when
    /// the controller is disabled. Writing on the way out is what also catches values that arrived without
    /// an edit -- a live scene saved by an earlier build still carries these members and restores them.
    /// </para>
    /// </summary>
    internal sealed class AvatarExpressionStore
    {
        public const string FormatIdentifier = "jp.lilium.livestudio.avatarexpressions";
        public const int CurrentFormatVersion = 1;
        private const int kMinSupportedVersion = 1;

        // Not "*.settings.json": the project settings store applies and prunes every file of that shape.
        private const string kFileName = "AvatarExpressions.json";

        /// <summary>Key of the avatar the controller shows when no avatar asset is selected.</summary>
        public const string DefaultAvatarKey = "default";

        private const string kBuiltinKeyPrefix = "builtin:";

        // An edit is written this long after the last one, so dragging a slider does not write every frame.
        private const float kWriteDelaySeconds = 0.5f;

        // The avatar whose settings the config currently holds, and the project they belong to. Both are
        // taken when the avatar is switched to, so a later project switch cannot redirect the outgoing
        // avatar's values into the incoming project.
        private string _avatarKey;
        private string _projectPath;

        // What the file holds for _avatarKey. The baseline that keeps an unchanged config unwritten.
        private string _writtenPayload;

        // Time of the first unwritten edit, or negative when there is none.
        private float _dirtySince = -1f;

        // True while restoring an entry: the restore raises the same change events an edit does.
        private bool _applying;

        public string avatarKey => _avatarKey;

        /// <summary>
        /// Moves the config over to another avatar: writes what it holds for the outgoing avatar, then
        /// restores the incoming avatar's entry. An avatar with no entry yet keeps the current values and
        /// is written with them, which is also how settings restored from an earlier build's live scene
        /// reach the project.
        /// </summary>
        /// <param name="projectPath">The open project (<see cref="ProjectManager.projectPath"/>). Empty
        /// when none is open, in which case nothing is read or written.</param>
        public void SwitchTo(string newAvatarKey, string projectPath, AvatarExpressionConfig config)
        {
            if (config == null || string.IsNullOrEmpty(newAvatarKey)) return;

            Flush(config);

            _avatarKey = newAvatarKey;
            _projectPath = projectPath;
            _writtenPayload = null;
            _dirtySince = -1f;

            // No project folder is open: there is nowhere to read from or write to.
            if (string.IsNullOrEmpty(_projectPath)) return;

            var entry = _ReadEntry(_projectPath, _avatarKey);
            if (entry == null)
            {
                Flush(config);
                return;
            }

            var handle = _HandleOf(config);
            _applying = true;
            try
            {
                LiveObjectSnapshot.Restore(entry.ToString(Formatting.None), handle);
            }
            finally
            {
                _applying = false;
            }

            // Taken from the config rather than from the file text: a file written by another build may
            // differ in formatting without differing in content.
            _writtenPayload = _Capture(handle);
        }

        /// <summary>Notes an edit to the config. It is written once edits have paused (<see cref="Tick"/>).</summary>
        public void MarkDirty()
        {
            if (_applying) return;
            if (_dirtySince < 0f) _dirtySince = Time.unscaledTime;
        }

        /// <summary>Writes a pending edit once it has been left alone for a moment. Call every frame.</summary>
        public void Tick(AvatarExpressionConfig config)
        {
            if (_dirtySince < 0f) return;
            if (Time.unscaledTime - _dirtySince < kWriteDelaySeconds) return;
            Flush(config);
        }

        /// <summary>Writes the config for the current avatar when it differs from what the file holds.</summary>
        public void Flush(AvatarExpressionConfig config)
        {
            _dirtySince = -1f;
            if (config == null || string.IsNullOrEmpty(_avatarKey) || string.IsNullOrEmpty(_projectPath)) return;

            var payload = _Capture(_HandleOf(config));
            if (string.Equals(payload, _writtenPayload, StringComparison.Ordinal)) return;

            if (_WriteEntry(_projectPath, _avatarKey, payload)) _writtenPayload = payload;
        }

        /// <summary>
        /// The key of the avatar that is out: the project-relative path of its source file (a preset
        /// resolves to the avatar file it references, since a preset is that avatar with other
        /// settings), the catalog GUID of a built-in avatar, or <see cref="DefaultAvatarKey"/> when no
        /// avatar asset is selected.
        /// </summary>
        public static string ResolveAvatarKey()
        {
            switch (ExternalAssetManager.current?.selectedExclusive)
            {
                case BuiltinAvatarAsset builtin when !string.IsNullOrEmpty(builtin.guid):
                    return kBuiltinKeyPrefix + builtin.guid;

                case AvatarAsset file:
                    var source = !string.IsNullOrEmpty(file.sourceFilePath) ? file.sourceFilePath : file.filePath;
                    if (string.IsNullOrEmpty(source)) break;
                    return PropPreset.Relativize(source, ProjectManager.projectPath).Replace('\\', '/');
            }
            return DefaultAvatarKey;
        }

        /// <summary>Absolute path of <c>{projectPath}/Settings/AvatarExpressions.json</c>.</summary>
        public static string GetFilePath(string projectPath)
            => Path.Combine(StartupStateStore.GetStateDir(projectPath), kFileName);

        private static LiveObjectHandle _HandleOf(AvatarExpressionConfig config)
            => LiveObjectRegistry.GetOrCreateWithoutId(LiveClass.Get<AvatarExpressionConfig>(), config);

        private static string _Capture(LiveObjectHandle handle)
            => LiveObjectSnapshot.Capture(handle, PersistScope.Custom);

        private static JObject _ReadEntry(string projectPath, string avatarKey)
        {
            var root = _ReadFile(GetFilePath(projectPath));
            return root?["avatars"]?[avatarKey] as JObject;
        }

        // The file's root, or null when it does not exist or cannot be used (already logged).
        private static JObject _ReadFile(string fullPath)
        {
            if (!File.Exists(fullPath)) return null;

            JObject root;
            try
            {
                root = JObject.Parse(File.ReadAllText(fullPath));
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is JsonException)
            {
                Debug.LogError($"[LiveStudio] Failed to read the avatar expression settings '{fullPath}': {e.Message}");
                return null;
            }

            if (!FormatHeader.TryReadVersion(root, "Avatar expression settings", CurrentFormatVersion, kMinSupportedVersion, out _)) return null;
            return root;
        }

        // Read-modify-write, so the other avatars' entries are kept.
        private static bool _WriteEntry(string projectPath, string avatarKey, string payload)
        {
            var fullPath = GetFilePath(projectPath);

            // An existing file this build cannot read is left alone rather than replaced by one holding a
            // single avatar.
            var root = _ReadFile(fullPath);
            if (root == null && File.Exists(fullPath)) return false;
            root ??= new JObject();

            FormatHeader.Write(root, FormatIdentifier, CurrentFormatVersion);
            if (!(root["avatars"] is JObject avatars))
            {
                avatars = new JObject();
                root["avatars"] = avatars;
            }
            avatars[avatarKey] = JObject.Parse(payload);

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
                File.WriteAllText(fullPath, root.ToString(Formatting.Indented));
                return true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogError($"[LiveStudio] Failed to write the avatar expression settings '{fullPath}': {e.Message}");
                return false;
            }
        }
    }
}
