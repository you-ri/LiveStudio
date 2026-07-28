using System;
using Unity.Cinemachine;
using UnityEngine.Scripting.APIUpdating;
using Lilium.RemoteControl;

using UnityEngine;

namespace Lilium.LiveStudio
{
    [Serializable]
    [LiveClass]
    [MovedFrom(false, "Lilium.Virgo.Studio", "Lilium.Virgo.Studio2", null)]
    public class CaptureCameraController : ICameraController
    {
        private CaptureCameraTracker _captureCameraTracker;

        [LiveProperty(label="CAMERA_LOCKROLL")]
        public bool lockRoll
        {
            get => _lockRoll;
            set
            {
                _lockRoll = value;
                if (_captureCameraTracker != null)
                    _captureCameraTracker.lockRoll = value;
            }
        }

        [UnityEngine.SerializeField, LiveField, Hide]
        [FormerlyNamedAs("lockRoll")]
        private bool _lockRoll = true;

        [LiveProperty(label="CAMERA_CHANNELINDEX")]
        public int channelIndex
        {
            get => _channelIndex;
            set
            {
                _channelIndex = value;
                if (_captureCameraTracker != null)
                    _captureCameraTracker.channelIndex = value;
            }
        }

        // トラッカーは Setup/Teardown で transient に再生成されるため、channelIndex の
        // 信頼できる保存先はコントローラー側のこのフィールド。Setup でトラッカーへ焼き込む。
        [UnityEngine.SerializeField, LiveField, Hide]
        private int _channelIndex = 0;

        public override void Setup(CinemachineCamera camera)
        {
            if (camera == null) return;

            _captureCameraTracker = GameObjectUtility.GetOrAddComponent<CaptureCameraTracker>(camera.gameObject);
            _captureCameraTracker.lockRoll = _lockRoll;
            _captureCameraTracker.channelIndex = _channelIndex;
        }

        public override void Teardown(CinemachineCamera camera)
        {
            if (camera == null) return;

            GameObjectUtility.RemoveComponent<CaptureCameraTracker>(camera.gameObject, immediate: true);
            _captureCameraTracker = null;
        }

        public override void Update(CinemachineCamera camera)
        {
        }
    }
}
