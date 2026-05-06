using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using Lilium.RemoteControl;
using Lilium.RemoteControl.Server;
using Lilium.RemoteControl.RestApi;
using Newtonsoft.Json;

namespace Lilium.LiveStudio
{
    [System.Serializable]
    public class ExpressionApiSettings
    {
        public string action; // "add_expression", "remove_expression", "bind_expression", "remove_binding", "set_weight"
        public string expressionName;
        public int bindingIndex;
        public float weight;
    }

    [System.Serializable]
    public class ExpressionApiInfo
    {
        public string name;
        public string displayName;
        public bool isPreset;
        public string[] bindings;
        public string[] bindingDisplayNames;
        public float weight;
    }

    [System.Serializable]
    public class ExpressionListResponse
    {
        public bool success;
        public ExpressionApiInfo[] expressions;
        public ExpressionApiInfo[] availableExpressions;
        public string timestamp;
    }

    [System.Serializable]
    public class ExpressionControlRequest
    {
        public string type;
        public ExpressionApiSettings data;
    }

    [System.Serializable]
    public class ExpressionControlResponse
    {
        public bool success;
        public string message;
        public string expressionName;
        public string action;
        public string newBinding;
        public string timestamp;
    }

    public class ExpressionsApiHandler : BaseRemoteControlApiHandler
    {
        private double _lastUpdateTime = 0f;
        private const float kUpdateInterval = 0.1f; // 100ms間隔で更新
        private int _lastSentExpressionCount = 0;
        
        public ExpressionsApiHandler(RemoteControlServerCore server) : base(server)
        {
        }

        public override void Cleanup()
        {
        }

        private InputAction FindInputAction(string actionName)
        {
            return InputActionService.FindInputAction(actionName);
        }
        
        // Update method for periodic expression weight broadcasting
        public void Update()
        {
            var time = TimeUtility.GetTime();
            // 定期的に表情ウェイト値を更新
            if (time - _lastUpdateTime >= kUpdateInterval)
            {
                _lastUpdateTime = time;
                _ = BroadcastExpressionWeightUpdate();
            }
        }

        public override bool CanHandle(HttpListenerRequest request)
        {
            return request.Url.AbsolutePath.Equals("/api/expressions", StringComparison.OrdinalIgnoreCase);
        }

        protected override bool SupportsGet() => true;
        protected override bool SupportsPost() => true;

        protected override async Task HandleGetRequest(HttpListenerContext context)
        {
            var response = await ExecuteOnMainThread(() => new ExpressionListResponse
            {
                success = true,
                expressions = GetCurrentExpressions(),
                availableExpressions = GetAvailableExpressions(),
                timestamp = GetISOTimestamp()
            });
            var json = JsonConvert.SerializeObject(response);
            var buffer = Encoding.UTF8.GetBytes(json);

            context.Response.StatusCode = 200;
            await WriteResponse(context.Response, json);
        }

        protected override async Task HandlePostRequest(HttpListenerContext context)
        {
            var body = await ReadRequestBody(context.Request);
                
            if (string.IsNullOrEmpty(body))
            {
                context.Response.StatusCode = 400;
                await WriteResponse(context.Response, "{\"error\":\"Empty request body\"}");
                return;
            }

            var request = JsonConvert.DeserializeObject<ExpressionControlRequest>(body);
            
            if (request?.data != null)
            {
                var response = await ExecuteOnMainThread(() => {
                    var result = ExecuteExpressionAction(request.data);
                    
                    // SSEで他のクライアントに通知
                    _ = BroadcastExpressionUpdate();
                    
                    return result;
                });
                var json = JsonConvert.SerializeObject(response);
                context.Response.StatusCode = 200;
                await WriteResponse(context.Response, json);
            }
            else
            {
                context.Response.StatusCode = 400;
                await WriteResponse(context.Response, "{\"error\":\"Invalid request format\"}");
            }
        }

