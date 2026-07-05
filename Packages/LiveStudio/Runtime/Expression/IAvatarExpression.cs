// Copyright (c) You-Ri, 2026

namespace Lilium.LiveStudio
{
    public interface IAvatarExpression
    {
        // ウェイト値変更時のイベント
        System.Action<string, float> OnExpressionWeightChanged { get; set; }

        // 表情関連の操作。キー割り当ては OperationManager (OperationSet -> SetPropertyOperation で
        // expressions[name].weight を駆動) が担うためここには含めない。
        FacialKey[] GetAvailableExpressions();
        float GetExpressionWeight(FacialKey facialKey);
        void SetExpressionWeight(FacialKey facialKey, float weight);
    }
}
