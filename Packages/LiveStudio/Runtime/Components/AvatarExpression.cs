using System;
using System.Collections.Generic;
using UnityEngine;
using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    [DefaultExecutionOrder(200)]
    public class AvatarExpression : MonoBehaviour, IAvatarExpression
    {
        private IAvatarService _avatarService;

        private IExpressionAvatar _facialController => _avatarService?.target != null ? _avatarService.target.GetComponent<IExpressionAvatar>() : null;

        // ウェイト値変更時のイベント
        public System.Action<string, float> OnExpressionWeightChanged { get; set; }

        void Initialize()
        {
            _avatarService = GetComponent<IAvatarService>();
        }

        // 非アクティブ状態でも表情を読み取れるよう、登録は enabled/active 状態に依存させず
        // Awake/OnDestroy のライフサイクルに紐付ける (OnEnable/OnDisable だと GameObject 無効化で
        // Unregister され、AvatarController.expressions 読み取り時に警告が出てしまう)。
        void Awake()
        {
            Service<IAvatarExpression>.Register(this);
        }

        void OnDestroy()
        {
            Service<IAvatarExpression>.Unregister(this);
        }

        void OnValidate()
        {
            Initialize();
        }

        void Start()
        {
            Initialize();
        }

        /// <summary>
        /// 利用可能な表情リストを取得。アバター本体の表情に加え、アバターが装着している
        /// AvatarItem 固有の表情も名前で重複排除しつつマージして返す。
        /// </summary>
        public FacialKey[] GetAvailableExpressions()
        {
            var result = new List<FacialKey>();
            var seen = new HashSet<string>();

            var controller = _facialController;
            if (controller != null)
            {
                foreach (var key in controller.GetExpressions())
                {
                    if (seen.Add(key.name)) result.Add(key);
                }
            }

            // アバターが装着している item の表情もマージする (item はアバターの子)。
            var target = _avatarService?.target;
            if (target != null)
            {
                var items = target.GetComponentsInChildren<AvatarItem>(includeInactive: true);
                foreach (var item in items)
                {
                    foreach (var key in item.GetExpressions())
                    {
                        if (seen.Add(key.name)) result.Add(key);
                    }
                }
            }

            return result.ToArray();
        }

        /// <summary>
        /// 指定した表情の現在のウェイト値を取得
        /// </summary>
        public float GetExpressionWeight(FacialKey facialKey)
        {
            if (_facialController == null) return 0f;
            return _facialController.GetWeight(facialKey);
        }

        /// <summary>
        /// 指定した表情のウェイト値を直接設定
        /// </summary>
        public void SetExpressionWeight(FacialKey facialKey, float weight)
        {
            if (_facialController == null) return;
            _facialController.SetWeight(facialKey, Mathf.Clamp01(weight));
        }
    }
}
