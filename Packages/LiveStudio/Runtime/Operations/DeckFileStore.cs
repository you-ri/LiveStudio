// Copyright (c) You-Ri, 2026

using System;
using System.Collections.Generic;
using System.IO;
using Lilium.RemoteControl;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Owns the mapping between decks and the project's files (<c>*.deck.json</c>) — one file is one
    /// deck (one tab).
    /// <para>
    /// Decks are the one part of the operations model that follows "the set of files in the project"
    /// rather than the live scene. Everything that exception needs — which deck is which file, what was
    /// last written, which project the model was built from, and whether the files may be touched at all
    /// — is gathered here instead of being spread through the desk (<see cref="OperationManager"/>)
    /// alongside its other jobs: input, per-frame evaluation, tile placement.
    /// </para>
    /// <para>
    /// There is no save button and no unsaved state: a deck is a file, and every edit lands in it.
    /// </para>
    /// </summary>
    internal sealed class DeckFileStore
    {
        // Deck name -> the file that deck is (absolute path). The one-tab-one-file mapping itself.
        private readonly Dictionary<string, string> _filePaths = new Dictionary<string, string>(StringComparer.Ordinal);

        // Deck name -> the text last written (or read). The baseline that keeps untouched decks unwritten.
        private readonly Dictionary<string, string> _writtenPayloads = new Dictionary<string, string>(StringComparer.Ordinal);

        // The project the current model was built from. A different one rebuilds everything.
        private string _syncedProjectPath;

        // True while applying: the apply restores the desk, which calls back in through the broadcast.
        private bool _applying;

        // True once the desk is running. Tells a bare instance (a unit test, a scene template being
        // deserialized) from a live one, so the former never writes into the user's project.
        private bool _ready;

        internal void SetReady(bool value) => _ready = value;

        /// <summary>Whether the files may be touched: a running desk, and not in the middle of an apply.</summary>
        internal bool isActive => _ready && !_applying;

        /// <summary>
        /// Rebuilds the decks from the project's deck files. This is where tabs come from: every
        /// <c>*.deck.json</c> the project crawl found becomes a deck, named after its file.
        /// <para>
        /// Only the set of files matters. A file already loaded is left alone (its in-memory state,
        /// including a manual hold in the middle of a show, must survive an unrelated crawl); files that
        /// appeared are read, files that disappeared take their deck and its operation sets with them.
        /// Does nothing when there is no difference, so it is safe to call every frame.
        /// </para>
        /// </summary>
        internal void Sync(OperationManager manager)
        {
            if (manager == null || !isActive) return;

            var projectPath = ProjectManager.projectPath;
            bool projectChanged = !string.Equals(projectPath, _syncedProjectPath, StringComparison.Ordinal);
            _syncedProjectPath = projectPath;
            if (projectChanged)
            {
                // Every deck belonged to the previous project. Custom-scope members are invisible to the
                // reset a project switch performs, so drop them here or they follow the user across.
                _filePaths.Clear();
                _writtenPayloads.Clear();
            }

            var desired = _Discover();
            if (!projectChanged && _MatchesLoaded(manager, desired)) return;

            _Apply(manager, desired);
        }

        /// <summary>
        /// Writes every deck whose contents differ from what its file holds. This is the whole save story.
        /// Does nothing on a desk that is not running.
        /// </summary>
        internal void FlushDirty(OperationManager manager)
        {
            if (manager == null || !isActive) return;

            var payloads = _BuildPayloads(manager);
            if (payloads == null) return;

            bool created = false;
            foreach (var pair in payloads)
            {
                if (_writtenPayloads.TryGetValue(pair.Key, out var written) &&
                    string.Equals(written, pair.Value, StringComparison.Ordinal))
                {
                    continue;
                }

                var path = _EnsureFilePath(pair.Key);
                if (string.IsNullOrEmpty(path)) continue;

                bool isNew = !File.Exists(path);
                if (!_WriteFile(path, pair.Value)) continue;
                _writtenPayloads[pair.Key] = pair.Value;
                created |= isNew;
            }

            // A file that did not exist before has to reach the project catalog, or the next crawl would
            // see a deck with no asset entry behind it and drop the tab.
            if (created) ProjectManager.RecrawlProject();
        }

        /// <summary>Adopts the current model as "what the files hold", without writing anything.</summary>
        internal void Rebase(OperationManager manager)
        {
            _writtenPayloads.Clear();
            var payloads = _BuildPayloads(manager);
            if (payloads == null) return;
            foreach (var pair in payloads) _writtenPayloads[pair.Key] = pair.Value;
        }

        /// <summary>Deletes a deck's file and forgets it. The deck itself is removed by the caller.</summary>
        internal void OnDeckRemoved(string deckName)
        {
            if (!isActive) return;
            if (_filePaths.TryGetValue(deckName, out var path) && !string.IsNullOrEmpty(path))
            {
                try
                {
                    if (File.Exists(path)) File.Delete(path);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[LiveStudio] Failed to delete the deck file '{path}': {e.Message}");
                }
            }
            _filePaths.Remove(deckName);
            _writtenPayloads.Remove(deckName);
        }

        /// <summary>
        /// Renames a deck's file to follow the deck's new name. No-op when the deck has no file yet
        /// (renamed before the first write); the file is then created under the new name.
        /// </summary>
        internal void OnDeckRenamed(string fromName, string toName)
        {
            if (!isActive) return;
            if (!_filePaths.TryGetValue(fromName, out var fromPath) || string.IsNullOrEmpty(fromPath))
            {
                return;
            }

            _filePaths.Remove(fromName);
            _writtenPayloads.TryGetValue(fromName, out var written);
            _writtenPayloads.Remove(fromName);

            var dir = Path.GetDirectoryName(fromPath);
            var toPath = string.IsNullOrEmpty(dir)
                ? toName + DeckFile.Extension
                : Path.Combine(dir, toName + DeckFile.Extension);

            try
            {
                if (File.Exists(fromPath)) File.Move(fromPath, toPath);
            }
            catch (Exception e)
            {
                Debug.LogError($"[LiveStudio] Failed to rename the deck file '{fromPath}': {e.Message}");
                return;
            }

            _filePaths[toName] = toPath;
            if (written != null) _writtenPayloads[toName] = written;
        }

        // The deck files the project crawl knows about, as (deck name, absolute path) in tab order.
        // Names come from the file names and are made unique, since two folders may hold the same name.
        private static List<KeyValuePair<string, string>> _Discover()
        {
            var result = new List<KeyValuePair<string, string>>();
            var assets = ExternalAssetManager.current;
            if (assets == null) return result;

            var paths = new List<string>();
            var view = assets.assetsView;
            for (int i = 0; i < view.Count; i++)
            {
                if (view[i] is DeckAsset deck && !string.IsNullOrEmpty(deck.filePath)) paths.Add(deck.filePath);
            }
            // The catalog's order follows the crawl; sort so the tabs do not shuffle between runs.
            paths.Sort(StringComparer.OrdinalIgnoreCase);

            var used = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < paths.Count; i++)
            {
                var name = AssetTypeRegistry.DeriveName(paths[i]);
                if (string.IsNullOrEmpty(name)) name = "Deck";
                var unique = name;
                int n = 2;
                while (!used.Add(unique)) unique = name + " " + (n++);
                result.Add(new KeyValuePair<string, string>(unique, paths[i]));
            }
            return result;
        }

        // True when the loaded decks already are exactly these files, in this order.
        private bool _MatchesLoaded(OperationManager manager, List<KeyValuePair<string, string>> desired)
        {
            if (desired.Count != manager.decks.Count) return false;
            for (int i = 0; i < desired.Count; i++)
            {
                var deck = manager.decks[i];
                if (deck == null || deck.name != desired[i].Key) return false;
                if (!_filePaths.TryGetValue(deck.name, out var path) ||
                    !string.Equals(path, desired[i].Value, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
            return true;
        }

        // Builds the whole model (decks + operation sets) from the given files and applies it in one
        // restore. Going through the serializer rather than mutating the lists keeps a single code path
        // for turning a file's JSON into OperationSet objects — the same one the live scene uses.
        private void _Apply(OperationManager manager, List<KeyValuePair<string, string>> desired)
        {
            var handle = manager.liveObject;
            if (!handle.HasValue) return;

            var current = JObject.Parse(LiveObjectSnapshot.Capture(handle.Value, PersistScope.Custom));
            var currentSets = current["operationSets"] as JArray ?? new JArray();

            var newDecks = new JArray();
            var newSets = new JArray();
            var newPaths = new Dictionary<string, string>(StringComparer.Ordinal);

            for (int i = 0; i < desired.Count; i++)
            {
                var name = desired[i].Key;
                var path = desired[i].Value;

                int columns;
                JArray sets;
                if (_filePaths.TryGetValue(name, out var loadedPath) &&
                    string.Equals(loadedPath, path, StringComparison.OrdinalIgnoreCase))
                {
                    // Already loaded: keep what is in memory rather than re-reading the file.
                    columns = DeckLayout.ColumnsOf(manager.decks, name);
                    sets = SetsOnDeck(currentSets, name);
                }
                else if (!_TryReadFile(path, name, out columns, out sets))
                {
                    // Unreadable file: leave the tab out entirely rather than showing an empty one that
                    // would overwrite the file on the next edit.
                    continue;
                }

                newDecks.Add(new JObject { ["@type"] = "Deck", ["name"] = name, ["columns"] = columns });
                for (int s = 0; s < sets.Count; s++) newSets.Add(sets[s]);
                newPaths[name] = path;
            }

            var payload = new JObject
            {
                ["@type"] = "OperationManager",
                ["operationSets"] = newSets,
                ["decks"] = newDecks,
            };

            _applying = true;
            try
            {
                LiveObjectSnapshot.Restore(payload.ToString(Formatting.None), handle.Value);
            }
            finally
            {
                _applying = false;
            }

            _filePaths.Clear();
            foreach (var pair in newPaths) _filePaths[pair.Key] = pair.Value;

            // What is on disk is what was just applied, so nothing counts as an unwritten edit. Taking the
            // baseline from the model (rather than from the file text) makes it exact: a file written by
            // another build may differ in formatting without differing in content.
            Rebase(manager);

            manager.NotifyDecksRebuilt();
        }

        // Reads one deck file. Returns false (already logged) when it cannot be used.
        private static bool _TryReadFile(string fullPath, string deckName, out int columns, out JArray sets)
        {
            columns = 0;
            sets = null;

            string json;
            try { json = File.ReadAllText(fullPath); }
            catch (Exception e)
            {
                Debug.LogError($"[LiveStudio] Failed to read the deck file '{fullPath}': {e.Message}");
                return false;
            }

            if (!DeckFile.TryParse(json, fullPath, out var fileColumns, out var setsJson)) return false;

            columns = fileColumns > 0 ? fileColumns : DeckLayout.FallbackColumns;
            try { sets = JArray.Parse(setsJson); }
            catch { sets = new JArray(); }
            // The file decides which deck its sets are on; the stored deckName is only a leftover of where
            // they were when written (and is wrong outright for a file copied in from another project).
            for (int i = 0; i < sets.Count; i++)
            {
                if (sets[i] is JObject set && set["control"] is JObject control) control["deckName"] = deckName;
            }
            return true;
        }

        /// <summary>The serialized sets placed on one deck, in order.</summary>
        internal static JArray SetsOnDeck(JArray sets, string deckName)
        {
            var result = new JArray();
            for (int i = 0; i < sets.Count; i++)
            {
                if (sets[i] is JObject set &&
                    set["control"] is JObject control &&
                    string.Equals(control["deckName"]?.Value<string>(), deckName, StringComparison.Ordinal))
                {
                    result.Add(set);
                }
            }
            return result;
        }

        // The file text each deck would be written as, keyed by deck name.
        private static Dictionary<string, string> _BuildPayloads(OperationManager manager)
        {
            if (manager == null) return null;
            var handle = manager.liveObject;
            if (!handle.HasValue) return null;

            var current = JObject.Parse(LiveObjectSnapshot.Capture(handle.Value, PersistScope.Custom));
            var sets = current["operationSets"] as JArray ?? new JArray();

            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < manager.decks.Count; i++)
            {
                var deck = manager.decks[i];
                if (deck == null || string.IsNullOrEmpty(deck.name)) continue;
                result[deck.name] = DeckFile.BuildJson(
                    deck.columns, SetsOnDeck(sets, deck.name).ToString(Formatting.None));
            }
            return result;
        }

        // The file a deck is, creating the mapping for a deck that does not have one yet (added from the
        // remote app). Null when no project is open to write into.
        private string _EnsureFilePath(string deckName)
        {
            if (_filePaths.TryGetValue(deckName, out var known) && !string.IsNullOrEmpty(known)) return known;

            var projectPath = ProjectManager.projectPath;
            if (string.IsNullOrEmpty(projectPath))
            {
                Debug.LogError("[LiveStudio] No project folder is open to write the deck into.");
                return null;
            }

            var path = Path.Combine(projectPath, DeckFile.Subfolder, deckName + DeckFile.Extension);
            _filePaths[deckName] = path;
            return path;
        }

        private static bool _WriteFile(string fullPath, string json)
        {
            try
            {
                var dir = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(fullPath, json);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[LiveStudio] Failed to write the deck file '{fullPath}': {e.Message}");
                return false;
            }
        }
    }
}
