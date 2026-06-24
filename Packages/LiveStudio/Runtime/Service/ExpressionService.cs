using System.Linq;
using UnityEngine;
using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// AvatarExpressionへのアクセスを提供するstaticマネージャークラス
    /// InputActionProviderパターンに基づく実装
    /// </summary>
    public static class ExpressionService
    {
        /// <summary>
        /// 利用可能な表情リストを取得
        /// </summary>
        public static FacialKey[] GetAvailableExpressions()
        {
            var controller = Service<IAvatarExpression>.subjects.FirstOrDefault();
            if (controller == null)
            {
                Debug.LogWarning("[Studio] AvatarExpression is not available");
                return new FacialKey[0];
            }
            return controller.GetAvailableExpressions();
        }

        /// <summary>
        /// 指定した表情の現在のウェイト値を取得
        /// </summary>
        public static float GetExpressionWeight(FacialKey facialKey)
        {
            var controller = Service<IAvatarExpression>.subjects.FirstOrDefault();
            if (controller == null)
            {
                return 0f;
            }
            return controller.GetExpressionWeight(facialKey);
        }

        /// <summary>
        /// 指定した表情のウェイト値を直接設定
        /// </summary>
        public static void SetExpressionWeight(FacialKey facialKey, float weight)
        {
            var controller = Service<IAvatarExpression>.subjects.FirstOrDefault();
            controller?.SetExpressionWeight(facialKey, weight);
        }

        /// <summary>
        /// AvatarExpressionが利用可能かチェック
        /// </summary>
        public static bool IsAvailable()
        {
            return Service<IAvatarExpression>.subjects.FirstOrDefault() != null;
        }

        /// <summary>
        /// ウェイト値変更イベントを設定
        /// </summary>
        public static void SetOnExpressionWeightChanged(System.Action<string, float> callback)
        {
            var controller = Service<IAvatarExpression>.subjects.FirstOrDefault();
            if (controller != null)
            {
                controller.OnExpressionWeightChanged = callback;
            }
        }

        /// <summary>
        /// ウェイト値変更イベントを追加
        /// </summary>
        public static void AddOnExpressionWeightChanged(System.Action<string, float> callback)
        {
            var controller = Service<IAvatarExpression>.subjects.FirstOrDefault();
            if (controller != null)
            {
                controller.OnExpressionWeightChanged += callback;
            }
        }

        /// <summary>
        /// ウェイト値変更イベントを削除
        /// </summary>
        public static void RemoveOnExpressionWeightChanged(System.Action<string, float> callback)
        {
            var controller = Service<IAvatarExpression>.subjects.FirstOrDefault();
            if (controller != null)
            {
                controller.OnExpressionWeightChanged -= callback;
            }
        }
    }
}