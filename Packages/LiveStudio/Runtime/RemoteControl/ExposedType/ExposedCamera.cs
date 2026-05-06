using System;
using System.Runtime.CompilerServices;
using UnityEngine;
using Unity.Cinemachine;
using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    public interface IExposedCamera
    {
        public bool isLive { get; }

        public Guid guid { get; }

        public string displayName { get; }

        public float aspect { get; }

        public Texture2D image { get; }

        public void RequestCameraImage();

        public void SetPriority(int priority);
    }

    [Serializable]
    [ExposedClass(Category = "Camera", Icon = "videocam")]
    public class ExposedCamera : ExposedUnityObjectProxy<ExposedCamera, CinemachineCamera>, IExposedCamera
    {
        public Guid guid => Guid.TryParse(id, out var guid) ? guid : Guid.Empty;

        private Texture2D _texture2D;

        public string displayName => _reference != null ? _reference.name : default(string);

        public bool isLive => _reference != null ? _reference.IsLive : false;

        public float aspect
        {
            get => _reference != null ? _reference.Lens.Aspect : default(float);
        }

        [ExposedFunction(label = "CAMERA_SWITCH")]
        public void Switch()
        {
            CameraService.SwitchCamera(guid);
        }


        [ExposedField, CameraController]
        [SerializeReference, Select]
        public ICameraController controller = new OrbitalFollowCameraController();

        public int priority
        {
            get => _reference != null ? _reference.Priority : default(int);
            set
            {
                if (_reference != null)
                {
                    _reference.Priority = value;
                    PropertyUtility.Apply(_reference);
                }
            }
        }

        public Texture2D image => _texture2D;

        public ExposedCamera() : base(null) { }

        public ExposedCamera(CinemachineCamera camera) : base(camera)
        {
            if (_reference != null)
            {
                controller?.Setup(_reference);
            }
        }

        public override void Update()
        {
            if (controller != null && _reference != null)
            {
                controller.Update(_reference);
            }
        }
        
        public void RequestCameraImage()
        {
            //Debug.Log($"RequestCameraImage: {displayName}, id:{this.id} reference:{_reference} ");
            if (_reference == null) return;

            CameraUtility.CalculateThumbnailSize(aspect, out int thumbnailWidth, out int thumbnailHeight);
            if (_texture2D == null || _texture2D.width != thumbnailWidth || _texture2D.height != thumbnailHeight)
            {
                GetOrCreateTexture(thumbnailWidth, thumbnailHeight);
            }

            var renderCamera = _FindRenderCamera();
            if (_reference.gameObject && renderCamera != null)
            {
                CaptureCamera(renderCamera, _reference, _texture2D);
            }
        }


        public void SetPriority(int priority)
        {
            this.priority = priority;
            ForceUpdateCinemachineBrain();
        }

        private void ForceUpdateCinemachineBrain()
        {
#if UNITY_EDITOR
            // Editorモードでない場合は何もしない
            if (UnityEditor.EditorApplication.isPlaying) return;

            // Priority=1のカメラを検索してSoloCameraに設定
            if (this.priority == 1)
            {
                // ExposedCameraの内部参照であるCinemachineCameraを取得
                var cinemachineCamera = this.reference as Unity.Cinemachine.CinemachineCamera;
                if (cinemachineCamera != null)
                {
                    // SOLOボタンと同じ効果: SoloCameraに設定
                    Unity.Cinemachine.CinemachineCore.SoloCamera = cinemachineCamera;

                    // Scene Viewの更新
                    UnityEditor.SceneView.RepaintAll();
                    return;
                }
            }
#endif
        }
        
        public Texture2D GetOrCreateTexture(int width, int height)
        {
            if (_texture2D != null)
            {
                Lilium.RemoteControl.GameObjectUtility.Destroy(_texture2D);
                _texture2D = null;
            }
            if (_texture2D == null)
            {
                _texture2D = new Texture2D(width, height, TextureFormat.RGB24, false);
            }
            return _texture2D;
        }
        
        private static Camera _cachedRenderCamera;

        /// <summary>
        /// CinemachineBrainを持つCameraを検索する。Camera.mainが無い場合のフォールバック。
        /// </summary>
        private static Camera _FindRenderCamera()
        {
            // キャッシュが有効ならそれを返す
            if (_cachedRenderCamera != null)
                return _cachedRenderCamera;

            // Camera.mainがあればそれを使う
            if (Camera.main != null)
            {
                _cachedRenderCamera = Camera.main;
                return _cachedRenderCamera;
            }

            // CinemachineBrainを持つCameraを探す
            var brain = UnityEngine.Object.FindAnyObjectByType<CinemachineBrain>();
            if (brain != null)
            {
                _cachedRenderCamera = brain.GetComponent<Camera>();
                return _cachedRenderCamera;
            }

            return null;
        }

        static void CaptureCamera(Camera camera, CinemachineCamera cinemachineCamera, Texture2D texture2D)
        {
            try
            {
                var width = texture2D.width;
                var height = texture2D.height;

                var positionPrev = camera.transform.position;
                var rotationPrev = camera.transform.rotation;
                var fieldOfViewPrev = camera.fieldOfView;
                camera.transform.position = cinemachineCamera.State.RawPosition;
                camera.transform.rotation = cinemachineCamera.State.RawOrientation;
                camera.fieldOfView = cinemachineCamera.Lens.FieldOfView;

                var targetCamera = camera.targetTexture;

                RenderTexture renderTexture = new RenderTexture(width, height, 24);
                camera.targetTexture = renderTexture;
                camera.Render();

                RenderTexture.active = renderTexture;
                texture2D.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                texture2D.Apply();

                RenderTexture.active = null;
                Lilium.RemoteControl.GameObjectUtility.Destroy(renderTexture);

                camera.targetTexture = targetCamera;
                camera.transform.position = positionPrev;
                camera.transform.rotation = rotationPrev;
                camera.fieldOfView = fieldOfViewPrev;
            }
            finally
            {
            }
        }
        
        public void Dispose()
        {
            if (_texture2D != null)
            {
                Lilium.RemoteControl.GameObjectUtility.Destroy(_texture2D);
                _texture2D = null;
            }
        }
        


        public override void OnEnable()
        {
            base.OnEnable();
            Service<IExposedCamera>.Register(this);

            controller?.Setup(_reference);

            if (_exposedObject?.targetType != null)
                _exposedObject.targetType.onPropertyChanged += _OnPropertyChanged;
        }

        public override void OnDisable()
        {
            if (_exposedObject?.targetType != null)
                _exposedObject.targetType.onPropertyChanged -= _OnPropertyChanged;

            controller?.Teardown(_reference);

            base.OnDisable();
            Service<IExposedCamera>.Unregister(this);
        }

        private void _OnPropertyChanged(ExposedProperty property, object oldValue)
        {
            // 自分自身のExposedObjectの変更のみ処理する
            if (property.owner != _exposedObject) return;
            if (property.type.name != "controller") return;

            if (oldValue is ICameraController oldController)
                oldController.Teardown(_reference);

            if (property.GetValue() is ICameraController newController)
                newController.Setup(_reference);
        }

    }
}