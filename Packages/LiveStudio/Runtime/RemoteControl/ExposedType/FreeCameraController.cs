// Copyright (c) You-Ri, 2026
using System;
using UnityEngine;
using Unity.Cinemachine;
using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    [Serializable]
    [ExposedClass]
    public class FreeCameraController : ICameraController, IExposedSerializeCallback, IExposedDeserializeCallback
    {
        [SerializeField, ExposedField]
        TransformRef _target = new TransformRef("", "");

        public TransformRef target => _target;

        [SerializeField, ExposedField, Hide]
        [FormerlyExposedAs("yaw")]
        float _yaw;

        [ExposedProperty, Slider(-180f, 180f, 1f)]
        public float yaw
        {
            get => _yaw;
            set => _yaw = value;
        }

        [SerializeField, ExposedField, Hide]
        [FormerlyExposedAs("pitch")]
        float _pitch;

        [ExposedProperty, Slider(-89f, 89f, 1f)]
        public float pitch
        {
            get => _pitch;
            set => _pitch = Mathf.Clamp(value, -89f, 89f);
        }

        [SerializeField, ExposedField, Hide]
        [FormerlyExposedAs("position")]
        Vector3 _position;

        [ExposedProperty]
        public Vector3 position
        {
            get => _position;
            set => _position = value;
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

        CinemachineRotationComposer _rotationComposer;

        CinemachineCamera _camera;

        bool _seeded;

        public override void Setup(CinemachineCamera camera)
        {
            if (camera == null) return;

            _camera = camera;
            _SeedFromTransform();

            _target.onChanged += _OnTargetChanged;
            SelectableService<IAvatarService>.onRegistered += _OnAvatarRegistered;
            SelectableService<IAvatarService>.onUnregistered += _OnAvatarUnregistered;

            _ApplyTarget();
            // _ApplyCameraSettings() はここでは呼ばない。
            // Setup は ExposedCamera.OnEnable から呼ばれるため、シリアライズされた default 値で
            // Cinemachine の Inspector 設定 (FOV / ScreenPosition) を上書きしてしまう。
            // position / yaw / pitch は _SeedFromTransform で Inspector の Transform から seed
            // されているため、Update() で適用されても Inspector 値が保持される。
            // JSON ロード時のみ OnAfterExposedDeserialize で適用する。
        }

        void _ApplyCameraSettings()
        {
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
            SelectableService<IAvatarService>.onRegistered -= _OnAvatarRegistered;
            SelectableService<IAvatarService>.onUnregistered -= _OnAvatarUnregistered;

            Lilium.RemoteControl.GameObjectUtility.RemoveComponent<CinemachineRotationComposer>(camera.gameObject, immediate: true);

            _rotationComposer = null;
            _camera = null;
            _seeded = false;
        }

        public override void Update(CinemachineCamera camera)
        {
            if (camera == null) return;

            camera.transform.position = _position;
            if (_rotationComposer == null)
            {
                camera.transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            }
        }

        void _SeedFromTransform()
        {
            if (_seeded || _camera == null) return;

            Transform t = _camera.transform;
            if (_position == Vector3.zero && _yaw == 0f && _pitch == 0f)
            {
                _position = t.position;
                Vector3 euler = t.rotation.eulerAngles;
                _yaw = _WrapAngle(euler.y);
                _pitch = Mathf.Clamp(_WrapAngle(euler.x), -89f, 89f);
            }
            _seeded = true;
        }

        static float _WrapAngle(float angle)
        {
            angle %= 360f;
            if (angle > 180f) angle -= 360f;
            return angle;
        }

        void _OnTargetChanged() => _ApplyTarget();

        void _OnAvatarRegistered(string id, IAvatarService avatar)
        {
            avatar.onAvatarChanged += _OnAvatarChanged;
            _ApplyTarget();
        }

        void _OnAvatarUnregistered(string id, IAvatarService avatar)
        {
            avatar.onAvatarChanged -= _OnAvatarChanged;
            _ApplyTarget();
        }

        void _OnAvatarChanged()
        {
            _ApplyTarget();
        }

        void _ApplyTarget()
        {
            if (_camera == null) return;

            Transform resolved = _target.Resolve();
            _camera.Target.TrackingTarget = resolved;

            if (resolved != null)
            {
                _rotationComposer = Lilium.RemoteControl.GameObjectUtility.GetOrAddComponent<CinemachineRotationComposer>(_camera.gameObject);
            }
            else
            {
                Lilium.RemoteControl.GameObjectUtility.RemoveComponent<CinemachineRotationComposer>(_camera.gameObject, immediate: true);
                _rotationComposer = null;
            }
        }
    }
}
