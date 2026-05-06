using System;
using System.Net;
using System.Threading.Tasks;
using UnityEngine;

using Lilium.RemoteControl.Server;
using Lilium.RemoteControl.RestApi;
using Lilium.LiveStudio;
using Newtonsoft.Json;

namespace Lilium.LiveStudio
{

    [Obsolete("CommandsApiHandler is deprecated and will be removed in future versions. repleace with ExposedFunctions.")]
    public class CommandsApiHandler : BaseRemoteControlApiHandler
    {
        [System.Serializable]
        private class CommandRequest
        {
            public string type;
            public string actionName;
            public bool pressed;
            public float actorHeight;
        }

        [System.Serializable]
        private class CommandResponse
        {
            public bool success;
            public string message;
            public string timestamp;
        }

        public CommandsApiHandler(RemoteControlServerCore server) : base(server)
        {
        }



        public override void Cleanup()
        {
        }

        public override bool CanHandle(HttpListenerRequest request)
        {
            return request.Url.AbsolutePath.Equals("/api/commands", StringComparison.OrdinalIgnoreCase);
        }

        protected override bool SupportsGet() => false;
        protected override bool SupportsPost() => true;

        protected override async Task HandlePostRequest(HttpListenerContext context)
        {
            var body = await ReadRequestBody(context.Request);

            if (string.IsNullOrEmpty(body))
            {
                context.Response.StatusCode = 400;
                await WriteResponse(context.Response, "{\"error\":\"Empty request body\"}");
                return;
            }

            var request = JsonConvert.DeserializeObject<CommandRequest>(body);

            if (request?.type != null)
            {
                var response = await ExecuteOnMainThread(() =>
                {
                    return ExecuteCommand(request);
                });

                var json = JsonConvert.SerializeObject(response);
                context.Response.StatusCode = response.success ? 200 : 400;
                await WriteResponse(context.Response, json);
            }
            else
            {
                context.Response.StatusCode = 400;
                await WriteResponse(context.Response, "{\"error\":\"Invalid request format - missing type\"}");
            }
        }

        private CommandResponse ExecuteCommand(CommandRequest request)
        {
            try
            {
                Debug.Log($"[Fusion] API: Executing command: {request.type}");

                switch (request.type.ToLower())
                {
                    case "input_action":
                        return ExecuteInputAction(request.actionName, request.pressed);

                    default:
                        return new CommandResponse
                        {
                            success = false,
                            message = $"Unknown command type: {request.type}",
                            timestamp = GetISOTimestamp()
                        };
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Fusion] Command execution failed: {ex.Message}");
                return new CommandResponse
                {
                    success = false,
                    message = $"Command execution failed: {ex.Message}",
                    timestamp = GetISOTimestamp()
                };
            }
        }




        private CommandResponse ExecuteInputAction(string actionName, bool pressed)
        {
            if (string.IsNullOrEmpty(actionName))
            {
                return new CommandResponse
                {
                    success = false,
                    message = "Action name is required for input_action command",
                    timestamp = GetISOTimestamp()
                };
            }

            // InputActionServiceを使って入力アクションを実行
            try
            {
                var inputAction = InputActionService.FindInputAction(actionName);
                if (inputAction == null)
                {
                    return new CommandResponse
                    {
                        success = false,
                        message = $"Input action '{actionName}' not found",
                        timestamp = GetISOTimestamp()
                    };
                }

                // InputActionを実行
                if (pressed)
                {
                    // アクションを開始（ボタンを押す）
                    inputAction.Enable();
                    // TODO: Unity InputSystemでのButton Press simulation
                    // 現在は有効化のみ行う
                    Debug.Log($"[Fusion] API: Input action '{actionName}' enabled (pressed: {pressed})");
                }
                else
                {
                    // アクションを無効化（ボタンを離す）
                    inputAction.Disable();
                    Debug.Log($"[Fusion] API: Input action '{actionName}' disabled (pressed: {pressed})");
                }

                return new CommandResponse
                {
                    success = true,
                    message = $"Input action '{actionName}' executed successfully",
                    timestamp = GetISOTimestamp()
                };
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Fusion] Input action failed: {ex.Message}");
                return new CommandResponse
                {
                    success = false,
                    message = $"Input action failed: {ex.Message}",
                    timestamp = GetISOTimestamp()
                };
            }
        }


    }


}