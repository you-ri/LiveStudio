using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Lilium.RemoteControl.Core;
using Lilium.RemoteControl.Server;
using Newtonsoft.Json;

namespace Lilium.RemoteControl.RestApi.Controllers
{
    /// <summary>
    /// ハートビート・接続確認エンドポイント
    /// </summary>
    public class HeartbeatHandler : BaseRemoteControlApiHandler
    {
        private readonly string _path;
        private readonly RouteRule[] _routes;

        public HeartbeatHandler(RemoteControlServerCore server, string path = "/api/heartbeat")
            : base(server)
        {
            _path = path;
            _routes = new[] { new RouteRule(_path, RouteMatch.Exact) };
        }

        public override void Cleanup()
        {
        }

        protected override IReadOnlyList<RouteRule> Routes => _routes;

        protected override bool SupportsGet() => true;
        protected override bool SupportsPost() => true;

        protected override Task HandleGetRequest(HttpListenerContext context)
        {
            return HandleHeartbeat(context);
        }

        protected override Task HandlePostRequest(HttpListenerContext context)
        {
            return HandleHeartbeat(context);
        }
        
        private async Task HandleHeartbeat(HttpListenerContext httpContext)
        {
            var request = httpContext.Request;
            var response = httpContext.Response;

            // クライアントIDを取得
            var clientId = GetClientId(request);

            // クライアントの最終アクティビティ時間を更新
            this._context.eventQueue.UpdateClientActivity(clientId);

            Debug.Log($"[RemoteControl] HeartbeatHandler: Heartbeat from client {clientId}");

            // レスポンス作成
            var responseData = new
            {
                success = true,
                clientId = clientId,
                serverTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                message = "Heartbeat acknowledged",
                serverStatus = new
                {
                    isRunning = true,
                    connectedClients = this._context.eventQueue.GetConnectedClientCount(),
                    uptime = GetServerUptime()
                }
            };

            await WriteJson(httpContext, responseData, 200, Formatting.Indented);

            // ハートビートイベントを他のクライアントに通知（オプション）
            var heartbeatEvent = new
            {
                type = "client_heartbeat",
                clientId = clientId,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            this._context.eventQueue.AddEvent(heartbeatEvent, excludeClient: clientId);
        }
        
        private double GetServerUptime()
        {
            try
            {
                return (DateTimeOffset.UtcNow - System.Diagnostics.Process.GetCurrentProcess().StartTime).TotalSeconds;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RemoteControl] Failed to get server uptime: {ex.Message}");
                return -1;
            }
        }
    }
}