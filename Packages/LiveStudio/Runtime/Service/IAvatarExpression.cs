// Copyright (c) You-Ri, 2026
using System.Collections.Generic;

namespace Lilium.LiveStudio
{
    public interface IAvatarExpression
    {
        // ウェイト値変更時のイベント
        System.Action<string, float> OnExpressionWeightChanged { get; set; }

        // 表情関連の操作
        System.Threading.Tasks.Task<bool> StartExpressionBindingAsync(string expressionName);
        FacialKey[] GetAvailableExpressions();
        List<string> GetAllExpressionNames();
        float GetExpressionWeight(FacialKey facialKey);
        void SetExpressionWeight(FacialKey facialKey, float weight);
        bool AddExpression(string expressionName);
        bool RemoveExpression(string expressionName);
    }
}
