using System;
using UnityEngine;
using Lilium.RemoteControl;
using UnityEngine.Serialization;
using System.Configuration;


namespace Lilium.LiveStudio
{
    [Serializable]
    [LiveClass(Category = "Light", Icon = "light_mode")]
    public class EnvironmentLight : ILiveObject, ILiveDeserializeCallback
    {
        const string kId = "d9714ab0-e81b-44c8-9e76-22f177864ebe";

        public string name { get; set; } = "Environment Light";

        public LiveObjectHandle? liveObject => LiveObjectRegistry.FindByTarget(this);

        public string id => kId;

        public EnvironmentLight()
        {
        }

        public void OnEnable()
        {
            LiveObjectRegistry.Create<EnvironmentLight>(this, kId);

            _ambientLightSource = RenderSettings.ambientMode == UnityEngine.Rendering.AmbientMode.Flat
                ? BackgroundType.SolidColor
                : BackgroundType.Skybox;
            _ambientColor = RenderSettings.ambientLight;
            _ambientIntensity = RenderSettings.ambientIntensity;

            Lilium.RemoteControl.LiveScene.RemoteControlBehaviour.onBaseSceneReloaded += _OnBaseSceneReloaded;

            _ApplyAmbient();
        }

        public void OnDisable()
        {
            Lilium.RemoteControl.LiveScene.RemoteControlBehaviour.onBaseSceneReloaded -= _OnBaseSceneReloaded;
            LiveObjectRegistry.FindByTarget(this)?.Unregister();
        }

        // After a base-scene switch (persistent host), Unity reset RenderSettings.ambient* to the new
        // scene's authored values. Re-assert our current settings so the user's ambient persists across
        // the switch (a saved live scene still overrides these afterwards via OnAfterLiveDeserialize).
        private void _OnBaseSceneReloaded() => _ApplyAmbient();

        public void OnDispose()
        {
        }

        public void Update()
        {
        }

        public void Reset()
        {
        }

        [LiveField, Hide]
        [FormerlyNamedAs("ambientLightSource")]
        private BackgroundType _ambientLightSource = BackgroundType.SolidColor;

        [LiveProperty]
        public BackgroundType ambientLightSource
        {
            get => _ambientLightSource;
            set
            {
                _ambientLightSource = value;
                _ApplyAmbient();
            }
        }

        [LiveField, Hide]
        [FormerlyNamedAs("ambientColor")]
        private Color _ambientColor = Color.gray;

        [LiveProperty, ShowIf(nameof(ambientLightSource), (int)BackgroundType.SolidColor)]
        public Color ambientColor
        {
            get => _ambientColor;
            set
            {
                _ambientColor = value;
                _ApplyAmbient();
            }
        }

        [LiveField, Hide]
        [FormerlyNamedAs("ambientIntensity")]
        private float _ambientIntensity = 1f;

        [LiveProperty, Slider(0, 8, 0.1f), ShowIf(nameof(ambientLightSource), (int)BackgroundType.Skybox)]
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

        public void OnAfterLiveDeserialize() => _ApplyAmbient();
    }

}
