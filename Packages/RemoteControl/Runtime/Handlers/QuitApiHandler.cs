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
    /// POST /live/commands/quit
    /// </summary>
    public class QuitApiHandler : BaseRemoteControlApiHandler
    {
        /// <summary>
        /// 終了リクエスト時に発火するイベント
        /// Application.Quit() の前に後処理が必要な場合に使用
        /// </summary>
        public static event Action onQuitRequested;

        /// <summary>
        /// 終了してよいかを問う。1 つでも false を返したら終了を中止する。
        ///
        /// Player なら <see cref="Application.wantsToQuit"/> が同じ役目を果たすが、Editor では
        /// 再生停止が強制で、そこから起きる ExitingPlayMode は中止できない。未保存確認のように
        /// 非同期の回答を待つ必要があるものは、ここで止めないと Editor だけ確認を出せないまま
        /// 終了してしまう。回答後は改めて終了を要求し直す前提。
        /// </summary>
        public static event Func<bool> onQuitRequesting;

        // Domain Reload 無効でも前セッションの購読が残らないように、実行開始時に初期化する。
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void _ResetStatics()
        {
            onQuitRequested = null;
            onQuitRequesting = null;
        }

        // 購読者のうち 1 つでも拒否したら終了しない。例外は「拒否」ではなく無視する
        // (ハンドラの不具合でアプリが終了できなくなる方が困る)。
        private static bool _IsQuitAllowed()
        {
            var gates = onQuitRequesting;
            if (gates == null) return true;

            foreach (Func<bool> gate in gates.GetInvocationList())
            {
                try
                {
                    if (!gate()) return false;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[RemoteControl] Quit gate threw, ignoring it: {ex}");
                }
            }
            return true;
        }

        public QuitApiHandler(RemoteControlServerCore server)
            : base(server, new RouteRule("/live/commands/quit", RouteMatch.Exact))
        {
        }

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

                    // 未保存確認などで保留された場合はここで打ち切る。確認に answer があれば
                    // 購読側が改めて終了を実行する。
                    if (!_IsQuitAllowed())
                    {
                        Debug.Log("[Debug][RemoteControl] Quit held by a quit gate");
                        return;
                    }

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
