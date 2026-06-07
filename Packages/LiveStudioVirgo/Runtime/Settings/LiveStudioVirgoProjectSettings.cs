// Copyright (c) You-Ri, 2026

using UnityEngine;

namespace Lilium.LiveStudio.Virgo
{
    /// <summary>
    /// Project-wide settings container for the LiveStudio Virgo package.
    /// The Fusion launch options have moved to the <c>FusionApp</c> component, so this
    /// currently holds no fields; it is retained as the project-settings entry point
    /// (<c>Project/Live Studio/Virgo</c>) for future package-wide settings.
    /// </summary>
    public class LiveStudioVirgoProjectSettings : ScriptableObject
    {
        public const string kConfigKey = "jp.lilium.livestudio.virgo.settings";

        /// <summary>Per-project override path (created on first edit).</summary>
        public const string kAssetPath = "Assets/Settings/LiveStudioVirgoProjectSettings.asset";

        /// <summary>Read-only package default referenced when no per-project override exists.</summary>
        public const string kPackageDefaultPath = "Packages/jp.lilium.livestudio.virgo/Contents/Settings/LiveStudioVirgoProjectSettings.asset";

        static LiveStudioVirgoProjectSettings _instance;

        public static LiveStudioVirgoProjectSettings Instance
        {
            get
            {
                if (_instance != null) return _instance;

#if UNITY_EDITOR
                // 1. Per-project override registered as a config object.
                UnityEditor.EditorBuildSettings.TryGetConfigObject(kConfigKey, out _instance);
                if (_instance != null) return _instance;

                // 2. Per-project override that exists in AssetDatabase but is not yet registered.
                _instance = UnityEditor.AssetDatabase.LoadAssetAtPath<LiveStudioVirgoProjectSettings>(kAssetPath);
                if (_instance != null) return _instance;

                // 3. Read-only package default.
                _instance = UnityEditor.AssetDatabase.LoadAssetAtPath<LiveStudioVirgoProjectSettings>(kPackageDefaultPath);
                if (_instance != null) return _instance;
#endif

                // 4. Final fallback for player builds that ship no preloaded asset.
                var fallback = CreateInstance<LiveStudioVirgoProjectSettings>();
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
