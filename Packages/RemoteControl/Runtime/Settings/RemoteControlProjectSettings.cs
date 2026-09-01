// Copyright (c) You-Ri, 2026

using System.Collections.Generic;

using UnityEngine;

namespace Lilium.RemoteControl
{
    /// <summary>
    /// Project-wide remote control settings. Resolved from (in order) a per-project override
    /// registered as a config object, the same override found in the AssetDatabase, and finally
    /// the default asset shipped in this package's Resources — the same shape LiveStudio uses for
    /// its own settings, so an app overrides what it needs and inherits the rest.
    ///
    /// Its one job today is <see cref="liveClassAssets"/>: the live class assets applied to the
    /// whole project. A <c>RemoteControlContainer</c> applies the assets it carries while it is
    /// enabled — right for declarations that travel with a set bundle, since they have to leave
    /// with it — but wrong for declarations that describe the app itself, which would then have to
    /// be wired into every scene, and quietly stop being saved in any scene that forgot the
    /// reference (an unregistered type is skipped by the serializer). Those go here instead.
    /// </summary>
    public class RemoteControlProjectSettings : ScriptableObject
    {
        public const string kConfigKey = "jp.lilium.remotecontrol.settings";

        /// <summary>Per-project override path (created on first edit).</summary>
        public const string kAssetPath = "Assets/Settings/RemoteControlProjectSettings.asset";

        /// <summary>Resources path of the package default asset (no extension, relative to a Resources folder).</summary>
        public const string kResourcesPath = "Settings/RemoteControlProjectSettings";

        [SerializeField]
        [Tooltip("Live class assets applied to every scene at startup. Declarations that ship with a set bundle belong on that bundle's Remote Control Container instead.")]
        List<LiveClassAsset> _liveClassAssets = new List<LiveClassAsset>();

        /// <summary>Live class assets applied to the whole project. Never null.</summary>
        public IReadOnlyList<LiveClassAsset> liveClassAssets
            => (IReadOnlyList<LiveClassAsset>)_liveClassAssets ?? System.Array.Empty<LiveClassAsset>();

        static RemoteControlProjectSettings _instance;

        public static RemoteControlProjectSettings Instance
        {
            get
            {
                if (_instance != null) return _instance;

#if UNITY_EDITOR
                // 1. Per-project override registered as a config object.
                UnityEditor.EditorBuildSettings.TryGetConfigObject(kConfigKey, out _instance);
                if (_instance != null) return _instance;

                // 2. Per-project override that exists in AssetDatabase but is not yet registered.
                _instance = UnityEditor.AssetDatabase.LoadAssetAtPath<RemoteControlProjectSettings>(kAssetPath);
                if (_instance != null) return _instance;
#endif

                // 3. Package default shipped via Resources. Works in both Editor and player builds.
                _instance = Resources.Load<RemoteControlProjectSettings>(kResourcesPath);
                if (_instance != null) return _instance;

                // 4. Final fallback when no asset can be located. Warn so the cause is visible in player logs.
                Debug.LogWarning("[RemoteControl] RemoteControlProjectSettings asset not found in Resources; using empty fallback. No project-wide live class asset will be applied.");
                var fallback = CreateInstance<RemoteControlProjectSettings>();
                fallback.hideFlags = HideFlags.DontSave;
                return fallback;
            }
        }

        /// <summary>
        /// Applies every live class asset the settings name. Runs before the first scene's objects
        /// wake up (and at editor load, since remote control also serves a stopped editor), after
        /// <see cref="LiveClassAssetSystem"/> has cleared its bookkeeping for the session.
        /// Registration is idempotent, so applying again is cheap.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#endif
        public static void ApplyLiveClassAssets()
        {
            var settings = Instance;
            if (settings != null) settings.Apply();
        }

        /// <summary>Applies this asset's live class assets. See <see cref="ApplyLiveClassAssets"/>.</summary>
        public void Apply()
        {
            var assets = _liveClassAssets;
            if (assets == null) return;

            for (int i = 0; i < assets.Count; i++)
            {
                // A null entry is an asset the project deleted (or an empty row left in the
                // inspector); say which slot so it can be found, and keep applying the rest.
                if (assets[i] == null)
                {
                    Debug.LogWarning($"[RemoteControl] Live class asset slot {i} of the project settings is empty.");
                    continue;
                }
                // Permanent: an asset named here describes the app rather than a set that comes and
                // goes, so a container that also lists it must not be able to unregister its types.
                LiveClassAssetSystem.RegisterTypesPermanent(assets[i]);
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
