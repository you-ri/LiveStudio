// Copyright (c) You-Ri, 2026

using UnityEngine;

namespace Lilium.LiveStudio
{
    public class LiveStudioProjectSettings : ScriptableObject
    {
        public const string kConfigKey = "jp.lilium.livestudio.settings";

        /// <summary>Per-project override path (created on first edit).</summary>
        public const string kAssetPath = "Assets/Settings/LiveStudioProjectSettings.asset";

        /// <summary>Read-only package default referenced when no per-project override exists.</summary>
        public const string kPackageDefaultPath = "Packages/jp.lilium.livestudio/Contents/Settings/LiveStudioProjectSettings.asset";

        public VRMAvatarSetupSettings vrmAvatarSetupSettings => _vrmAvatarSetupSettings;

        [SerializeField]
        VRMAvatarSetupSettings _vrmAvatarSetupSettings;

        public string savedBaseSubDir => _savedBaseSubDir;

        [SerializeField]
        [Tooltip("Documents フォルダ配下のサブディレクトリ。空ならパッケージ既定値 (LiveStudio/Saved) を使用。アプリのブランド名を設定する想定 (例: \"Virgo Motion/Saved\")。")]
        string _savedBaseSubDir = "";

        [Header("Remote App")]
        [Tooltip("If enabled, the configured Remote app is launched on application startup and stopped on shutdown.")]
        [SerializeField] bool _launchRemoteOnStartup = true;

        [SerializeField] PathType _remotePathType = PathType.PackageRelative;

        [Tooltip("Application path relative to the resolved root (Tools~ folder for PackageRelative, Tools/ for ProjectRelative).")]
        [SerializeField] string _remoteApplicationPath = "VirgoMotionRemote/VirgoMotionRemote.exe";

        [Tooltip("Package name used to resolve Tools~ when pathType is PackageRelative.")]
        [SerializeField] string _remotePackageName = "jp.lilium.remotecontrol";

        [SerializeField] string _remoteArguments = "";

        [SerializeField] bool _remoteHideWindow = true;

        public bool launchRemoteOnStartup => _launchRemoteOnStartup;
        public PathType remotePathType => _remotePathType;
        public string remoteApplicationPath => _remoteApplicationPath;
        public string remotePackageName => _remotePackageName;
        public string remoteArguments => _remoteArguments;
        public bool remoteHideWindow => _remoteHideWindow;

        static LiveStudioProjectSettings _instance;

        public static LiveStudioProjectSettings Instance
        {
            get
            {
                if (_instance != null) return _instance;

#if UNITY_EDITOR
                // 1. Per-project override registered as a config object.
                UnityEditor.EditorBuildSettings.TryGetConfigObject(kConfigKey, out _instance);
                if (_instance != null) return _instance;

                // 2. Per-project override that exists in AssetDatabase but is not yet registered.
                _instance = UnityEditor.AssetDatabase.LoadAssetAtPath<LiveStudioProjectSettings>(kAssetPath);
                if (_instance != null) return _instance;

                // 3. Read-only package default.
                _instance = UnityEditor.AssetDatabase.LoadAssetAtPath<LiveStudioProjectSettings>(kPackageDefaultPath);
                if (_instance != null) return _instance;
#endif

                // 4. Final fallback for player builds that ship no preloaded asset.
                var fallback = CreateInstance<LiveStudioProjectSettings>();
                fallback.hideFlags = HideFlags.DontSave;
                return _instance != null ? _instance : fallback;
            }
        }

        void OnEnable()
        {
            if (_instance == null || _instance == this)
            {
                _instance = this;
                return;
            }

            // A real per-project asset should override any in-memory fallback or package default
            // that was loaded earlier in the session.
            if ((_instance.hideFlags & HideFlags.DontSave) != 0 || _IsPackageAsset(_instance))
            {
                _instance = this;
            }
        }

        static bool _IsPackageAsset(Object obj)
        {
#if UNITY_EDITOR
            var path = UnityEditor.AssetDatabase.GetAssetPath(obj);
            return !string.IsNullOrEmpty(path) && path.StartsWith("Packages/");
#else
            return false;
#endif
        }
    }
}
