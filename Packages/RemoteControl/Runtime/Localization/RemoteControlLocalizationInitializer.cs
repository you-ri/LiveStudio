// Copyright (c) You-Ri, 2026
using UnityEngine;

namespace Lilium.RemoteControl
{
    /// <summary>
    /// RemoteControl パッケージの翻訳データを LocalizationSystem に登録する初期化クラス。
    /// </summary>
    public static class RemoteControlLocalizationInitializer
    {
        private static bool _initialized;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void _Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            _LoadLocale("en");
            _LoadLocale("ja");
            _LoadLocale("zh-CN");
        }

        private static void _LoadLocale(string language)
        {
            var path = $"RemoteControlLocales/{language}";
            var textAsset = Resources.Load<TextAsset>(path);
            if (textAsset != null)
            {
                LocalizationSystem.LoadTranslations(language, textAsset.text);
                Resources.UnloadAsset(textAsset);
            }
        }

#if UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void _ResetStatics()
        {
            _initialized = false;
        }
#endif
    }
}