        private ExpressionControlResponse ExecuteExpressionAction(ExpressionApiSettings settings)
        {
            if (!ExpressionService.IsAvailable())
            {
                return new ExpressionControlResponse
                {
                    success = false,
                    message = "ExpressionManager is not available",
                    action = settings.action,
                    expressionName = settings.expressionName,
                    timestamp = GetISOTimestamp()
                };
            }

            switch (settings.action?.ToLower())
            {
                case "add_expression":
                    return HandleAddExpression(settings);

                case "remove_expression":
                    return HandleRemoveExpression(settings);

                case "bind_expression":
                    return HandleBindExpression(settings);

                case "remove_binding":
                    return HandleRemoveBinding(settings);

                case "set_weight":
                    return HandleSetWeight(settings);

                default:
                    return new ExpressionControlResponse
                    {
                        success = false,
                        message = $"Unknown action: {settings.action}",
                        action = settings.action,
                        expressionName = settings.expressionName,
                        timestamp = GetISOTimestamp()
                    };
            }
        }

        private ExpressionControlResponse HandleAddExpression(ExpressionApiSettings settings)
        {
            if (string.IsNullOrEmpty(settings.expressionName))
            {
                return new ExpressionControlResponse
                {
                    success = false,
                    message = "Expression name is required",
                    action = settings.action,
                    expressionName = settings.expressionName,
                    timestamp = GetISOTimestamp()
                };
            }

            bool success = ExpressionService.AddExpression(settings.expressionName);
            Debug.Log($"[LiveStudio] API: {(success ? "Added" : "Failed to add")} expression: {settings.expressionName}");

            return new ExpressionControlResponse
            {
                success = success,
                message = success ? "Expression added successfully" : "Failed to add expression",
                action = settings.action,
                expressionName = settings.expressionName,
                timestamp = GetISOTimestamp()
            };
        }

        private ExpressionControlResponse HandleRemoveExpression(ExpressionApiSettings settings)
        {
            if (string.IsNullOrEmpty(settings.expressionName))
            {
                return new ExpressionControlResponse
                {
                    success = false,
                    message = "Expression name is required",
                    action = settings.action,
                    expressionName = settings.expressionName,
                    timestamp = GetISOTimestamp()
                };
            }

            bool success = ExpressionService.RemoveExpression(settings.expressionName);
            Debug.Log($"[LiveStudio] API: {(success ? "Removed" : "Failed to remove")} expression: {settings.expressionName}");

            return new ExpressionControlResponse
            {
                success = success,
                message = success ? "Expression removed successfully" : "Failed to remove expression",
                action = settings.action,
                expressionName = settings.expressionName,
                timestamp = GetISOTimestamp()
            };
        }

        private ExpressionControlResponse HandleBindExpression(ExpressionApiSettings settings)
        {
            if (string.IsNullOrEmpty(settings.expressionName))
            {
                return new ExpressionControlResponse
                {
                    success = false,
                    message = "Expression name is required",
                    action = settings.action,
                    expressionName = settings.expressionName,
                    timestamp = GetISOTimestamp()
                };
            }

            Debug.Log($"[LiveStudio] API: Starting expression binding for '{settings.expressionName}' at binding index {settings.bindingIndex}");

            // 非同期でバインド処理を実行し、結果をSSEで送信
            _ = Task.Run(async () =>
            {
                await ExecuteOnMainThread(async () =>
                {
                    bool success = await ExpressionService.StartExpressionBindingAsync(settings.expressionName);

                    string newBinding = "";
                    if (success)
                    {
                        // バインディング成功時の新しいバインディングパスを取得
                        string actionName = "Expression." + settings.expressionName;
                        var action = FindInputAction(actionName);
                        if (action != null && settings.bindingIndex < action.bindings.Count)
                        {
                            newBinding = action.bindings[settings.bindingIndex].effectivePath;
                        }
                    }

                    // SSEでバインド結果を送信
                    _ = BroadcastExpressionBindingResult(settings.expressionName, settings.bindingIndex, success,
                        success ? "Expression binding completed successfully" : "Expression binding was cancelled or failed",
                        newBinding);

                    // 表情リストを更新
                    if (success)
                    {
                        _ = BroadcastExpressionUpdate();
                    }
                });
            });

            return new ExpressionControlResponse
            {
                success = true,
                message = "Expression binding started",
                action = settings.action,
                expressionName = settings.expressionName,
                timestamp = GetISOTimestamp()
            };
        }

