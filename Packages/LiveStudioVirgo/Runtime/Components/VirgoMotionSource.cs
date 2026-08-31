using System.Collections;

using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
using Unity.Collections.LowLevel.Unsafe;

using Lilium.LiveStudio;
using Lilium.LiveStudio.Virgo.Networking;
using Lilium.RemoteControl;
using Lilium.RemoteControl.Frames;


namespace Lilium.LiveStudio.Virgo
{
    
    public static class FusionNetwork
    {
        public const string BaseURL = "http://127.0.0.1:3005";

        public static bool isConnected;


        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Initialize()
        {
            isConnected = false;
        }

    }

    [DefaultExecutionOrder(-100)]
    [LiveClass("VirgoMotionSource", Icon = "accessibility", Category = "Motion")]
    [MovedFrom(false, "Lilium.Virgo.Studio", "Lilium.Virgo.Studio2", null)]
    public class VirgoMotionSource : MotionSourceBase
    {
        const int kResetCameraDelayCount = 2; // 受信してから何フレーム目でカメラリセットするか。受信した情報が安定していない可能性があるため、数フレーム遅らせる。

        // Update の補間サンプリングが穴埋めのために前後何フレームまで neighbor を探索するか。
        // この幅以内の飛び (gap) は補間で滑らかに跨げるため、受信側の gap 警告閾値もこれに合わせる。
        const int kNeighborSearchFrames = 4;

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
        [LiveField]
        private int _port = 0;

        private UDPConnection _udpConnection = new UDPConnection();

        FrameBuffer<AvatarAnimationData> _animationFrameBuffer = new FrameBuffer<AvatarAnimationData>(30);

        private FrameRate _frameRate = FrameRate.FPS60;

        private int _frameOffset;

        // 受信フレームより何秒遅延させて再生するか。補間先 (i0+1) を在庫させるための余裕。
        // 大きいほど最新フレームを追い越して hold する頻度が減るが、表示レイテンシは増える。
        [SerializeField]
        [LiveField]
        private float _delaySeconds = 0.0167f; // 約1フレーム (60fps)

        // Height (meters) the capture camera sits above the subject (the warp mark). With cameraDistance it
        // places VirgoMotionSource (= _position, the point the captured camera is pinned to in ResetCamera)
        // at the assumed real camera location. When both match the real rig, the avatar — placed at its
        // captured offset from the camera — lands on the mark.
        [SerializeField]
        [LiveField]
        private float _cameraHeight = 1.3f;

        // Horizontal distance (meters) from the subject anchor to the capture camera, along the anchor's
        // forward (+Z) axis. With cameraHeight this fully specifies the capture-camera origin
        // geometrically, so this GameObject's position is determined even before/without capture pose data.
        [SerializeField]
        [LiveField]
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

        // Set by the receive thread once enough valid frames have arrived, consumed at the frame
        // head. The reset writes the placement offsets, which are part of the reference point, and
        // the reference point has a single writer: the frame head.
        private volatile bool _resetCameraRequested;

        // Exposed id of the object this source belongs to. The string is kept, not the interned
        // number: finding it walks every registered object, but the number it interns to is only
        // good until the next gate reset.
        private string _ownerLiveId;

        // Resolved once against its declaration in AssemblyInfo. Declared sources keep the same id
        // across a gate reset, so this stays valid for the life of the domain.
        private static readonly FrameSource _fusionSource = FrameGate.ResolveSource("fusion");

        // The frame the automatic reset was requested for, copied by the receive thread before it
        // raises the flag. Read at the frame head instead of _lastReceivedFrameData, which the
        // receive thread keeps overwriting -- the struct is far too large to be read while it is
        // being written. The flag is volatile and set once, so the copy is complete by the time the
        // frame head sees it.
        private AvatarAnimationData _resetCameraSample;

