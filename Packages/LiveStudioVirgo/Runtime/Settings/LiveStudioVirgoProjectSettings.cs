// Copyright (c) You-Ri, 2026

using UnityEngine;

namespace Lilium.LiveStudio.Virgo
{
    /// <summary>
    /// Project-wide settings for the LiveStudio Virgo package.
    /// Currently holds the launch options for the bundled VirgoMotionFusion process.
    /// </summary>
    public class LiveStudioVirgoProjectSettings : ScriptableObject
    {
        public const string kConfigKey = "jp.lilium.livestudio.virgo.settings";

        [Header("Fusion App")]
        [Tooltip("If enabled, VirgoMotionFusion is launched on application startup and stopped on shutdown.")]
        [SerializeField] bool _launchFusionOnStartup = true;

        [SerializeField] PathType _fusionPathType = PathType.PackageRelative;

        [Tooltip("Application path relative to the resolved root (Tools~ folder for PackageRelative, Tools/ for ProjectRelative).")]
        [SerializeField] string _fusionApplicationPath = "VirgoMotionFusion/VirgoMotionFusion.exe";

        [Tooltip("Package name used to resolve Tools~ when pathType is PackageRelative.")]
        [SerializeField] string _fusionPackageName = "jp.lilium.livestudio.virgo";

        [SerializeField] string _fusionArguments = "-batchmode -nographics";

        [SerializeField] bool _fusionHideWindow = true;

        public bool launchFusionOnStartup => _launchFusionOnStartup;
        public PathType fusionPathType => _fusionPathType;
        public string fusionApplicationPath => _fusionApplicationPath;
        public string fusionPackageName => _fusionPackageName;
        public string fusionArguments => _fusionArguments;
        public bool fusionHideWindow => _fusionHideWindow;

        static LiveStudioVirgoProjectSettings _instance;

        public static LiveStudioVirgoProjectSettings Instance
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
