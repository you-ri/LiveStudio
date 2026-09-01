// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json.Linq;

namespace Lilium.RemoteControl
{
    /// <summary>
    /// 翻訳の管理・解決を担当する静的クラス。
    /// キーに対応する翻訳テキストを返し、見つからない場合は元のテキストをフォールバックする。
    /// </summary>
    public static class LocalizationSystem
    {
        private const string kPlayerPrefsKey = "RemoteControl_Language";
        private const string kDefaultLanguage = "en";

        // language -> (key -> translated text)
        private static Dictionary<string, Dictionary<string, string>> _translations
            = new Dictionary<string, Dictionary<string, string>>();

        private static List<string> _availableLanguages = new List<string>();

        private static string _currentLanguage;

        private static int _generation;

        /// <summary>
        /// Bumped whenever the answer <see cref="Translate"/> gives could have changed -- the active
        /// language, or the table behind it.
        ///
        /// For readers that resolve a key once and keep the result: an editor window writes a label
        /// when it builds the row, and never looks at the key again. Watching this is how such a
        /// reader learns it has to ask again. The count only ever moves forward, so a stale value
        /// never compares equal to a current one.
        /// </summary>
        public static int generation => _generation;

        /// <summary>
        /// 現在の言語コード（例: "en", "ja"）
        /// </summary>
        public static string currentLanguage
        {
            get
            {
                if (_currentLanguage == null)
                    _Initialize();
                return _currentLanguage;
            }
            set
            {
                if (string.IsNullOrEmpty(value)) return;
                if (_currentLanguage == value) return;

                _currentLanguage = value;
                _generation++;
                PlayerPrefs.SetString(kPlayerPrefsKey, value);
                PlayerPrefs.Save();
            }
        }

        /// <summary>
        /// 利用可能な言語一覧
        /// </summary>
        public static IReadOnlyList<string> availableLanguages
        {
            get
            {
                if (_currentLanguage == null)
                    _Initialize();
                return _availableLanguages;
            }
        }

        /// <summary>
        /// 初期化。PlayerPrefsまたはシステム言語から現在の言語を決定する。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void _Initialize()
        {
            if (_currentLanguage != null) return;

            if (PlayerPrefs.HasKey(kPlayerPrefsKey))
            {
                _currentLanguage = PlayerPrefs.GetString(kPlayerPrefsKey);
            }
            else
            {
                _currentLanguage = _SystemLanguageToCode(Application.systemLanguage);
            }

            // en は常に利用可能
            if (!_availableLanguages.Contains(kDefaultLanguage))
                _availableLanguages.Add(kDefaultLanguage);
        }

        /// <summary>
        /// キーに対応する翻訳テキストを返す。見つからない場合は元のテキストをフォールバック。
        /// </summary>
        public static string Translate(string key)
        {
            if (string.IsNullOrEmpty(key)) return key;

            if (_currentLanguage == null)
                _Initialize();

            if (_translations.TryGetValue(_currentLanguage, out var dict))
            {
                if (dict.TryGetValue(key, out var translated))
                    return translated;
            }

            return key;
        }

        /// <summary>
        /// Looks a key up in one named language, without falling back to anything.
        ///
        /// For callers that want to choose their own fallback -- the editor windows read the active
        /// language first and English second, because a window whose every label is a key is worse
        /// than one written in a language the reader did not ask for.
        /// </summary>
        public static bool TryTranslate(string language, string key, out string text)
        {
            text = null;

            if (string.IsNullOrEmpty(language) || string.IsNullOrEmpty(key)) return false;

            return _translations.TryGetValue(language, out var dict) && dict.TryGetValue(key, out text);
        }

        /// <summary>
        /// Translates a key whose text carries values, and fills them in.
        ///
        /// The placeholders belong in the translated text rather than around it: word order is the
        /// first thing a translation changes, so a sentence assembled from translated pieces reads
        /// correctly in the language it was written for and nowhere else.
        ///
        /// Formatting is left to <see cref="string.Format(string, object[])"/>, so a translation
        /// carrying a broken placeholder throws here rather than quietly printing the wrong thing.
        /// That is the loud failure the locale file wants: the fix is in the file, not at the call.
        /// </summary>
        public static string Format(string key, params object[] args)
        {
            var text = Translate(key);

            return args == null || args.Length == 0 ? text : string.Format(text, args);
        }

        /// <summary>
        /// 外部パッケージから翻訳データを登録する。
        /// JSON形式: { "key": "translated text", ... }
        /// 既存のキーは上書きされる。
        /// </summary>
        public static void LoadTranslations(string language, string json)
        {
            if (string.IsNullOrEmpty(language) || string.IsNullOrEmpty(json))
            {
                Debug.LogWarning("[RemoteControl] LoadTranslations: language or json is null/empty.");
                return;
            }

            try
            {
                var jObject = JObject.Parse(json);

                if (!_translations.TryGetValue(language, out var dict))
                {
                    dict = new Dictionary<string, string>();
                    _translations[language] = dict;
                }

                foreach (var property in jObject.Properties())
                {
                    dict[property.Name] = property.Value.ToString();
                }

                if (!_availableLanguages.Contains(language))
                    _availableLanguages.Add(language);

                _generation++;
            }
            catch (Exception ex)
            {
                Debug.LogError("[RemoteControl] Failed to load translations for '" + language + "': " + ex.Message);
            }
        }

        /// <summary>
        /// Application.systemLanguage を言語コードに変換
        /// </summary>
        private static string _SystemLanguageToCode(SystemLanguage lang)
        {
            switch (lang)
            {
                case SystemLanguage.Japanese: return "ja";
                case SystemLanguage.English: return "en";
                case SystemLanguage.Chinese:
                case SystemLanguage.ChineseSimplified:
                case SystemLanguage.ChineseTraditional: return "zh-CN";
                case SystemLanguage.Korean: return "ko";
                case SystemLanguage.French: return "fr";
                case SystemLanguage.German: return "de";
                case SystemLanguage.Spanish: return "es";
                case SystemLanguage.Portuguese: return "pt";
                case SystemLanguage.Russian: return "ru";
                default: return kDefaultLanguage;
            }
        }

#if UNITY_EDITOR
        /// <summary>
        /// Domain Reload対応: エディタ再生時に状態をリセット
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void _ResetStatics()
        {
            _currentLanguage = null;
            _translations = new Dictionary<string, Dictionary<string, string>>();
            _availableLanguages = new List<string>();

            // Counted as a change like any other: entering play mode empties the table, and a reader
            // holding text resolved in edit mode has to notice that its keys are gone. Bumped rather
            // than reset, so the number an editor window is holding cannot match by accident.
            _generation++;
        }
#endif
    }
}
