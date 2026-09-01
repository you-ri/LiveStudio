// Copyright (c) You-Ri, 2026
using UnityEditor;
using UnityEngine;

namespace Lilium.RemoteControl.Editor
{
    /// <summary>
    /// The words the Lilium Remote Control editor windows are written in.
    ///
    /// The same <see cref="LocalizationSystem"/> the application and the remote app share, so a
    /// window follows whatever language was chosen there rather than holding one of its own -- one
    /// setting, one answer to "what language is this in".
    ///
    /// The files, though, are the editor's own and live outside <c>Resources</c>: these strings
    /// describe windows that exist only in the editor, and a player build has no use for the bytes.
    /// They are read through the asset database from a package-relative path, the way the shared
    /// stylesheet is, so a window never spells out where the package is installed.
    /// </summary>
    internal static class RemoteControlEditorLocalization
    {
        private const string kPackageRoot = "Packages/jp.lilium.remotecontrol/";
        private const string kLocaleFolder = "Editor/Localization/RemoteControlEditorLocales/";

        /// <summary>The language the text is written in, and what every other one falls back to.</summary>
        private const string kSourceLanguage = "en";

        private static readonly string[] kLanguages = { kSourceLanguage, "ja", "zh-CN" };

        private static int _loadedGeneration = -1;
        private static bool _missingReported;

        /// <summary>
        /// Bumped whenever the text these windows are drawn with could have changed.
        ///
        /// A window resolves a label once, when it builds the row it sits in, so this is what tells
        /// it to build that row again. Folded into whatever the window already compares to decide
        /// that, beside the font generation.
        /// </summary>
        public static int generation
        {
            get
            {
                EnsureLoaded();
                return LocalizationSystem.generation;
            }
        }

        /// <summary>The text for a key, in the active language, or English, or the key itself.</summary>
        public static string Tr(string key)
        {
            EnsureLoaded();

            if (LocalizationSystem.TryTranslate(LocalizationSystem.currentLanguage, key, out var text))
            {
                return text;
            }

            // English rather than the key: a window nobody has translated yet is still readable, and
            // a window full of LDS_ tokens is not.
            return LocalizationSystem.TryTranslate(kSourceLanguage, key, out var source) ? source : key;
        }

        /// <summary>The text for a key that carries values, with the values filled in.</summary>
        public static string Tr(string key, params object[] args)
        {
            var text = Tr(key);

            return args == null || args.Length == 0 ? text : string.Format(text, args);
        }

        /// <summary>
        /// Registers the editor locales, if they are not registered already.
        ///
        /// Called before every read rather than only at load: entering play mode empties the table
        /// -- <see cref="LocalizationSystem"/> resets its statics for domain-reload-off, and only
        /// the runtime initializer puts its own files back -- and an editor window left open across
        /// that would otherwise draw itself in keys from then on. The generation makes the check a
        /// comparison of two integers, so it costs nothing to make it at every call.
        /// </summary>
        public static void EnsureLoaded()
        {
            if (_loadedGeneration == LocalizationSystem.generation) return;

            for (int i = 0; i < kLanguages.Length; i++) _Load(kLanguages[i]);

            _loadedGeneration = LocalizationSystem.generation;
        }

        [InitializeOnLoadMethod]
        private static void _Initialize() => EnsureLoaded();

        private static void _Load(string language)
        {
            var path = kPackageRoot + kLocaleFolder + language + ".json";
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);

            if (asset == null)
            {
                // Without the file the windows still work, only in whatever language is left -- so
                // this warns once rather than failing anything. The source language is the one worth
                // warning about: everything falls back to it.
                if (language == kSourceLanguage && !_missingReported)
                {
                    _missingReported = true;
                    Debug.LogWarning($"[RemoteControl] Editor locale not found at \"{path}\".");
                }
                return;
            }

            LocalizationSystem.LoadTranslations(language, asset.text);
        }
    }
}
