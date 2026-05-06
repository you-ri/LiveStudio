// Copyright (c) You-Ri, 2026

using UnityEngine;

namespace Lilium.LiveStudio
{
    public class LiveStudioProjectSettings : ScriptableObject
    {
        public const string kConfigKey = "jp.lilium.livestudio.settings";

        public VRMAvatarSetupSettings vrmAvatarSetupSettings => _vrmAvatarSetupSettings;

        [SerializeField]
        VRMAvatarSetupSettings _vrmAvatarSetupSettings;

        public string savedBaseSubDir => _savedBaseSubDir;

        [SerializeField]
        [Tooltip("Documents フォルダ配下のサブディレクトリ。空ならパッケージ既定値 (LiveStudio/Saved) を使用。アプリのブランド名を設定する想定 (例: \"Virgo Motion/Saved\")。")]
        string _savedBaseSubDir = "";

        static LiveStudioProjectSettings _instance;

        public static LiveStudioProjectSettings Instance
        {
            get
            {
                if (_instance != null) return _instance;

#if UNITY_EDITOR
                UnityEditor.EditorBuildSettings.TryGetConfigObject(kConfigKey, out _instance);
#endif
                return _instance;
            }
        }

        void OnEnable()
        {
            _instance = this;
        }
    }
}
