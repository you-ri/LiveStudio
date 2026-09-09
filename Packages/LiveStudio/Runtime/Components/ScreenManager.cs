// Copyright (c) You-Ri, 2026

using System;

using UnityEngine;
using UnityEngine.SceneManagement;

using Unity.Cinemachine;

using Lilium.RemoteControl;
using Lilium.RemoteControl.Utility;

#if KEIJIRO_KLAK_SPOUT
using Klak.Spout;
#endif

namespace Lilium.LiveStudio
{
    [LiveEnum("BackgroundType")]
    public enum BackgroundType
    {
        SolidColor,
        Skybox,
    }

    /// <summary>
    /// The screen: where this machine puts its picture, and which camera in the loaded scenes produces
    /// it. A persistent object like <see cref="StageManager"/> and <see cref="EnvironmentLight"/> rather
    /// than a component on a camera, because the camera is no longer ours — it belongs to whichever set
    /// is standing, and it comes and goes with that set.
    ///
    /// Every member is <see cref="PersistScope.Project"/>: this is the machine's output configuration
    /// (resolution, full screen, how the background is keyed, Spout), not part of the show. Project
    /// scope also keeps it off the frame (FrameLaneRules) — recording it and restoring it on replay
    /// would change the picture of whoever is watching.
    ///
    /// ⚠ What this class owns is the *settings*; the camera it applies them to is found, not held. See
    /// <see cref="_ResolveOutputCamera"/> for the rules, and <see cref="_ApplyExclusivity"/> for why
    /// only one camera is left running.
    /// </summary>
    [Serializable]
    [LiveClass("Screen", Category = "Screen", Icon = "monitor")]
    public class ScreenManager : ILiveObject, ILiveDeserializeCallback
    {
        const string kId = "5f3c1d84-7a26-4b90-8e1c-24d0a6f3b715";

        // The live instance, so callers that only have a static reach (LiveCamera's thumbnail capture)
        // can ask which camera is on air. Set in OnEnable, cleared in OnDisable. Reset on subsystem
        // registration for safety when Domain Reload is disabled.
        [NonSerialized]
        private static ScreenManager _current;

        public static ScreenManager current => _current;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void _InitializeCurrent() => _current = null;

        public string name { get; set; } = "Screen";

        public LiveObjectHandle? liveObject => LiveObjectRegistry.FindByTarget(this);

        public string id => kId;

        public ScreenManager()
        {
        }

        // --- Application-wide output settings ---------------------------------------------------
        //
        // Resolution and full screen are properties of the window this app owns, not of any camera, so
        // they are applied once and survive every camera switch.

        [LiveField(persistScope = PersistScope.Project), Hide]
        [FormerlyNamedAs("width")]
        private int _width = 1920;

        [LiveProperty]
        public int width
        {
            get
            {
#if UNITY_EDITOR
                var cam = _outputCamera;
                return cam != null ? cam.pixelWidth : Screen.width;
#else
                return Screen.width;
#endif
            }
            set
            {
                _width = value;
                _ApplyResolution();
            }
        }

        [LiveField(persistScope = PersistScope.Project), Hide]
        [FormerlyNamedAs("height")]
        private int _height = 1080;

        [LiveProperty]
        public int height
        {
            get
            {
#if UNITY_EDITOR
                var cam = _outputCamera;
                return cam != null ? cam.pixelHeight : Screen.height;
#else
                return Screen.height;
#endif
            }
            set
            {
                _height = value;
                _ApplyResolution();
            }
        }

        [LiveField(persistScope = PersistScope.Project), Hide]
        [FormerlyNamedAs("isFullScreen")]
        private bool _isFullScreen;

        [LiveProperty]
        public bool isFullScreen
        {
            get => Screen.fullScreen;
            set
            {
                _isFullScreen = value;
                Screen.fullScreen = value;
            }
        }

        // --- Camera-bound settings ---------------------------------------------------------------
        //
        // These follow the output camera: they are re-applied whenever the camera changes, and the
        // camera being left behind is put back the way it was found.

        [LiveField(persistScope = PersistScope.Project), Hide]
        [FormerlyNamedAs("backgroundType")]
        private BackgroundType _backgroundType = BackgroundType.Skybox;

        [LiveProperty]
        public BackgroundType backgroundType
        {
            get => _backgroundType;
            set
            {
                _backgroundType = value;
                _ApplyBackground();
            }
        }

