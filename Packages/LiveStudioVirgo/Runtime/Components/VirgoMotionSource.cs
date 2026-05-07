using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
using Unity.Collections.LowLevel.Unsafe;

using Lilium.LiveStudio;
using Lilium.LiveStudio.Virgo.Networking;
using Lilium.RemoteControl;


namespace Lilium.LiveStudio.Virgo
{
    
    public static class FusionNetwork
    {
        public const string BaseURL = "http://127.0.0.1:3005";

        public static bool isConnected;

        public static System.Action onSSEConnected;

        public static System.Action onSSEDisconnected;


        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Initialize()
        {
            isConnected = false;
            onSSEConnected = null;
            onSSEDisconnected = null;
        }

    }

    [DefaultExecutionOrder(-100)]
    [ExposedClass("VirgoMotionSource", Icon = "accessibility", Category = "Motion")]
    [MovedFrom(false, "Lilium.Virgo.Studio", "Lilium.Virgo.Studio2", null)]
    public class VirgoMotionSource : MotionSourceBase, IAvatarBuildObserver
    {
        const int kResetCameraDelayCount = 2; // 受信してから何フレーム目でカメラリセットするか。受信した情報が安定していない可能性があるため、数フレーム遅らせる。

        public int port
        {
            get { return _port; }
            set { _port = value; }
        }

        [SerializeField]
        [ExposedField]
        private int _port = 0;

        private UDPConnection _udpConnection = new UDPConnection();

        FrameBuffer<AvatarAnimationData> _animationFrameBuffer = new FrameBuffer<AvatarAnimationData>(30);

        private System.DateTime _startupTime;

        private Timecode _timecode;

        private FrameRate _frameRate = FrameRate.FPS60;

        private long frameNumber => _timecode.ToFrameNumber(_frameRate);

        private int _frameOffset;

        [SerializeField]
        private Vector3 _offsetPosition = Vector3.zero;

        [SerializeField]
        private Vector3 _offsetRotation = Vector3.zero;

        private AvatarAnimationData _lastReceivedFrameData;


        private Vector3 _position;

        private Quaternion _rotation = Quaternion.identity;

        /// <summary>
        /// 自動でカメラリセットを行うかどうか
        /// </summary>
        public bool resetCameraAtReceived = true;

        private int _receivedFrameCount = 0;

        void OnEnable()
        {
            Lilium.RemoteControl.Service<IAvatarBuildObserver>.Register(this);
            Open();
        }

        void OnDisable()
        {
            Lilium.RemoteControl.Service<IAvatarBuildObserver>.Unregister(this);
            Close();
        }

        void IAvatarBuildObserver.OnAvatarBuilt(in AvatarBuildData data)
        {
            StartCoroutine(FusionRequestSystem.BuildAvatar(data));
        }


        void OnDestroy()
        {
            Close();
        }


        [ExposedFunction]
        public void Open()
        {
            if (_udpConnection.isOpened)
            {
                Close();
            }

            _udpConnection.onDataReceived += OnDataReceived;
            _udpConnection.Open(port);

        }

        public void Close()
        {
            _udpConnection.onDataReceived -= OnDataReceived;
            _udpConnection.Close();
        }

        void Update()
        {
            _position = this.transform.position;
            _rotation = this.transform.rotation;
        }

        void LateUpdate()
        {
            double time = Time.realtimeSinceStartupAsDouble;
            _timecode = new Timecode(time, _frameRate);            
        }

        unsafe void OnDataReceived(byte[] receivedData)
        {
            if (receivedData.Length != UnsafeUtility.SizeOf<AnimationFrameData>())
            {
                Debug.LogError("[Studio] Invalid data size");
                return;
            }

            AvatarAnimationData receivedFrameData;
            fixed (byte* pData = receivedData)
            {
                AnimationFrameData wireFrame;
                UnsafeUtility.CopyPtrToStructure(pData, out wireFrame);
                AnimationFrameBridge.ToLiveStudio(in wireFrame, out receivedFrameData);
                _lastReceivedFrameData = receivedFrameData;

                // 0フレーム目はまだ受信した情報が安定していないため、カメラリセットしない。
                if (_receivedFrameCount == kResetCameraDelayCount && resetCameraAtReceived)
                {
                    ResetCamera();
                }

                AvatarAnimationSystem.Transform(in receivedFrameData, Matrix4x4.TRS(_rotation * _offsetPosition + _position, _rotation * Quaternion.Euler(_offsetRotation), Vector3.one), out var transformedFrameData);
                _animationFrameBuffer.Set(receivedFrameData.frames, in transformedFrameData);

                _receivedFrameCount ++;
            }

            // 1フレーム遅延したデータを使うことで安定して同期するように
            if (_animationFrameBuffer.TryGet(frameNumber + _frameOffset - 1, out AvatarAnimationData gettingFrameData))
            {
                frameData = gettingFrameData;
            }
            else
            {
                _frameOffset = (int)(receivedFrameData.frames - frameNumber);
                //Debug.Log("[Studio] Frame data not found:  Adjust timing frames set to " + _adjustTimingFrames);
            }
        }

        [ContextMenu("Reset Camera")]
        [ExposedFunction]
        public override void ResetCamera()
        {
            _offsetRotation =  new Vector3(0, _lastReceivedFrameData.camera.rotation.eulerAngles.y - _lastReceivedFrameData.root.rotation.eulerAngles.y, 0);

            // オフセット適用後のカメラワールド位置を原点に合わせる。キャラクターも同じ行列で変換されるため、撮影時のカメラ-キャラクター相対位置は保たれる。
            var rotation = Quaternion.Euler(_offsetRotation);
            _offsetPosition = -(rotation * _lastReceivedFrameData.root.position) / _lastReceivedFrameData.root.scale.x; // スケールの影響を受けないようにする
        }
    }
}