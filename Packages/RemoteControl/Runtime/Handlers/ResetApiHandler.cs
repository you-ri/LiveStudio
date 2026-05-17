using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using UnityEngine;
using Lilium.RemoteControl;
using Lilium.RemoteControl.Server;
using Lilium.RemoteControl.RestApi;
using Newtonsoft.Json;

namespace Lilium.Virgo.RemoteControl
{
    public class ResetApiHandler : BaseRemoteControlApiHandler
    {
        public ResetApiHandler(RemoteControlServerCore server) : base(server)
        {
        }

        public override void Cleanup()
        {
        }

        private static readonly RouteRule[] _kRoutes =
        {
            new RouteRule("/api/commands/reset", RouteMatch.Exact)
        };

        protected override IReadOnlyList<RouteRule> Routes => _kRoutes;

        protected override bool SupportsGet() => false;
        protected override bool SupportsPost() => true;

        protected override async Task HandlePostRequest(HttpListenerContext context)
        {
            Debug.Log("[RemoteControl] Reset request received");

            var response = new CommandResponse
            {
                success = true,
                message = "Settings reset initiated. Application will restart.",
                timestamp = GetISOTimestamp()
            };

            await WriteJson(context, response);

            // レスポンス送信後にリセット処理を実行
            _ = Task.Run(async () =>
            {
                // 少し待ってからリセットを実行（レスポンスが確実に送信されるように）
                await Task.Delay(100);

                await ExecuteOnMainThread(() =>
                {
                    PlayerPrefs.DeleteAll();
                    RemoteControlService.ResetData();
                    Application.Quit();
                });
            });
        }
    }
}