        private ExpressionControlResponse HandleRemoveBinding(ExpressionApiSettings settings)
        {
            if (string.IsNullOrEmpty(settings.expressionName))
            {
                return new ExpressionControlResponse
                {
                    success = false,
                    message = "Expression name is required",
                    action = settings.action,
                    expressionName = settings.expressionName,
                    timestamp = GetISOTimestamp()
                };
            }

            string actionName = "Expression." + settings.expressionName;
            var action = FindInputAction(actionName);

            if (action == null)
            {
                return new ExpressionControlResponse
                {
                    success = false,
                    message = $"Expression '{settings.expressionName}' not found",
                    action = settings.action,
                    expressionName = settings.expressionName,
                    timestamp = GetISOTimestamp()
                };
            }

            if (settings.bindingIndex >= action.bindings.Count)
            {
                return new ExpressionControlResponse
                {
                    success = false,
                    message = $"Binding index {settings.bindingIndex} is out of range",
                    action = settings.action,
                    expressionName = settings.expressionName,
                    timestamp = GetISOTimestamp()
                };
            }

            // バインディングを削除（InputSystemのAPIを使用）
            var bindingIndex = settings.bindingIndex;
            if (bindingIndex < action.bindings.Count)
            {
                action.ChangeBinding(bindingIndex).Erase();
            }
            Debug.Log($"[LiveStudio] API: Removed expression binding: {settings.expressionName}[{settings.bindingIndex}]");

            return new ExpressionControlResponse
            {
                success = true,
                message = "Expression binding removed successfully",
                action = settings.action,
                expressionName = settings.expressionName,
                timestamp = GetISOTimestamp()
            };
        }

        private ExpressionControlResponse HandleSetWeight(ExpressionApiSettings settings)
        {
            if (string.IsNullOrEmpty(settings.expressionName))
            {
                return new ExpressionControlResponse
                {
                    success = false,
                    message = "Expression name is required",
                    action = settings.action,
                    expressionName = settings.expressionName,
                    timestamp = GetISOTimestamp()
                };
            }

            var facialKey = FacialKey.CreateCustom(settings.expressionName);
            ExpressionService.SetExpressionWeight(facialKey, settings.weight);
            Debug.Log($"[LiveStudio] API: Set expression '{settings.expressionName}' weight to: {settings.weight}");

            return new ExpressionControlResponse
            {
                success = true,
                message = $"Expression weight set to {settings.weight}",
                action = settings.action,
                expressionName = settings.expressionName,
                timestamp = GetISOTimestamp()
            };
        }

        private ExpressionApiInfo[] GetCurrentExpressions()
        {
            if (!ExpressionService.IsAvailable())
            {
                Debug.LogWarning("[LiveStudio] ExpressionManager is not available");
                return new ExpressionApiInfo[0];
            }

            var expressionInfoList = new List<ExpressionApiInfo>();
            var allExpressionNames = ExpressionService.GetAllExpressionNames();

            foreach (var expressionName in allExpressionNames)
            {
                string actionName = "Expression." + expressionName;
                var action = FindInputAction(actionName);

                if (action != null)
                {
                    // バインディング情報を取得
                    var bindingPaths = new string[action.bindings.Count];
                    var bindingDisplayNames = new string[action.bindings.Count];
                    for (int i = 0; i < action.bindings.Count; i++)
                    {
                        bindingPaths[i] = action.bindings[i].effectivePath;
                        bindingDisplayNames[i] = UnityEngine.InputSystem.InputControlPath.ToHumanReadableString(action.bindings[i].effectivePath, UnityEngine.InputSystem.InputControlPath.HumanReadableStringOptions.UseShortNames);
                    }

                    // FacialKeyを作成してプリセット判定
                    var facialKey = FacialKey.CreateCustom(expressionName);
                    bool isPreset = IsPresetExpression(expressionName);

                    // 現在のウェイト値を取得
                    float currentWeight = ExpressionService.GetExpressionWeight(facialKey);

                    var expressionInfo = new ExpressionApiInfo
                    {
                        name = expressionName,
                        displayName = expressionName,
                        isPreset = isPreset,
                        bindings = bindingPaths,
                        bindingDisplayNames = bindingDisplayNames,
                        weight = currentWeight
                    };

                    expressionInfoList.Add(expressionInfo);
                }
            }

            return expressionInfoList.ToArray();
        }

