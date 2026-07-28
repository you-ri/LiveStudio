using System;
using Lilium.RemoteControl;
using Lilium.RemoteControl.Utility;
using UnityEngine;
using Unity.Cinemachine;

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

    [LiveClass("Screen", Category = "Screen", Icon = "monitor")]
    [RequireComponent(typeof(Camera))]
    [RequireComponent(typeof(CinemachineBrain))]
    public class ScreenController : MonoBehaviour, ILiveDeserializeCallback
    {
        // 出力解像度はどのシーンを開いても共通の出力設定なので Project scope で永続化する。
        [LiveField(persistScope = PersistScope.Project), Hide]
        [FormerlyNamedAs("width")]
        private int _width = 1920;

        [LiveProperty]
        public int width
        {
            get
            {
#if UNITY_EDITOR
                return _camera != null ? _camera.pixelWidth : Screen.width;
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
                return _camera != null ? _camera.pixelHeight : Screen.height;
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

        [LiveField, Hide]
        [FormerlyNamedAs("backgroundType")]
        private BackgroundType _backgroundType = BackgroundType.Skybox;

        [LiveProperty]
        public BackgroundType backgroundType
        {
            get => _backgroundType;
            set
            {
                _backgroundType = value;
                _ApplyBackgroundType();
            }
        }

        [LiveField, Hide]
        [FormerlyNamedAs("backgroundColor")]
        private Color _backgroundColor = Color.black;

        [LiveProperty, ShowIf(nameof(backgroundType), (int)BackgroundType.SolidColor)]
        public Color backgroundColor
        {
            get => _backgroundColor;
            set
            {
                _backgroundColor = value;
                _ApplyBackgroundColor();
            }
        }

        void _ApplyResolution()
        {
            Screen.SetResolution(_width, _height, _isFullScreen);
#if KEIJIRO_KLAK_SPOUT
            _ResizeSpoutRenderTexture(_width, _height);
#endif
        }

        void _ApplyBackgroundType()
        {
            if (_camera == null) _Initialize();
            if (_camera != null)
                _camera.clearFlags = _backgroundType == BackgroundType.Skybox ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
        }

        void _ApplyBackgroundColor()
        {
            if (_camera == null) _Initialize();
            if (_camera != null) _camera.backgroundColor = _backgroundColor;
        }

        RenderTexture _spoutRenderTexture;

        Camera _camera;
        CinemachineBrain _brain;

        [SerializeField]
        private int _channel;

#if KEIJIRO_KLAK_SPOUT
        [SerializeField]
        [Tooltip("SpoutResources asset from the Klak.Spout package (required for runtime SpoutSender creation).")]
        SpoutResources _spoutResources;

        SpoutSender _spoutSender;
#endif

        [SerializeField, LiveField(persistScope = PersistScope.Project), Hide]
        [FormerlyNamedAs("useSpout")]
        private bool _useSpout;

#if KEIJIRO_KLAK_SPOUT
        [LiveProperty]
        [Help("SCREEN_USESPOUT")]
        public bool useSpout
        {
            get => _spoutSender != null && _spoutSender.enabled;
            set
            {
                _useSpout = value;
                _SetUseSpout(value);
            }
        }
#endif

        private void OnValidate()
        {
            _Initialize();
        }

        void Awake()
        {
            _Initialize();
        }

        void OnEnable()
        {
            _ApplyAll();
        }

        public void OnAfterLiveDeserialize() => _ApplyAll();

        void _ApplyAll()
        {
            _ApplyResolution();
            Screen.fullScreen = _isFullScreen;
            _ApplyBackgroundType();
            _ApplyBackgroundColor();
#if KEIJIRO_KLAK_SPOUT
            _SetUseSpout(_useSpout);
#endif
        }

        void OnDestroy()
        {
            if (_spoutRenderTexture != null)
            {
                _spoutRenderTexture.Release();
                Destroy(_spoutRenderTexture);
                _spoutRenderTexture = null;
            }
        }

        void _Initialize()
        {
            if (_camera == null)
                _camera = GetComponent<Camera>();

            _brain = GetComponent<CinemachineBrain>();
            _brain.ChannelMask = (OutputChannels)(1 << _channel);

            _backgroundColor = _camera.backgroundColor;
            _backgroundType = _camera.clearFlags == CameraClearFlags.Skybox ? BackgroundType.Skybox : BackgroundType.SolidColor;
            _height = Screen.height;
            _width = Screen.width;
            _isFullScreen = Screen.fullScreen;

#if KEIJIRO_KLAK_SPOUT
            if (_spoutRenderTexture == null)
            {
                _spoutRenderTexture = new RenderTexture(width, height, 24)
                {
                    name = $"{gameObject.name}_SpoutRT",
                    antiAliasing = 1,
                };
                _spoutRenderTexture.Create();
            }
            _SetUseSpout(false);
#endif
        }

#if KEIJIRO_KLAK_SPOUT
        void _ResizeSpoutRenderTexture(int w, int h)
        {
            if (_spoutRenderTexture == null) return;
            _spoutRenderTexture.Release();
            _spoutRenderTexture.width = w;
            _spoutRenderTexture.height = h;
            _spoutRenderTexture.Create();
        }

        void _SetUseSpout(bool use)
        {
            if (use)
            {
                if (_spoutSender == null)
                {
                    _spoutSender = GetComponent<SpoutSender>();
                    if (_spoutSender == null)
                        _spoutSender = gameObject.AddComponent<SpoutSender>();
                    if (_spoutResources != null)
                        _spoutSender.SetResources(_spoutResources);
                    else
                        Debug.LogWarning("[LiveStudio] SpoutResources is not assigned on ScreenController; SpoutSender will not work.");
                }
                _spoutSender.enabled = true;
                _spoutSender.sourceTexture = _spoutRenderTexture;
                _spoutSender.spoutName = $"{gameObject.name}";
                _spoutSender.captureMethod = CaptureMethod.Texture;
            }
            else
            {
                if (_spoutSender != null)
                {
                    GameObjectUtility.Destroy(_spoutSender);
                    _spoutSender = null;
                }
            }
            _camera.targetTexture = use ? _spoutRenderTexture : null;
        }
#endif
    }

}
