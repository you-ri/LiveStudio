using System;
using UnityEngine;
using Unity.Cinemachine;
using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    [Serializable]
    [LiveClass]
    public class OrbitalFollowCameraController : ICameraController, ILiveSerializeCallback, ILiveDeserializeCallback
    {
        [SerializeField, LiveField]
        TransformRef _target = new TransformRef("Main Avatar", "S_Head", TransformRef.SearchType.Name);

        public TransformRef target => _target;

        [SerializeField, LiveField, Hide]
        [FormerlyNamedAs("yaw")]
        private float _yaw = 0f;

        [LiveProperty, Slider(-180f, 180f, 1f)]
        [FormerlyNamedAs("roll")]
        public float yaw
        {
            get => _yaw;
            set
            {
                _yaw = value;
                if (_orbit != null)
                {
                    _orbit.yaw = value;
                }
            }
        }

        [SerializeField, LiveField, Hide]
        [FormerlyNamedAs("pitch")]
        private float _pitch = 0f;

        [LiveProperty, Slider(-180f, 180f, 1f)]
        public float pitch
        {
            get => _pitch;
            set
            {
                _pitch = value;
                if (_orbit != null)
                {
                    _orbit.pitch = value;
                }
            }
        }

        [SerializeField, LiveField, Hide]
        [FormerlyNamedAs("distance")]
        private float _distance = 1f;

        [LiveProperty, Slider(0.2f, 10f, 0.2f)]
        public float distance
        {
            get => _distance;
            set
            {
                _distance = value;
                if (_orbit != null)
                {
                    _orbit.distance = value;
                }
            }
        }

        [SerializeField, LiveField, Hide]
        [FormerlyNamedAs("fov")]
        private float _fov = 40f;

        [LiveProperty, Slider(1f, 80f, 1f)]
        public float fov
        {
            get => _fov;
            set
            {
                _fov = value;
                if (_camera != null)
                {
                    _camera.Lens.FieldOfView = value;
                }
            }
        }

        [SerializeField, LiveField, Hide]
        [FormerlyNamedAs("screenPosition")]
        private Vector2 _screenPosition = Vector2.zero;

        [LiveProperty]
        public Vector2 screenPosition
        {
            get => _screenPosition;
            set
            {
                _screenPosition = value;
                if (_rotationComposer != null)
                {
                    _rotationComposer.Composition.ScreenPosition = value;
                }
            }
        }

        LiveStudioOrbitalFollow _orbit;

        CinemachineRotationComposer _rotationComposer;

        CinemachineCamera _camera;

        public override void Setup(CinemachineCamera camera)
        {
            if (camera == null) return;

            _orbit = Lilium.RemoteControl.GameObjectUtility.GetOrAddComponent<LiveStudioOrbitalFollow>(camera.gameObject);
            _rotationComposer = Lilium.RemoteControl.GameObjectUtility.GetOrAddComponent<CinemachineRotationComposer>(camera.gameObject);
            _camera = camera;

            _target.onChanged += _OnTargetChanged;
            TransformStructureService.onStructureChanged += _OnStructureChanged;

            _ApplyTarget();
            // _ApplyCameraSettings() および Radius のハードコードはここでは呼ばない。
            // Setup は LiveCamera.OnEnable から呼ばれるため、シリアライズされた default 値で
            // Cinemachine の Inspector 設定 (Radius / axes / FOV / ScreenPosition) を上書きしてしまう。
            // JSON ロード時のみ OnAfterLiveDeserialize で適用する。
        }

        void _ApplyCameraSettings()
        {
            if (_orbit != null)
            {
                _orbit.yaw = _yaw;
                _orbit.pitch = _pitch;
                _orbit.distance = _distance;
            }
            if (_camera != null)
            {
                var lens = _camera.Lens;
                lens.FieldOfView = _fov;
                _camera.Lens = lens;
            }
            if (_rotationComposer != null)
            {
                _rotationComposer.Composition.ScreenPosition = _screenPosition;
            }
        }

        public void OnBeforeLiveSerialize()
        {
            if (_orbit != null)
            {
                _yaw = _orbit.yaw;
                _pitch = _orbit.pitch;
                _distance = _orbit.distance;
            }
            if (_camera != null)
            {
                _fov = _camera.Lens.FieldOfView;
            }
            if (_rotationComposer != null)
            {
                _screenPosition = _rotationComposer.Composition.ScreenPosition;
            }
        }

        public void OnAfterLiveDeserialize() => _ApplyCameraSettings();

        public override void Teardown(CinemachineCamera camera)
        {
            if (camera == null) return;

            _target.onChanged -= _OnTargetChanged;
            TransformStructureService.onStructureChanged -= _OnStructureChanged;

            Lilium.RemoteControl.GameObjectUtility.RemoveComponent<LiveStudioOrbitalFollow>(camera.gameObject, immediate: true);
            Lilium.RemoteControl.GameObjectUtility.RemoveComponent<CinemachineRotationComposer>(camera.gameObject, immediate: true);

            _camera = null;
        }


        public override void Update(CinemachineCamera camera)
        {
            if (camera == null) return;

            //camera.UpdateCameraState(Vector3.up, Time.deltaTime);
        }

        void _OnTargetChanged() => _ApplyTarget();

        /// <summary>
        /// owner GameObject の内部 hierarchy 変化通知。ownerName 一致時のみ target を再 resolve する。
        /// </summary>
        void _OnStructureChanged(GameObject owner)
        {
            if (owner == null) return;
            if (_target.ownerName != owner.name) return;
            _ApplyTarget();
        }

        void _ApplyTarget()
        {
            if (_camera == null) return;
            // Drive both position (orbit) and aim from a single tracking target: the RotationComposer's
            // LookAt falls back to the tracking target when CustomLookAtTarget is off, so force it off and
            // clear any stale custom LookAt left by a previous controller. The tracking target may resolve
            // to null until the avatar is registered; it is retried on the next change / structure event.
            _camera.Target.CustomLookAtTarget = false;
            _camera.Target.LookAtTarget = null;
            _camera.Target.TrackingTarget = _target.Resolve();
        }

    }
}
