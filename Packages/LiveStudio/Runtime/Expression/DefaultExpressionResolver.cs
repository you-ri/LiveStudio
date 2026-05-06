// Copyright (c) You-Ri, 2026

using System;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
using Unity.Collections;

using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// デフォルトの表情計算実装
    /// ExpressionConfig評価 → 目線計算 → Mix → Smooth の共通パイプラインを実行
    /// </summary>
    [Serializable]
    [ExposedClass(Icon = "psychology")]
    [MovedFrom(false, "Lilium.Virgo.Studio", "Lilium.Virgo.Studio2", null)]
    public class DefaultExpressionResolver : IExpressionResolver
    {
        [SerializeField]
        public AvatarExpressionConfig _expressionConfig;

        private ExpressionWeights _expressionWeights;
        private ExpressionWeights _mixedExpressionWeights;
        private ExpressionWeights _smoothedOutputs;
        private ExpressionWorkData _expressionWorkData;
        private ARKitWeightData _arkitWeightData;
        private Vector2 _eyeDirection;
        private float _totalWeight;
        private bool _isSetup;

        public override AvatarExpressionConfig expressionConfig
        {
            get => _expressionConfig;
            set => _expressionConfig = value;
        }

        public override ARKitWeightData arkitWeightData => _arkitWeightData;
        public override Vector2 eyeDirection => _eyeDirection;
        public override ExpressionWeights smoothedOutputs => _smoothedOutputs;
        public override float totalWeight => _totalWeight;
        public override bool isSetup => _isSetup;

        public override void Setup()
        {
            _expressionWeights = ExpressionWeights.Create(Allocator.Persistent);
            _mixedExpressionWeights = ExpressionWeights.Create(Allocator.Persistent);
            _smoothedOutputs = ExpressionWeights.Create(Allocator.Persistent);
            _expressionWorkData = ExpressionWorkData.Create(0, Allocator.Persistent);
            _arkitWeightData = new ARKitWeightData();
            _eyeDirection = Vector2.zero;
            _totalWeight = 0f;
            _isSetup = true;
        }

        public override void Dispose()
        {
            if (_expressionWeights.IsCreated) _expressionWeights.Dispose();
            if (_mixedExpressionWeights.IsCreated) _mixedExpressionWeights.Dispose();
            if (_smoothedOutputs.IsCreated) _smoothedOutputs.Dispose();
            if (_expressionWorkData.IsCreated) _expressionWorkData.Dispose();
            _isSetup = false;
        }

        public override void SetWeight(string name, float weight)
        {
            if (!_isSetup) return;
            _expressionWeights.SetOrAdd(name, weight);
        }

        public override void RemoveWeight(string name)
        {
            if (!_isSetup) return;
            _expressionWeights.Remove(name);
        }

        public override void Resolve(in ARKitWeightData arkitWeightData)
        {
            if (!_isSetup) return;

            // 1. ExpressionConfigの評価
            if (_expressionConfig != null)
            {
                AvatarExpressionConfig.Evaluate(in arkitWeightData, _expressionConfig, ref _expressionWorkData);
            }

            // 2. 目線方向の計算
            ExpressionSystem.CalculateEyeDirection(_expressionWorkData.neutralArkitWeight, out _eyeDirection);

            // 3. 重みのミックス
            ExpressionSystem.Mix(in _expressionWeights, ref _mixedExpressionWeights);

            // 4. 重みが空の場合はニュートラル表情を適用して終了
            if (_mixedExpressionWeights.Length == 0)
            {
                _arkitWeightData = new ARKitWeightData();
                ExpressionSystem.AddARKitWeights(_expressionWorkData.neutralArkitWeight, ref _arkitWeightData, 1f);
                _totalWeight = 0f;
                return;
            }

            // 5. スムージング処理
            var result = ExpressionSystem.Smooth(
                in _mixedExpressionWeights,
                ref _smoothedOutputs,
                ref _expressionWeights,
                _expressionConfig,
                in _expressionWorkData);

            // 6. 結果を格納
            _arkitWeightData = result.arkitWeightData;
            _totalWeight = result.totalWeight;
        }
    }
}