        [LiveField(persistScope = PersistScope.Project), Hide]
        [FormerlyNamedAs("backgroundColor")]
        private Color _backgroundColor = Color.black;

        [LiveProperty, ShowIf(nameof(backgroundType), (int)BackgroundType.SolidColor)]
        public Color backgroundColor
        {
            get => _backgroundColor;
            set
            {
                _backgroundColor = value;
                _ApplyBackground();
            }
        }

        [LiveField(persistScope = PersistScope.Project), Hide]
        [FormerlyNamedAs("useSpout")]
        private bool _useSpout;

#if KEIJIRO_KLAK_SPOUT
        [LiveProperty]
        [Help("SCREEN_USESPOUT")]
        public bool useSpout
        {
            get => _useSpout;
            set
            {
                _useSpout = value;
                _ApplySpout();
            }
        }
#endif

        // --- Output camera ------------------------------------------------------------------------

        [NonSerialized]
        private Camera _outputCamera;

        /// <summary>
        /// The camera whose picture is the screen right now, or null before one is resolved.
        /// The single answer to "which camera is on air" — <see cref="LiveCamera"/> asks here rather
        /// than searching on its own, so a thumbnail is taken through the same camera the viewer sees.
        /// </summary>
        public Camera outputCamera => _outputCamera;

        // The scene the last resolve looked in. What Update compares against to notice the stage moved.
        [NonSerialized]
        private Scene _resolvedTargetScene;

        [NonSerialized]
        private bool _seeded;

        [NonSerialized]
        private bool _initialized;

        public void OnEnable()
        {
            _current = this;

            LiveObjectRegistry.Create<ScreenManager>(this, kId);

            Lilium.RemoteControl.LiveScene.RemoteControlBehaviour.onBaseSceneReloaded += _OnBaseSceneReloaded;
            SceneManager.sceneLoaded += _OnSceneLoaded;
            SceneManager.sceneUnloaded += _OnSceneUnloaded;

            _initialized = true;

            _ResolveAndApply();
        }

        public void OnDisable()
        {
            _initialized = false;

            Lilium.RemoteControl.LiveScene.RemoteControlBehaviour.onBaseSceneReloaded -= _OnBaseSceneReloaded;
            SceneManager.sceneLoaded -= _OnSceneLoaded;
            SceneManager.sceneUnloaded -= _OnSceneUnloaded;

            _ReleaseSpoutTexture();

            _outputCamera = null;
            _resolvedTargetScene = default;

            LiveObjectRegistry.FindByTarget(this)?.Unregister();

            if (_current == this) _current = null;
        }

        public void OnDispose()
        {
            OnDisable();
        }

        public void Update()
        {
            if (!_initialized) return;
            if (!Application.isPlaying) return;

            // Re-resolve only when the answer can have changed: the camera went away with its scene or
            // was switched off from outside, or the stage that owns it changed. Scene load / unload is
            // covered by the events above; the target-scene check catches activating a set whose scene
            // was already up, which loads nothing.
            //
            // ⚠ The test is which scene we are *looking in*, not which scene the camera is in: with no
            // set standing the two differ, and comparing them would re-search the whole object graph
            // every frame.
            if (_outputCamera == null || !_outputCamera.isActiveAndEnabled
                || _ResolveTargetScene() != _resolvedTargetScene)
            {
                _ResolveAndApply();
            }

        }

        public void Reset()
        {
        }

        /// <summary>
        /// Restores the values a live scene or the project settings just wrote. Both the app-wide and
        /// the camera-bound halves, since a restore can change either.
        /// </summary>
        public void OnAfterLiveDeserialize()
        {
            _ApplyResolution();
            Screen.fullScreen = _isFullScreen;
            _ApplyBackground();
            _ApplySpout();
        }

        private void _OnSceneLoaded(Scene scene, LoadSceneMode mode) => _ResolveAndApply();

        private void _OnSceneUnloaded(Scene scene) => _ResolveAndApply();

        // After a base-scene switch the whole camera population was replaced. Resolve again from
        // scratch rather than trusting the camera we were holding.
        private void _OnBaseSceneReloaded() => _ResolveAndApply();

        // --- Resolution ---------------------------------------------------------------------------

