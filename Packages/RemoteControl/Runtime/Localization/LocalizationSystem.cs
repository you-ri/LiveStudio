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
                _currentLanguage = value;
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
        }
#endif
    }
}
