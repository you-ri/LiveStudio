// Copyright (c) You-Ri, 2026
// Reproduces vowel-centric VRChat visemes from ARKit blendshapes by driving the
// avatar's AnimatorController "Viseme" (Int) / "Voice" (Float) parameters.
//
// TODO: VRCFTAvatar shares the eye-bone rig and BuildAvatar boilerplate with this
// component. Extract a common base / helper in a later refactor (kept duplicated
// here to respect the minimal-change rule).

using System;
using System.Collections.Generic;
using UnityEngine;
#if VRMC_VRM10
using UniVRM10;
#endif

using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// VRChat 表情マッピング。表情名と、その表情がアクティブな間に Animator に書き込む
    /// パラメータ群 (AnimationParameterOverride) のペア。VRChat の FX レイヤーが
    /// Animator パラメータ値で表情を切り替える仕組みに合わせる。
    /// </summary>
    [ExposedClass]
    [Serializable]
    public class VRCExpression
    {
        [ExposedField]
        [Tooltip("表情名 (FacialKey.name)。AvatarExpression の \"Expression.<name>\" InputAction と紐付く")]
        public string name;

        [ExposedField(label = "VRCAVATAR_EXPRESSION_PARAMETERS")]
        [Tooltip("この表情がアクティブな間に Animator に書き込むパラメータ")]
        public AnimationParameterOverride[] parameters = Array.Empty<AnimationParameterOverride>();
    }

    /// <summary>
    /// VRChat 由来アバター用コンポーネント。ARKit 52 値から母音中心の VRChat viseme
    /// (sil/aa/E/ih/oh/ou) を推定し、AnimatorController の Viseme (Int) / Voice (Float)
    /// パラメータへ書き込む。身体アニメーションは AvatarAnimationSystem.UpdateBodyAnimation。
    /// </summary>
    [DefaultExecutionOrder(10)]
    [RequireComponent(typeof(Animator))]
    public class VRCAvatar : MonoBehaviour, IAvatar
    {
        [SerializeReference, Select]
        public IExpressionResolver expressionResolver = new DefaultExpressionResolver();

#if UNITY_EDITOR
        // GUID of the shipped default ExpressionConfig asset (LiveStudio package:
        // Contents/SceneTemplate/Current Avatar Expression Config.asset).
        // Using GUID instead of a path keeps the default working after renames/moves.
        const string _kDefaultExpressionConfigGuid = "c7b12b2866458f44fb076018b48d8fb4";

        // Editor-only: populate the resolver's ExpressionConfig with the shipped default
        // when the component is first added (or reset). A C# field initializer cannot
        // reference a ScriptableObject asset, so this is the standard Unity entry point
        // for that kind of default.
        void Reset()
        {
            if (expressionResolver == null) return;
            if (expressionResolver.expressionConfig != null) return;

            var path = UnityEditor.AssetDatabase.GUIDToAssetPath(_kDefaultExpressionConfigGuid);
            if (string.IsNullOrEmpty(path)) return;

            var defaultConfig = UnityEditor.AssetDatabase.LoadAssetAtPath<AvatarExpressionConfig>(path);
            if (defaultConfig != null)
            {
                expressionResolver.expressionConfig = defaultConfig;
            }
        }
#endif

        //----------------------------------------------------------------------
        // ローカル viseme インデックス (0..VisemeCount-1) と VRChat viseme 値の対応
        //----------------------------------------------------------------------
        const int kSil = 0;
        const int kAa = 1;
        const int kEe = 2;
        const int kIh = 3;
        const int kOh = 4;
        const int kOu = 5;
        const int kVisemeCount = 6;

        // VRChat 標準 Viseme enum 値: sil=0, aa=10, E=11, ih=12, oh=13, ou=14
        static readonly int[] s_localToVrcViseme = { 0, 10, 11, 12, 13, 14 };
        static readonly string[] s_localVisemeNames = { "sil", "aa", "E", "ih", "oh", "ou" };

        // 母音判定のしきい値: 最大スコアがこれ未満、または voice がこれ未満なら sil。
        const float kVisemeThreshold = 0.10f;
        const float kVoiceThreshold = 0.05f;

        //----------------------------------------------------------------------
        // AvatarAnimatorTrackingControl の部位インデックス (状態保持用, 長さ10)
        //----------------------------------------------------------------------
        const int kTrackHead = 0;
        const int kTrackLeftHand = 1;
        const int kTrackRightHand = 2;
        const int kTrackHip = 3;
        const int kTrackLeftFoot = 4;
        const int kTrackRightFoot = 5;
        const int kTrackLeftFingers = 6;
        const int kTrackRightFingers = 7;
        const int kTrackEyes = 8;
        const int kTrackMouth = 9;
        const int kTrackCount = 10;

        [Header("Viseme")]

        [SerializeField]
        [Tooltip("AnimatorController の viseme パラメータ名 (Int)")]
        string _visemeParam = "Viseme";

        [SerializeField]
        [Tooltip("AnimatorController の voice パラメータ名 (Float)")]
        string _voiceParam = "Voice";

        [SerializeField]
        [Tooltip("開口量から voice (発話量) への増幅")]
        float _voiceGain = 1.5f;

        [SerializeField]
        [Range(0f, 1f)]
        [Tooltip("0=遅い (強いスムージング), 1=即時")]
        float _responsiveness = 0.5f;

        [SerializeField]
        [Tooltip("VisemeBlendShape lipsync 用メッシュ。設定時は Animator ではなく blendshape を直接駆動する")]
        SkinnedMeshRenderer _visemeMesh;

        [SerializeField]
        [Tooltip("ローカル viseme 順 (sil/aa/E/ih/oh/ou) の blendshape index。-1 は未割当")]
        int[] _visemeBlendShapeIndices;

        [Header("Eyes")]

        [SerializeField]
        [Tooltip("目の回転最大角度 (x: yaw, y: pitch)")]
        Vector2 _eyeRotationMax = new Vector2(40f, 40f);

        [SerializeField]
        [Tooltip("Eyelid blink blendshape 用メッシュ。設定時は ARKit eye blink から blendshape を直接駆動する")]
        SkinnedMeshRenderer _eyelidsMesh;

        [SerializeField]
        [Tooltip("Blink blendshape index。-1 は未割当")]
        int _blinkBlendShapeIndex = -1;

        [Header("Expression")]

        [SerializeField]
        [Tooltip("VRChat 表情マッピング。表情のウェイトのうち最大値のものをアクティブとし、その parameters を Animator に書き込む")]
        VRCExpression[] _expressions = Array.Empty<VRCExpression>();

        Animator _animator;
        MotionSourceBase _motionSource;
        bool _isTracking;

        int _visemeHash;
        int _voiceHash;
        bool _hasViseme;
        bool _hasVoice;

        // VisemeSkinnedMesh と index 配列が揃っていれば blendshape 直接駆動、
        // 揃わなければ Animator Viseme/Voice 経路へフォールバックする。
        bool _useVisemeBlendShapes;

        // EyelidsMesh と Blink blendshape index が有効ならば ARKit eye blink から
        // 直接 blendshape を駆動する。未設定時は何もしない (FX アニメーションに任せる)。
        bool _useBlinkBlendShape;

        float[] _target;
        float[] _smoothed;

        // 部位ごとの実効トラッキング状態。既定 Tracking = viseme/eye が効く現行挙動。
        // AvatarAnimatorTrackingControl から ApplyTrackingControl 経由で更新される。
        AvatarTrackingType[] _tracking;

        FacialKey[] _expressionKeys;

        int _activeExpressionIndex = -1;   // 現在 Animator に適用中のエントリ (-1 = なし)
        bool _expressionsResolved;         // 初回の default 値採取が走ったか

        Transform _leftEyeBone;
        Transform _rightEyeBone;
        Quaternion _leftEyeNeutral;
        Quaternion _rightEyeNeutral;
        Quaternion _leftEyeOffset;
        Quaternion _rightEyeOffset;
        float _eyeLeftX, _eyeLeftY, _eyeRightX, _eyeRightY;

#if VRMC_VRM10
        // Vrm10Instance がある場合は VRM の LookAt に視線を委譲する。
        // VRCAvatar が眼球ボーンを直接書くと、Vrm10Instance(order 11000) の LateUpdate
        // Process が後から眼球をニュートラルに上書きして競合するため。
        Vrm10Instance _vrm10Instance;
        Vrm10RuntimeLookAt _vrm10LookAt;
#endif

        void Start()
        {
            _animator = GetComponent<Animator>();

            _target = new float[kVisemeCount];
            _smoothed = new float[kVisemeCount];

            _tracking = new AvatarTrackingType[kTrackCount];
            for (int i = 0; i < kTrackCount; i++)
            {
                _tracking[i] = AvatarTrackingType.Tracking;
            }

            _visemeHash = Animator.StringToHash(_visemeParam);
            _voiceHash = Animator.StringToHash(_voiceParam);

            // Animator に実在するパラメータのみへ書き込む。
            foreach (var p in _animator.parameters)
            {
                if (p.nameHash == _visemeHash) _hasViseme = true;
                if (p.nameHash == _voiceHash) _hasVoice = true;
            }
            if (!_hasViseme)
            {
                Debug.LogWarning($"[Studio] VRCAvatar: '{_visemeParam}' parameter not found on Animator; mouth will not move.");
            }
            if (!_hasVoice)
            {
                Debug.LogWarning($"[Studio] VRCAvatar: '{_voiceParam}' parameter not found on Animator.");
            }

            // viseme (口形状) + ユーザー定義表情を統合した FacialKey 一覧を構築。
            // 名前未設定のエントリは custom key を作れないためスキップする。
            var keys = new List<FacialKey>();
            for (int i = 0; i < kVisemeCount; i++)
            {
                keys.Add(FacialKey.CreateCustom(s_localVisemeNames[i]));
            }
            if (_expressions != null)
            {
                foreach (var exp in _expressions)
                {
                    if (exp != null && !string.IsNullOrEmpty(exp.name))
                    {
                        keys.Add(FacialKey.CreateCustom(exp.name));
                    }
                }
            }
            _expressionKeys = keys.ToArray();

            expressionResolver.Setup();

            _useVisemeBlendShapes = _visemeMesh != null
                                    && _visemeMesh.sharedMesh != null
                                    && _visemeBlendShapeIndices != null
                                    && _visemeBlendShapeIndices.Length == kVisemeCount;

            _useBlinkBlendShape = _eyelidsMesh != null
                                  && _eyelidsMesh.sharedMesh != null
                                  && _blinkBlendShapeIndex >= 0
                                  && _blinkBlendShapeIndex < _eyelidsMesh.sharedMesh.blendShapeCount;

            _SetupEyeBones();
            _isTracking = false;

#if VRMC_VRM10
            // Vrm10Instance が同居していれば VRM の LookAt 経由で眼球を適用する。
            // Runtime プロパティは実行時のみアクセス可 (内部で Transform 操作を行う)。
            _vrm10Instance = GetComponent<Vrm10Instance>();
            if (_vrm10Instance != null)
            {
                _vrm10LookAt = _vrm10Instance.Runtime.LookAt;
            }
#endif

            ((IAvatar)this).BuildAvatar();
        }

        void _SetupEyeBones()
        {
            _leftEyeBone = _animator.GetBoneTransform(HumanBodyBones.LeftEye);
            _rightEyeBone = _animator.GetBoneTransform(HumanBodyBones.RightEye);
            var headBone = _animator.GetBoneTransform(HumanBodyBones.Head);

            if (_leftEyeBone != null && headBone != null)
            {
                _leftEyeNeutral = _leftEyeBone.localRotation;
                _leftEyeOffset = Quaternion.Inverse(headBone.rotation) * _leftEyeBone.rotation;
            }
            if (_rightEyeBone != null && headBone != null)
            {
                _rightEyeNeutral = _rightEyeBone.localRotation;
                _rightEyeOffset = Quaternion.Inverse(headBone.rotation) * _rightEyeBone.rotation;
            }
        }

        void OnDestroy()
        {
            expressionResolver?.Dispose();
        }

        void Update()
        {
            if (_motionSource == null || !_motionSource.frameData.isValid)
            {
                if (_isTracking)
                {
                    _SetShowMeshes(false);
                }
                _isTracking = false;
                return;
            }

            if (!_isTracking)
            {
                _SetShowMeshes(true);
            }
            _isTracking = true;

            // ARKit 52 weight を resolver で reshape (neutral 表情の差分 + 各表情ウェイト合成 + スムージング)。
            // 以降のロジックは resolver の arkitWeightData / smoothedOutputs を参照する。
            expressionResolver.Resolve(in _motionSource.frameData.expression);

            float voice = _ComputeVisemeScores();
            _SmoothScores();
            // Mouth & Jaw が Animation のときは FX のアニメ clip に口を明け渡す。
            // (スコア計算は継続し、Tracking 復帰時の不連続を防ぐ)
            if (_tracking[kTrackMouth] == AvatarTrackingType.Tracking)
            {
                if (_useVisemeBlendShapes)
                {
                    _WriteToBlendShapes(voice);
                }
                else
                {
                    _WriteToAnimator(voice);
                }
            }

            // Eyes & Eyelids が Animation のときは FX のアニメ clip に瞼を明け渡す。
            if (_useBlinkBlendShape && _tracking[kTrackEyes] == AvatarTrackingType.Tracking)
            {
                _WriteBlinkBlendShape();
            }

#if VRMC_VRM10
            // VRM がある場合は視線を VRM LookAt へ委譲する (VRM が LateUpdate で適用)。
            if (_vrm10LookAt != null && _tracking[kTrackEyes] == AvatarTrackingType.Tracking)
            {
                _ApplyEyeLookAt();
            }
#endif

            // VRChat 表情: 最大ウェイトの表情の AnimationParameterOverride を Animator へ反映
            _UpdateExpressionAnimationParameters();

            AvatarAnimationSystem.UpdateBodyAnimation(_animator, in _motionSource.frameData);
        }

        void LateUpdate()
        {
            if (!_isTracking || _motionSource == null) return;

            AvatarAnimationSystem.UpdateBodyAnimation(_animator, in _motionSource.frameData);

#if VRMC_VRM10
            // VRM 委譲時は Update で SetYawPitchManually 済み。Vrm10Instance の Process
            // (order 11000) が眼球を適用するため、ここでの直接書き込みは行わない。
            if (_vrm10LookAt != null) return;
#endif
            // Eyes & Eyelids が Animation のときは eye bone 回転を止め FX に明け渡す。
            if (_tracking[kTrackEyes] == AvatarTrackingType.Tracking)
            {
                _ApplyEyeRotation();
            }
        }

        // Vrm10Instance がある場合のみ、VRM の LookAt に視線(yaw/pitch)を委譲する。
        // VRM が VRM10Object の HorizontalOuter/Inner/VerticalUp/Down で範囲をクランプする。
        void _ApplyEyeLookAt()
        {
#if VRMC_VRM10
            if (_vrm10LookAt == null) return;

            // 水平は左右眼で LookOut の向きが逆のため、右眼(LookOut=右)から左眼(LookOut=左)を
            // 引いて統一視線を作る (正=右向き)。垂直は左右同方向なので平均でよい。
            // VRM の SetYawPitchManually も正の yaw=右向き / 正の pitch=上向き。
            float yaw = (_eyeRightX - _eyeLeftX) * 0.5f * _eyeRotationMax.x;
            float pitch = (_eyeRightY + _eyeLeftY) * 0.5f * _eyeRotationMax.y;
            _vrm10LookAt.SetYawPitchManually(yaw, pitch);
#endif
        }

        /// <summary>
        /// ARKit 口形状から母音 viseme スコア (_target) を算出し、voice (発話量) を返す。
        /// 入力は expressionResolver.Resolve 通過後の ARKit weight (neutral 差分 + 表情合成済み)。
        /// </summary>
        unsafe float _ComputeVisemeScores()
        {
            // ARKitWeightData は固定サイズバッファを持つ unmanaged struct。ローカルコピーした struct の
            // fixed buffer はスタックフレーム内で既に固定アドレスのため、追加の fixed 文は不要かつ不可。
            var arkitWeight = expressionResolver.arkitWeightData;
            float* bs = arkitWeight.weights;

            float open = Mathf.Clamp01(bs[(int)ARKitBlendShapeLocation.JawOpen]
                                       * (1f - 0.7f * bs[(int)ARKitBlendShapeLocation.MouthClose]));
            float smile = 0.5f * (bs[(int)ARKitBlendShapeLocation.MouthSmileLeft]
                                  + bs[(int)ARKitBlendShapeLocation.MouthSmileRight]);
            float stretch = 0.5f * (bs[(int)ARKitBlendShapeLocation.MouthStretchLeft]
                                    + bs[(int)ARKitBlendShapeLocation.MouthStretchRight]);
            float funnel = bs[(int)ARKitBlendShapeLocation.MouthFunnel];
            float pucker = bs[(int)ARKitBlendShapeLocation.MouthPucker];

            float wide = Mathf.Clamp01(0.6f * smile + 0.7f * stretch);
            float round = Mathf.Clamp01(0.7f * funnel + 0.5f * pucker);
            float purse = Mathf.Clamp01(pucker - 0.3f * funnel);

            _target[kAa] = open * (1f - 0.6f * round) * (1f - 0.5f * wide);
            _target[kOh] = Mathf.Min(open, 0.4f + 0.6f * round) * round * (1f - 0.7f * wide);
            _target[kOu] = purse * (1f - 0.6f * open);
            _target[kIh] = wide * (1f - 0.7f * open) * (1f - 0.6f * round);
            _target[kEe] = Mathf.Clamp01(0.5f * wide + 0.4f * open) * (1f - round) * (1f - 0.5f * open * open);
            _target[kSil] = 0f; // sil は viseme=0 として扱うため明示スコアは持たない

            // Eye gaze (LateUpdate で適用)
            _eyeLeftX = bs[(int)ARKitBlendShapeLocation.EyeLookOutLeft] - bs[(int)ARKitBlendShapeLocation.EyeLookInLeft];
            _eyeLeftY = bs[(int)ARKitBlendShapeLocation.EyeLookUpLeft] - bs[(int)ARKitBlendShapeLocation.EyeLookDownLeft];
            _eyeRightX = bs[(int)ARKitBlendShapeLocation.EyeLookOutRight] - bs[(int)ARKitBlendShapeLocation.EyeLookInRight];
            _eyeRightY = bs[(int)ARKitBlendShapeLocation.EyeLookUpRight] - bs[(int)ARKitBlendShapeLocation.EyeLookDownRight];

            return Mathf.Clamp01(_voiceGain * open);
        }

        void _SmoothScores()
        {
            float k = 1f - Mathf.Pow(1f - _responsiveness, Time.deltaTime * 60f);
            for (int i = 0; i < kVisemeCount; i++)
            {
                _smoothed[i] = Mathf.Lerp(_smoothed[i], _target[i], k);
            }
        }

        void _WriteToAnimator(float voice)
        {
            int best = kSil;
            float bestScore = 0f;
            for (int i = kAa; i < kVisemeCount; i++)
            {
                if (_smoothed[i] > bestScore)
                {
                    bestScore = _smoothed[i];
                    best = i;
                }
            }

            int viseme = (bestScore < kVisemeThreshold || voice < kVoiceThreshold)
                ? s_localToVrcViseme[kSil]
                : s_localToVrcViseme[best];

            if (_hasViseme) _animator.SetInteger(_visemeHash, viseme);
            if (_hasVoice) _animator.SetFloat(_voiceHash, voice);
        }

        /// <summary>
        /// 母音スコアから VisemeSkinnedMesh の blendshape を直接連続駆動する。
        /// VRChat の VisemeBlendShape lipsync 相当。sil (index 0) は値を持たず、
        /// 無音判定時は全 viseme blendshape を 0 にして口を閉じる。
        /// </summary>
        void _WriteToBlendShapes(float voice)
        {
            float bestScore = 0f;
            for (int i = kAa; i < kVisemeCount; i++)
            {
                if (_smoothed[i] > bestScore) bestScore = _smoothed[i];
            }

            bool silent = bestScore < kVisemeThreshold || voice < kVoiceThreshold;
            for (int i = kAa; i < kVisemeCount; i++)
            {
                int idx = _visemeBlendShapeIndices[i];
                if (idx < 0 || idx >= _visemeMesh.sharedMesh.blendShapeCount) continue;
                _visemeMesh.SetBlendShapeWeight(idx, silent ? 0f : _smoothed[i] * 100f);
                
            }
        }

        /// <summary>
        /// VisemeBlendShape lipsync 設定を注入する (変換ツールから呼ばれる)。
        /// localOrderIndices はローカル viseme 順 (sil/aa/E/ih/oh/ou) の blendshape index。
        /// 未割当は -1。設定後は実行時に Animator ではなく blendshape を直接駆動する。
        /// </summary>
        public void ConfigureVisemeBlendShapes(SkinnedMeshRenderer mesh, int[] localOrderIndices)
        {
            _visemeMesh = mesh;
            _visemeBlendShapeIndices = localOrderIndices;
        }

        /// <summary>
        /// ARKit eye blink (EyeBlinkLeft / EyeBlinkRight) の小さい方を 0..1 として
        /// Blink blendshape に直接書き込む。左右で値が異なるウィンク状態では小さい方を
        /// 採用することで、片目だけの動きを片瞼ブレンドシェイプには波及させない。
        /// </summary>
        unsafe void _WriteBlinkBlendShape()
        {
            var arkitWeight = expressionResolver.arkitWeightData;
            float* bs = arkitWeight.weights;

            float left = bs[(int)ARKitBlendShapeLocation.EyeBlinkLeft];
            float right = bs[(int)ARKitBlendShapeLocation.EyeBlinkRight];
            float blink = Mathf.Clamp01(Mathf.Min(left, right));
            _eyelidsMesh.SetBlendShapeWeight(_blinkBlendShapeIndex, blink * 100f);
        }

        /// <summary>
        /// VRCAvatarDescriptor.customEyeLookSettings の Blendshapes 型 Blink 設定を注入する
        /// (変換ツールから呼ばれる)。blinkIndex は eyelidsSkinnedMesh 上の Blink blendshape
        /// index (-1 は未割当)。設定後は ARKit eye blink から blendshape を直接駆動する。
        /// </summary>
        public void ConfigureBlinkBlendShape(SkinnedMeshRenderer mesh, int blinkIndex)
        {
            _eyelidsMesh = mesh;
            _blinkBlendShapeIndex = blinkIndex;
        }

        /// <summary>
        /// VRChat ExpressionsMenu から移植した表情マッピングを注入する (変換ツールから呼ばれる)。
        /// 各 VRCExpression.name は実行時に expressionResolver.smoothedOutputs から重みを引くキー
        /// として参照される (FacialKey 名を前提とする)。menu の Control 名そのままだとリゾルバが
        /// 重みを返さないため、移植直後は雛形扱いとなる。
        /// </summary>
        public void ConfigureExpressions(VRCExpression[] expressions)
        {
            _expressions = expressions ?? Array.Empty<VRCExpression>();
        }

        /// <summary>
        /// AvatarAnimatorTrackingControl から呼ばれ、部位ごとのトラッキング状態を更新する。
        /// NoChange の部位は据え置く (VRChat の挙動と同じ)。GC ゼロ。
        /// </summary>
        public void ApplyTrackingControl(AvatarAnimatorTrackingControl c)
        {
            if (c == null || _tracking == null) return;

            _Set(kTrackHead, c.trackingHead);
            _Set(kTrackLeftHand, c.trackingLeftHand);
            _Set(kTrackRightHand, c.trackingRightHand);
            _Set(kTrackHip, c.trackingHip);
            _Set(kTrackLeftFoot, c.trackingLeftFoot);
            _Set(kTrackRightFoot, c.trackingRightFoot);
            _Set(kTrackLeftFingers, c.trackingLeftFingers);
            _Set(kTrackRightFingers, c.trackingRightFingers);
            _Set(kTrackEyes, c.trackingEyes);
            _Set(kTrackMouth, c.trackingMouth);
        }

        void _Set(int region, AvatarTrackingType value)
        {
            if (value != AvatarTrackingType.NoChange)
            {
                _tracking[region] = value;
            }
        }

        void _ApplyEyeRotation()
        {
            if (_leftEyeBone != null)
            {
                // EyeLeftX: 正=左向き(LookOut) なので反転して正=右向きに統一
                float yaw = _eyeLeftX * _eyeRotationMax.x;
                float pitch = _eyeLeftY * _eyeRotationMax.y;
                Vector3 input = new Vector3(-pitch, -yaw, 0f);
                _leftEyeBone.localRotation = Quaternion.Inverse(_leftEyeOffset) * Quaternion.Euler(input) * _leftEyeOffset * _leftEyeNeutral;
            }

            if (_rightEyeBone != null)
            {
                float yaw = -_eyeRightX * _eyeRotationMax.x;
                float pitch = _eyeRightY * _eyeRotationMax.y;
                Vector3 input = new Vector3(-pitch, -yaw, 0f);
                _rightEyeBone.localRotation = Quaternion.Inverse(_rightEyeOffset) * Quaternion.Euler(input) * _rightEyeOffset * _rightEyeNeutral;
            }
        }

        void _SetShowMeshes(bool visible)
        {
            var renderers = GetComponentsInChildren<Renderer>();
            foreach (var renderer in renderers)
            {
                renderer.enabled = visible;
            }
        }

        #region IAvatar

        void IAvatar.BuildAvatar()
        {
            if (_animator == null || _animator.avatar == null)
            {
                return;
            }

            var humanDescription = _animator.avatar.humanDescription;
            var avatarBuildData = AvatarBuildSystem.CreateAvatarBuildData(transform, humanDescription);
            if (avatarBuildData.humanBones == null || avatarBuildData.humanBones.Length == 0)
            {
                Debug.LogError("[Studio] VRCAvatar: Failed to extract Avatar data.");
                return;
            }

            AvatarBuildNotifier.NotifyAvatarBuilt(in avatarBuildData);
        }

        void IAvatar.SetExpressionConfig(AvatarExpressionConfig config)
        {
            expressionResolver.expressionConfig = config;
        }

        void IAvatar.SetMotionSource(MotionSourceBase motionSource)
        {
            _motionSource = motionSource;
        }

        bool IExpressionAvatar.SetWeight(FacialKey key, float weight)
        {
            // viseme (口形状) - 既存経路
            int idx = _LocalVisemeIndex(key.name);
            if (idx >= 0)
            {
                _target[idx] = Mathf.Clamp01(weight);
                return true;
            }

            // VRChat 表情マッピング: resolver に流す。次の Resolve で smoothedOutputs に反映され、
            // _UpdateExpressionAnimationParameters が最大ウェイト表情を選んで AnimationParameter に書き込む。
            expressionResolver.SetWeight(key.name, weight);
            return true;
        }

        float IExpressionAvatar.GetWeight(FacialKey key)
        {
            int idx = _LocalVisemeIndex(key.name);
            if (idx >= 0) return _smoothed[idx];

            if (!expressionResolver.isSetup) return 0f;
            if (expressionResolver.smoothedOutputs.TryGet(key.name, out float weight)) return weight;
            return 0f;
        }

        ReadOnlySpan<FacialKey> IExpressionAvatar.GetExpressions()
        {
            return _expressionKeys ?? ReadOnlySpan<FacialKey>.Empty;
        }

        public void ResetPhysics()
        {
        }

        static int _LocalVisemeIndex(string name)
        {
            if (string.IsNullOrEmpty(name)) return -1;
            for (int i = 0; i < kVisemeCount; i++)
            {
                if (s_localVisemeNames[i] == name) return i;
            }
            return -1;
        }

        /// <summary>
        /// expressionResolver.smoothedOutputs から _expressions[*].name と一致するエントリを探し、
        /// 最大ウェイトの表情の AnimationParameterOverride を Animator に書き込む。
        /// 切り替わったタイミングのみ実 Animator 操作が走る。全表情ウェイトが 0 のときは
        /// 直前の parameters をデフォルト値に戻して終了。
        /// </summary>
        void _UpdateExpressionAnimationParameters()
        {
            if (_expressions == null || _expressions.Length == 0) return;
            if (_animator == null || _animator.runtimeAnimatorController == null) return;
            if (!expressionResolver.isSetup) return;

            var outputs = expressionResolver.smoothedOutputs;
            if (!outputs.IsCreated) return;

            // 最大ウェイト (0 < weight) の表情を選択。同値時は配列の先頭優先。
            int bestIndex = -1;
            float bestWeight = 0f;
            for (int i = 0; i < _expressions.Length; i++)
            {
                var exp = _expressions[i];
                if (exp == null || string.IsNullOrEmpty(exp.name)) continue;
                if (outputs.TryGet(exp.name, out float w) && w > bestWeight)
                {
                    bestWeight = w;
                    bestIndex = i;
                }
            }

            // 切り替わりなしかつ resolve 済なら何もしない。Animator 値は前回設定のまま保持される。
            if (bestIndex == _activeExpressionIndex && _expressionsResolved) return;

            // 前のアクティブ表情の parameters を default 値に戻す
            if (_activeExpressionIndex >= 0
                && _activeExpressionIndex < _expressions.Length
                && _expressions[_activeExpressionIndex] != null)
            {
                _RestoreExpressionParameters(_expressions[_activeExpressionIndex].parameters);
            }

            // 新しいアクティブ表情の parameters を適用
            if (bestIndex >= 0)
            {
                _ApplyExpressionParameters(_expressions[bestIndex].parameters);
            }

            _activeExpressionIndex = bestIndex;
            _expressionsResolved = true;
        }

        void _ApplyExpressionParameters(AnimationParameterOverride[] overrides)
        {
            if (overrides == null) return;
            for (int i = 0; i < overrides.Length; i++)
            {
                var o = overrides[i];
                if (o == null || string.IsNullOrEmpty(o.name)) continue;
                if (!_TryGetAnimatorParameter(o.name, out var param)) continue;
                if (param.type == AnimatorControllerParameterType.Trigger) continue;

                o.type = param.type;

                // AvatarController._ApplyAnimationParameterOverrides と同じく、最初に出現した時点の
                // Animator 値を default として保存しておき、表情解除時に書き戻す。
                if (!o.resolved)
                {
                    switch (param.type)
                    {
                        case AnimatorControllerParameterType.Float: o.defaultFloat = _animator.GetFloat(param.nameHash); break;
                        case AnimatorControllerParameterType.Int: o.defaultInt = _animator.GetInteger(param.nameHash); break;
                        case AnimatorControllerParameterType.Bool: o.defaultBool = _animator.GetBool(param.nameHash); break;
                    }
                    o.resolved = true;
                }

                switch (param.type)
                {
                    case AnimatorControllerParameterType.Float: _animator.SetFloat(param.nameHash, o.floatValue); break;
                    case AnimatorControllerParameterType.Int: _animator.SetInteger(param.nameHash, o.intValue); break;
                    case AnimatorControllerParameterType.Bool: _animator.SetBool(param.nameHash, o.boolValue); break;
                }
            }
        }

        void _RestoreExpressionParameters(AnimationParameterOverride[] overrides)
        {
            if (overrides == null) return;
            for (int i = 0; i < overrides.Length; i++)
            {
                var o = overrides[i];
                if (o == null || !o.resolved || string.IsNullOrEmpty(o.name)) continue;
                if (!_TryGetAnimatorParameter(o.name, out var param)) continue;
                switch (param.type)
                {
                    case AnimatorControllerParameterType.Float: _animator.SetFloat(param.nameHash, o.defaultFloat); break;
                    case AnimatorControllerParameterType.Int: _animator.SetInteger(param.nameHash, o.defaultInt); break;
                    case AnimatorControllerParameterType.Bool: _animator.SetBool(param.nameHash, o.defaultBool); break;
                }
            }
        }

        bool _TryGetAnimatorParameter(string name, out AnimatorControllerParameter result)
        {
            var parameters = _animator.parameters;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].name == name)
                {
                    result = parameters[i];
                    return true;
                }
            }
            result = default;
            return false;
        }

        #endregion
    }
}
