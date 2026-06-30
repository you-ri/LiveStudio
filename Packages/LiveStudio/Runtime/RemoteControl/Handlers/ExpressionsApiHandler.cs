using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using UnityEngine;
using Lilium.RemoteControl;
using Lilium.RemoteControl.Core;
using Lilium.RemoteControl.Server;
using Lilium.RemoteControl.RestApi;

namespace Lilium.LiveStudio
{
    [System.Serializable]
    public class ExpressionApiSettings
    {
        public string action; // "set_weight"
        public string expressionName;
        public float weight;
    }

    [System.Serializable]
    public class ExpressionApiInfo
    {
        public string name;
        public string displayName;
        public bool isPreset;
        public float weight;
    }

    [System.Serializable]
    public class ExpressionListResponse
    {
        public bool success;
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
        public string timestamp;
    }

    /// <summary>
    /// Broadcasts active expression weights over SSE (for the remote app's expression cards) and exposes a
    /// direct weight setter. Key bindings are no longer handled here — they live on the generic OperationManager
    /// as ordinary SetPropertyOperation sets driving expressions[name].weight (the remote app's "bind to key").
    /// </summary>
    public class ExpressionsApiHandler : BaseRemoteControlApiHandler
    {
        private double _lastUpdateTime = 0f;
        private const float kUpdateInterval = 0.1f; // 100ms間隔で更新
        private int _lastSentExpressionCount = 0;

        public ExpressionsApiHandler(RemoteControlServerCore server)
            : base(server, new RouteRule("/api/expressions", RouteMatch.Exact))
        {
        }

        // Update method for periodic expression weight broadcasting
        public void Update()
        {
            var time = TimeUtility.GetTime();
            if (time - _lastUpdateTime >= kUpdateInterval)
            {
                _lastUpdateTime = time;
                _ = BroadcastExpressionWeightUpdate();
            }
        }

        protected override bool SupportsGet() => true;
        protected override bool SupportsPost() => true;

        protected override async Task HandleGetRequest(HttpListenerContext context)
        {
            var response = await ExecuteOnMainThread(() => new ExpressionListResponse
            {
                success = true,
                availableExpressions = GetAvailableExpressions(),
                timestamp = GetISOTimestamp()
            });
            await WriteJson(context, response);
        }

        protected override async Task HandlePostRequest(HttpListenerContext context)
        {
            var (ok, request, error) = await TryReadRequest<ExpressionControlRequest>(context.Request);
            if (!ok)
            {
                await WriteError(context, 400, error);
                return;
            }

            if (request.data != null)
            {
                var response = await ExecuteOnMainThread(() => ExecuteExpressionAction(request.data));
                await WriteJson(context, response);
            }
            else
            {
                await WriteError(context, 400, "Invalid request format");
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

            return new ExpressionControlResponse
            {
                success = true,
                message = $"Expression weight set to {settings.weight}",
                action = settings.action,
                expressionName = settings.expressionName,
                timestamp = GetISOTimestamp()
            };
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
                expressionInfoList.Add(new ExpressionApiInfo
                {
                    name = facialKey.name,
                    displayName = facialKey.name,
                    isPreset = facialKey.preset != ExpressionPreset.custom,
                    weight = 0f
                });
            }

            return expressionInfoList.ToArray();
        }

        private async Task BroadcastExpressionWeightUpdate()
        {
            if (!ExpressionService.IsAvailable()) return;

            var weightUpdates = await ExecuteOnMainThread(() =>
            {
                var available = ExpressionService.GetAvailableExpressions();
                var weights = new List<ExpressionWeightInfo>();

                foreach (var facialKey in available)
                {
                    float currentWeight = ExpressionService.GetExpressionWeight(facialKey);

                    // アクティブな表情（ウェイト値 > 0）のみ送信
                    if (currentWeight > 0.001f)
                    {
                        weights.Add(new ExpressionWeightInfo
                        {
                            name = facialKey.name,
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
    }
}