        /// <summary>
        /// The scene the output camera should come from: the set that is standing, or the bootstrap
        /// scene when no set is active. This is what makes a set's own camera win over the base one.
        /// </summary>
        private Scene _ResolveTargetScene()
        {
            var assets = ExternalAssetManager.current;
            if (assets != null)
            {
                var view = assets.assetsView;
                for (int i = 0; i < view.Count; i++)
                {
                    if (view[i] is ISetAsset set && set.isActive && set.hasScene)
                        return set.scene;
                }
            }

            var stage = StageManager.current;
            if (stage != null)
            {
                var path = stage.persistentScenePath;
                if (!string.IsNullOrEmpty(path))
                {
                    var persistent = SceneManager.GetSceneByPath(path);
                    if (persistent.IsValid() && persistent.isLoaded) return persistent;
                }
            }

            return SceneManager.GetActiveScene();
        }

        /// <summary>
        /// Picks the output camera and puts every other one out of the way.
        ///
        /// Candidates are cameras carrying a <see cref="CinemachineBrain"/> on an active GameObject.
        /// The brain is the test because those are the cameras that would otherwise all follow the same
        /// virtual cameras and draw the same picture several times over; a camera without one (a render
        /// texture feed, an overlay) is somebody else's and is left alone. An inactive GameObject is
        /// read as the author's intent — a set that ships Unity's default camera switched off keeps it
        /// switched off, which is exactly what the sample sets have.
        ///
        /// ⚠ The Camera *component's* enabled flag is not part of the test, because it is the flag this
        /// class writes. Reading it back would mean a camera we switched off could never be chosen
        /// again, and unloading a set would leave the app with no picture at all.
        /// </summary>
        private void _ResolveAndApply()
        {
            var previous = _outputCamera;
            _resolvedTargetScene = _ResolveTargetScene();

            // Whatever the loaded scenes offer, and nothing when they offer nothing: the screen is a
            // camera somebody placed, not one this class conjures.
            _outputCamera = _ResolveOutputCamera();

            if (previous != null && previous != _outputCamera)
                _ReleaseCamera(previous);

            _ApplyExclusivity();

            // The first camera we ever see supplies the starting values, so a fresh project inherits
            // what the scene author set up rather than this class's constructor defaults.
            //
            // ⚠ Only the first. Seeding on every switch would let each set's authored background
            // overwrite the operator's saved setting the moment that set is loaded.
            if (!_seeded && _outputCamera != null)
            {
                _backgroundColor = _outputCamera.backgroundColor;
                _backgroundType = _outputCamera.clearFlags == CameraClearFlags.Skybox
                    ? BackgroundType.Skybox
                    : BackgroundType.SolidColor;
                _seeded = true;
            }

            _ApplyBackground();
            _ApplySpout();
        }

        /// <summary>
        /// The best camera the loaded scenes offer, or null when they offer none — in which case there
        /// is simply no screen this frame, and nothing is applied.
        /// </summary>
        private Camera _ResolveOutputCamera()
        {
            var brains = UnityEngine.Object.FindObjectsByType<CinemachineBrain>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (brains == null || brains.Length == 0) return null;

            var target = _ResolveTargetScene();

            Camera first = null;
            for (int i = 0; i < brains.Length; i++)
            {
                var cam = _CandidateCamera(brains[i]);
                if (cam == null) continue;
                if (cam.gameObject.scene == target) return cam;
                if (first == null) first = cam;
            }

            return first;
        }

        private static Camera _CandidateCamera(CinemachineBrain brain)
        {
            if (brain == null) return null;
            if (!brain.gameObject.activeInHierarchy) return null;
            return brain.GetComponent<Camera>();
        }

        /// <summary>
        /// Leaves exactly one camera running: the output one on, every other candidate off, and the
        /// same for their audio listeners (two enabled listeners is a warning and an undefined
        /// listening position).
        /// </summary>
        private void _ApplyExclusivity()
        {
            // ⚠ Only while playing. This object also lives in the Editor outside play mode (RemoteControl
            // runs there), and switching cameras off there would be an edit to the user's scene — a dirty
            // scene and a lost camera, made by something they never asked to run.
            if (!Application.isPlaying) return;

            var brains = UnityEngine.Object.FindObjectsByType<CinemachineBrain>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (brains == null) return;

            for (int i = 0; i < brains.Length; i++)
            {
                var cam = _CandidateCamera(brains[i]);
                if (cam == null) continue;

                bool isOutput = cam == _outputCamera;
                if (cam.enabled != isOutput) cam.enabled = isOutput;

                var listener = cam.GetComponent<AudioListener>();
                if (listener != null && listener.enabled != isOutput) listener.enabled = isOutput;
            }
        }

