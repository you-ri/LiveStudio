using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

using Lilium.RemoteControl.Server;
using Lilium.RemoteControl.RestApi;

using Newtonsoft.Json;
using Lilium.RemoteControl;
using Lilium.RemoteControl.Core;

namespace Lilium.LiveStudio
{
    [System.Serializable]
    public class InputActionsListResponse
    {
        public bool success;
        public InputActionInfo[] actions;
        public string timestamp;
    }

    [System.Serializable]
    public class InputActionBindRequest
    {
        public string actionName;
        public int bindingIndex;
    }

    [System.Serializable]
    public class InputActionBindResponse
    {
        public bool success;
        public string message;
        public string actionName;
        public int bindingIndex;
        public string newBinding;
        public string timestamp;
    }

    [System.Serializable]
    public class InputActionBindingUpdate
    {
        public string type;
        public string actionName;
        public int bindingIndex;
        public bool success;
        public string message;
        public string newBinding;
        public string timestamp;
    }

    public class InputActionsApiHandler : BaseRemoteControlApiHandler
    {
        private RuntimeKeyBindingData? _keyBindingData;
        private double _lastUpdateTime = 0f;
        private const float UPDATE_INTERVAL = 1.0f; // 1秒間隔で更新

        public InputActionsApiHandler(RemoteControlServerCore server) : base(server)
        {
        }

        public override void Cleanup()
        {
            _keyBindingData = null;
        }

        // Update method for periodic input actions state broadcasting
        public void Update()
        {
            var time = TimeUtility.GetTime();
            // 定期的にアクション状態を更新（バインディング中の場合など）
            if (time - _lastUpdateTime >= UPDATE_INTERVAL)
            {
                _lastUpdateTime = time;

                if (_keyBindingData.HasValue && _keyBindingData.Value.isWaitingForKey)
                {
                    // バインディング中の状態を通知
                    _ = BroadcastBindingStatus();
                }
            }
        }

        private static readonly RouteRule[] _kRoutes =
        {
            new RouteRule("/api/input-actions", RouteMatch.Exact),
            new RouteRule("/api/input-actions/bind", RouteMatch.Exact),
        };

        protected override IReadOnlyList<RouteRule> Routes => _kRoutes;

        protected override bool SupportsGet() => true;
        protected override bool SupportsPost() => true;

        protected override async Task HandleGetRequest(HttpListenerContext context)
        {
            if (context.Request.Url.AbsolutePath.Equals("/api/input-actions", StringComparison.OrdinalIgnoreCase))
            {
                await HandleGetActionsRequest(context);
                return;
            }

            await SendMethodNotAllowed(context);
        }

        protected override async Task HandlePostRequest(HttpListenerContext context)
        {
            if (context.Request.Url.AbsolutePath.Equals("/api/input-actions/bind", StringComparison.OrdinalIgnoreCase))
            {
                await HandleBindRequest(context);
                return;
            }

            await SendMethodNotAllowed(context);
        }

        private async Task HandleGetActionsRequest(HttpListenerContext context)
        {
            var response = await ExecuteOnMainThread(() => new InputActionsListResponse
            {
                success = true,
                actions = GetInputActionInfoList(),
                timestamp = GetISOTimestamp()
            });
            
            var json = JsonConvert.SerializeObject(response);
            context.Response.StatusCode = 200;
            await WriteResponse(context.Response, json);
        }

        private async Task HandleBindRequest(HttpListenerContext context)
        {
            var body = await ReadRequestBody(context.Request);
            
            if (string.IsNullOrEmpty(body))
            {
                await WriteError(context, 400, "Empty request body");
                return;
            }

            var request = JsonConvert.DeserializeObject<InputActionBindRequest>(body);
            
            if (request == null || string.IsNullOrEmpty(request.actionName))
            {
                await WriteError(context, 400, "Invalid request format");
                return;
            }

            var response = await StartBindAction(request.actionName, request.bindingIndex);
            var json = JsonConvert.SerializeObject(response);
            context.Response.StatusCode = 200;
            await WriteResponse(context.Response, json);
        }

        private InputActionInfo[] GetInputActionInfoList()
        {
            var actionInfoList = new List<InputActionInfo>();
            
            foreach (var inputActionMap in InputActionService.inputActionMaps)
            {
                foreach (var action in inputActionMap.actions)
                {
                    var bindingPaths = new string[action.bindings.Count];
                    var bindingDisplayNames = new string[action.bindings.Count];
                    
                    for (int i = 0; i < action.bindings.Count; i++)
                    {
                        bindingPaths[i] = action.bindings[i].effectivePath;
                        bindingDisplayNames[i] = InputControlPath.ToHumanReadableString(
                            action.bindings[i].effectivePath, 
                            InputControlPath.HumanReadableStringOptions.UseShortNames);
                    }

                    var actionInfo = new InputActionInfo
                    {
                        name = action.name,
                        bindings = bindingPaths,
                        bindingDisplayNames = bindingDisplayNames,
                        isEnabled = action.enabled,
                        actionType = action.type.ToString()
                    };

                    actionInfoList.Add(actionInfo);
                }
            }

            return actionInfoList.ToArray();
        }

