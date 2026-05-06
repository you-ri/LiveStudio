using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// AvatarInputの設定を管理する静的クラス
    /// </summary>
    public static class AvatarInputSettingsUtils
    {
        /// <summary>
        /// AvatarInputから設定データを作成
        /// </summary>
        internal static AvatarInputSettings CreateSettingsFromAvatarInput(AvatarInput inputProvider)
        {
            var settings = new AvatarInputSettings();
            settings.deviceName = inputProvider._deviceName ?? "";

            settings.bindings = new InputBindingSaveData[inputProvider.inputActionMap.actions.Count];

            if (inputProvider.inputActionMap != null)
            {
                int index = 0;
                foreach (var action in inputProvider.inputActionMap.actions)
                {
                    var bindingData = new InputBindingSaveData(action.name);
                    bindingData.actionType = action.type.ToString(); // ActionTypeを保存
                    
                    for (int i = 0; i < action.bindings.Count; i++)
                    {
                        var binding = action.bindings[i];
                        if (!string.IsNullOrEmpty(binding.effectivePath))
                        {
                            bindingData.bindingPaths.Add(binding.effectivePath);
                        }
                    }
                    
                    settings.bindings[index++] = bindingData;
                }
            }

            return settings;
        }

        /// <summary>
        /// 設定データをAvatarInputに適用
        /// </summary>
        /// <param name="inputProvider">適用先のAvatarInput</param>
        /// <param name="settings">設定データ</param>
        /// <summary>
        /// 設定データをAvatarInputに完全同期で適用。
        /// settingsにないアクションは削除し、settingsの内容とInputActionMapを完全に一致させる。
        /// </summary>
        public static void ApplySettingsToAvatarInput(AvatarInput inputProvider, AvatarInputSettings settings)
        {
            if (inputProvider == null || settings == null)
            {
                Debug.LogError("[LiveStudio] AvatarInput or settings is null");
                return;
            }

            try
            {
                // デバイス設定を復元
                if (!string.IsNullOrEmpty(settings.deviceName))
                {
                    inputProvider.PairDevice(settings.deviceName);
                }

                // バインディング設定を完全同期
                if (inputProvider.inputActionMap != null && settings.bindings != null)
                {
                    // 一時的にInputActionMapを無効化（アクション追加・変更のため）
                    bool wasEnabled = inputProvider.inputActionMap.enabled;
                    if (wasEnabled)
                    {
                        inputProvider.inputActionMap.Disable();
                    }

                    try
                    {
                        // settingsにないアクションを削除
                        var settingActionNames = new HashSet<string>();
                        foreach (var b in settings.bindings)
                            settingActionNames.Add(b.actionName);

                        var actionsToRemove = new List<InputAction>();
                        foreach (var action in inputProvider.inputActionMap.actions)
                        {
                            if (!settingActionNames.Contains(action.name))
                                actionsToRemove.Add(action);
                        }
                        foreach (var action in actionsToRemove)
                        {
                            action.RemoveAction();
                        }

                        // settingsのアクションを追加・更新
                        foreach (var bindingData in settings.bindings)
                        {
                            var action = inputProvider.inputActionMap.FindAction(bindingData.actionName);
                            if (action != null)
                            {
                                RestoreActionBindings(action, bindingData.bindingPaths);
                            }
                            else
                            {
                                // アクションが存在しない場合は作成してバインディングを設定
                                InputActionType actionType = InputActionType.Value;
                                if (!string.IsNullOrEmpty(bindingData.actionType))
                                {
                                    if (Enum.TryParse<InputActionType>(bindingData.actionType, out var parsedType))
                                    {
                                        actionType = parsedType;
                                    }
                                }

                                var createdAction = InputActionMapUtils.SafeCreateAction(inputProvider.inputActionMap, bindingData.actionName, null, actionType);
                                if (createdAction != null)
                                {
                                    RestoreActionBindings(createdAction, bindingData.bindingPaths);
                                }
                            }
                        }

                        InputActionMapUtils.RefreshAndMarkDirty(inputProvider.inputActionMap);
                    }
                    finally
                    {
                        if (wasEnabled)
                        {
                            inputProvider.inputActionMap.Enable();
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[LiveStudio] Failed to apply settings to AvatarInput: {e.Message}");
            }
        }

        /// <summary>
        /// アクションにバインディングパスを復元
        /// </summary>
        private static void RestoreActionBindings(InputAction action, List<string> bindingPaths)
        {
            if (action == null || bindingPaths == null || bindingPaths.Count == 0)
                return;

            // 既存のバインディングをクリア
            action.RemoveAllBindingOverrides();

            // バインディングパスを順番に適用
            for (int i = 0; i < bindingPaths.Count; i++)
            {
                string bindingPath = bindingPaths[i];
                if (string.IsNullOrEmpty(bindingPath))
                    continue;

                if (i < action.bindings.Count)
                {
                    // 既存のバインディングを更新
                    action.ChangeBinding(i).WithPath(bindingPath);
                }
                else
                {
                    // 新規バインディングを追加
                    action.AddBinding().WithPath(bindingPath);
                }
            }
        }
    }
}