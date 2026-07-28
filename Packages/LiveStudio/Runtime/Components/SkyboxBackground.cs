// Copyright (c) You-Ri, 2026
using System;
using Lilium.RemoteControl;
using Lilium.RemoteControl.Utility;
using UnityEngine;

namespace Lilium.LiveStudio
{

    [LiveEnum]
    public enum BackgroundMode
    {
        Cubemap,
        Image,
    }

    [LiveEnum]
    public enum ImageFitMode
    {
        AutoFit,
        FitWidth,
        FitHeight,
    }

    [Serializable]
    [LiveClass(Category = "Background", Icon = "landscape")]
    public class SkyboxBackground : ILiveObject, ILiveDeserializeCallback
    {
        const string kId = "93258b4a-7f2a-40ac-8d0d-0782f155e364";

        static readonly int kMainTexId = Shader.PropertyToID("_MainTex");

        static readonly int kScaleOffsetId = Shader.PropertyToID("_ScaleOffset");

        public string name { get; set; } = "Skybox Background";

        public LiveObjectHandle? liveObject => LiveObjectRegistry.FindByTarget(this);

        public string id => kId;

        [NonSerialized] Cubemap _skyboxCubemap;
        [NonSerialized] Material _cubemapMaterial;
        [NonSerialized] Material _imageMaterial;
        [NonSerialized] Material _defaultSkybox;
        [NonSerialized] bool _dirty = true;

        public SkyboxBackground()
        {
        }


        [LiveProperty]
        public BackgroundMode backgroundMode
        {
            get => _backgroundMode;
            set
            {
                _backgroundMode = value;
                _ApplyTexture();
                _ApplyMode();
            }
        }

        [LiveField, Hide]
        [FormerlyNamedAs("backgroundMode")]
        BackgroundMode _backgroundMode = BackgroundMode.Cubemap;

        [LiveProperty]
        [Help("BACKGROUND_TEXTURE")]
        public ExternalTexture backgroundTexture
        {
            get => _backgroundTexture;

            set
            {
                if (value == null) return;

                if (value.texture == null)
                {
                    if (_defaultSkybox != null && _backgroundMode == BackgroundMode.Cubemap)
                    {
                        RenderSettings.skybox = _defaultSkybox;
                    }
                }
                else
                {
                    _backgroundTexture = value;
                    _backgroundTexture.Reload();
                    _ApplyTexture();
                }
            }
        }

        [LiveField, Hide]
        [FormerlyNamedAs("backgroundTexture")]
        private ExternalTexture _backgroundTexture;

        // --- Cubemap mode ---

        [LiveField, Hide]
        [FormerlyNamedAs("skyboxRotation")]
        private float _skyboxRotation = 0f;

        [LiveProperty, Slider(-180f, 180f, 1f), ShowIf(nameof(backgroundMode), (int)BackgroundMode.Cubemap)]
        public float skyboxRotation
        {
            get => _skyboxRotation;
            set
            {
                _skyboxRotation = value;
                _ApplySkyboxShaderProperties();
            }
        }

        [LiveField, Hide]
        [FormerlyNamedAs("skyboxExposure")]
        private float _skyboxExposure = 1f;

        [LiveProperty, Slider(0f, 8f, 0.1f), ShowIf(nameof(backgroundMode), (int)BackgroundMode.Cubemap)]
        public float skyboxExposure
        {
            get => _skyboxExposure;
            set
            {
                _skyboxExposure = value;
                _ApplySkyboxShaderProperties();
            }
        }

        // --- Image mode ---

        [LiveProperty, ShowIf(nameof(backgroundMode), (int)BackgroundMode.Image)]
        public ImageFitMode imageFitMode
        {
            get => _imageFitMode;
            set
            {
                _imageFitMode = value;
                _dirty = true;
            }
        }

        [LiveField, Hide]
        [FormerlyNamedAs("imageFitMode")]
        ImageFitMode _imageFitMode = ImageFitMode.AutoFit;

        static bool _HasMaterialProperty(Material mat, string name)
        {
            return mat != null && mat.HasProperty(name);
        }

        public void OnEnable()
        {
            _defaultSkybox = RenderSettings.skybox;
            _backgroundMode = RenderSettings.skybox != null && RenderSettings.skybox.HasProperty(kMainTexId)
                ? BackgroundMode.Image
                : BackgroundMode.Cubemap;

            _InitializeMaterials();
            LiveObjectRegistry.Create<SkyboxBackground>(this, kId);

            Lilium.RemoteControl.LiveScene.RemoteControlBehaviour.onBaseSceneReloaded += _OnBaseSceneReloaded;

            _ApplyAll();
        }

        public void OnAfterLiveDeserialize() => _ApplyAll();

        // After a base-scene switch (persistent host), Unity reset RenderSettings to the new scene's
        // authored skybox. Re-capture it as the default and re-apply our background on top, so the
        // "no texture -> restore default" path uses the new scene's skybox, not the old one's. The
        // background mode and texture are left to the saved-data restore (or kept as-is) so a base-scene
        // switch does not clobber the user's chosen mode.
        private void _OnBaseSceneReloaded()
        {
            _defaultSkybox = RenderSettings.skybox;
            _ApplyAll();
        }

        void _ApplyAll()
        {
            // Ensure materials exist: OnAfterLiveDeserialize can run before OnEnable,
            // so we cannot rely on _InitializeMaterials() having been called yet.
            _InitializeMaterials();

            // Reload persisted background texture (previously done in property setter)
            _backgroundTexture?.Reload();

            _SaveAndApply();

            _ApplyMode();
        }

