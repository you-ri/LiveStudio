// Copyright (c) You-Ri, 2026

using System;
using System.IO;

using UnityEngine;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// File format helpers for deck files (<c>*.deck.json</c>).
    ///
    /// <b>One file is one deck</b> — one tab of the remote app's operations page. Every deck file in the
    /// project folder shows up as a tab, so a deck can be added, copied and removed as a file, and there is
    /// no "which deck file is open" state to keep anywhere.
    ///
    /// <para>Structure:</para>
    /// <code>
    /// {
    ///   "format": "jp.lilium.livestudio.deck",
    ///   "formatVersion": 1,
    ///   "columns": 8,
    ///   "operationSets": [ { "@type": "OperationSet", ... }, ... ]
    /// }
    /// </code>
    ///
    /// <para>The deck's name is <b>not</b> in the file: the file name is the name (see
    /// <see cref="AssetTypeRegistry.DeriveName"/>), so renaming a tab renames the file and there is no second
    /// source of truth to keep in step. <see cref="DeckControl.deckName"/> inside the sets is likewise
    /// redundant once the file is known, and is rewritten from the file name on load.</para>
    ///
    /// <para>Versioning follows the shared <see cref="FormatHeader"/> policy (missing = min, below min =
    /// reject, above current = best-effort), so a deck written by a newer build still opens.</para>
    /// </summary>
    public static class DeckFile
    {
        /// <summary>Compound suffix of a deck file (case-insensitive).</summary>
        public const string Extension = ".deck.json";

        /// <summary>Project subfolder deck files are created in.</summary>
        public const string Subfolder = "Decks";

        /// <summary>Format discriminator stored in the <c>format</c> field.</summary>
        public const string FormatId = "jp.lilium.livestudio.deck";

        /// <summary>Current file format version written by <see cref="BuildJson"/>.</summary>
        public const int CurrentFormatVersion = 1;

        /// <summary>
        /// Oldest format version this reader still understands. Files below this are rejected; files at or
        /// above <see cref="CurrentFormatVersion"/> are read best-effort (forward tolerance). Prefer a new
        /// <see cref="FormatId"/> over bumping this for an incompatible break.
        /// </summary>
        public const int MinSupportedVersion = 1;

        /// <summary>True if <paramref name="path"/> names a deck file (<c>*.deck.json</c>).</summary>
        public static bool IsDeckFile(string path)
            => !string.IsNullOrEmpty(path) && path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Builds the deck file JSON for one deck. <paramref name="operationSetsJson"/> is the serialized
        /// array of the sets placed on it; it is embedded as parsed JSON (not an escaped string) so the file
        /// stays readable and diffable.
        /// </summary>
        public static string BuildJson(int columns, string operationSetsJson)
        {
            JArray sets;
            if (string.IsNullOrEmpty(operationSetsJson))
            {
                sets = new JArray();
            }
            else
            {
                try { sets = JArray.Parse(operationSetsJson); }
                catch { sets = new JArray(); }
            }

            var root = new JObject();
            FormatHeader.Write(root, FormatId, CurrentFormatVersion);
            root["columns"] = columns;
            root["operationSets"] = sets;
            return root.ToString(Formatting.Indented);
        }

        /// <summary>
        /// Parses deck file JSON. Returns false (and logs) if the content is unparseable, is not a deck file,
        /// or predates <see cref="MinSupportedVersion"/>. <paramref name="label"/> names the file in the log.
        /// <paramref name="columns"/> is the deck's grid width and <paramref name="operationSetsJson"/> the
        /// serialized array of the sets placed on it. The deck's name is the file's, so it is not returned.
        /// </summary>
        public static bool TryParse(string json, string label, out int columns, out string operationSetsJson)
        {
            columns = 0;
            operationSetsJson = null;
            if (string.IsNullOrEmpty(json))
            {
                Debug.LogError($"[LiveStudio] Deck file is empty: '{label}'.");
                return false;
            }

            JObject root;
            try { root = JObject.Parse(json); }
            catch (Exception e)
            {
                Debug.LogError($"[LiveStudio] Failed to parse the deck file '{label}': {e.Message}");
                return false;
            }

            var format = root["format"]?.Value<string>();
            if (!string.Equals(format, FormatId, StringComparison.Ordinal))
            {
                Debug.LogError($"[LiveStudio] Not a deck file (format='{format}'): '{label}'.");
                return false;
            }

            if (!FormatHeader.TryReadVersion(root, "Deck", CurrentFormatVersion, MinSupportedVersion, out _)) return false;

            // A file from the first (single-file-holds-every-deck) shape carries "state" instead. It never
            // shipped, so it is reported and skipped rather than converted — silently rewriting a user's file
            // is worse than telling them which one to delete.
            if (root["operationSets"] == null && root["state"] != null)
            {
                Debug.LogError(
                    $"[LiveStudio] '{label}' uses the previous deck format (one file held every deck). " +
                    "A deck file is now one deck; delete this file and rebuild the deck.");
                return false;
            }

            columns = root["columns"]?.Value<int>() ?? 0;
            operationSetsJson = (root["operationSets"] as JArray ?? new JArray()).ToString(Formatting.None);
            return true;
        }

        /// <summary>
        /// Replaces characters not allowed in file names with '_', returning a non-empty fallback when the
        /// input reduces to nothing. Deck names become file names, so this runs on every rename.
        /// </summary>
        public static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Deck";
            var invalid = Path.GetInvalidFileNameChars();
            var chars = name.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (Array.IndexOf(invalid, chars[i]) >= 0) chars[i] = '_';
            }
            var result = new string(chars).Trim();
            return string.IsNullOrEmpty(result) ? "Deck" : result;
        }
    }
}
