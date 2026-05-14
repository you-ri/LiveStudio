using System;
using UnityEngine;
using Unity.Cinemachine;
using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    [Serializable]
    [ExposedClass]
    public class OrbitalFollowCameraController : ICameraController, IExposedSerializeCallback, IExposedDeserializeCallback
    {
        [SerializeField, ExposedField]
        TransformRef _target = new TransformRef("Main Avatar", "Head", TransformRef.SearchType.Name);

        public TransformRef target => _target;

        [SerializeField, ExposedField, Hide]
        [FormerlyExposedAs("roll")]
        private float _roll = 0f;

        [ExposedProperty, Slider(-180f, 180f, 1f)]
        public float roll
        {
            get => _roll;
            set
            {
                _roll = value;
                if (_orbitalFollow != null)
                {
                    _orbitalFollow.HorizontalAxis.Value = value;
                }
            }
        }

        [SerializeField, ExposedField, Hide]
        [FormerlyExposedAs("pitch")]
        private float _pitch = 0f;

        [ExposedProperty, Slider(-180f, 180f, 1f)]
        public float pitch
        {
            get => _pitch;
            set
            {
                _pitch = value;
                if (_orbitalFollow != null)
                {
                    _orbitalFollow.VerticalAxis.Value = value;
                }
            }
        }

        [SerializeField, ExposedField, Hide]
        [FormerlyExposedAs("distance")]
        private float _distance = 1f;

        [ExposedProperty, Slider(0.2f, 10f, 0.2f)]
        public float distance
        {
            get => _distance;
            set
            {
                _distance = value;
                if (_orbitalFollow != null)
                {
                    _orbitalFollow.RadialAxis.Value = value;
                }
            }
        }

        [SerializeField, ExposedField, Hide]
        [FormerlyExposedAs("fov")]
        private float _fov = 40f;

        [ExposedProperty, Slider(1f, 80f, 1f)]
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

        [SerializeField, ExposedField, Hide]
        [FormerlyExposedAs("screenPosition")]
        private Vector2 _screenPosition = Vector2.zero;

        [ExposedProperty]
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

        CinemachineOrbitalFollow _orbitalFollow;

        CinemachineRotationComposer _rotationComposer;

        CinemachineCamera _camera;

        public override void Setup(CinemachineCamera camera)
        {
            if (camera == null) return;

            _orbitalFollow = Lilium.RemoteControl.GameObjectUtility.GetOrAddComponent<CinemachineOrbitalFollow>(camera.gameObject);
            _orbitalFollow.Radius = 1;
            _rotationComposer = Lilium.RemoteControl.GameObjectUtility.GetOrAddComponent<CinemachineRotationComposer>(camera.gameObject);
            _camera = camera;

            _target.onChanged += _OnTargetChanged;
            TransformStructureService.onStructureChanged += _OnStructureChanged;

            _ApplyTarget();
            // _ApplyCameraSettings() および Radius のハードコードはここでは呼ばない。
            // Setup は ExposedCamera.OnEnable から呼ばれるため、シリアライズされた default 値で
            // Cinemachine の Inspector 設定 (Radius / axes / FOV / ScreenPosition) を上書きしてしまう。
            // JSON ロード時のみ OnAfterExposedDeserialize で適用する。
        }

        void _ApplyCameraSettings()
        {
            if (_orbitalFollow != null)
            {
                _orbitalFollow.HorizontalAxis.Value = _roll;
                _orbitalFollow.VerticalAxis.Value = _pitch;
                _orbitalFollow.RadialAxis.Value = _distance;
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

        public void OnBeforeExposedSerialize()
        {
            if (_orbitalFollow != null)
            {
                _roll = _orbitalFollow.HorizontalAxis.Value;
                _pitch = _orbitalFollow.VerticalAxis.Value;
                _distance = _orbitalFollow.RadialAxis.Value;
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

        public void OnAfterExposedDeserialize() => _ApplyCameraSettings();

        public override void Teardown(CinemachineCamera camera)
        {
            if (camera == null) return;

            _target.onChanged -= _OnTargetChanged;
            TransformStructureService.onStructureChanged -= _OnStructureChanged;

            Lilium.RemoteControl.GameObjectUtility.RemoveComponent<CinemachineOrbitalFollow>(camera.gameObject, immediate: true);
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
            // 解決できない場合は null のまま残し、StudioCameraLookAt の socket("head") フォールバックに委ねる
            _camera.Target.TrackingTarget = _target.Resolve();
        }

    }
}
