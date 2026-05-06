using System;
using UnityEngine;
using Lilium.RemoteControl;
using UnityEngine.Serialization;
using System.Configuration;


namespace Lilium.LiveStudio
{
    [Serializable]
    [ExposedClass(Category = "Light", Icon = "light_mode")]
    public class EnvironmentLight : IExposedObject, IExposedDeserializeCallback
    {
        const string kId = "d9714ab0-e81b-44c8-9e76-22f177864ebe";

        // TODO: [Farm] was a brainstorm placeholder for FormerlyExposedAs. Replace with the actual former name or remove.
        public string name { get; set; } = "Environment Light";

        public ExposedObject exposedObject => ExposedObjectRegistry.FindByTarget(this);

        public string id => kId;

        public EnvironmentLight()
        {
        }

        public void OnEnable()
        {
            ExposedObjectRegistry.Create<EnvironmentLight>(this, kId);
            _ApplyAmbient();
        }

        public void OnDisable()
        {
            ExposedObjectRegistry.FindByTarget(this)?.Unregister();
        }

        public void OnDispose()
        {
        }

        public void Update()
        {
        }

        public void Reset()
        {
        }

        [SerializeField, ExposedField, Hide]
        [FormerlyExposedAs("ambientLightSource")]
        private BackgroundType _ambientLightSource = BackgroundType.SolidColor;

        [ExposedProperty]
        public BackgroundType ambientLightSource
        {
            get => _ambientLightSource;
            set
            {
                _ambientLightSource = value;
                _ApplyAmbient();
            }
        }

        [SerializeField, ExposedField, Hide]
        [FormerlyExposedAs("ambientColor")]
        private Color _ambientColor = Color.gray;

        [ExposedProperty, ShowIf(nameof(ambientLightSource), (int)BackgroundType.SolidColor)]
        public Color ambientColor
        {
            get => _ambientColor;
            set
            {
                _ambientColor = value;
                _ApplyAmbient();
            }
        }

        [SerializeField, ExposedField, Hide]
        [FormerlyExposedAs("ambientIntensity")]
        private float _ambientIntensity = 1f;

        [ExposedProperty, Slider(0, 8, 0.1f), ShowIf(nameof(ambientLightSource), (int)BackgroundType.Skybox)]
        public float ambientIntensity
        {
            get => _ambientIntensity;
            set
            {
                _ambientIntensity = value;
                _ApplyAmbient();
            }
        }

        void _ApplyAmbient()
        {
            RenderSettings.ambientMode = _ambientLightSource == BackgroundType.SolidColor
                ? UnityEngine.Rendering.AmbientMode.Flat
                : UnityEngine.Rendering.AmbientMode.Skybox;
            RenderSettings.ambientLight = _ambientColor;
            RenderSettings.ambientIntensity = _ambientIntensity;
        }

        public void OnAfterExposedDeserialize() => _ApplyAmbient();
    }

}
