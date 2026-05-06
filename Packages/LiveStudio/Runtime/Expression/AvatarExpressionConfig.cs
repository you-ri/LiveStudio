// Copyright (c) You-Ri, 2026

using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
using Unity.Collections;

using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    [CreateAssetMenu(fileName = "AvatarExpressionConfig", menuName = "LiveStudio/AvatarExpressionConfig")]
    [ExposedClass(Icon = "tune")]
    [MovedFrom(false, "Lilium.Virgo.Studio", "Lilium.Virgo.Studio2", null)]
    public class AvatarExpressionConfig : ScriptableObject
    {
        [ExposedField]
        public ARKitWeightAdjustmentData sourceWeightAdjustments = ARKitWeightAdjustmentData.Default;

        [ExposedField]
        public ExpressionData neutralExpression = ExpressionData.Default;

        [ExposedField]
        public ExpressionData[] expressions = new ExpressionData[0];

        public static void Evaluate(in ARKitWeightData arkitWeightData, AvatarExpressionConfig expressionConfig, ref ExpressionWorkData workData)
        {
            ARKitWeightData sourceAdjustedData;

            ExpressionSystem.UpdateWeights(expressionConfig.sourceWeightAdjustments, in arkitWeightData, out sourceAdjustedData);

            // neutralの計算結果をworkDataに格納
            ExpressionSystem.UpdateWeights(expressionConfig.neutralExpression, in sourceAdjustedData, out workData.neutralArkitWeight);

            // 配列サイズ確保
            workData.EnsureCapacity(expressionConfig.expressions.Length, Allocator.Persistent);

            // 各expressionの計算結果をworkDataに格納
            for (int i = 0; i < expressionConfig.expressions.Length; i++)
            {
                ExpressionSystem.UpdateWeights(expressionConfig.expressions[i], in sourceAdjustedData, out var arkitWeight);
                workData.expressionArkitWeights[i] = arkitWeight;
            }
        }
    }
}