        // Puts a camera we are leaving back the way we found it: nothing of ours stays attached to a
        // camera that is no longer the screen.
        private void _ReleaseCamera(Camera camera)
        {
            if (camera == null) return;

#if KEIJIRO_KLAK_SPOUT
            if (camera.targetTexture == _spoutRenderTexture) camera.targetTexture = null;

            var sender = camera.GetComponent<SpoutSender>();
            if (sender != null) GameObjectUtility.Destroy(sender);
#endif
        }

        // --- Apply ---------------------------------------------------------------------------------

        private void _ApplyResolution()
        {
            Screen.SetResolution(_width, _height, _isFullScreen);
#if KEIJIRO_KLAK_SPOUT
            _ResizeSpoutRenderTexture(_width, _height);
#endif
        }

        private void _ApplyBackground()
        {
            // Same reason as _ApplyExclusivity: outside play mode this would be an unasked-for edit to
            // the scene's camera.
            if (!Application.isPlaying) return;

            var camera = _outputCamera;
            if (camera == null) return;

            camera.clearFlags = _backgroundType == BackgroundType.Skybox
                ? CameraClearFlags.Skybox
                : CameraClearFlags.SolidColor;
            camera.backgroundColor = _backgroundColor;
        }

        // --- Spout -----------------------------------------------------------------------------------

        [NonSerialized]
        private RenderTexture _spoutRenderTexture;

        private void _ApplySpout()
        {
#if KEIJIRO_KLAK_SPOUT
            // Adding / removing a sender component outside play mode would edit the scene. See
            // _ApplyExclusivity.
            if (!Application.isPlaying) return;

            var camera = _outputCamera;
            if (camera == null) return;

            if (!_useSpout)
            {
                var existing = camera.GetComponent<SpoutSender>();
                if (existing != null) GameObjectUtility.Destroy(existing);
                if (camera.targetTexture == _spoutRenderTexture) camera.targetTexture = null;
                return;
            }

            _EnsureSpoutTexture();

            var sender = camera.GetComponent<SpoutSender>();
            if (sender == null) sender = camera.gameObject.AddComponent<SpoutSender>();

            var settings = LiveStudioProjectSettings.Instance;
            var resources = settings != null ? settings.spoutResources : null;
            if (resources != null)
                sender.SetResources(resources);
            else
                Debug.LogWarning("[LiveStudio] SpoutResources is not configured in LiveStudioProjectSettings; the Spout sender will not work.");

            sender.enabled = true;
            sender.sourceTexture = _spoutRenderTexture;
            // A fixed name rather than the camera's: the sender moves between cameras as sets come and
            // go, and a name that moved with it would drop the source out of OBS on every switch.
            sender.spoutName = kSpoutName;
            sender.captureMethod = CaptureMethod.Texture;

            camera.targetTexture = _spoutRenderTexture;
#endif
        }

#if KEIJIRO_KLAK_SPOUT
        const string kSpoutName = "LiveStudio";

        private void _EnsureSpoutTexture()
        {
            if (_spoutRenderTexture != null) return;

            _spoutRenderTexture = new RenderTexture(Mathf.Max(_width, 1), Mathf.Max(_height, 1), 24)
            {
                name = "Screen_SpoutRT",
                antiAliasing = 1,
            };
            _spoutRenderTexture.Create();
        }

        private void _ResizeSpoutRenderTexture(int w, int h)
        {
            if (_spoutRenderTexture == null) return;
            if (_spoutRenderTexture.width == w && _spoutRenderTexture.height == h) return;

            _spoutRenderTexture.Release();
            _spoutRenderTexture.width = Mathf.Max(w, 1);
            _spoutRenderTexture.height = Mathf.Max(h, 1);
            _spoutRenderTexture.Create();
        }
#else
        private void _ResizeSpoutRenderTexture(int w, int h)
        {
        }
#endif

        private void _ReleaseSpoutTexture()
        {
            _ReleaseCamera(_outputCamera);

            if (_spoutRenderTexture == null) return;
            _spoutRenderTexture.Release();
            GameObjectUtility.Destroy(_spoutRenderTexture);
            _spoutRenderTexture = null;
        }
    }
}
