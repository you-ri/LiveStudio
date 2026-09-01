using System;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
using Unity.Cinemachine;
using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    public interface ILiveCamera
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
    [LiveClass(Category = "Camera", Icon = "videocam")]
    [FormerlyNamedAs("ExposedCamera")]
    [MovedFrom(false, null, null, "ExposedCamera")]
    public partial class LiveCamera : LiveUnityObjectProxy<LiveCamera, CinemachineCamera>, ILiveCamera
    {
        public Guid guid => Guid.TryParse(id, out var guid) ? guid : Guid.Empty;

        private Texture2D _texture2D;

        public string displayName => _reference != null ? _reference.name : default(string);

        // Getter-only, so read-only on the wire and never persisted: both are derived from the
        // CinemachineCamera every time they are read. RemoteApp draws the LIVE badge and sizes the
        // preview from these, so they have to be on the generic object surface (the UE proxy
        // exposes the same two).
        [LiveProperty]
        public bool isLive => _reference != null ? _reference.IsLive : false;

        [LiveProperty, Hide]
        public float aspect
        {
            get => _reference != null ? _reference.Lens.Aspect : default(float);
        }

        [LiveFunction(label = "CAMERA_SWITCH")]
        public void Switch()
        {
            CameraService.SwitchCamera(guid);
        }


        [LiveField, CameraController]
        [SerializeReference, Select]
        public CameraControllerBase controller = new OrbitalFollowCameraController();

        // Shadow Field for priority. The live value lives on the CinemachineCamera (the Brain
        // derives the live camera from the highest priority), so persisting it is what makes the
        // active camera survive a save/load: SwitchCamera writes 1/0 through the property setter,
        // the scene file stores the values, and OnAfterLiveDeserialize re-applies them on load.
        // Synced from the reference in OnEnable so a file without the key (older saves) applies a
        // no-op instead of clobbering the scene-authored priority with the field's default.
        //
        // State lane, because a recording has to answer "which camera is live" at every frame, not
        // only at the frames where a switch happened: the event lane carries no snapshot of the
        // values a take started with, so scrubbing into the middle of a recording had no priority
        // to restore and every camera looked untouched. Priority rather than a derived "is live"
        // flag -- the Brain derives the live camera from priority, so the flag is the folded
        // result and cannot be folded back (restoring false says nothing about which value to
        // write, which breaks the ordering the next live switch depends on).
        [SerializeField, LiveField(lane = FrameLane.State), Hide]
        [FormerlyNamedAs("priority")]
        private int _priority;

        [LiveProperty, Hide]
        public int priority
        {
            get => _reference != null ? _reference.Priority : _priority;
            set
            {
                // Switching happens through direct C# writes (Switch() -> CameraService.SwitchCamera),
                // which the REST write path never sees — so without this, nothing lands in the change
                // feed and a remote client's LIVE badge freezes (the removed bespoke route used to push
                // a camera_update event instead). Record only real transitions: SwitchCamera writes
                // every camera on each switch, and the ones already at 0 must not spam the feed.
                var changed = priority != value;

                _priority = value;
                if (_reference != null)
                {
                    _reference.Priority = value;
                    PropertyUtility.Apply(_reference);
                }

                if (changed && !string.IsNullOrEmpty(id))
                {
                    LiveChangeLog.Record(id);
                }
            }
        }

        public override void OnBeforeLiveSerialize()
        {
            base.OnBeforeLiveSerialize();
            if (_reference != null) _priority = _reference.Priority;
        }

        public override void OnAfterLiveDeserialize()
        {
            base.OnAfterLiveDeserialize();
            if (_reference != null)
            {
                _reference.Priority = _priority;
                PropertyUtility.Apply(_reference);
            }
        }

        public Texture2D image => _texture2D;

        /// <summary>
        /// The camera's preview picture, served straight off the generic object surface: a direct
        /// GET of this member answers PNG bytes, and JSON reads carry the member's own address
        /// instead (see <see cref="LiveImageData"/>), so listing cameras never renders one.
        /// Renders into the cached thumbnail-sized texture and encodes per request — the client's
        /// poll is the refresh loop, exactly like the dedicated /live/camera/image route this
        /// replaces.
        /// </summary>
        [LiveProperty, ImagePreview]
        public LiveImageData preview
        {
            get
            {
                RequestCameraImage();
                if (_texture2D == null) return LiveImageData.none;
                return new LiveImageData(_texture2D.EncodeToPNG());
            }
        }

        public LiveCamera() : base(null) { }

        public LiveCamera(CinemachineCamera camera) : base(camera)
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
                // LiveCameraの内部参照であるCinemachineCameraを取得
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

                // Detach before releasing: destroying a RenderTexture that is still the camera's
                // targetTexture makes Unity log "Releasing render texture that is set as Camera.targetTexture!".
                camera.targetTexture = targetCamera;
                Lilium.RemoteControl.GameObjectUtility.Destroy(renderTexture);
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

            // Adopt the scene-authored priority as the shadow's starting value, so deserializing a
            // file that carries no priority key re-applies the current value (no-op) and the dirty
            // baseline starts from the authored state.
            if (_reference != null) _priority = _reference.Priority;

            Service<ILiveCamera>.Register(this);

            controller?.Setup(_reference);

            if (_liveObject?.targetType != null)
                _liveObject.Value.targetType.onPropertyChanged += _OnPropertyChanged;
        }

        public override void OnDisable()
        {
            if (_liveObject?.targetType != null)
                _liveObject.Value.targetType.onPropertyChanged -= _OnPropertyChanged;

            controller?.Teardown(_reference);

            base.OnDisable();
            Service<ILiveCamera>.Unregister(this);
        }

        private void _OnPropertyChanged(LiveProperty property, object oldValue)
        {
            // 自分自身のLiveObjectの変更のみ処理する
            if (property.owner != _liveObject) return;
            if (property.type.name != "controller") return;

            if (oldValue is CameraControllerBase oldController)
                oldController.Teardown(_reference);

            if (property.GetValue() is CameraControllerBase newController)
                newController.Setup(_reference);
        }

    }
}