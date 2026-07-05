using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

using UnityEngine.InputSystem.LowLevel;


#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Lilium.LiveStudio
{

    public static class RuntimeKeyBindingSystem
    {

        public static void ResetAllBindings(InputActionMap playerActionMap, System.Action onBindingChanged = null)
        {
            if (playerActionMap == null)
            {
                Debug.LogError("[Fusion] PlayerActionMap is not initialized");
                return;
            }

            foreach (InputAction action in playerActionMap.actions)
            {
                action.RemoveAllBindingOverrides();
            }
            onBindingChanged?.Invoke();
            Debug.Log("[Fusion] All key bindings reset");
        }

        // 現在のバインディング情報を表示
        public static void ShowCurrentBindings(InputActionMap playerActionMap)
        {
            if (playerActionMap == null)
            {
                Debug.LogError("[Fusion] PlayerActionMap is not initialized");
                return;
            }

            foreach (InputAction action in playerActionMap.actions)
            {
                for (int i = 0; i < action.bindings.Count; i++)
                {
                    var binding = action.bindings[i];
                    Debug.Log($"[Fusion] {action.name}[{i}]: {binding.effectivePath}");
                }
            }
        }

        /// <summary>
        /// 入力検出によるバインド
        /// </summary>
        public static async Task<(bool success, RuntimeKeyBindingData data)> StartBindingAsync(RuntimeKeyBindingData data, InputActionMap playerActionMap, string actionName, int bindingIndex = 0, System.Action onBindingChanged = null)
        {
            InputAction action = playerActionMap.FindAction(actionName);
            if (action == null)
            {
                Debug.LogError($"[Fusion] Action '{actionName}' not found");
                return (false, data);
            }

            // バインディングインデックスの検証
            if (action.bindings.Count == 0)
            {
                Debug.Log($"[Fusion] Action '{actionName}' has 0 bindings, will add new binding when input is detected");
                bindingIndex = 0; // 新しいバインディングのインデックス
            }
            else if (bindingIndex < 0 || bindingIndex >= action.bindings.Count)
            {
                Debug.LogError($"[Fusion] Invalid binding index {bindingIndex} for action '{actionName}' (count: {action.bindings.Count})");
                return (false, data);
            }

            data.SetActionName(actionName);
            data.bindingIndexToRebind = bindingIndex;

            // Runtimeモードの場合のみアクションを無効化
            bool wasActionEnabled = false;
            if (Application.isPlaying)
            {
                wasActionEnabled = action.enabled;
                if (wasActionEnabled)
                {
                    action.Disable();
                }
            }

            var tcs = new System.Threading.Tasks.TaskCompletionSource<bool>();
            string detectedControlPath = null;
            bool isListening = true;

            // 共通の入力検出コールバックを作成
            var onEvent = CreateInputDetectionCallback(
                actionName,
                tcs,
                controlPath => {
                    detectedControlPath = controlPath;
                    isListening = false;
                },
                () => !isListening
            );

            InputSystem.onEvent += onEvent;

            try
            {
                // タイムアウト付きで待機（30秒）
                var timeoutTask = System.Threading.Tasks.Task.Delay(30000);
                var completedTask = await System.Threading.Tasks.Task.WhenAny(tcs.Task, timeoutTask);

                bool success = false;

                if (completedTask == timeoutTask)
                {
                    Debug.LogWarning($"[Fusion] Rebinding timed out for {actionName}");
                    isListening = false;
                }
                else
                {
                    success = await tcs.Task;

                    if (success && !string.IsNullOrEmpty(detectedControlPath))
                    {
                        UpdateActionBinding(action, bindingIndex, detectedControlPath, playerActionMap);
                        onBindingChanged?.Invoke();
                    }
                }

                return (success, data);
            }
            finally
            {
                // クリーンアップ
                InputSystem.onEvent -= onEvent;
                isListening = false;

                // Runtimeモードの場合のみアクションを元の状態に戻す
                if (Application.isPlaying && wasActionEnabled)
                {
                    action.Enable();
                }
            }
        }

        private const float kValueThreshold = 0.5f;

        /// <summary>
        /// 入力検出の共通処理
        /// </summary>
        private static System.Action<InputEventPtr, InputDevice> CreateInputDetectionCallback(
            string actionName,
            TaskCompletionSource<bool> tcs,
            System.Action<string> onControlDetected,
            System.Func<bool> shouldStop = null)
        {

            return (eventPtr, device) =>
            {
                // マウス、タッチ入力を除外
                if (device is Mouse ||
                    device is Pointer ||
                    device is Touchscreen ||
                    device is Pen)
                    return;

                // Editor専用のリスニング停止チェック
                if (shouldStop?.Invoke() == true) return;

                if (!eventPtr.IsA<StateEvent>() && !eventPtr.IsA<DeltaStateEvent>())
                    return;

                // ESCキーチェック
                if (device is Keyboard keyboard)
                {
                    var escapeKey = keyboard.escapeKey;
                    if (escapeKey.ReadValueFromEvent(eventPtr, out var escValue) && escValue >= kValueThreshold)
                    {
                        Debug.Log($"[Fusion] Rebinding cancelled for {actionName}");
                        tcs.SetResult(false);
                        return;
                    }
                }

                // 優先度付きで検出されたコントロールを保存
                string bestControlPath = null;
                int bestPriority = int.MaxValue;
                var buttonPressPoint = kValueThreshold;
                foreach (var control in eventPtr.EnumerateChangedControls())
                {
                    //Debug.Log($"Control {control} changed value to {control.ReadValueFromEventAsObject(eventPtr)}");                

                    // Float型コントロール（ボタン、トリガーなど）
                    if (control is InputControl<float> floatControl)
                    {
                        if (floatControl.synthetic || floatControl.noisy)
                            continue;

                        if (floatControl.ReadValueFromEvent(eventPtr, out var value) && value >= buttonPressPoint)
                        {
                            string controlPath = floatControl.path;
                            int priority = GetControlPriority(device, controlPath);

                            // anyが含まれるコントロールは除外
                            if (controlPath.Contains("any")) continue;


                            Debug.Log($"[Fusion] Control detected: {controlPath} (device: {device.name}, priority: {priority}, value: {value:F3})");

                            if (priority < bestPriority)
                            {
                                bestPriority = priority;
                                bestControlPath = controlPath;
                            }
                        }
                    }
                }

                // 最適なコントロールが検出された場合
                if (bestControlPath != null)
                {
                    Debug.Log($"[Fusion] Selected control: {bestControlPath} (priority: {bestPriority})");
                    onControlDetected(bestControlPath);
                    tcs.SetResult(true);
                    return;
                }
            };
        }

        /// <summary>
        /// バインディング更新の共通処理
        /// </summary>
        private static void UpdateActionBinding(InputAction action, int bindingIndex, string controlPath, InputActionMap playerActionMap = null)
        {
            if (action.bindings.Count == 0)
            {
                // バインディングが0個の場合は新規追加
                action.AddBinding().WithPath(controlPath);
            }
            else
            {
                // 既存のバインディングを更新
                action.ChangeBinding(bindingIndex).WithPath(controlPath);
            }

#if UNITY_EDITOR
            // Editor上での永続化
            if (playerActionMap?.asset != null)
            {
                EditorUtility.SetDirty(playerActionMap.asset);
            }
#endif
        }

        /// <summary>
        /// コントロールの優先度を取得（数値が低いほど高優先度）
        /// </summary>
        private static int GetControlPriority(InputDevice device, string controlPath)
        {
            // ゲームパッドボタンが最優先
            if (device is UnityEngine.InputSystem.Gamepad)
            {
                if (controlPath.Contains("/button")) return 1;
                if (controlPath.Contains("/trigger")) return 2;
                if (controlPath.Contains("/stick")) return 100; // スティックは除外傾向
            }
            
            // キーボードは2番目の優先度
            if (device is UnityEngine.InputSystem.Keyboard)
            {
                return 10;
            }
            
            // その他のデバイス
            return 50;
        }

    }

}