// Copyright (c) You-Ri, 2026

using System;
using System.IO;

using UnityEngine;

using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Keeps each avatar's own settings — its expression config, mesh visibility, animator parameter
    /// overrides, body override clip, layer, lower-body lock — in a preset file that belongs to that
    /// avatar, instead of in the live scene.
    /// <para>
    /// These settings are about one model: which of *its* meshes are hidden, how *its* blend shapes answer
    /// the tracked face. Saved with the scene they were lost the moment another scene was opened, and
    /// carried over onto whichever avatar came next. They are declared <see cref="PersistScope.Custom"/> on
    /// <see cref="AvatarController"/>, which keeps the live scene out of them, and this is the owner that
    /// writes them. Where the file goes is <see cref="AvatarPresetFile"/>'s answer; its shape is the
    /// ordinary preset format (<see cref="PropPreset"/>), holding the delta from the avatar's defaults.
    /// </para>
    /// <para>
    /// There is no save button, as with a deck file: an edit lands in the file shortly after it is made,
    /// and what the controller holds is written for the outgoing avatar whenever the avatar changes and
    /// when the controller is disabled. Writing on the way out is also what carries settings that arrived
    /// without an edit — a live scene saved by an earlier build still restores them — into the avatar's file.
    /// </para>
    /// </summary>
    internal sealed class AvatarPresetStore
    {
        // An edit is written this long after the last one, so dragging a slider does not write every frame.
        private const float kWriteDelaySeconds = 0.5f;

        // The outgoing avatar's file and the source reference recorded in it. Both are taken when the
        // avatar is switched to, so a later project switch cannot redirect its values elsewhere.
        private string _filePath;
        private string _source;
        private string _displayName;

        // The state the file holds. The baseline that keeps an unchanged avatar unwritten.
        private string _writtenState;

        // Time of the first unwritten edit, or negative when there is none.
        private float _dirtySince = -1f;

        // True while applying a file: the apply raises the same change events an edit does.
        private bool _applying;

        /// <summary>The file the current avatar's settings are written to, or null when there is none.</summary>
        public string filePath => _filePath;

        /// <summary>
        /// Writes down what the controller was authored with, as the defaults every avatar's settings are a
        /// difference from. Call before anything can have been applied to it — the live scene's restore
        /// included, since a scene saved by an earlier build still carries these members and reapplying them
        /// here is what carries them over into the avatar's own file.
        /// </summary>
        public void CaptureDefaults(GameObject controllerGO)
        {
            if (controllerGO == null) return;
            AssetStateSnapshot.EnsureScopedDefaults(controllerGO, PersistScope.Custom);
        }

        /// <summary>
        /// Moves the controller over to another avatar: writes what it holds for the outgoing avatar, puts
        /// every avatar-owned member back to its defaults, then applies the incoming avatar's file. An
        /// avatar with no file yet is left at the defaults, which is what it looked like before anyone
        /// touched it.
        /// </summary>
        public void SwitchTo(GameObject controllerGO, string projectPath)
        {
            if (controllerGO == null) return;

            Flush(controllerGO);

            _filePath = AvatarPresetFile.Resolve(projectPath, out _source, out _displayName);
            _writtenState = null;
            _dirtySince = -1f;

            // A controller that came up after the scene did (a set brought its own) has no defaults yet.
            CaptureDefaults(controllerGO);

            _applying = true;
            try
            {
                // The controller is one object driving whichever avatar is out, so the previous avatar's
                // values are still on it. Clearing them first is what makes the incoming file a complete
                // answer rather than an overlay on someone else's settings.
                AssetStateSnapshot.RestoreScopedDefaults(controllerGO, PersistScope.Custom);

                var state = _ReadState(_filePath);
                if (!string.IsNullOrEmpty(state))
                {
                    AssetStateSnapshot.Restore(state, controllerGO);
                    _writtenState = state;
                }
            }
            finally
            {
                _applying = false;
            }
        }

        /// <summary>Notes an edit. It is written once edits have paused (<see cref="Tick"/>).</summary>
        public void MarkDirty()
        {
            if (_applying) return;
            if (_dirtySince < 0f) _dirtySince = Time.unscaledTime;
        }

        /// <summary>Writes a pending edit once it has been left alone for a moment. Call every frame.</summary>
        public void Tick(GameObject controllerGO)
        {
            if (_dirtySince < 0f) return;
            if (Time.unscaledTime - _dirtySince < kWriteDelaySeconds) return;
            Flush(controllerGO);
        }

        /// <summary>
        /// Writes the current avatar's settings when they differ from what its file holds. An avatar left
        /// at its defaults writes no file, and an existing file whose settings were all reset is removed,
        /// so "nothing was changed" does not leave a file saying otherwise.
        /// </summary>
        public void Flush(GameObject controllerGO)
        {
            _dirtySince = -1f;
            if (controllerGO == null || string.IsNullOrEmpty(_filePath)) return;

            var state = AssetStateSnapshot.CaptureDelta(controllerGO, PersistScope.Custom);
            if (string.IsNullOrEmpty(state)) state = string.Empty;
            if (string.Equals(state, _writtenState, StringComparison.Ordinal)) return;

            if (_IsEmptyState(state))
            {
                _Delete(_filePath);
                _writtenState = string.Empty;
                return;
            }

            if (_Write(_filePath, _source, _displayName, state)) _writtenState = state;
        }

        // The state envelope of a preset file, or null when there is no file / it cannot be used.
        private static string _ReadState(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return null;

            string json;
            try
            {
                json = File.ReadAllText(filePath);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogError($"[LiveStudio] Failed to read the avatar settings '{filePath}': {e.Message}");
                return null;
            }

            return PropPreset.TryParse(json, out var data) ? data.state : null;
        }

        private static bool _Write(string filePath, string source, string displayName, string state)
        {
            var json = PropPreset.BuildJson(PropPreset.AssetKind.Avatar, displayName, source, state);
            try
            {
                var dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(filePath, json);
                return true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                // The avatar may sit in a folder this app cannot write to (a read-only medium, someone
                // else's share). Reported once per attempt rather than retried: the settings are still
                // live, they just have nowhere to be kept.
                Debug.LogError($"[LiveStudio] Failed to write the avatar settings '{filePath}': {e.Message}");
                return false;
            }
        }

        private static void _Delete(string filePath)
        {
            try
            {
                if (File.Exists(filePath)) File.Delete(filePath);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogError($"[LiveStudio] Failed to remove the avatar settings '{filePath}': {e.Message}");
            }
        }

        // True when the captured delta carries no value at all — every handle reported only metadata, or
        // the envelope came back without a wrapper or a component in it.
        private static bool _IsEmptyState(string state)
        {
            if (string.IsNullOrEmpty(state)) return true;
            try
            {
                var root = Newtonsoft.Json.Linq.JObject.Parse(state);
                var wrapper = root["wrapper"] as Newtonsoft.Json.Linq.JObject;
                var components = root["components"] as Newtonsoft.Json.Linq.JObject;
                bool hasWrapper = wrapper != null && wrapper.Count > 0;
                bool hasComponents = components != null && components.Count > 0;
                return !hasWrapper && !hasComponents;
            }
            catch
            {
                return false;
            }
        }
    }
}
