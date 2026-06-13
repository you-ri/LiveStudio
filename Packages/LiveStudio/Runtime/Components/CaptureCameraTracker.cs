using UnityEngine;
using Unity.Cinemachine;
using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    [RequireComponent(typeof(CinemachineCamera))]
    public class CaptureCameraTracker : MonoBehaviour
    {
        public Vector3 offsetPosition = Vector3.zero;

        public Quaternion offsetRotation = Quaternion.Euler(0, 0, 0);

        public bool lockRoll = true;

        [SerializeField]
        [ExposedField(label = "CAMERATRACKER_MOTIONSOURCE"), ObjectSelector]
        [Tooltip("参照するモーションソース。未指定時はシーン内の MotionSourceBase を自動検出する。")]
        private MotionSourceBase _motionSource;

        [SerializeField]
        [Tooltip("Capture channel index to follow (0 = channel0, 1 = channel1).")]
        private int _channelIndex = 0;

        private CinemachineCamera _virtualCamera;

        void Awake()
        {
            _virtualCamera = GetComponent<CinemachineCamera>();
        }

        void Update()
        {
            if (_motionSource == null)
            {
                _motionSource = FindFirstObjectByType<MotionSourceBase>();
                if (_motionSource == null) return;
            }

            if (!_motionSource.frameData.isValid) return;

            if (_channelIndex < 0 || _channelIndex >= AvatarAnimationData.kCameraChannelCount) return;

            ref CameraData data = ref _motionSource.frameData.AsCamera(_channelIndex);

            _virtualCamera.transform.localPosition = offsetPosition + data.position;
            var eulerAngles = data.rotation.eulerAngles;
            if (lockRoll)
            {
                eulerAngles.z = 0f;
            }
            _virtualCamera.transform.localRotation = offsetRotation * Quaternion.Euler(eulerAngles);
            _virtualCamera.Lens.FieldOfView = data.fieldOfView;
        }



    }
}
