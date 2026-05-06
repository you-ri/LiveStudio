// Copyright (c) You-Ri, 2026

using System;
using System.IO;

using UnityEngine;

using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    [DefaultExecutionOrder(250)]
    [ExposedClass("VRMAvatarSource", Category = "Avatar", Icon = "deployed_code")]
    public class VRMAvatarSource : MonoBehaviour, IAvatarSource, IVRMLoadObserver
    {
        public event Action<GameObject> onAvatarReady;

        [SerializeField]
        [ExposedField(label = "AVATAR_MODELFILEPATH"), GLTFFileSelector("vrm")]
        [ExposedHelp("AVATAR_VRMMODELFILEPATH_HELP")]
        string _modelFilePath;

        public string modelFilePath => _modelFilePath;

        public void RequestLoadVRM(string filepath)
        {
            _modelFilePath = filepath;
            _LoadIfFileExists();
        }

        void OnEnable()
        {
            Service<IVRMLoadObserver>.Register(this);
            ExposedClass.Get<VRMAvatarSource>().onPropertyChanged += OnPropertyChanged;
        }

        void OnDisable()
        {
            ExposedClass.Get<VRMAvatarSource>().onPropertyChanged -= OnPropertyChanged;
            Service<IVRMLoadObserver>.Unregister(this);
        }

        void _LoadIfFileExists()
        {
            if (File.Exists(_modelFilePath))
            {
                _ = VRMLoader.LoadVRMModel(_modelFilePath, this.transform);
            }
        }

        void OnPropertyChanged(ExposedProperty property, object oldValue)
        {
            if (property.PathContains(nameof(_modelFilePath)))
            {
                _LoadIfFileExists();
            }
        }

        void IVRMLoadObserver.OnVRMLoadStarted(string filePath)
        {
        }

        void IVRMLoadObserver.OnVRMLoaded(GameObject newTarget)
        {
            Debug.Assert(newTarget != null);
            onAvatarReady?.Invoke(newTarget);
        }

        void IVRMLoadObserver.OnVRMLoadError(string error)
        {
            Debug.LogError($"[LiveStudio] VRM Load Error: {error}");
        }

        void IVRMLoadObserver.OnVRMLoadProgress(float progress)
        {
        }
    }
}