        void OnEnable()
        {
            AvatarBuildNotifier.onAvatarBuilt += _OnAvatarBuilt;

            // Sampling runs at the head of a frame rather than in Update, so the pose for frame N
            // is produced at a fixed point instead of wherever this component happens to be in the
            // script order. The gate applies that frame's inputs first, so a write to the reference
            // point lands before the pose that is placed by it.
            FrameGate.AddFrameHeadHandler(_OnFrameHead);

            Open();
        }

        void OnDisable()
        {
            AvatarBuildNotifier.onAvatarBuilt -= _OnAvatarBuilt;
            FrameGate.RemoveFrameHeadHandler(_OnFrameHead);
            Close();
        }

        // Running retry coroutine for the buildavatar POST; superseded on each rebuild.
        private Coroutine _buildAvatarRetry;

        // Fusion may not be listening yet when the avatar is first built (Studio can start before
        // Fusion, or the avatar is built during scene restore). Keep retrying the buildavatar POST
        // until it lands so a late-starting Fusion still receives the skeleton, and supersede any
        // in-flight retry on rebuild so only the latest avatar is sent.
        void _OnAvatarBuilt(in AvatarBuildData data)
        {
            if (_buildAvatarRetry != null)
            {
                StopCoroutine(_buildAvatarRetry);
                _buildAvatarRetry = null;
            }
            _buildAvatarRetry = StartCoroutine(_SendBuildAvatarLoop(data));
        }

        private IEnumerator _SendBuildAvatarLoop(AvatarBuildData data)
        {
            const float kRetryDelay = 2f;
            var wait = new WaitForSeconds(kRetryDelay);
            bool warned = false;

            while (this != null && isActiveAndEnabled)
            {
                bool ok = false;
                string error = null;
                yield return FusionRequestSystem.BuildAvatar(data, (success, err) =>
                {
                    ok = success;
                    error = err;
                });

                if (ok)
                {
                    break;
                }

                // Warn only on the first failure so the console is not flooded while retrying
                // (Fusion may legitimately be offline during Studio-only sessions).
                if (!warned)
                {
                    warned = true;
                    Debug.LogWarning($"[Studio] Failed to send avatar to Fusion, retrying every {kRetryDelay:0}s: {error}");
                }

                yield return wait;
            }

            _buildAvatarRetry = null;
        }


        void OnDestroy()
        {
            Close();
        }


        [LiveFunction]
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

        /// <summary>
        /// Produces the pose for this frame: build the reference point, sample the received frames,
        /// then place the sample with the reference point.
        ///
        /// Order matters. The reference point used to be applied on the receive thread, which left
        /// two problems: the placement origin was written here and read there with nothing to order
        /// them, and a recording of the placed pose could never be re-placed, so editing the camera
        /// afterwards would have had no effect. Sampling first and placing last fixes both.
        ///
        /// Interpolating before placing gives the same result as placing before interpolating: the
        /// placement is a rotation and a translation, spherical interpolation is invariant under a
        /// rotation applied from the left, and translation commutes with linear interpolation. The
        /// two differ only while the reference point itself is moving, and then only by one frame's
        /// worth of its motion.
        /// </summary>
        void _OnFrameHead(ref Frame frame)
        {
            _UpdatePlacementOrigin();

            // Consumed here rather than run on the receive thread, so the placement offsets have a
            // single writer.
            if (_resetCameraRequested)
            {
                _resetCameraRequested = false;
                _ResetCameraFrom(in _resetCameraSample);
            }

            if (!_TryResolvePose(ref frame, out var sampled)) return;

            // Runs on a supplied frame too, and that is the point: the pose on the state lane is the
            // one before placement, so the reference point can be edited and the take drawn again.
            // Fold the placement into what is recorded and this step has nothing left to do.
            //
            // height/distance are already part of the placement origin, so they are not added again.
            AvatarAnimationSystem.Transform(in sampled, _PlacementMatrix(), out frameData);
        }

