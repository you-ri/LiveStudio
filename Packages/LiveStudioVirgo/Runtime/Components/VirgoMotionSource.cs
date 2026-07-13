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

        /// <summary>これまでに受信した valid な AvatarAnimationData のフレーム数。カメラリセットの基準に使う。</summary>
        public int validFrameCount => _validFrameCount;

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

        // Height (meters) the capture camera sits above the subject (the warp mark). With cameraDistance it
        // places VirgoMotionSource (= _position, the point the captured camera is pinned to in ResetCamera)
        // at the assumed real camera location. When both match the real rig, the avatar — placed at its
        // captured offset from the camera — lands on the mark.
        [SerializeField]
        [ExposedField]
        private float _cameraHeight = 1.3f;

        // Horizontal distance (meters) from the subject anchor to the capture camera, along the anchor's
        // forward (+Z) axis. With cameraHeight this fully specifies the capture-camera origin
        // geometrically, so this GameObject's position is determined even before/without capture pose data.
        [SerializeField]
        [ExposedField]
        private float _cameraDistance = 0.7f;

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

        // valid な AvatarAnimationData を受信した数。カメラリセットの遅延基準に使う。
        // 無効フレーム (isValid=false) はカメラの基準姿勢が不安定なため数えない。
        // バックグラウンドスレッドで ++、メインスレッドで読むため volatile。
        private volatile int _validFrameCount = 0;

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
            // VirgoMotionSource（この GameObject）はキャプチャカメラの基準点: anchor(= AvatarController,
            // WarpTo の移動先 mark = 被写体) の cameraHeight 上・cameraDistance 前方(+forward) に置く。
            // アバターは ResetCamera で mark の正面(+Z = source.forward)を向くよう揃えられ、撮影カメラは
            // その正面側に居る。マーカーは被写体を見返すよう Y180° 反転（+Z が AvatarController を向く）。
            // standalone（anchor 未設定）時は自身の transform を基準点にする。
            var source = anchor != null ? anchor : this.transform;
            if (source != this.transform)
            {
                var cameraOrigin = source.position + Vector3.up * _cameraHeight + source.forward * _cameraDistance;
                transform.SetPositionAndRotation(cameraOrigin, source.rotation * Quaternion.Euler(0f, 180f, 0f));
            }
            // 配置行列原点 _position = VirgoMotionSource.transform（ResetCamera で撮影カメラ(cam0)が固定される
            // 基準点）。_rotation はマーカーの反転回転ではなく被写体(mark)の回転を使う（matrix の従来挙動を維持）。
            _position = transform.position;
            _rotation = source.rotation;

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
                    // log every time so it can be correlated with on-screen motion.
                    int newOffset = (int)(latest - (long)localFrame);
                    Debug.Log($"[Studio] Resync playback position: playbackPos={playbackPos:F2} latest={latest} offsetJump={newOffset - _frameOffset}");
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

                // カメラリセットは valid なフレームだけを数えて遅延させる。無効フレームでは
                // リセットの基準となるカメラ姿勢が安定しないため、valid カウントで測る。
                if (receivedFrameData.isValid)
                {
                    // 0フレーム目はまだ受信した情報が安定していないため、カメラリセットしない。
                    if (_validFrameCount == kResetCameraDelayCount && resetCameraAtReceived)
                    {
                        ResetCamera();
                    }

                    _validFrameCount++;
                }

                // height/distance は _position（= transform.position）に既に含まれるため、ここでは加算しない。
                AvatarAnimationSystem.Transform(in receivedFrameData, Matrix4x4.TRS(_rotation * _offsetPosition + _position, _rotation * Quaternion.Euler(_offsetRotation), Vector3.one), out var transformedFrameData);
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
                        Debug.Log($"[Studio] Received frame gap: prev={_lastReceivedFrames} curr={receivedFrameData.frames} delta={delta}");
                    }
                }
                _lastReceivedFrames = receivedFrameData.frames;

                _receivedFrameCount ++;
            }

            // frameData の生成 (補間サンプリング) は Update (メインスレッド) で毎フレーム行う。
            // ここ (ワーカースレッド) ではバッファ書き込みのみに留める。
        }

        /// <summary>
        /// Re-lock playback timing by clearing the received-frame buffer and the accumulated
        /// frame-offset, so playback re-syncs to whatever the sender streams next.
        ///
        /// Sender (Fusion) and receiver (Studio) number frames from their own wall clocks in
        /// separate processes, so <see cref="_frameOffset"/> bridges the two. When the sender
        /// restarts, its frame numbering drops back to low values — but the buffer's frameCount
        /// only ever increases, so playback stays pinned to a stale, overwritten frame and the
        /// avatar freezes; the per-frame auto-resync in <see cref="Update"/> cannot escape this
        /// because it samples the same stuck frameCount. Clearing the buffer lets frameCount
        /// rebuild from the new numbering and the next Update re-locks the offset. This also
        /// recovers from long stalls or clock jumps.
        ///
        /// Runs on the main thread (ExposedFunction invocations are marshaled there); the
        /// buffer's own lock keeps the reset safe against the receive thread.
        /// </summary>
        [ContextMenu("Resync Timing")]
        [ExposedFunction]
        public void ResyncTiming()
        {
            _animationFrameBuffer.Reset();
            _frameOffset = 0;
            // Forget the last received frame number so a sender that restarted its numbering
            // does not emit a spurious "went backwards" warning on the next packet.
            _lastReceivedFrames = -1;
            Debug.Log("[Studio] Manual resync timing: cleared frame buffer and offset; re-locking on next received frames.");
        }

        [ContextMenu("Reset Camera")]
        [ExposedFunction]
        public override void ResetCamera()
        {
            // Pin the capture camera (cam0) to the placement origin (_position = VirgoMotionSource, the camera
            // reference at cameraHeight/cameraDistance from the mark) — camera-anchored position.
            // (ref var avoids naming CameraData: the wire-side Lilium.LiveStudio.Virgo.CameraData would shadow
            //  the Lilium.LiveStudio.CameraData returned here.)
            ref var camera = ref _lastReceivedFrameData.AsCamera(0);

            // Align the capture camera (cam0) so its +Z faces the mark, matching this GameObject's own
            // placement orientation (source.rotation * 180°, which points back at the mark). The avatar then
            // keeps its captured orientation *relative to the camera* and lands laterally on the mark.
            // Cancel the CAMERA yaw, NOT the ROOT (body) yaw: aligning the body yaw leaves the camera off-axis,
            // which swings the avatar sideways off the mark (the reported symptom). The +180 is required because
            // the placement itself is flipped to face back at the mark — dropping it (using -cameraYaw) points
            // the camera away and flips the avatar backwards.
            _offsetRotation = new Vector3(0, 180f - camera.rotation.eulerAngles.y, 0);

            // オフセット適用後のカメラワールド位置を原点 (_position = VirgoMotionSource) に合わせる（cam.pos≈0）。
            var rotation = Quaternion.Euler(_offsetRotation);
            _offsetPosition = -(rotation * camera.position) / _lastReceivedFrameData.root.scale.x;
        }
    }
}