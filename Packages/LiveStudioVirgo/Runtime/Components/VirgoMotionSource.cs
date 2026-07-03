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

        /// <summary>これまでに受信したフレーム数。受信検知のインジケータ用。</summary>
        public int receivedFrameCount => _receivedFrameCount;

        /// <summary>UDP 受信ソケットが開いているか。</summary>
        public bool isOpened => _udpConnection.isOpened;

        [SerializeField]
        [ExposedField]
        private int _port = 0;

        private UDPConnection _udpConnection = new UDPConnection();

        FrameBuffer<AvatarAnimationData> _animationFrameBuffer = new FrameBuffer<AvatarAnimationData>(30);

        private FrameRate _frameRate = FrameRate.FPS60;

        private int _frameOffset;

        // 受信フレームより何秒遅延させて再生するか。補間先 (i0+1) を在庫させるための余裕。
        // 大きいほど最新フレームを追い越して hold する頻度が減るが、表示レイテンシは増える。
        [SerializeField]
        [ExposedField]
        private float _delaySeconds = 0.0167f; // 約1フレーム (60fps)

        // Height (meters) above the anchor for the placed output. Reset zeroes the capture camera
        // position to the anchor, which would otherwise drop the virtual camera to the ground; lift
        // it to eye level so the avatar lands on the floor for a typical hand-held capture height.
        [SerializeField]
        [ExposedField]
        private float _cameraHeight = 1.6f;

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

        // バックグラウンドスレッドで ++、メインスレッド(エディタ)で読むため volatile で可視性を担保する。
        private volatile int _receivedFrameCount = 0;

        // Last received frame number, used to detect discontinuities in the sender's
        // frame numbering (gaps from real hitches / packet loss, or going backwards).
        // Written on the receive thread only.
        long _lastReceivedFrames = -1;

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
            // anchor 未設定時は従来通り自身の transform を配置基準にする。
            var anchorTransform = anchor != null ? anchor : this.transform;
            _position = anchorTransform.position;
            _rotation = anchorTransform.rotation;

            // 受信は 60fps 固定だが render は可変 fps。受信フレームをそのまま hold すると
            // フレーム間で姿勢が据え置きになり、揺れ物 (SpringBone) がその上で震える。
            // 連続 2 フレームを実時間から求めた係数で補間し、毎フレーム滑らかに進める。
            double localFrame = Time.realtimeSinceStartupAsDouble * _frameRate.AsDecimal();
            // _delaySeconds 分だけ遅延させ、補間先 (i0+1) が受信済みになるようにする。
            // 遅延が大きいほど先端を追い越しにくい。負値 (未来予測) は無効なので 0 でクランプ。
            double delayFrames = Mathf.Max(0f, _delaySeconds) * _frameRate.AsDecimal();
            double playbackPos = localFrame + _frameOffset - delayFrames;
            long i0 = (long)System.Math.Floor(playbackPos);

            // Gap-tolerant sampling: the frame numbering from Fusion is wall-clock
            // quantized, so a beat against its update phase periodically skips one
            // number (duplicate then a +2 jump). UDP loss also leaves holes. Instead
            // of requiring i0/i0+1 to exist, search nearby frames and interpolate
            // across the hole with the ratio recomputed for the actual span.
            const int kNeighborSearchFrames = 4;

            long prevNo = -1, nextNo = -1;
            AvatarAnimationData prev = default, next = default;
            for (long f = i0; f > i0 - kNeighborSearchFrames; f--)
            {
                if (_animationFrameBuffer.TryGet(f, out prev)) { prevNo = f; break; }
            }
            for (long f = i0 + 1; f <= i0 + kNeighborSearchFrames; f++)
            {
                if (_animationFrameBuffer.TryGet(f, out next)) { nextNo = f; break; }
            }

            if (prevNo >= 0 && nextNo >= 0)
            {
                float spanT = (float)((playbackPos - prevNo) / (nextNo - prevNo));
                AvatarAnimationSystem.Lerp(prev, next, spanT, out AvatarAnimationData sampled);
                frameData = sampled;
            }
            else if (prevNo >= 0)
            {
                // Caught up with the newest frame (or a short overrun past it):
                // hold the last available pose instead of resyncing, which would
                // jump the playback position backwards.
                frameData = prev;
            }
            else
            {
                // No frames anywhere near the playback position (startup, a large gap
                // or clock drift): resync the offset to the newest received frame.
                // frameCount is lock-protected and safe to read from the main thread.
                long latest = _animationFrameBuffer.frameCount - 1;
                if (latest >= 0)
                {
                    // A resync jumps the playback position, which is visible as a hitch —
                    // warn every time so it can be correlated with on-screen motion.
                    int newOffset = (int)(latest - (long)localFrame);
                    Debug.LogWarning($"[Studio] Resync playback position: playbackPos={playbackPos:F2} latest={latest} offsetJump={newOffset - _frameOffset}");
                    _frameOffset = newOffset;
                }
            }
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

                AvatarAnimationSystem.Transform(in receivedFrameData, Matrix4x4.TRS(_rotation * _offsetPosition + _position + Vector3.up * _cameraHeight, _rotation * Quaternion.Euler(_offsetRotation), Vector3.one), out var transformedFrameData);
                _animationFrameBuffer.Set(receivedFrameData.frames, in transformedFrameData);

                // Warn on discontinuities in the received frame numbering. delta==1 is
                // normal; delta==0 (duplicate, overwritten in place) is expected from the
                // sender's wall-clock quantization and stays silent. Gaps and backwards
                // jumps indicate real hitches, packet loss or a sender clock problem.
                if (_lastReceivedFrames >= 0)
                {
                    long delta = receivedFrameData.frames - _lastReceivedFrames;
                    if (delta < 0)
                    {
                        Debug.LogWarning($"[Studio] Received frame went backwards: prev={_lastReceivedFrames} curr={receivedFrameData.frames}");
                    }
                    else if (delta >= 2)
                    {
                        Debug.LogWarning($"[Studio] Received frame gap: prev={_lastReceivedFrames} curr={receivedFrameData.frames} delta={delta}");
                    }
                }
                _lastReceivedFrames = receivedFrameData.frames;

                _receivedFrameCount ++;
            }

            // frameData の生成 (補間サンプリング) は Update (メインスレッド) で毎フレーム行う。
            // ここ (ワーカースレッド) ではバッファ書き込みのみに留める。
        }

        [ContextMenu("Reset Camera")]
        [ExposedFunction]
        public override void ResetCamera()
        {
            // The Fusion side drives the avatar root transform from the body pose (IK), so reading
            // root.* latches onto a jittery pose snapshot and every reset lands slightly differently.
            // The capture camera channel carries the device/ARKit camera orientation instead, which is
            // independent of the avatar pose and is the yaw that actually drifts over time — cancel that.
            // (ref var avoids naming CameraData: in this Lilium.LiveStudio.Virgo namespace the wire-side
            //  Lilium.LiveStudio.Virgo.CameraData would shadow the Lilium.LiveStudio.CameraData returned here.)
            ref var camera = ref _lastReceivedFrameData.AsCamera(0);

            // Cancel the captured camera Y so that the target's world Y rotation stays at the anchor's Y rotation.
            _offsetRotation = new Vector3(0, -camera.rotation.eulerAngles.y, 0);

            // オフセット適用後のカメラワールド位置を原点に合わせる。キャラクターも同じ行列で変換されるため、撮影時のカメラ-キャラクター相対位置は保たれる。
            var rotation = Quaternion.Euler(_offsetRotation);
            _offsetPosition = -(rotation * camera.position) / _lastReceivedFrameData.root.scale.x; // スケールの影響を受けないようにする
        }
    }
}