        /// <summary>
        /// Puts this frame's pose into the state lane.
        ///
        /// The pose stored is the one before placement. Placement is a property of the camera rig,
        /// not of the capture, and folding it in would make the recorded pose unusable for anything
        /// but the camera position it happened to be shot with.
        ///
        /// Read back on a supplied frame by <see cref="_TryResolvePose"/>, so the value that reaches
        /// the avatar comes off the frame either way.
        /// </summary>
        private void _PublishState(ref Frame frame, in AvatarAnimationData sampled, long sampledFrom)
        {
            if (frame.state == null) return;

            var owner = _OwnerId();
            if (owner == FrameSymbolTable.kNone) return;

            ref var element = ref frame.state.GetOrCreate<AvatarAnimationData>().GetOrCreate(owner);
            element.source = _fusionSource;

            // The sender's frame number, not this frame's. The two run off different clocks, and
            // keeping the sender's is what lets an alignment be applied afterwards.
            element.time = sampledFrom;
            element.value = sampled;
        }

        /// <summary>
        /// Announces the pose type so a recording carrying it can be played back into a block, even
        /// on a run that has not published one live -- which is every run that only ever replays.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void _RegisterStateType()
        {
            StateTypeRegistry.Register<AvatarAnimationData>();
        }

        /// <summary>
        /// This frame's pose, from whichever side is supplying the frame.
        ///
        /// One opening, two suppliers. Live, the pose is sampled from what arrived over the wire and
        /// written onto the frame; supplied, it is read straight back off the frame. Everything after
        /// this point is the same code either way, which is what stops a replay from being a second
        /// path that can quietly drift from the first.
        ///
        /// A supplied frame with nothing for this source is not an error -- the recording simply had
        /// no pose at that point -- so the avatar is left as it was rather than snapped to nothing.
        /// </summary>
        private bool _TryResolvePose(ref Frame frame, out AvatarAnimationData pose)
        {
            if (frame.isSupplied) return _TryReadState(in frame, out pose);

            if (!_TrySamplePose(out pose, out var sampledFrom)) return false;

            _PublishState(ref frame, in pose, sampledFrom);
            return true;
        }

        private bool _TryReadState(in Frame frame, out AvatarAnimationData pose)
        {
            pose = default;

            if (frame.state == null) return false;

            var owner = _OwnerId();
            if (owner == FrameSymbolTable.kNone) return false;

            var block = frame.state.Find<AvatarAnimationData>();
            if (block == null) return false;

            var index = block.IndexOfOwner(owner);
            if (index < 0) return false;

            pose = block[index].value;
            return true;
        }

        /// <summary>
        /// Interned id of this source as an exposed object, or none while it has not been registered.
        ///
        /// Two different lifetimes, so two different things are kept. The exposed id is found once,
        /// because finding it walks every registered object and the answer only changes when this
        /// component's object is registered or dropped. The number it interns to is taken every
        /// frame, because a gate reset wipes the symbol table and a kept number would then name
        /// whatever took its place -- object ids are not re-interned in a fixed order the way
        /// declared sources are.
        ///
        /// The id is asked of the transform, not of this component. A component is not registered
        /// under its own target -- the registry holds the proxy for its GameObject -- so a lookup by
        /// target returns nothing, and nothing is indistinguishable from "not ready yet". That is
        /// what used to make every pose published here go nowhere.
        /// </summary>
        private int _OwnerId()
        {
            if (_ownerLiveId == null)
            {
                var id = LiveObjectRegistry.FindOwnLiveId(transform);
                if (string.IsNullOrEmpty(id)) return FrameSymbolTable.kNone;

                _ownerLiveId = id;
            }

            return FrameGate.symbols.Intern(_ownerLiveId);
        }

        private Matrix4x4 _PlacementMatrix()
            => Matrix4x4.TRS(_rotation * _offsetPosition + _position,
                _rotation * Quaternion.Euler(_offsetRotation), Vector3.one);

