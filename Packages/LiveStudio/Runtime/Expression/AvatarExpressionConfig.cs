// Copyright (c) You-Ri, 2026

using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
using Unity.Collections;

using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    [CreateAssetMenu(fileName = "AvatarExpressionConfig", menuName = "Live Studio/Avatar Expression Config")]
    [LiveClass(Icon = "tune")]
    [MovedFrom(false, "Lilium.Virgo.Studio", "Lilium.Virgo.Studio2", null)]
    public class AvatarExpressionConfig : ScriptableObject
    {
        [LiveField]
        public ARKitWeightAdjustmentData sourceWeightAdjustments = ARKitWeightAdjustmentData.Default;

        // モデルによっては左右の瞬きウェイトの精度に偏りが出て非対称なまばたきになるため、
        // 有効時は左右の瞬きウェイトを小さい方に揃えて同期させる。
        [LiveField]
        public bool syncBlink = false;

        [LiveField]
        public ExpressionData neutralExpression = ExpressionData.Default;

        [LiveField]
        public ExpressionData[] expressions = new ExpressionData[0];

        public static void Evaluate(in ARKitWeightData arkitWeightData, AvatarExpressionConfig expressionConfig, ref ExpressionWorkData workData)
        {
            ARKitWeightData sourceAdjustedData;

            ExpressionSystem.UpdateWeights(expressionConfig.sourceWeightAdjustments, in arkitWeightData, out sourceAdjustedData);

            // 左右の瞬きを同期（小さい方に揃える）
            if (expressionConfig.syncBlink)
            {
                ExpressionSystem.SyncBlink(ref sourceAdjustedData);
            }

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
