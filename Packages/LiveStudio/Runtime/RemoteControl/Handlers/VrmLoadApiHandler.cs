using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using UnityEngine;
using Lilium.RemoteControl;
using Lilium.RemoteControl.Core;
using Lilium.RemoteControl.RestApi;
using Lilium.RemoteControl.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// VRM読み込み専用REST APIハンドラー
    /// </summary>
    public class VrmLoadApiHandler : BaseRemoteControlApiHandler
    {
        // VRM読み込み状態追跡用
        private string _currentClientId;
        private string _currentFilePath;

        public VrmLoadApiHandler(RemoteControlServerCore server)
            : base(server,
                new RouteRule("/live/vrm/load", RouteMatch.Exact),
                new RouteRule("/live/vrm/reset", RouteMatch.Exact))
        {
            // VRMLoader のロードイベントを購読する
            VRMLoader.onLoadStarted += OnVRMLoadStarted;
            VRMLoader.onLoaded += OnVRMLoaded;
            VRMLoader.onLoadError += OnVRMLoadError;
            VRMLoader.onLoadProgress += OnVRMLoadProgress;

            // クライアントが初めて名乗ったときに VRM 読み込み中であれば開始イベントを積む
            _server.onClientConnected += OnClientConnected;
        }

        public override void Cleanup()
        {
            VRMLoader.onLoadStarted -= OnVRMLoadStarted;
            VRMLoader.onLoaded -= OnVRMLoaded;
            VRMLoader.onLoadError -= OnVRMLoadError;
            VRMLoader.onLoadProgress -= OnVRMLoadProgress;
            _server.onClientConnected -= OnClientConnected;
        }

        private void OnClientConnected(RestApiClient client)
        {
            // 途中から繋いだクライアントにも読み込み中であることを伝える (受信箱に積む)
            if (VRMLoader.IsLoading && !string.IsNullOrEmpty(VRMLoader.CurrentLoadingFilePath))
            {
                var startData = new
                {
                    type = "vrm_load_start",
                    progress = 0f,
                    isLoading = true,
                    error = (string)null,
                    timestamp = TimeUtility.GetUnixTimeMilliseconds(),
                    filename = Path.GetFileName(VRMLoader.CurrentLoadingFilePath),
                    applicationName = "VirgoMotionStudio"
                };

                _server?.SendEventToClient(client.ClientId, startData, "vrm_load_start");
            }
        }
        
        protected override bool SupportsPost() => true;

        protected override async Task HandlePostRequest(HttpListenerContext context)
        {
            var path = context.Request.Url.AbsolutePath;

            if (path.Equals("/live/vrm/load", StringComparison.OrdinalIgnoreCase))
            {
                await HandleVrmLoadRequest(context);
                return;
            }
            if (path.Equals("/live/vrm/reset", StringComparison.OrdinalIgnoreCase))
            {
                await HandleVrmResetRequest(context);
                return;
            }

            await SendNotFound(context);
        }
        
        private async Task HandleVrmLoadRequest(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

             await ExecuteOnMainThread(async () =>
            {
                // クライアントIDを取得
                var clientId = GetClientId(request);

                // リクエストボディを読み取り
                var requestBody = await ReadRequestBody(request);

                if (string.IsNullOrEmpty(requestBody))
                {
                    await WriteError(context, 400, "Empty request body");
                    return;
                }

                // JSONを解析
                var requestData = JObject.Parse(requestBody);
                var filePath = requestData["filePath"]?.ToString();

                if (string.IsNullOrEmpty(filePath))
                {
                    await WriteError(context, 400, "Missing filePath parameter");
                    return;
                }

                // ファイルパス検証
                if (!IsValidVrmFilePath(filePath))
                {
                    await WriteError(context, 400, "Invalid VRM file path");
                    return;
                }

                // VRM読み込み処理を非同期で開始（完了を待たない）
                // 結果は受信箱経由で通知される
                _ = ProcessVrmLoadAsync(clientId, filePath);

                // 即座にレスポンスを返す
                var responseData = new
                {
                    success = true,
                    message = "VRM load started",
                    timestamp = TimeUtility.GetUnixTimeMilliseconds(),
                    filePath = filePath
                };

                var json = JsonConvert.SerializeObject(responseData, Formatting.Indented);

                response.StatusCode = 200;
                await WriteResponse(response, json);

            }); // Ensure we're on main thread

        }
        
        private async Task HandleVrmResetRequest(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            await ExecuteOnMainThread(async () =>
            {
                // クライアントIDを取得
                var clientId = GetClientId(request);

                Debug.Log($"[Studio] VrmResetHandler: Received reset request from client {clientId}");

                // アバターをリセット
                AvatarService.ResetAvatar("current");

                // 成功レスポンス
                var responseData = new
                {
                    success = true,
                    message = "Avatar reset successfully",
                    timestamp = TimeUtility.GetUnixTimeMilliseconds()
                };

                var json = JsonConvert.SerializeObject(responseData, Formatting.Indented);

                response.StatusCode = 200;
                await WriteResponse(response, json);

                // リセット完了を受信箱へ
                var resetData = new
                {
                    type = "vrm_reset_complete",
                    timestamp = TimeUtility.GetUnixTimeMilliseconds(),
                    applicationName = "VirgoMotionStudio"
                };

                _ = _server?.BroadcastMessage(resetData, "vrm_reset_complete");

                Debug.Log($"[Studio] Avatar reset completed for client {clientId}");
            });
        }
        
        private bool IsValidVrmFilePath(string filePath)
        {
            // パスの基本検証
            if (string.IsNullOrWhiteSpace(filePath))
                return false;
                
            // ファイル拡張子チェック
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            if (extension != ".vrm")
                return false;
                
            // ファイル存在チェック
            return File.Exists(filePath);
        }
        
        /// <summary>
        /// VRM読み込みを非同期で開始（Fire-and-forget方式）
        /// 結果はVRMLoaderのロードイベント経由で受信箱に積まれる
        /// </summary>
        private async Task ProcessVrmLoadAsync(string clientId, string filePath)
        {
            Debug.Log($"[Studio] Starting VRM load (async): {filePath} from client {clientId}");

            // 現在の読み込み状態を設定
            _currentClientId = clientId;
            _currentFilePath = filePath;

            // AvatarServiceを通じてVRM読み込みを実行
            // 開始通知はVRMLoader.onLoadStartedイベント経由で受信箱に積まれる
            // 結果はVRMLoaderのロードイベント経由で通知される
            AvatarService.Load("current", filePath);

            // 注意: この時点でメソッドは終了するが、読み込みは継続中
            // OnVRMLoaded/OnVRMLoadErrorコールバックで状態がクリアされる
            await Task.CompletedTask;
        }
        

        
        #region VRMLoader event handlers

        private void OnVRMLoadStarted(string filePath)
        {
            // Studio側から直接読み込まれた場合もファイルパスを保持
            _currentFilePath = filePath;

            // 開始を受信箱へ
            var startData = new
            {
                type = "vrm_load_start",
                progress = 0f,
                isLoading = true,
                error = (string)null,
                timestamp = TimeUtility.GetUnixTimeMilliseconds(),
                filename = Path.GetFileName(filePath),
                applicationName = "VirgoMotionStudio"
            };

            _ = _server?.BroadcastMessage(startData, "vrm_load_start");
        }

        private void OnVRMLoaded(GameObject vrm)
        {
            Debug.Log($"[Studio] VRM loaded successfully: {vrm?.name}");

            // 完了を受信箱へ
            var completeData = new
            {
                type = "vrm_load_complete",
                progress = 100f,
                isLoading = false,
                error = (string)null,
                timestamp = TimeUtility.GetUnixTimeMilliseconds(),
                filename = Path.GetFileName(_currentFilePath),
                avatarName = vrm?.name,
                applicationName = "VirgoMotionStudio"
            };

            _ = _server?.BroadcastMessage(completeData, "vrm_load_complete");

            // 状態をクリア
            _currentClientId = null;
            _currentFilePath = null;
        }

        private void OnVRMLoadError(string error)
        {
            Debug.LogError($"[Studio] VRM load failed: {error}");

            // エラーを受信箱へ
            var errorData = new
            {
                type = "vrm_load_error",
                progress = 0f,
                isLoading = false,
                error = error,
                timestamp = TimeUtility.GetUnixTimeMilliseconds(),
                filename = Path.GetFileName(_currentFilePath),
                applicationName = "VirgoMotionStudio"
            };

            _ = _server?.BroadcastMessage(errorData, "vrm_load_error");

            // 状態をクリア
            _currentClientId = null;
            _currentFilePath = null;
        }

        private void OnVRMLoadProgress(float progress)
        {
            // 進捗情報を他のクライアントにブロードキャスト
            if (_currentClientId != null && _currentFilePath != null)
            {
                var progressData = new
                {
                    type = "vrm_load_progress",
                    progress = progress * 100f,
                    isLoading = true,
                    error = (string)null,
                    timestamp = TimeUtility.GetUnixTimeMilliseconds(),
                    filename = Path.GetFileName(_currentFilePath),
                    applicationName = "VirgoMotionStudio"
                };
                
                _ = _server?.BroadcastMessage(progressData, "vrm_load_progress");
            }
        }
        
        #endregion
    }
}