        private async Task<InputActionBindResponse> StartBindAction(string actionName, int bindingIndex)
        {
            // Unity APIはメインスレッドで実行する必要があるため、Task.Runは使用しない
            // 直接メインスレッドで実行
            foreach (var inputActionMap in InputActionService.inputActionMaps)
            {
                var action = inputActionMap.FindAction(actionName);
                if (action == null)
                {
                    continue;
                }

                // 新規バインディングの場合（bindingIndex = 0, bindings.Count = 0）は許可
                // それ以外の不正なインデックスはエラー
                if (bindingIndex < 0 || (bindingIndex > 0 && bindingIndex >= action.bindings.Count))
                {
                    return new InputActionBindResponse
                    {
                        success = false,
                        message = $"Binding index {bindingIndex} is out of range",
                        actionName = actionName,
                        bindingIndex = bindingIndex,
                        timestamp = GetISOTimestamp()
                    };
                }

                Debug.Log($"[Fusion] API: Starting binding for action '{actionName}' at binding index {bindingIndex}");

                // nullableの場合は新しいインスタンスを作成
                var bindingData = _keyBindingData ?? new RuntimeKeyBindingData();
                
                var result = await RuntimeKeyBindingSystem.StartBindingAsync(
                    bindingData,
                    inputActionMap,
                    actionName,
                    bindingIndex);
                
                _keyBindingData = result.data;

                if (result.success)
                {
                    var updatedAction = inputActionMap.FindAction(actionName);
                    string newBinding = "";
                    if (updatedAction != null && bindingIndex < updatedAction.bindings.Count)
                    {
                        newBinding = updatedAction.bindings[bindingIndex].effectivePath;
                    }

                    // SSEで他のクライアントに通知
                    await BroadcastBindingResult(actionName, bindingIndex, true, "Binding completed successfully", newBinding);
                    
                    // アクションリストの更新も通知
                    await BroadcastInputActionsUpdate();

                    return new InputActionBindResponse
                    {
                        success = true,
                        message = "Binding completed successfully",
                        actionName = actionName,
                        bindingIndex = bindingIndex,
                        newBinding = newBinding,
                        timestamp = GetISOTimestamp()
                    };
                }
                else
                {
                    await BroadcastBindingResult(actionName, bindingIndex, false, "Binding was cancelled or failed");
                    
                    return new InputActionBindResponse
                    {
                        success = false,
                        message = "Binding was cancelled or failed",
                        actionName = actionName,
                        bindingIndex = bindingIndex,
                        timestamp = GetISOTimestamp()
                    };
                }
            }

            return new InputActionBindResponse
            {
                success = false,
                message = $"Action '{actionName}' not found",
                actionName = actionName,
                bindingIndex = bindingIndex,
                timestamp = GetISOTimestamp()
            };
        }

        private async Task BroadcastBindingResult(string actionName, int bindingIndex, bool success, string message, string newBinding = "")
        {
            var bindingUpdate = new InputActionBindingUpdate
            {
                type = "input_action_binding_result",
                actionName = actionName,
                bindingIndex = bindingIndex,
                success = success,
                message = message,
                newBinding = newBinding,
                timestamp = GetISOTimestamp()
            };

            await _server?.BroadcastMessage(bindingUpdate, "input_action_binding_result");
            Debug.Log($"[Fusion] API: Sent binding result: {actionName} - {(success ? "Success" : "Failed")}");
        }

        private async Task BroadcastInputActionsUpdate()
        {
            var updateData = await ExecuteOnMainThread(() => new
            {
                type = "input_actions_update",
                actions = GetInputActionInfoList(),
                timestamp = GetISOTimestamp()
            });
            
            await _server?.BroadcastMessage(updateData, "input_actions_update");
        }

        private async Task BroadcastBindingStatus()
        {
            if (_keyBindingData.HasValue && _keyBindingData.Value.isWaitingForKey)
            {
                var statusData = new
                {
                    type = "input_action_binding_status",
                    isBinding = true,
                    actionName = _keyBindingData.Value.GetActionName(),
                    bindingIndex = _keyBindingData.Value.bindingIndexToRebind,
                    timestamp = GetISOTimestamp()
                };
                
                await _server?.BroadcastMessage(statusData, "input_action_binding_status");
            }
        }
    }
}