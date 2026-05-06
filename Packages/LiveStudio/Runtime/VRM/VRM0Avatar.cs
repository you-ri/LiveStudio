#if VRMC_VRM10
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;
using VRM;

using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// VRM 0.x用のFacialController
    /// VRMBlendShapeProxyを使用して表情を制御する
    /// </summary>
    [DefaultExecutionOrder(10)]
    [RequireComponent(typeof(Animator))]
    [RequireComponent(typeof(VRMBlendShapeProxy))]
    public class VRM0Avatar : MonoBehaviour, IAvatar
    {
        private bool _isTracking = false;

        private Animator _animator;

        private MotionSourceBase _motionSource;

        [SerializeReference, Select]
        public IExpressionResolver expressionResolver = new DefaultExpressionResolver();

        private VRMBlendShapeProxy _blendShapeProxy;

        public ExpressionMode expressionMode
        {
            get => _expressionMode;
            set => _expressionMode = value;
        }

        [SerializeField]
        private Vector2 _eyeRotationMax = new Vector2(90, 90);

        [SerializeField]
        public ExpressionMode _expressionMode = ExpressionMode.Preset;

        // VRM 0.xのBlendShapeKey
        private BlendShapeKey? _lookRightKey;
        private BlendShapeKey? _lookLeftKey;
        private BlendShapeKey? _lookUpKey;
        private BlendShapeKey? _lookDownKey;
        private BlendShapeKey? _blinkKey;
        private BlendShapeKey? _blinkLKey;
        private BlendShapeKey? _blinkRKey;
        private BlendShapeKey? _jawOpenKey;

        // キャッシュ用のフィールド
        private Dictionary<string, BlendShapeKey?> _blendShapeKeyCache;
        private Dictionary<ARKitBlendShapeLocation, BlendShapeKey?> _arkitBlendShapeKeyCache;
        private Dictionary<string, string> _blendShapeNameCache;
        private FacialKey[] _expressionKeys;

        private ARKitWeightDataView _sourceArKitWeightDataView = new ARKitWeightDataView();

        // VRM 0.x LookAt参照
        private VRMLookAtHead _lookAtHead;

        // SpringBone参照
        private VRMSpringBone[] _springBones;

        // スムージング処理用（GC回避）
        private const int kMaxExpressionCount = 64;

        void _Initialize()
        {
            _animator = GetComponent<Animator>();
            _blendShapeProxy = GetComponent<VRMBlendShapeProxy>();

            _InitializeBlendShapeKeys();
            _InitializeCache();
            _InitializeLookAt();
            _InitializeSpringBones();

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

            ((IAvatar)this).BuildAvatar();
        }

        void OnDestroy()
        {
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

            if (_animator != null)
            {
                AvatarAnimationSystem.UpdateBodyAnimation(_animator, in _motionSource.frameData);
            }

            if (_blendShapeProxy == null) return;

            expressionResolver.Resolve(in _motionSource.frameData.expression);

            // スムージングされた表情を最初に適用
            ApplySmoothedWeights();

            ApplyBlink();
            ApplyLookAtBones();
            //ApplyLookAtExpressions();
            ApplyFacialExpressions();
        }

        private void _InitializeBlendShapeKeys()
        {
            if (_blendShapeProxy == null) return;

            // プリセット表情のキーを取得
            _lookRightKey = _CreateBlendShapeKeyFromPreset(BlendShapePreset.LookRight);
            _lookLeftKey = _CreateBlendShapeKeyFromPreset(BlendShapePreset.LookLeft);
            _lookUpKey = _CreateBlendShapeKeyFromPreset(BlendShapePreset.LookUp);
            _lookDownKey = _CreateBlendShapeKeyFromPreset(BlendShapePreset.LookDown);
            _blinkKey = _CreateBlendShapeKeyFromPreset(BlendShapePreset.Blink);
            _blinkLKey = _CreateBlendShapeKeyFromPreset(BlendShapePreset.Blink_L);
            _blinkRKey = _CreateBlendShapeKeyFromPreset(BlendShapePreset.Blink_R);
            _jawOpenKey = _CreateBlendShapeKeyFromPreset(BlendShapePreset.A);
        }

        /// <summary>
        /// キャッシュ生成
        /// </summary>
        private void _InitializeCache()
        {
            if (_blendShapeProxy == null) return;

            // BlendShapeKeyキャッシュの初期化
            _blendShapeKeyCache = new Dictionary<string, BlendShapeKey?>();
            _blendShapeNameCache = new Dictionary<string, string>();

            var facialKeysList = new List<FacialKey>();
            var expressionKeysList = new List<FacialKey>();

            // VRMBlendShapeProxyからクリップを取得
            var blendShapeAvatar = _blendShapeProxy.BlendShapeAvatar;
            if (blendShapeAvatar == null) return;

            foreach (var clip in blendShapeAvatar.Clips)
            {
                if (clip == null) continue;

                var clipName = clip.BlendShapeName;
                var normalizedName = clipName.ToLowerInvariant();

                BlendShapeKey blendShapeKey;
                if (clip.Preset != BlendShapePreset.Unknown)
                {
                    blendShapeKey = BlendShapeKey.CreateFromPreset(clip.Preset);
                }
                else
                {
                    blendShapeKey = BlendShapeKey.CreateUnknown(clipName);
                }

                _blendShapeKeyCache[normalizedName] = blendShapeKey;
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

            // ARKit BlendShapeロケーションとVRM BlendShapeKeyのマッピング
            _arkitBlendShapeKeyCache = new Dictionary<ARKitBlendShapeLocation, BlendShapeKey?>();
            foreach (ARKitBlendShapeLocation loc in System.Enum.GetValues(typeof(ARKitBlendShapeLocation)))
            {
                if (loc == ARKitBlendShapeLocation.Max) continue;

                var locName = loc.ToString();
                var blendShapeKey = _CreateBlendShapeKey(locName);
                if (blendShapeKey.HasValue)
                {
                    _arkitBlendShapeKeyCache[loc] = blendShapeKey;
                }
            }
        }

        private void _InitializeLookAt()
        {
            _lookAtHead = GetComponent<VRMLookAtHead>();
            if (_lookAtHead != null)
            {
                // VRMLookAtHeadのUpdateTypeをNoneに設定して自動更新を無効化
                // このコントローラーから手動で制御する
                _lookAtHead.UpdateType = UpdateType.None;
            }
        }

        private void _InitializeSpringBones()
        {
            _springBones = GetComponentsInChildren<VRMSpringBone>(true);
        }

        private BlendShapeKey? _CreateBlendShapeKeyFromPreset(BlendShapePreset preset)
        {
            if (_blendShapeProxy == null) return null;

            var key = BlendShapeKey.CreateFromPreset(preset);
            // キーが存在するか確認
            var avatar = _blendShapeProxy.BlendShapeAvatar;
            if (avatar != null)
            {
                foreach (var clip in avatar.Clips)
                {
                    if (clip != null && clip.Preset == preset)
                    {
                        return key;
                    }
                }
            }

            return null;
        }

        private BlendShapeKey? _CreateBlendShapeKey(string name)
        {
            if (_blendShapeProxy == null) return null;

            var avatar = _blendShapeProxy.BlendShapeAvatar;
            if (avatar == null) return null;

            foreach (var clip in avatar.Clips)
            {
                if (clip != null && clip.BlendShapeName.ToLower().Contains(name.ToLower()))
                {
                    if (clip.Preset != BlendShapePreset.Unknown)
                    {
                        return BlendShapeKey.CreateFromPreset(clip.Preset);
                    }
                    else
                    {
                        return BlendShapeKey.CreateUnknown(clip.BlendShapeName);
                    }
                }
            }

            return null;
        }

        private BlendShapeKey? _GetCachedBlendShapeKey(string name)
        {
            if (_blendShapeKeyCache == null) return null;

            var normalizedName = name.ToLowerInvariant();

            // 完全一致を試す
            if (_blendShapeKeyCache.TryGetValue(normalizedName, out var exactMatch))
            {
                return exactMatch;
            }

            // 部分一致を試す
            foreach (var kvp in _blendShapeKeyCache)
            {
                if (kvp.Key.Contains(normalizedName) || normalizedName.Contains(kvp.Key))
                {
                    return kvp.Value;
                }
            }

            return null;
        }

        private BlendShapeKey? _GetCachedARKitBlendShapeKey(ARKitBlendShapeLocation location)
        {
            if (_arkitBlendShapeKeyCache == null) return null;

            if (_arkitBlendShapeKeyCache.TryGetValue(location, out var blendShapeKey))
            {
                return blendShapeKey;
            }

            return null;
        }

        private float _GetWeight(BlendShapeKey key)
        {
            if (_blendShapeProxy == null) return 0;
            return _blendShapeProxy.GetValue(key);
        }

        private void _SetWeight(BlendShapeKey key, float value)
        {
            if (_blendShapeProxy == null) return;
            _blendShapeProxy.ImmediatelySetValue(key, value);
        }

        private void ApplyBlink()
        {
            if (_blendShapeProxy == null) return;
            if (expressionMode != ExpressionMode.Preset) return;

            var arkitWeight = expressionResolver.arkitWeightData;

            // まばたき制御
            if (_blinkLKey.HasValue)
                _SetWeight(_blinkLKey.Value, arkitWeight.AtWeight(ARKitBlendShapeLocation.EyeBlinkLeft));
            if (_blinkRKey.HasValue)
                _SetWeight(_blinkRKey.Value, arkitWeight.AtWeight(ARKitBlendShapeLocation.EyeBlinkRight));
        }

        private void ApplyLookAtExpressions()
        {
            if (_blendShapeProxy == null) return;

            // 目線制御
            var eyeDir = expressionResolver.eyeDirection;
            var yaw = eyeDir.x;
            var pitch = eyeDir.y;

            // 表情での目線制御
            if (_lookRightKey.HasValue)
                _SetWeight(_lookRightKey.Value, Mathf.Clamp01(-yaw));
            if (_lookLeftKey.HasValue)
                _SetWeight(_lookLeftKey.Value, Mathf.Clamp01(yaw));
            if (_lookUpKey.HasValue)
                _SetWeight(_lookUpKey.Value, Mathf.Clamp01(pitch));
            if (_lookDownKey.HasValue)
                _SetWeight(_lookDownKey.Value, Mathf.Clamp01(-pitch));
        }

        private void ApplyLookAtBones()
        {
            if (_lookAtHead == null) return;

            // _eyeDirection: x = yaw (-1 ~ 1), y = pitch (-1 ~ 1)
            // VRMLookAtHead: yaw (度), pitch (度)
            var eyeDir = expressionResolver.eyeDirection;
            var yaw = - eyeDir.x * _eyeRotationMax.x;
            var pitch = eyeDir.y * _eyeRotationMax.y;

            // VRMLookAtHead経由で視線を設定
            // これにより VRMLookAtBoneApplyer または VRMLookAtBlendShapeApplyer が適用される
            _lookAtHead.RaiseYawPitchChanged(yaw, pitch);
        }

        private void ApplySmoothedWeights()
        {
            if (_blendShapeProxy == null) return;

            var outputs = expressionResolver.smoothedOutputs;
            if (!outputs.IsCreated) return;

            // 結果を適用
            int count = Mathf.Min(outputs.Length, kMaxExpressionCount);
            for (int i = 0; i < count; i++)
            {
                var output = outputs.values[i];
                var blendShapeKey = _GetCachedBlendShapeKey(output.name.ToString());
                if (blendShapeKey.HasValue)
                {
                    _SetWeight(blendShapeKey.Value, output.weight);
                }
            }

            _sourceArKitWeightDataView.SetData(expressionResolver.arkitWeightData);
        }

        private void ApplyFacialExpressions()
        {
            if (_expressionMode == ExpressionMode.Preset)
            {
                _ApplyPresetExpressions();
            }
            else if (_expressionMode == ExpressionMode.PerfectSync)
            {
                _ApplyPerfectSyncExpressions();
            }
        }

        // IAvatar implementation
        void IAvatar.BuildAvatar()
        {
            if (_animator == null || _animator.avatar == null)
            {
                Debug.LogError("[Studio] VRM0Avatar: Animator or Avatar is null.");
                return;
            }

            var humanDescription = _animator.avatar.humanDescription;
            var avatarBuildData = AvatarBuildSystem.CreateAvatarBuildData(transform, humanDescription);
            if (avatarBuildData.humanBones == null || avatarBuildData.humanBones.Length == 0)
            {
                Debug.LogError("[Studio] VRM0Avatar: Failed to extract Avatar data.");
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
            if (_blendShapeProxy == null) return false;

            expressionResolver.SetWeight(key.name, weight);
            return true;
        }

        float IExpressionAvatar.GetWeight(FacialKey key)
        {
            if (_blendShapeProxy == null) return 0;

            var blendShapeKey = _GetCachedBlendShapeKey(key.name);
            if (blendShapeKey.HasValue)
            {
                return _GetWeight(blendShapeKey.Value);
            }

            return 0;
        }

        ReadOnlySpan<FacialKey> IExpressionAvatar.GetExpressions()
        {
            return _expressionKeys ?? ReadOnlySpan<FacialKey>.Empty;
        }

        private void _ApplyPresetExpressions()
        {
            if (_blendShapeProxy != null && _jawOpenKey.HasValue)
            {
                _SetWeight(_jawOpenKey.Value, expressionResolver.arkitWeightData.AtWeight(ARKitBlendShapeLocation.JawOpen));
            }
        }

        private void _ApplyPerfectSyncExpressions()
        {
            if (_blendShapeProxy == null || _blendShapeNameCache == null || _blendShapeKeyCache == null) return;

            var arkitWeight = expressionResolver.arkitWeightData;

            // ARKit関連のBlendShapeの重みを適用する
            for (int i = 0; i < (int)ARKitBlendShapeLocation.Max; i++)
            {
                var arkitLocation = (ARKitBlendShapeLocation)i;
                float weight;
                unsafe
                {
                    weight = arkitWeight.weights[i];
                }

                var blendShapeKey = _GetCachedARKitBlendShapeKey(arkitLocation);
                if (blendShapeKey.HasValue)
                {
                    _SetWeight(blendShapeKey.Value, weight);
                }
            }
        }

        void OnGUI()
        {
            //_sourceArKitWeightDataView.startPosition = new Vector2(10, 500);
            //_sourceArKitWeightDataView.Draw();
        }

        [ContextMenu("Reset Physics")]
        public void ResetPhysics()
        {
            if (_springBones == null) return;

            foreach (var springBone in _springBones)
            {
                if (springBone != null)
                {
                    // VRMSpringBoneの状態をリセット
                    springBone.Setup();
                }
            }
        }
    }
}
#endif
