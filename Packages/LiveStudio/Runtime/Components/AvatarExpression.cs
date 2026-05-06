using System;
using System.Collections.Generic;
using UnityEngine;
using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    [DefaultExecutionOrder(200)]
    [RequireComponent(typeof(AvatarInput))]
    public class AvatarExpression : MonoBehaviour, IAvatarExpression
    {
        private AvatarInput _inputActionsController;

        private IAvatarService _avatarService;

        private IExpressionAvatar _facialController => _avatarService?.target != null ? _avatarService.target.GetComponent<IExpressionAvatar>() : null;

        // ウェイト値変更時のイベント
        public System.Action<string, float> OnExpressionWeightChanged { get; set; }

        // Expression アクション名のプレフィックス
        private const string EXPRESSION_ACTION_PREFIX = "Expression.";

        void Initialize()
        {
            _inputActionsController = GetComponent<AvatarInput>();
            _avatarService = GetComponent<IAvatarService>();

        }

        void OnEnable()
        {
            Service<IAvatarExpression>.Register(this);
        }

        void OnDisable()
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

            if (_inputActionsController?.inputActionMap != null)
            {
                InputActionMapUtils.RefreshInputActionMap(_inputActionsController.inputActionMap);

                // 既存のExpression.*アクションにコールバックを登録
                _RegisterExpressionCallbacks();
            }
            else
            {
                Debug.LogError("[LiveStudio] AvatarInput or InputActionMap is not assigned");
            }
        }

        /// <summary>
        /// 表情名からアクション名を作成
        /// </summary>
        private string CreateActionName(string expressionName)
        {
            return EXPRESSION_ACTION_PREFIX + expressionName;
        }

        /// <summary>
        /// InputActionの入力コールバックを処理
        /// </summary>
        private void _OnExpressionActionPerformed(UnityEngine.InputSystem.InputAction.CallbackContext context)
        {
            if (_facialController == null) return;

            string actionName = context.action.name;
            if (!actionName.StartsWith(EXPRESSION_ACTION_PREFIX)) return;

            string expressionName = actionName.Substring(EXPRESSION_ACTION_PREFIX.Length);
            FacialKey facialKey = FacialKey.CreateCustom(expressionName);

            // ボタンの押下状態に基づいて表情の重みを設定
            float weight = context.ReadValue<float>() > 0.5f ? 1f : 0f;
            _facialController.SetWeight(facialKey, weight);

            // ウェイト値変更イベントを発火
            OnExpressionWeightChanged?.Invoke(expressionName, weight);
        }

        /// <summary>
        /// 既存のExpression.*アクションにコールバックを登録
        /// </summary>
        private void _RegisterExpressionCallbacks()
        {
            if (_inputActionsController?.inputActionMap == null) return;

            foreach (var action in _inputActionsController.inputActionMap.actions)
            {
                if (action.name.StartsWith(EXPRESSION_ACTION_PREFIX))
                {
                    // 重複登録を避けるため、一度削除してから追加
                    action.performed -= _OnExpressionActionPerformed;
                    action.canceled -= _OnExpressionActionPerformed;

                    action.performed += _OnExpressionActionPerformed;
                    action.canceled += _OnExpressionActionPerformed;
                }
            }
        }


        /// <summary>
        /// 表情に対するバインドを開始
        /// </summary>
        /// <param name="expressionName">表情名</param>
        public async System.Threading.Tasks.Task<bool> StartExpressionBindingAsync(string expressionName)
        {
            if (_inputActionsController == null || _inputActionsController.inputActionMap == null)
            {
                Debug.LogError("[LiveStudio] AvatarInput or InputActionMap is not available");
                return false;
            }

            // Expression.{expressionName}の形式でアクション名を作成
            string actionName = CreateActionName(expressionName);

            // アクションが存在しない場合は先に作成
            var action = InputActionService.FindInputAction(actionName);
            if (action == null)
            {

                // InputActionMap一時無効化してアクション作成
                bool wasEnabled = _inputActionsController.inputActionMap.enabled;
                if (wasEnabled)
                {
                    _inputActionsController.inputActionMap.Disable();
                }

                try
                {
                    string newActionName = CreateActionName(expressionName);
                    var newAction = InputActionMapUtils.SafeCreateAction(_inputActionsController.inputActionMap, newActionName, "<Value>");

                    // 新規作成されたアクションはバインディング0個の状態で作成
                    Debug.Log($"[LiveStudio] Created new action '{newActionName}' with 0 bindings (ready for rebinding)");

                    // InputActionコールバックを登録
                    if (newAction != null)
                    {
                        newAction.performed += _OnExpressionActionPerformed;
                        newAction.canceled += _OnExpressionActionPerformed;
                    }

                }
                finally
                {
                    // InputActionMapを元の状態に戻す
                    if (wasEnabled)
                    {
                        _inputActionsController.inputActionMap.Enable();
                    }
                }

                action = _inputActionsController.inputActionMap.FindAction(actionName);
                if (action == null)
                {
                    Debug.LogError($"[LiveStudio] Failed to create action for expression: {expressionName}");
                    return false;
                }
            }

            // RuntimeKeyBindingSystemを使用してリバインディングを実行
            // カスタムデータが指定されている場合はそれを使用、そうでなければ新規作成
            var bindingData = new RuntimeKeyBindingData();
            var (success, updatedData) = await RuntimeKeyBindingSystem.StartBindingAsync(
                bindingData,
                _inputActionsController.inputActionMap,
                actionName,
                0 // 最初のバインディングを対象
            );

            if (success)
            {
                Debug.Log($"[LiveStudio] Expression rebinding successful for: {expressionName}");
            }
            else
            {
                Debug.LogWarning($"[LiveStudio] Expression rebinding cancelled or failed for: {expressionName}");
            }

            return success;
        }



        /// <summary>
        /// 利用可能な表情リストを取得
        /// </summary>
        public FacialKey[] GetAvailableExpressions()
        {
            var controller = _facialController;
            if (controller != null)
            {
                var expressions = controller.GetExpressions();
                if (expressions.Length > 0)
                {
                    return expressions.ToArray();
                }
            }

            return Array.Empty<FacialKey>();
        }


        /// <summary>
        /// 現在の表情バインディング状況を表示
        /// </summary>
        [ContextMenu("Show Expression Bindings")]
        public void ShowExpressionBindings()
        {
            if (_inputActionsController?.inputActionMap == null)
            {
                Debug.LogError("[LiveStudio] AvatarInput or InputActionMap is not available");
                return;
            }

            Debug.Log($"[LiveStudio] InputActionMap: {_inputActionsController.inputActionMap.name}");
            Debug.Log($"[LiveStudio] Total actions in map: {_inputActionsController.inputActionMap.actions.Count}");

            int bindingCount = 0;
            foreach (var action in _inputActionsController.inputActionMap.actions)
            {
                Debug.Log($"[LiveStudio] Action found: {action.name}");

                if (action.name.StartsWith(EXPRESSION_ACTION_PREFIX))
                {
                    string expressionName = action.name.Substring(EXPRESSION_ACTION_PREFIX.Length);
                    Debug.Log($"[LiveStudio] Expression: {expressionName} -> {action.name} (bindings: {action.bindings.Count})");
                    bindingCount++;
                }
            }
            Debug.Log($"[LiveStudio] Total expression bindings: {bindingCount}");
        }

        /// <summary>
        /// すべての表情バインディング名を取得
        /// </summary>
        public List<string> GetAllExpressionNames()
        {
            var expressionNames = new List<string>();
            if (_inputActionsController?.inputActionMap != null)
            {
                foreach (var action in _inputActionsController.inputActionMap.actions)
                {
                    if (action.name.StartsWith(EXPRESSION_ACTION_PREFIX))
                    {
                        string expressionName = action.name.Substring(EXPRESSION_ACTION_PREFIX.Length);
                        expressionNames.Add(expressionName);
                    }
                }
            }
            return expressionNames;
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

        /// <summary>
        /// 新しい表情アクションを追加
        /// </summary>
        /// <param name="expressionName">表情名</param>
        /// <returns>追加に成功した場合true</returns>
        public bool AddExpression(string expressionName)
        {
            if (_inputActionsController?.inputActionMap == null)
            {
                Debug.LogError("[LiveStudio] AvatarInput or InputActionMap is not available");
                return false;
            }

            string actionName = CreateActionName(expressionName);

            // アクションが既に存在するかチェック
            var existingAction = _inputActionsController.inputActionMap.FindAction(actionName);
            if (existingAction != null)
            {
                Debug.LogWarning($"[LiveStudio] Expression '{expressionName}' already exists");
                return false;
            }

            try
            {
                // InputActionMapを一時的に無効化してアクションを追加
                bool wasEnabled = _inputActionsController.inputActionMap.enabled;
                if (wasEnabled)
                {
                    _inputActionsController.inputActionMap.Disable();
                }

                // InputActionMapUtilsを使用してアクションを作成
                var newAction = InputActionMapUtils.SafeCreateAction(_inputActionsController.inputActionMap, actionName, "<Value>");

                if (newAction != null)
                {
                    // コールバックを登録
                    newAction.performed += _OnExpressionActionPerformed;
                    newAction.canceled += _OnExpressionActionPerformed;

                    Debug.Log($"[LiveStudio] Added expression: {expressionName}");
                }

                // InputActionMapを元の状態に復元
                if (wasEnabled)
                {
                    _inputActionsController.inputActionMap.Enable();
                }

                return newAction != null;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[LiveStudio] Error adding expression '{expressionName}': {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// 指定した表情アクションを削除
        /// </summary>
        /// <param name="expressionName">表情名</param>
        /// <returns>削除に成功した場合true</returns>
        public bool RemoveExpression(string expressionName)
        {
            if (_inputActionsController?.inputActionMap == null)
            {
                Debug.LogError("[LiveStudio] AvatarInput or InputActionMap is not available");
                return false;
            }

            string actionName = CreateActionName(expressionName);
            var action = _inputActionsController.inputActionMap.FindAction(actionName);

            if (action == null)
            {
                Debug.LogWarning($"[LiveStudio] Expression '{expressionName}' not found");
                return false;
            }

            try
            {
                // InputActionMapを一時的に無効化
                bool wasEnabled = _inputActionsController.inputActionMap.enabled;
                if (wasEnabled)
                {
                    _inputActionsController.inputActionMap.Disable();
                }

                // コールバックを削除
                action.performed -= _OnExpressionActionPerformed;
                action.canceled -= _OnExpressionActionPerformed;

                // InputActionMapUtilsを使用してアクションを削除
                bool success = InputActionMapUtils.SafeRemoveAction(_inputActionsController.inputActionMap, actionName);

                // InputActionMapを元の状態に復元
                if (wasEnabled)
                {
                    _inputActionsController.inputActionMap.Enable();
                }


                return success;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[LiveStudio] Error removing expression '{expressionName}': {e.Message}");
                return false;
            }
        }
    }
}