        public void OnDisable()
        {
            Lilium.RemoteControl.LiveScene.RemoteControlBehaviour.onBaseSceneReloaded -= _OnBaseSceneReloaded;

            _RestoreSkybox();

            _DestroyMaterials();

            LiveObjectRegistry.FindByTarget(this)?.Unregister();
        }

        public void OnDispose()
        {
            OnDisable();
        }

        public void Update()
        {
            if (_backgroundMode != BackgroundMode.Image) return;

            var tex = _backgroundTexture?.texture;
            if (tex == null) return;

            if (_dirty)
            {
                _UpdateScaleOffset(tex);
                _dirty = false;
            }
        }

        public void Reset()
        {
        }

        void _InitializeMaterials()
        {
            if (_cubemapMaterial == null)
            {
                // Skybox/Cubemap is a built-in shader; in player builds it is stripped unless it is
                // referenced by a scene material or listed in Graphics > Always Included Shaders.
                // Guard against a null shader so a missing skybox shader does not abort the caller
                // (this runs from LiveObjectContainer.Initialize and must not throw).
                var cubemapShader = Shader.Find("Skybox/Cubemap");
                if (cubemapShader == null)
                {
                    Debug.LogError("[Studio] Skybox/Cubemap shader not found. Add it to Graphics > Always Included Shaders for player builds.");
                }
                else
                {
                    _cubemapMaterial = new Material(cubemapShader);
                }
            }

            if (_imageMaterial == null)
            {
                var shader = Shader.Find("Skybox/ImageBackground");
                if (shader == null)
                {
                    Debug.LogError("[Studio] ImageBackground shader not found.");
                    return;
                }
                _imageMaterial = new Material(shader);
            }
        }

        void _DestroyMaterials()
        {
            if (_cubemapMaterial != null)
            {
                UnityEngine.Object.DestroyImmediate(_cubemapMaterial);
                _cubemapMaterial = null;
            }

            if (_imageMaterial != null)
            {
                UnityEngine.Object.DestroyImmediate(_imageMaterial);
                _imageMaterial = null;
            }
        }

        void _SaveAndApply()
        {
            _ApplyTexture();
            _ApplyMode();
            _dirty = true;
        }

        void _RestoreSkybox()
        {
            RenderSettings.skybox = _defaultSkybox;
        }

        void _ApplyTexture()
        {
            var tex = _backgroundTexture?.texture;
            if (tex == null)
            {
                _skyboxCubemap = null;
                return;
            }

            switch (_backgroundMode)
            {
                case BackgroundMode.Cubemap:
                    _skyboxCubemap = Texture2DToCubemap.FromEquirectangular(
                        tex,
                        cubemapSize: 1024,
                        format: TextureFormat.RGB24,
                        mipmap: true
                    );
                    _cubemapMaterial.SetTexture("_Tex", _skyboxCubemap);
                    RenderSettings.skybox = _cubemapMaterial;
                    break;

                case BackgroundMode.Image:
                    _imageMaterial.SetTexture(kMainTexId, tex);
                    RenderSettings.skybox = _imageMaterial;
                    _dirty = true;
                    break;
            }
        }

        void _ApplyMode()
        {
            switch (_backgroundMode)
            {
                case BackgroundMode.Cubemap:
                    if (_skyboxCubemap != null)
                        RenderSettings.skybox = _cubemapMaterial;
                    else
                        RenderSettings.skybox = _defaultSkybox;
                    break;

                case BackgroundMode.Image:
                    if (_imageMaterial != null)
                        RenderSettings.skybox = _imageMaterial;
                    _dirty = true;
                    break;
            }

            _ApplySkyboxShaderProperties();
        }

        void _ApplySkyboxShaderProperties()
        {
            var skyboxMat = RenderSettings.skybox;
            if (_HasMaterialProperty(skyboxMat, "_Rotation"))
                skyboxMat.SetFloat("_Rotation", _skyboxRotation);
            if (_HasMaterialProperty(skyboxMat, "_Exposure"))
                skyboxMat.SetFloat("_Exposure", _skyboxExposure);
        }

        void _UpdateScaleOffset(Texture tex)
        {
            float texAspect = (float)tex.width / tex.height;
            float scrAspect = (float)Screen.width / Screen.height;

            Vector4 scaleOffset;

            switch (_imageFitMode)
            {
                case ImageFitMode.FitWidth:
                {
                    float scaleY = texAspect / scrAspect;
                    scaleOffset = new Vector4(1f, scaleY, 0f, (1f - scaleY) * 0.5f);
                    break;
                }
                case ImageFitMode.FitHeight:
                {
                    float scaleX = scrAspect / texAspect;
                    scaleOffset = new Vector4(scaleX, 1f, (1f - scaleX) * 0.5f, 0f);
                    break;
                }
                default: // AutoFit
                {
                    if (scrAspect > texAspect)
                    {
                        float scaleY = texAspect / scrAspect;
                        scaleOffset = new Vector4(1f, scaleY, 0f, (1f - scaleY) * 0.5f);
                    }
                    else
                    {
                        float scaleX = scrAspect / texAspect;
                        scaleOffset = new Vector4(scaleX, 1f, (1f - scaleX) * 0.5f, 0f);
                    }
                    break;
                }
            }

            _imageMaterial.SetVector(kScaleOffsetId, scaleOffset);
        }
    }
}
