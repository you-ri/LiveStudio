#if VRMC_VRM10

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;
using UniVRM10;

using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    [DefaultExecutionOrder(10)]
    [RequireComponent(typeof(Animator))]
    [RequireComponent(typeof(Vrm10Instance))]
    public class VRM1Avatar : MonoBehaviour, IAvatar
    {
        private bool _isTracking = false;

        private Animator _animator;

        private MotionSourceBase _motionSource;

        [SerializeReference, Select]
        public IExpressionResolver expressionResolver = new DefaultExpressionResolver();

        private Vrm10Instance _vrm10Instance;

        private Vrm10RuntimeExpression _vrm10RuntimeExpression;

        private Vrm10RuntimeLookAt _vrm10LookAt;

        public ExpressionMode expressionMode
        {
            get => _expressionMode;
            set => _expressionMode = value;
        }

        [SerializeField]
        private Vector2 _eyeRotationMax = new Vector2(90, 90);

        [SerializeField]
        private ExpressionMode _expressionMode = ExpressionMode.Preset;

        // VRM1.0のExpression用のキー
        private UniVRM10.ExpressionKey? _lookRightKey;
        private UniVRM10.ExpressionKey? _lookLeftKey;
        private UniVRM10.ExpressionKey? _lookUpKey;
        private UniVRM10.ExpressionKey? _lookDownKey;
        private UniVRM10.ExpressionKey? _blinkKey;
        private UniVRM10.ExpressionKey? _blinkLKey;
        private UniVRM10.ExpressionKey? _blinkRKey;
        private UniVRM10.ExpressionKey? _jawOpenKey;

        // キャッシュ用のフィールド
        private Dictionary<FixedString32Bytes, UniVRM10.ExpressionKey?> _expressionKeyCache;
        private Dictionary<ARKitBlendShapeLocation, UniVRM10.ExpressionKey?> _arkitExpressionKeyCache;
        private Dictionary<string, string> _blendShapeNameCache;
        private FacialKey[] _expressionKeys;

        // スムージング処理用
        private const int kMaxExpressionCount = 64;

        void _Initialize()
        {
            _animator = GetComponent<Animator>();
            _vrm10Instance = GetComponent<Vrm10Instance>();

            _InitializeExpressionKeys();
            _InitializeCache();

            _isTracking = false;
        }

        void OnValidate()
        {
            _Initialize();
        }

        void Start()
        {
            _Initialize();

            expressionResolver.Setup();

            // Runtimeプロパティへのアクセスは実行時のみ行う
            // 内部でTransform.SetParent()を呼び出しているため、OnValidateやAwakeで呼び出すことができない
            _vrm10RuntimeExpression = _vrm10Instance.Runtime.Expression;
            _vrm10LookAt = _vrm10Instance.Runtime.LookAt;

            //ResetPhysics();

            ((IAvatar)this).BuildAvatar();
        }

        void OnDestroy()
        {
            expressionResolver.Dispose();
        }

        void Update()
        {
            if (_motionSource == null || !_motionSource.frameData.isValid)
            {
                if (_isTracking)
                {
                    // トラッキングロスト
                    SetShowMeshes(false);
                }
                _isTracking = false;
                return;
            }
            else
            {
                if (!_isTracking)
                {
                    // トラッキング復帰
                    SetShowMeshes(true);
                }
                _isTracking = true;
            }

            ref AvatarAnimationData frameData = ref _motionSource.frameData;

            if (_animator != null)
            {
                AvatarAnimationSystem.UpdateBodyAnimation(_animator, in frameData);
            }

            expressionResolver.Resolve(in frameData.expression);

            // スムージングされた表情を最初に適用
            ApplySmoothedWeights();

            ApplyBlink();
            ApplyLookAtBones();
            ApplyLookAtExpressions();
            ApplyFacialExpressions();
        }

        void SetShowMeshes(bool visible)
        {
            var renderers = GetComponentsInChildren<Renderer>();
            foreach (var renderer in renderers)
            {
                renderer.enabled = visible;
            }
        }


        private void _InitializeExpressionKeys()
        {
            if (_vrm10Instance?.Vrm?.Expression == null) return;

            var expression = _vrm10Instance.Vrm.Expression;

            // プリセット表情のキーを取得
            _lookRightKey = _CreateExpressionKey(expression, "lookRight");
            _lookLeftKey = _CreateExpressionKey(expression, "lookLeft");
            _lookUpKey = _CreateExpressionKey(expression, "lookUp");
            _lookDownKey = _CreateExpressionKey(expression, "lookDown");
            _blinkKey = _CreateExpressionKey(expression, "blink");
            _blinkLKey = _CreateExpressionKey(expression, "blinkLeft");
            _blinkRKey = _CreateExpressionKey(expression, "blinkRight");
            _jawOpenKey = _CreateExpressionKey(expression, "aa");
        }

        /// <summary>
        /// キャッシュ生成
        /// </summary>
        private void _InitializeCache()
        {
            if (_vrm10Instance?.Vrm?.Expression == null) return;

            var expression = _vrm10Instance.Vrm.Expression;

            // ExpressionKeyキャッシュの初期化
            _expressionKeyCache = new Dictionary<FixedString32Bytes, UniVRM10.ExpressionKey?>();
            _blendShapeNameCache = new Dictionary<string, string>();

            var facialKeysList = new List<FacialKey>();
            var expressionKeysList = new List<FacialKey>();

            foreach (var clip in expression.Clips)
            {
                if (clip.Clip == null) continue;

                var clipName = clip.Clip.name;
                var normalizedName = clipName.ToLowerInvariant();
                var expressionKey = expression.CreateKey(clip.Clip);

                _expressionKeyCache[normalizedName] = expressionKey;
                var facialKey = FacialKey.CreateCustom(clipName);
                facialKeysList.Add(facialKey);

                if (!FacialKey.IsARKitKey(facialKey))
                {
                    expressionKeysList.Add(facialKey);
                }

                // 左右反転名のキャッシュ
                var flippedName = clipName;
                if (clipName.Contains("Right"))
                {
                    flippedName = clipName.Replace("Right", "Left");
                }
                else if (clipName.Contains("Left"))
                {
                    flippedName = clipName.Replace("Left", "Right");
                }
                _blendShapeNameCache[clipName] = flippedName;
            }

            _expressionKeys = expressionKeysList.ToArray();


            _arkitExpressionKeyCache = new Dictionary<ARKitBlendShapeLocation, UniVRM10.ExpressionKey?>();
            foreach (ARKitBlendShapeLocation loc in System.Enum.GetValues(typeof(ARKitBlendShapeLocation)))
            {
                if (loc == ARKitBlendShapeLocation.Max) continue;

                var locName = loc.ToString();
                var expressionKey = _CreateExpressionKey(expression, locName);
                if (expressionKey.HasValue)
                {
                    _arkitExpressionKeyCache[loc] = expressionKey;
                }
            }
        }

        private static UniVRM10.ExpressionKey? _CreateExpressionKey(VRM10ObjectExpression expression, string name)
        {
            foreach (var clip in expression.Clips)
            {
                if (clip.Clip != null && clip.Clip.name.ToLower().Contains(name.ToLower()))
                {
                    return expression.CreateKey(clip.Clip);
                }
            }

            return null;
        }

        private UniVRM10.ExpressionKey? _GetCachedExpressionKey(FixedString32Bytes name)
        {
            if (_expressionKeyCache == null) return null;

            var normalizedName = name.ToLowerAscii();

            // 完全一致を試す
            if (_expressionKeyCache.TryGetValue(normalizedName, out var exactMatch))
            {
                return exactMatch;
            }

            return null;
        }

        private UniVRM10.ExpressionKey? _GetCachedARKitExpressionKey(ARKitBlendShapeLocation location)
        {
            if (_arkitExpressionKeyCache == null) return null;

            if (_arkitExpressionKeyCache.TryGetValue(location, out var expressionKey))
            {
                return expressionKey;
            }

            return null;
        }

        private void ApplyBlink()
        {
            if (_vrm10RuntimeExpression == null) return;
            if (expressionMode != ExpressionMode.Preset) return;

            var arkitWeight = expressionResolver.arkitWeightData;

            // まばたき制御
            if (_blinkLKey.HasValue)
                _vrm10RuntimeExpression.SetWeight(_blinkLKey.Value, arkitWeight.AtWeight(ARKitBlendShapeLocation.EyeBlinkLeft));
            if (_blinkRKey.HasValue)
                _vrm10RuntimeExpression.SetWeight(_blinkRKey.Value, arkitWeight.AtWeight(ARKitBlendShapeLocation.EyeBlinkRight));

        }

        private void ApplyLookAtExpressions()
        {
            if (_vrm10RuntimeExpression == null) return;

            // 目線制御
            var eyeDir = expressionResolver.eyeDirection;
            var yaw = eyeDir.x;
            var pitch = eyeDir.y;

            // VRM1.0のLookAt処理は今のところ表情での制御のみ実装
            // 表情での目線制御
            if (_lookRightKey.HasValue)
                _vrm10RuntimeExpression.SetWeight(_lookRightKey.Value, Mathf.Clamp01(-yaw));
            if (_lookLeftKey.HasValue)
                _vrm10RuntimeExpression.SetWeight(_lookLeftKey.Value, Mathf.Clamp01(yaw));
            if (_lookUpKey.HasValue)
                _vrm10RuntimeExpression.SetWeight(_lookUpKey.Value, Mathf.Clamp01(pitch));
            if (_lookDownKey.HasValue)
                _vrm10RuntimeExpression.SetWeight(_lookDownKey.Value, Mathf.Clamp01(-pitch));
        }

        private void ApplyLookAtBones()
        {
            if (_vrm10LookAt == null) return;

            var eyeDir = expressionResolver.eyeDirection;
            _vrm10LookAt.SetYawPitchManually(-eyeDir.x * _eyeRotationMax.x, eyeDir.y * _eyeRotationMax.y);
        }

        private void ApplySmoothedWeights()
        {
            if (_vrm10RuntimeExpression == null) return;

            var outputs = expressionResolver.smoothedOutputs;
            if (!outputs.IsCreated) return;

            // 結果を適用
            int count = Mathf.Min(outputs.Length, kMaxExpressionCount);
            for (int i = 0; i < count; i++)
            {
                var output = outputs.values[i];
                var expressionKey = _GetCachedExpressionKey(output.name);
                if (expressionKey.HasValue)
                {
                    _vrm10RuntimeExpression.SetWeight(expressionKey.Value, output.weight);
                }
            }
        }

        private void ApplyFacialExpressions()
        {
            if (_expressionMode == ExpressionMode.Preset)
            {
                // Presetモード: 基本的な定義済み表情のみ適用
                _ApplyPresetExpressions();
            }
            else if (_expressionMode == ExpressionMode.PerfectSync)
            {
                // PerfectSyncモード: より詳細な表情制御
                _ApplyPerfectSyncExpressions();
            }
        }

        // IAvatar implementation
        void IAvatar.BuildAvatar()
        {
            if (_animator == null || _animator.avatar == null)
            {
                Debug.LogError("[Studio] VRM1Avatar: Animator or Avatar is null.");
                return;
            }

            var humanDescription = _animator.avatar.humanDescription;
            var avatarBuildData = AvatarBuildSystem.CreateAvatarBuildData(transform, humanDescription);
            if (avatarBuildData.humanBones == null || avatarBuildData.humanBones.Length == 0)
            {
                Debug.LogError("[Studio] VRM1Avatar: Failed to extract Avatar data.");
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
            if (_vrm10RuntimeExpression == null) return false;

            expressionResolver.SetWeight(key.name, weight);
            return true;
        }

        float IExpressionAvatar.GetWeight(FacialKey key)
        {
            if (!expressionResolver.isSetup) return 0f;

            if (expressionResolver.smoothedOutputs.TryGet(key.name, out float weight))
            {
                return weight;
            }

            return 0f;
        }

        ReadOnlySpan<FacialKey> IExpressionAvatar.GetExpressions()
        {
            return _expressionKeys ?? ReadOnlySpan<FacialKey>.Empty;
        }


        private void _ApplyPresetExpressions()
        {
            if (_vrm10RuntimeExpression == null || !_jawOpenKey.HasValue) return;

            _vrm10RuntimeExpression.SetWeight(_jawOpenKey.Value, expressionResolver.arkitWeightData.AtWeight(ARKitBlendShapeLocation.JawOpen));
        }

        private unsafe void _ApplyPerfectSyncExpressions()
        {
            if (_vrm10RuntimeExpression == null || _blendShapeNameCache == null || _expressionKeyCache == null) return;

            var arkitWeight = expressionResolver.arkitWeightData;

            // ARKit関連のBlendShapeの重みを適用する
            for (int i = 0; i < (int)ARKitBlendShapeLocation.Max; i++)
            {
                var arkitLocation = (ARKitBlendShapeLocation)i;
                float weight = arkitWeight.weights[i];

                var expressionKey = _GetCachedARKitExpressionKey(arkitLocation);
                if (expressionKey.HasValue)
                {
                    _vrm10RuntimeExpression.SetWeight(expressionKey.Value, weight);
                }
            }
        }

        [ContextMenu("Reset Physics")]
        public void ResetPhysics()
        {
            _vrm10Instance.Runtime.SpringBone.RestoreInitialTransform();

            ReconstructPhysics();
        }

        public void ReconstructPhysics()
        {
            var headBone = _animator.GetBoneTransform(HumanBodyBones.Spine);
            foreach (var spring in _vrm10Instance.SpringBone.Springs)
            {
                spring.Center = headBone;
            }
            _vrm10Instance.Runtime.SpringBone.ReconstructSpringBone();
        }
    }
}

#endif
