// Copyright (c) You-Ri, 2026
// Reproduces vowel-centric VRChat visemes from ARKit blendshapes by driving the
// avatar's AnimatorController "Viseme" (Int) / "Voice" (Float) parameters.
//
// TODO: VRCFTAvatar shares the eye-bone rig and BuildAvatar boilerplate with this
// component. Extract a common base / helper in a later refactor (kept duplicated
// here to respect the minimal-change rule).

using System;
using UnityEngine;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// VRChat 由来アバター用コンポーネント。ARKit 52 値から母音中心の VRChat viseme
    /// (sil/aa/E/ih/oh/ou) を推定し、AnimatorController の Viseme (Int) / Voice (Float)
    /// パラメータへ書き込む。身体アニメーションは AvatarAnimationSystem.UpdateBodyAnimation。
    /// </summary>
    [DefaultExecutionOrder(10)]
    [RequireComponent(typeof(Animator))]
    public class VRCAvatar : MonoBehaviour, IAvatar
    {
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
        [Tooltip("目の回転最大角度 (x: yaw, y: pitch)")]
        Vector2 _eyeRotationMax = new Vector2(40f, 40f);

        Animator _animator;
        MotionSourceBase _motionSource;
        bool _isTracking;

        int _visemeHash;
        int _voiceHash;
        bool _hasViseme;
        bool _hasVoice;

        float[] _target;
        float[] _smoothed;

        // 部位ごとの実効トラッキング状態。既定 Tracking = viseme/eye が効く現行挙動。
        // AvatarAnimatorTrackingControl から ApplyTrackingControl 経由で更新される。
        AvatarTrackingType[] _tracking;

        AvatarExpressionConfig _expressionConfig;
        FacialKey[] _expressionKeys;

        Transform _leftEyeBone;
        Transform _rightEyeBone;
        Quaternion _leftEyeNeutral;
        Quaternion _rightEyeNeutral;
        Quaternion _leftEyeOffset;
        Quaternion _rightEyeOffset;
        float _eyeLeftX, _eyeLeftY, _eyeRightX, _eyeRightY;

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

            _expressionKeys = new FacialKey[kVisemeCount];
            for (int i = 0; i < kVisemeCount; i++)
            {
                _expressionKeys[i] = FacialKey.CreateCustom(s_localVisemeNames[i]);
            }

            _SetupEyeBones();
            _isTracking = false;

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

            float voice = _ComputeVisemeScores();
            _SmoothScores();
            // Mouth & Jaw が Animation のときは FX のアニメ clip に口を明け渡す。
            // (スコア計算は継続し、Tracking 復帰時の不連続を防ぐ)
            if (_tracking[kTrackMouth] == AvatarTrackingType.Tracking)
            {
                _WriteToAnimator(voice);
            }

            AvatarAnimationSystem.UpdateBodyAnimation(_animator, in _motionSource.frameData);
        }

        void LateUpdate()
        {
            if (!_isTracking || _motionSource == null) return;

            AvatarAnimationSystem.UpdateBodyAnimation(_animator, in _motionSource.frameData);
            // Eyes & Eyelids が Animation のときは eye bone 回転を止め FX に明け渡す。
            if (_tracking[kTrackEyes] == AvatarTrackingType.Tracking)
            {
                _ApplyEyeRotation();
            }
        }

        /// <summary>
        /// ARKit 口形状から母音 viseme スコア (_target) を算出し、voice (発話量) を返す。
        /// </summary>
        unsafe float _ComputeVisemeScores()
        {
            ref var frame = ref _motionSource.frameData;
            fixed (float* bs = frame.expression.weights)
            {
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
            _expressionConfig = config;
        }

        void IAvatar.SetMotionSource(MotionSourceBase motionSource)
        {
            _motionSource = motionSource;
        }

        bool IExpressionAvatar.SetWeight(FacialKey key, float weight)
        {
            int idx = _LocalVisemeIndex(key.name);
            if (idx < 0) return false;

            _target[idx] = Mathf.Clamp01(weight);
            return true;
        }

        float IExpressionAvatar.GetWeight(FacialKey key)
        {
            int idx = _LocalVisemeIndex(key.name);
            if (idx < 0) return 0f;

            return _smoothed[idx];
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

        #endregion
    }
}
