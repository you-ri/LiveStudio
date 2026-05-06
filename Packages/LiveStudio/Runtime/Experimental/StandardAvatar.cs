using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Animations;

using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// 目の回転を制御するAnimationJob
    /// PlayableGraphのパイプライン内で実行される
    /// </summary>
    public struct EyeRotationJob : IAnimationJob
    {
        public TransformStreamHandle leftEye;
        public TransformStreamHandle rightEye;
        public TransformStreamHandle head;

        public NativeReference<Vector2> eyeDirection;
        public Vector2 eyeRotationMax;
        public Quaternion leftEyeBaseRotation;
        public Quaternion rightEyeBaseRotation;

        public void ProcessAnimation(AnimationStream stream)
        {
            var dir = eyeDirection.Value;
            float yaw = -dir.x * eyeRotationMax.x;
            float pitch = -dir.y * eyeRotationMax.y;

            // Headの回転を取得してオフセットを計算
            Quaternion headRot = Quaternion.identity;
            if (head.IsValid(stream))
            {
                headRot = head.GetRotation(stream);
            }

            // Yaw-Pitch回転を計算
            var eyeRotOffset = Quaternion.Euler(pitch, yaw, 0);

            if (leftEye.IsValid(stream))
            {
                //leftEye.SetRotation(stream, headRot * eyeRotOffset * leftEyeBaseRotation);
            }
            if (rightEye.IsValid(stream))
            {
                //rightEye.SetRotation(stream, headRot * eyeRotOffset * rightEyeBaseRotation);
            }
        }

        public void ProcessRootMotion(AnimationStream stream) { }
    }

    /// <summary>
    /// 表情クリップエントリ
    /// </summary>
    [Serializable]
    public class FacialClipEntry
    {
        public string name;
        public AnimationClip clip;
    }

    /// <summary>
    /// AnimationClipによる表情制御
    /// VRM0/VRM10共通で使用可能
    /// </summary>
    [DefaultExecutionOrder(10)]
    [RequireComponent(typeof(Animator))]
    public class StandardAvatar : MonoBehaviour, IAvatar
    {
        private bool _isTracking = false;

        Animator _animator;

        MotionSourceBase _motionSource;

        [SerializeReference, Select]
        public IExpressionResolver expressionResolver = new DefaultExpressionResolver();

        [Header("Expression Mode")]
        [SerializeField]
        [Tooltip("表情モード")]
        public ExpressionMode expressionMode = ExpressionMode.Preset;

        [Header("Expression Clips")]
        [SerializeField]
        [Tooltip("表情クリップのリスト")]
        private List<FacialClipEntry> _facialClips = new List<FacialClipEntry>();

        [SerializeField]
        [Tooltip("無表情クリップ")]
        private AnimationClip _neutralClip;

        [Header("ARKit Clips (for PerfectSync)")]
        [SerializeField]
        [Tooltip("ARKitブレンドシェイプに対応するクリップ")]
        private List<FacialClipEntry> _arkitClips = new List<FacialClipEntry>();

        [Header("Eye Control")]
        [SerializeField]
        [Tooltip("目線回転の最大角度 (x: yaw, y: pitch)")]
        private Vector2 _eyeRotationMax = new Vector2(90f, 90f);

        [SerializeField]
        [Tooltip("左目ボーン（オフセット付き）")]
        private Lilium.LiveStudio.OffsetOnTransform LeftEye;

        [SerializeField]
        [Tooltip("右目ボーン（オフセット付き）")]
        private Lilium.LiveStudio.OffsetOnTransform RightEye;

        private Lilium.LiveStudio.OffsetOnTransform Head;

        private PlayableGraph _graph;
        private AnimationMixerPlayable _mixer;
        private AnimationScriptPlayable _eyeRotationPlayable;
        private NativeReference<Vector2> _eyeDirectionNative;

        // クリップ名からインデックスへのマッピング
        private Dictionary<string, int> _clipIndexMap = new Dictionary<string, int>();

        // ARKitクリップ用のインデックスマップ
        private Dictionary<ARKitBlendShapeLocation, int> _arkitClipIndexMap = new Dictionary<ARKitBlendShapeLocation, int>();

        // 現在の重み値
        private Dictionary<string, float> _currentWeights = new Dictionary<string, float>();

        // ARKitウェイトデータビュー
        private ARKitWeightDataView _sourceArKitWeightDataView = new ARKitWeightDataView();

        // 表情キーのキャッシュ
        private FacialKey[] _expressionKeys;

        // スムージング処理用（GC回避）
        private const int kMaxExpressionCount = 64;

        public Animator animator => _animator != null ? _animator : (_animator = GetComponent<Animator>());

        /// <summary>
        /// 目線方向を設定
        /// </summary>
        /// <param name="direction">目線方向 (x: 左右, y: 上下) -1〜1の範囲</param>
        public void SetEyeDirection(Vector2 direction)
        {
            var eyeDir = new Vector2(
                Mathf.Clamp(direction.x, -1f, 1f),
                Mathf.Clamp(direction.y, -1f, 1f)
            );

            // NativeReferenceにも反映
            if (_eyeDirectionNative.IsCreated)
            {
                _eyeDirectionNative.Value = eyeDir;
            }
        }

        void Start()
        {
            // NativeReferenceを初期化
            _eyeDirectionNative = new NativeReference<Vector2>(Allocator.Persistent);

            expressionResolver.Setup();

            _Initialize();

            // Lilium.LiveStudio.OffsetOnTransform の初期化
            LeftEye.Setup();
            RightEye.Setup();
            Head.Setup();

            ((IAvatar)this).BuildAvatar();
        }

        void OnDestroy()
        {
            _ClearGraph();

            // NativeReferenceを解放
            if (_eyeDirectionNative.IsCreated)
            {
                _eyeDirectionNative.Dispose();
            }

            expressionResolver.Dispose();
        }

        void _SetShowMeshes(bool visible)
        {
            var renderers = GetComponentsInChildren<Renderer>();
            foreach (var renderer in renderers)
            {
                renderer.enabled = visible;
            }
        }

        void Update()
        {
            if (_motionSource == null || !_motionSource.frameData.isValid)
            {
                if (_isTracking)
                {
                    // トラッキングロスト
                    _SetShowMeshes(false);
                }
                _isTracking = false;
                return;
            }
            else
            {
                if (!_isTracking)
                {
                    // トラッキング復帰
                    _SetShowMeshes(true);
                }
                _isTracking = true;
            }

            expressionResolver.Resolve(in _motionSource.frameData.expression);

            // NativeReferenceに目線方向を設定（EyeRotationJobで使用）
            if (_eyeDirectionNative.IsCreated)
            {
                _eyeDirectionNative.Value = expressionResolver.eyeDirection;
            }

            _ApplySmoothedWeights();

            // PerfectSyncモードではARKitブレンドシェイプを適用
            if (expressionMode == ExpressionMode.PerfectSync)
            {
                _ApplyPerfectSyncExpressions();
            }

            //_graph.Evaluate(Time.deltaTime);

            if (_animator != null)
            {
                AvatarAnimationSystem.UpdateBodyAnimation(_animator, in _motionSource.frameData);
            }
        }

        void LateUpdate()
        {
            if (_motionSource == null) return;

            if (_animator != null)
            {
                AvatarAnimationSystem.UpdateBodyAnimation(_animator, in _motionSource.frameData);
            }

        }


        private void _Initialize()
        {
            _animator = GetComponent<Animator>();

            // クリップインデックスマップを構築
            _BuildClipIndexMap();

            // 表情キーのキャッシュを構築
            _BuildExpressionKeys();

            // PlayableGraphを構築
            _BuildGraph();

            _isTracking = false;
        }

        private void _BuildClipIndexMap()
        {
            _clipIndexMap.Clear();
            _arkitClipIndexMap.Clear();

            int index = 0;

            // neutralクリップを最初に登録
            if (_neutralClip != null)
            {
                _clipIndexMap["neutral"] = index++;
            }

            // 表情クリップを登録
            foreach (var entry in _facialClips)
            {
                if (entry != null && entry.clip != null && !string.IsNullOrEmpty(entry.name))
                {
                    if (!_clipIndexMap.ContainsKey(entry.name))
                    {
                        _clipIndexMap[entry.name] = index++;
                    }
                }
            }

            // ARKitクリップを登録
            foreach (var entry in _arkitClips)
            {
                if (entry != null && entry.clip != null && !string.IsNullOrEmpty(entry.name))
                {
                    // ARKitBlendShapeLocationに変換を試みる
                    if (Enum.TryParse<ARKitBlendShapeLocation>(entry.name, out var location))
                    {
                        if (!_arkitClipIndexMap.ContainsKey(location))
                        {
                            _clipIndexMap[entry.name] = index;
                            _arkitClipIndexMap[location] = index++;
                        }
                    }
                }
            }
        }

        private void _BuildExpressionKeys()
        {
            var keys = new List<FacialKey>();
            foreach (var entry in _facialClips)
            {
                if (entry != null && !string.IsNullOrEmpty(entry.name))
                {
                    keys.Add(FacialKey.CreateCustom(entry.name));
                }
            }
            _expressionKeys = keys.ToArray();
        }

        private void _BuildGraph()
        {
            _ClearGraph();



            _graph = PlayableGraph.Create($"{gameObject.name}.StandardAvatar");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);

            int clipCount = _clipIndexMap.Count;
            _mixer = AnimationMixerPlayable.Create(_graph, clipCount);

            // neutralクリップを追加
            if (_neutralClip != null && _clipIndexMap.ContainsKey("neutral"))
            {
                var clipPlayable = AnimationClipPlayable.Create(_graph, _neutralClip);
                clipPlayable.SetApplyFootIK(false);
                _mixer.ConnectInput(_clipIndexMap["neutral"], clipPlayable, 0, 1f);
            }

            // 表情クリップを追加
            foreach (var entry in _facialClips)
            {
                if (entry != null && entry.clip != null && !string.IsNullOrEmpty(entry.name))
                {
                    if (_clipIndexMap.TryGetValue(entry.name, out int idx))
                    {
                        var clipPlayable = AnimationClipPlayable.Create(_graph, entry.clip);
                        clipPlayable.SetApplyFootIK(false);
                        _mixer.ConnectInput(idx, clipPlayable, 0, 0f);
                    }
                }
            }

            // ARKitクリップを追加
            foreach (var entry in _arkitClips)
            {
                if (entry != null && entry.clip != null && !string.IsNullOrEmpty(entry.name))
                {
                    if (_clipIndexMap.TryGetValue(entry.name, out int idx))
                    {
                        var clipPlayable = AnimationClipPlayable.Create(_graph, entry.clip);
                        clipPlayable.SetApplyFootIK(false);
                        _mixer.ConnectInput(idx, clipPlayable, 0, 0f);
                    }
                }
            }

            // EyeRotationJobを作成（Mixerの後に目の回転を適用）
            var leftEyeTransform = LeftEye.Transform != null ? LeftEye.Transform : _animator.GetBoneTransform(HumanBodyBones.LeftEye);
            var rightEyeTransform = RightEye.Transform != null ? RightEye.Transform : _animator.GetBoneTransform(HumanBodyBones.RightEye);
            var headTransform = Head.Transform != null ? Head.Transform : _animator.GetBoneTransform(HumanBodyBones.Head);

            var eyeJob = new EyeRotationJob
            {
                leftEye = leftEyeTransform != null ? _animator.BindStreamTransform(leftEyeTransform) : default,
                rightEye = rightEyeTransform != null ? _animator.BindStreamTransform(rightEyeTransform) : default,
                head = headTransform != null ? _animator.BindStreamTransform(headTransform) : default,
                eyeDirection = _eyeDirectionNative,
                eyeRotationMax = _eyeRotationMax,
                leftEyeBaseRotation = leftEyeTransform != null ? Quaternion.Inverse(headTransform.rotation) * leftEyeTransform.rotation : Quaternion.identity,
                rightEyeBaseRotation = rightEyeTransform != null ? Quaternion.Inverse(headTransform.rotation) * rightEyeTransform.rotation : Quaternion.identity,
            };

            _eyeRotationPlayable = AnimationScriptPlayable.Create(_graph, eyeJob);
            _eyeRotationPlayable.AddInput(_mixer, 0, 1f);

            var output = AnimationPlayableOutput.Create(_graph, name, animator);
            output.SetSourcePlayable(_eyeRotationPlayable);

            _graph.Play();
        }

        private void _ClearGraph()
        {
            if (_graph.IsValid())
            {
                _graph.Destroy();
            }
        }

        private void _UpdateMixerWeights()
        {
            if (!_mixer.IsValid())
            {
                return;
            }

            float totalWeight = 0f;

            // 全ての表情クリップの重みを設定
            foreach (var entry in _facialClips)
            {
                if (entry != null && !string.IsNullOrEmpty(entry.name))
                {
                    if (_clipIndexMap.TryGetValue(entry.name, out int idx))
                    {
                        float weight = 0f;
                        if (_currentWeights.TryGetValue(entry.name, out float w))
                        {
                            weight = w;
                        }
                        _mixer.SetInputWeight(idx, weight);
                        totalWeight += weight;
                    }
                }
            }

            // neutralクリップの重みを計算
            if (_clipIndexMap.TryGetValue("neutral", out int neutralIndex))
            {
                float neutralWeight = Mathf.Clamp01(1f - totalWeight);
                _mixer.SetInputWeight(neutralIndex, neutralWeight);
            }
        }

        private void _ApplyPerfectSyncExpressions()
        {
            if (!_mixer.IsValid())
            {
                return;
            }

            var arkitWeight = expressionResolver.arkitWeightData;

            // ARKitブレンドシェイプの重みを適用
            for (int i = 0; i < (int)ARKitBlendShapeLocation.Max; i++)
            {
                var arkitLocation = (ARKitBlendShapeLocation)i;
                float weight;
                unsafe
                {
                    weight = arkitWeight.weights[i];
                }

                if (_arkitClipIndexMap.TryGetValue(arkitLocation, out int idx))
                {
                    _mixer.SetInputWeight(idx, weight);
                }
            }
        }

        #region IAvatar Implementation
        void IAvatar.BuildAvatar()
        {
            if (_animator == null || _animator.avatar == null)
            {
                Debug.LogError("[Studio] StandardAvatar: Animator or Avatar is null.");
                return;
            }

            var humanDescription = _animator.avatar.humanDescription;
            var avatarBuildData = AvatarBuildSystem.CreateAvatarBuildData(transform, humanDescription);
            if (avatarBuildData.humanBones == null || avatarBuildData.humanBones.Length == 0)
            {
                Debug.LogError("[Studio] StandardAvatar: Failed to extract Avatar data.");
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
            if (string.IsNullOrEmpty(key.name))
            {
                return false;
            }

            if (!_clipIndexMap.ContainsKey(key.name))
            {
                return false;
            }

            // ターゲット値をバッファに保存（スムージング用）
            expressionResolver.SetWeight(key.name, Mathf.Clamp01(weight));
            return true;
        }

        float IExpressionAvatar.GetWeight(FacialKey key)
        {
            if (string.IsNullOrEmpty(key.name))
            {
                return 0f;
            }

            if (_currentWeights.TryGetValue(key.name, out float weight))
            {
                return weight;
            }

            return 0f;
        }

        ReadOnlySpan<FacialKey> IExpressionAvatar.GetExpressions()
        {
            return _expressionKeys ?? ReadOnlySpan<FacialKey>.Empty;
        }

        #endregion


        public void ResetPhysics()
        {
            // 物理リセットは個別の実装が必要な場合にオーバーライド
        }

        private void _ApplySmoothedWeights()
        {
            if (!_mixer.IsValid()) return;

            var outputs = expressionResolver.smoothedOutputs;
            if (!outputs.IsCreated) return;

            // 重みが空の場合はニュートラル表情を適用して終了
            if (outputs.Length == 0)
            {
                if (_clipIndexMap.TryGetValue("neutral", out int idx))
                {
                    _mixer.SetInputWeight(idx, 1f);
                }
                _sourceArKitWeightDataView.SetData(expressionResolver.arkitWeightData);
                return;
            }

            // 結果を適用
            int count = Mathf.Min(outputs.Length, kMaxExpressionCount);
            for (int i = 0; i < count; i++)
            {
                var output = outputs.values[i];
                string clipName = output.name.ToString();
                if (_clipIndexMap.TryGetValue(clipName, out int clipIndex))
                {
                    _currentWeights[clipName] = output.weight;
                    _mixer.SetInputWeight(clipIndex, output.weight);
                }
            }

            // neutral weightを設定
            var neutralWeight = ExpressionSystem.CalculateNeutralWeight(expressionResolver.totalWeight);
            if (_clipIndexMap.TryGetValue("neutral", out int neutralIndex))
            {
                _mixer.SetInputWeight(neutralIndex, neutralWeight);
            }

            _sourceArKitWeightDataView.SetData(expressionResolver.arkitWeightData);
        }

        private void Reset()
        {
            Setup();
        }

        [ContextMenu("Setup")]
        public void Setup()
        {
            LeftEye = Lilium.LiveStudio.OffsetOnTransform.Create(animator.GetBoneTransform(HumanBodyBones.LeftEye));
            RightEye = Lilium.LiveStudio.OffsetOnTransform.Create(animator.GetBoneTransform(HumanBodyBones.RightEye));
            Head = Lilium.LiveStudio.OffsetOnTransform.Create(animator.GetBoneTransform(HumanBodyBones.Head));
        }

    }
}