        private void _UpdatePlacementOrigin()
        {
            // VirgoMotionSource（この GameObject）はキャプチャカメラの基準点: anchor(= AvatarController,
            // WarpTo の移動先 mark = 被写体) の cameraHeight 上・cameraDistance 前方(+forward) に置く。
            // アバターは ResetCamera で mark の正面(+Z = source.forward)を向くよう揃えられ、撮影カメラは
            // その正面側に居る。マーカーは被写体を見返すよう Y180° 反転（+Z が AvatarController を向く）。
            // standalone（anchor 未設定）時は自身の transform を基準点にする。
            //
            // Read at the frame head, which is before this frame's animation update: the reference
            // point therefore follows the anchor one frame behind. That lag was already there and
            // was non-deterministic (whichever value the receive thread happened to see); it is now
            // a fixed one frame.
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
        }

        /// <summary>
        /// Samples the received frames at the current playback position. False when there is nothing
        /// to sample, in which case the previous pose is left alone.
        ///
        /// <paramref name="sampledFrom"/> is the sender's frame number the sample was anchored on --
        /// the producer's own time axis, which is what an alignment is expressed against.
        /// </summary>
        private bool _TrySamplePose(out AvatarAnimationData sampled, out long sampledFrom)
        {
            sampled = default;
            sampledFrom = -1;

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
                AvatarAnimationSystem.Lerp(prev, next, spanT, out sampled);
                sampledFrom = prevNo;
                return true;
            }
            else if (prevNo >= 0)
            {
                // Caught up with the newest frame (or a short overrun past it):
                // hold the last available pose instead of resyncing, which would
                // jump the playback position backwards.
                sampled = prev;
                sampledFrom = prevNo;
                return true;
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

                return false;
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

                // Detect discontinuities in the sender's frame numbering BEFORE writing to the
                // buffer, so a detected restart clears the buffer before the new frame is stored.
                //
                // delta==1 is normal; delta==0 (duplicate, overwritten in place) is expected from
                // the sender's wall-clock quantization and stays silent. Small gaps (delta up to
                // kNeighborSearchFrames, incl. the benign +2 quantization beat) are bridged by
                // Update's gap-tolerant interpolation and stay silent too.
                //
                // A large BACKWARD jump means Fusion restarted: it numbers frames from its own
                // realtimeSinceStartup, so a restart drops the numbering from a high value back to
                // near zero. FrameBuffer.frameCount only ever increases, so without intervention
                // the auto-resync in Update stays pinned to the stale high frame (already
                // overwritten by the new low numbering), spamming "Resync playback position" and
                // freezing the avatar until the manual ResyncTiming button is pressed. Clearing the
                // buffer resets frameCount to 0 so it rebuilds from the new numbering and Update
                // re-locks the offset on the next frames — the same recovery as ResyncTiming, done
                // automatically on every reconnect. Reset() is thread-safe (its own lock) and the
                // offset re-locks on the main thread, so nothing else is touched from this thread.
                if (_lastReceivedFrames >= 0)
                {
                    long delta = receivedFrameData.frames - _lastReceivedFrames;
                    if (delta < -kNeighborSearchFrames)
                    {
                        Debug.LogWarning($"[Studio] Received frame numbering reset (sender restart?): prev={_lastReceivedFrames} curr={receivedFrameData.frames}; clearing buffer to re-lock.");
                        _animationFrameBuffer.Reset();
                    }
                    else if (delta < 0)
                    {
                        // Small backward step: an out-of-order or duplicate-late UDP packet. The
                        // buffer overwrites it in place and the next in-order frame recovers, so
                        // there is nothing to reset.
                        Debug.LogWarning($"[Studio] Received frame went backwards: prev={_lastReceivedFrames} curr={receivedFrameData.frames} delta={delta}");
                    }
                    else if (delta > kNeighborSearchFrames)
                    {
                        Debug.LogWarning($"[Studio] Received frame gap: prev={_lastReceivedFrames} curr={receivedFrameData.frames} delta={delta}");
                    }
                }
                _lastReceivedFrames = receivedFrameData.frames;

                // カメラリセットは valid なフレームだけを数えて遅延させる。無効フレームでは
                // リセットの基準となるカメラ姿勢が安定しないため、valid カウントで測る。
                if (receivedFrameData.isValid)
                {
                    // 0フレーム目はまだ受信した情報が安定していないため、カメラリセットしない。
                    // Requested rather than run here: the reset writes the placement offsets, and
                    // those belong to the frame head.
                    if (_validFrameCount == kResetCameraDelayCount && resetCameraAtReceived)
                    {
                        _resetCameraSample = receivedFrameData;
                        _resetCameraRequested = true;
                    }

                    _validFrameCount++;
                }

                // Stored unplaced. The reference point is applied at the frame head, after
                // interpolation, so that the pose kept here stays independent of where the camera
                // was standing when the packet arrived.
                _animationFrameBuffer.Set(receivedFrameData.frames, in receivedFrameData);

                _receivedFrameCount ++;
            }

