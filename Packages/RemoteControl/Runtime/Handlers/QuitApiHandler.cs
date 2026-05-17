using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using UnityEngine;
using Lilium.RemoteControl.Server;

namespace Lilium.RemoteControl.RestApi
{
    /// <summary>
    /// アプリケーション終了用APIハンドラー
    /// POST /api/commands/quit
    /// </summary>
    public class QuitApiHandler : BaseRemoteControlApiHandler
    {
        /// <summary>
        /// 終了リクエスト時に発火するイベント
        /// Application.Quit() の前に後処理が必要な場合に使用
        /// </summary>
        public static event Action onQuitRequested;

        public QuitApiHandler(RemoteControlServerCore server) : base(server)
        {
        }

        public override void Cleanup()
        {
        }

        private static readonly RouteRule[] _kRoutes =
        {
            new RouteRule("/api/commands/quit", RouteMatch.Exact)
        };

        protected override IReadOnlyList<RouteRule> Routes => _kRoutes;

        protected override bool SupportsGet() => false;
        protected override bool SupportsPost() => true;

        protected override async Task HandlePostRequest(HttpListenerContext context)
        {
            Debug.Log($"[RemoteControl] Quit request received from {GetClientId(context.Request)}");
            Debug.Log($"[Debug][RemoteControl] Quit handler entered: mainThreadContext={(_mainThreadContext != null ? "ok" : "NULL")}");

            var response = new CommandResponse
            {
                success = true,
                message = "Application quit initiated.",
                timestamp = GetISOTimestamp()
            };

            await WriteJson(context, response);
            Debug.Log("[Debug][RemoteControl] Quit response written, scheduling Task.Run");

            // レスポンス送信後に終了処理を実行
            _ = Task.Run(async () =>
            {
                Debug.Log("[Debug][RemoteControl] Quit Task.Run started");

                // 少し待ってからQuitを実行（レスポンスが確実に送信されるように）
                await Task.Delay(100);
                Debug.Log("[Debug][RemoteControl] Quit Task.Delay finished, posting to main thread");

                await ExecuteOnMainThread(() =>
                {
                    Debug.Log("[Debug][RemoteControl] Quit main-thread callback entered");

                    // 終了前イベントを発火
                    onQuitRequested?.Invoke();
                    Debug.Log("[Debug][RemoteControl] onQuitRequested invoked");

                    // アプリケーション終了
                    Debug.Log("[Debug][RemoteControl] Calling Application.Quit()");
                    Application.Quit();

#if UNITY_EDITOR
                    // エディタの場合はPlayModeを停止
                    UnityEditor.EditorApplication.isPlaying = false;
#endif
                    Debug.Log("[Debug][RemoteControl] Application.Quit() returned (quit not yet final)");
                });

                Debug.Log("[Debug][RemoteControl] Quit ExecuteOnMainThread awaited");
            });
        }
    }
}
