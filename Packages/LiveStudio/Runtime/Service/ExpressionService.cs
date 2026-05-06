using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
        /// 表情に対するバインドを開始
        /// </summary>
        public static async Task<bool> StartExpressionBindingAsync(string expressionName)
        {
            var controller = Service<IAvatarExpression>.subjects.FirstOrDefault();
            if (controller == null)
            {
                Debug.LogError("[Studio] AvatarExpression is not available");
                return false;
            }
            return await controller.StartExpressionBindingAsync(expressionName);
        }

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
        /// すべての表情バインディング名を取得
        /// </summary>
        public static List<string> GetAllExpressionNames()
        {
            var controller = Service<IAvatarExpression>.subjects.FirstOrDefault();
            if (controller == null)
            {
            Debug.LogWarning("[Studio] AvatarExpression is not available");
                return new List<string>();
            }
            return controller.GetAllExpressionNames();
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
        /// 新しい表情アクションを追加
        /// </summary>
        public static bool AddExpression(string expressionName)
        {
            var controller = Service<IAvatarExpression>.subjects.FirstOrDefault();
            if (controller == null)
            {
                Debug.LogError("[Studio] AvatarExpression is not available");
                return false;
            }
            return controller.AddExpression(expressionName);
        }

        /// <summary>
        /// 指定した表情アクションを削除（
        /// </summary>
        public static bool RemoveExpression(string expressionName)
        {
            var controller = Service<IAvatarExpression>.subjects.FirstOrDefault();
            if (controller == null)
            {
                Debug.LogError("[Studio] AvatarExpression is not available");
                return false;
            }
            return controller.RemoveExpression(expressionName);
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