            // frameData の生成 (補間サンプリングと配置) はフレーム先頭 (メインスレッド) で毎フレーム行う。
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
        /// Runs on the main thread (LiveFunction invocations are marshaled there); the
        /// buffer's own lock keeps the reset safe against the receive thread.
        /// </summary>
        [ContextMenu("Resync Timing")]
        [LiveFunction]
        public void ResyncTiming()
        {
            // The pipeline has two timing baselines: Fusion's (its offset onto the capture stream) and
            // this one (playback position). Re-locking only one leaves the other's drift in place, so
            // this single call covers both. Fusion is asked first — without waiting for the reply — and
            // the local reset below always runs, so a Studio-only session still resyncs.
            _RequestFusionResync();

            _animationFrameBuffer.Reset();
            _frameOffset = 0;
            // Forget the last received frame number so a sender that restarted its numbering
            // does not emit a spurious "went backwards" warning on the next packet.
            _lastReceivedFrames = -1;
            Debug.Log("[Studio] Manual resync timing: cleared frame buffer and offset; re-locking on next received frames.");
        }

        /// <summary>
        /// Asks Fusion to re-lock its capture timing. The reply is not awaited: both sides re-lock on
        /// the frames that arrive next, so there is nothing to sequence.
        /// </summary>
        void _RequestFusionResync()
        {
            // Coroutines need play mode, and outside it nothing is being received anyway (OnEnable /
            // Update do not run), so there is no timing to resync.
            if (!Application.isPlaying || !isActiveAndEnabled)
            {
                return;
            }

            StartCoroutine(_RequestFusionResyncRoutine());
        }

        static IEnumerator _RequestFusionResyncRoutine()
        {
            bool ok = false;
            string error = null;
            yield return FusionRequestSystem.ResyncTiming((success, err) =>
            {
                ok = success;
                error = err;
            });

            if (!ok)
            {
                // Fusion being offline is a normal case (Studio-only sessions), and the Studio side has
                // already resynced, so this stays a warning.
                Debug.LogWarning($"[Studio] Failed to ask Fusion to resync timing; the Studio side resynced anyway: {error}");
            }
        }

        [ContextMenu("Reset Camera")]
        [LiveFunction]
        public override void ResetCamera() => _ResetCameraFrom(in _lastReceivedFrameData);

        private void _ResetCameraFrom(in AvatarAnimationData reference)
        {
            // Pin the capture camera (cam0) to the placement origin (_position = VirgoMotionSource, the camera
            // reference at cameraHeight/cameraDistance from the mark) — camera-anchored position.
            // (ref var avoids naming CameraData: the wire-side Lilium.LiveStudio.Virgo.CameraData would shadow
            //  the Lilium.LiveStudio.CameraData returned here.)
            //
            // Copied locally because AsCamera hands back a ref, which needs somewhere mutable to
            // point at, and because the caller may be handing over a field the receive thread writes.
            var sample = reference;
            ref var camera = ref sample.AsCamera(0);

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
            _offsetPosition = -(rotation * camera.position) / sample.root.scale.x;
        }
    }
}