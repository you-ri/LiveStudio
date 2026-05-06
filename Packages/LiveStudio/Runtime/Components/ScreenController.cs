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
    [ExposedEnum("BackgroundType")]
    public enum BackgroundType
    {
        SolidColor,
        Skybox,
    }

    [ExposedClass("Screen", Category = "Screen", Icon = "monitor")]
    [RequireComponent(typeof(Camera))]
    [RequireComponent(typeof(CinemachineBrain))]
    public class ScreenController : MonoBehaviour, IExposedDeserializeCallback
    {
        [SerializeField, ExposedField, Hide]
        [FormerlyExposedAs("width")]
        private int _width = 1920;

        [ExposedProperty]
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

        [SerializeField, ExposedField, Hide]
        [FormerlyExposedAs("height")]
        private int _height = 1080;

        [ExposedProperty]
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

        [SerializeField, ExposedField, Hide]
        [FormerlyExposedAs("isFullScreen")]
        private bool _isFullScreen;

        [ExposedProperty]
        public bool isFullScreen
        {
            get => Screen.fullScreen;
            set
            {
                _isFullScreen = value;
                Screen.fullScreen = value;
            }
        }

        [SerializeField, ExposedField, Hide]
        [FormerlyExposedAs("backgroundType")]
        private BackgroundType _backgroundType = BackgroundType.Skybox;

        [ExposedProperty]
        public BackgroundType backgroundType
        {
            get => _backgroundType;
            set
            {
                _backgroundType = value;
                _ApplyBackgroundType();
            }
        }

        [SerializeField, ExposedField, Hide]
        [FormerlyExposedAs("backgroundColor")]
        private Color _backgroundColor = Color.black;

        [ExposedProperty, ShowIf(nameof(backgroundType), (int)BackgroundType.SolidColor)]
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
        SpoutSender _spoutSender;
#endif

        [SerializeField, ExposedField, Hide]
        [FormerlyExposedAs("useSpout")]
        private bool _useSpout;

#if KEIJIRO_KLAK_SPOUT
        [ExposedProperty]
        [ExposedHelp("SCREEN_USESPOUT")]
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

        public void OnAfterExposedDeserialize() => _ApplyAll();

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

#if KEIJIRO_KLAK_SPOUT
            if (_spoutSender == null)
                _spoutSender = GetComponent<SpoutSender>();
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
            if (_spoutSender != null)
            {
                _spoutSender.enabled = use;
                _spoutSender.sourceTexture = _spoutRenderTexture;
                _spoutSender.spoutName = $"{gameObject.name}";
                _spoutSender.captureMethod = CaptureMethod.Texture;
            }
            _camera.targetTexture = use ? _spoutRenderTexture : null;
        }
#endif
    }

}