        private ExpressionApiInfo[] GetAvailableExpressions()
        {
            if (!ExpressionService.IsAvailable())
            {
                Debug.LogWarning("[LiveStudio] ExpressionManager is not available");
                return new ExpressionApiInfo[0];
            }

            var availableExpressions = ExpressionService.GetAvailableExpressions();
            
            var expressionInfoList = new List<ExpressionApiInfo>();

            foreach (var facialKey in availableExpressions)
            {
                var expressionInfo = new ExpressionApiInfo
                {
                    name = facialKey.name,
                    displayName = facialKey.name,
                    isPreset = facialKey.preset != ExpressionPreset.custom,
                    bindings = new string[0], // 利用可能リストにはバインディング情報は含まない
                    bindingDisplayNames = new string[0],
                    weight = 0f
                };

                expressionInfoList.Add(expressionInfo);
            }

            return expressionInfoList.ToArray();
        }

        private bool IsPresetExpression(string expressionName)
        {
            // プリセット表情名かどうかを判定
            return System.Enum.TryParse<ExpressionPreset>(expressionName.ToLower(), out ExpressionPreset preset)
                   && preset != ExpressionPreset.custom;
        }

        private async Task BroadcastExpressionUpdate()
        {
            var updateData = await ExecuteOnMainThread(() => new
            {
                type = "expression_update",
                expressions = GetCurrentExpressions(),
                availableExpressions = GetAvailableExpressions(),
                timestamp = GetISOTimestamp()
            });
            
            await _server?.BroadcastMessage(updateData, "expression_update");
        }

        private async Task BroadcastExpressionWeightUpdate()
        {
            if (!ExpressionService.IsAvailable()) return;

            var weightUpdates = await ExecuteOnMainThread(() =>
            {
                var allExpressionNames = ExpressionService.GetAllExpressionNames();
                var weights = new List<ExpressionWeightInfo>();

                foreach (var expressionName in allExpressionNames)
                {
                    var facialKey = FacialKey.CreateCustom(expressionName);
                    float currentWeight = ExpressionService.GetExpressionWeight(facialKey);
                    
                    // アクティブな表情（ウェイト値 > 0）のみ送信
                    if (currentWeight > 0.001f)
                    {
                        weights.Add(new ExpressionWeightInfo
                        {
                            name = expressionName,
                            weight = currentWeight
                        });
                    }
                }

                return new
                {
                    type = "expression_weight_update",
                    expressions = weights.ToArray(),
                    timestamp = GetISOTimestamp()
                };
            });

            // 全て0でも、前回は表情があった場合は1回だけ送信（状態変化を通知）
            if (weightUpdates.expressions.Length == 0 && _lastSentExpressionCount == 0)
            {
                return; // 連続で空の場合はスキップ
            }
            _lastSentExpressionCount = weightUpdates.expressions.Length;

            await _server?.BroadcastMessage(weightUpdates, "expression_weight_update");
        }

        private async Task BroadcastExpressionBindingResult(string expressionName, int bindingIndex, bool success, string message, string newBinding)
        {
            var bindingResult = await ExecuteOnMainThread(() => new
            {
                type = "expression_binding_result",
                expressionName = expressionName,
                bindingIndex = bindingIndex,
                success = success,
                message = message,
                newBinding = newBinding,
                timestamp = GetISOTimestamp()
            });

            await _server?.BroadcastMessage(bindingResult, "expression_binding_result");
            Debug.Log($"[LiveStudio] API: Sent expression binding result: {expressionName} - {(success ? "Success" : "Failed")}");
        }
    }
}