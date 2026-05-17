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
    public class VrmLoadApiHandler : BaseRemoteControlApiHandler, IVRMLoadObserver
    {
        private readonly string _path;

        // VRM読み込み状態追跡用
        private string _currentClientId;
        private string _currentFilePath;

        public VrmLoadApiHandler(RemoteControlServerCore server) : base(server)
        {
            _path = "/api/vrm/load";

            // IVRMLoadObserverとしてサービスに登録
            Service<IVRMLoadObserver>.Register(this);

            // SSEクライアント接続時にVRM読み込み中であれば開始イベントを送信
            _server.onClientConnected += OnClientConnected;
        }

        public override void Cleanup()
        {
            Service<IVRMLoadObserver>.Unregister(this);
            _server.onClientConnected -= OnClientConnected;
        }

        private void OnClientConnected(RestApiClient client)
        {
            // SSEクライアント接続時にVRM読み込み中であれば、そのクライアントに開始イベントを送信
            if (VRMLoader.IsLoading && !string.IsNullOrEmpty(VRMLoader.CurrentLoadingFilePath))
            {
                var startData = new
                {
                    type = "vrm_load_start",
                    progress = 0f,
                    isLoading = true,
                    error = (string)null,
                    timestamp = GetTimestamp(),
                    filename = Path.GetFileName(VRMLoader.CurrentLoadingFilePath),
                    applicationName = "VirgoMotionStudio"
                };

                _server?.SendEventToClient(client.ClientId, startData, "vrm_load_start");
            }
        }
        
        private static readonly RouteRule[] _kRoutes =
        {
            new RouteRule("/api/vrm/load", RouteMatch.Exact),
            new RouteRule("/api/vrm/reset", RouteMatch.Exact),
        };

        protected override IReadOnlyList<RouteRule> Routes => _kRoutes;
        
        public override async Task HandleRequest(HttpListenerContext context)
        {
            var endpoints = new EndpointConfig[]
            {
                new EndpointConfig
                {
                    Path = "/api/vrm/load",
                    SupportedMethods = new[] { "POST", "OPTIONS" },
                    PostHandler = HandleVrmLoadRequest
                },
                new EndpointConfig
                {
                    Path = "/api/vrm/reset",
                    SupportedMethods = new[] { "POST", "OPTIONS" },
                    PostHandler = HandleVrmResetRequest
                }
            };
            
            await HandleMultipleEndpoints(context, endpoints);
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
                    response.StatusCode = 400;
                    await WriteResponse(response, "{\"error\":\"Empty request body\"}");
                    return;
                }

                // JSONを解析
                var requestData = JObject.Parse(requestBody);
                var filePath = requestData["filePath"]?.ToString();

                if (string.IsNullOrEmpty(filePath))
                {
                    response.StatusCode = 400;
                    await WriteResponse(response, "{\"error\":\"Missing filePath parameter\"}");
                    return;
                }

                // ファイルパス検証
                if (!IsValidVrmFilePath(filePath))
                {
                    response.StatusCode = 400;
                    await WriteResponse(response, "{\"error\":\"Invalid VRM file path\"}");
                    return;
                }

                // VRM読み込み処理を非同期で開始（完了を待たない）
                // 結果はSSE経由で通知される
                _ = ProcessVrmLoadAsync(clientId, filePath);

                // 即座にレスポンスを返す
                var responseData = new
                {
                    success = true,
                    message = "VRM load started",
                    timestamp = GetTimestamp(),
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
                    timestamp = GetTimestamp()
                };

                var json = JsonConvert.SerializeObject(responseData, Formatting.Indented);

                response.StatusCode = 200;
                await WriteResponse(response, json);

                // リセット完了をSSE配信
                var resetData = new
                {
                    type = "vrm_reset_complete",
                    timestamp = GetTimestamp(),
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
        /// 結果はIVRMLoadObserverコールバック経由でSSE通知される
        /// </summary>
        private async Task ProcessVrmLoadAsync(string clientId, string filePath)
        {
            Debug.Log($"[Studio] Starting VRM load (async): {filePath} from client {clientId}");

            // 現在の読み込み状態を設定
            _currentClientId = clientId;
            _currentFilePath = filePath;

            // AvatarServiceを通じてVRM読み込みを実行
            // 開始通知はVRMLoader→OnVRMLoadStartedコールバック経由でSSE送信される
            // 結果はIVRMLoadObserverのコールバック経由で通知される
            AvatarService.LoadVRM("current", filePath);

            // 注意: この時点でメソッドは終了するが、読み込みは継続中
            // OnVRMLoaded/OnVRMLoadErrorコールバックで状態がクリアされる
            await Task.CompletedTask;
        }
        

        
        #region IVRMLoadObserver Implementation

        public void OnVRMLoadStarted(string filePath)
        {
            // Studio側から直接読み込まれた場合もファイルパスを保持
            _currentFilePath = filePath;

            // 開始をSSE配信
            var startData = new
            {
                type = "vrm_load_start",
                progress = 0f,
                isLoading = true,
                error = (string)null,
                timestamp = GetTimestamp(),
                filename = Path.GetFileName(filePath),
                applicationName = "VirgoMotionStudio"
            };

            _ = _server?.BroadcastMessage(startData, "vrm_load_start");
        }

        public void OnVRMLoaded(GameObject vrm)
        {
            Debug.Log($"[Studio] VRM loaded successfully: {vrm?.name}");

            // 完了をSSE配信
            var completeData = new
            {
                type = "vrm_load_complete",
                progress = 100f,
                isLoading = false,
                error = (string)null,
                timestamp = GetTimestamp(),
                filename = Path.GetFileName(_currentFilePath),
                avatarName = vrm?.name,
                applicationName = "VirgoMotionStudio"
            };

            _ = _server?.BroadcastMessage(completeData, "vrm_load_complete");

            // 状態をクリア
            _currentClientId = null;
            _currentFilePath = null;
        }

        public void OnVRMLoadError(string error)
        {
            Debug.LogError($"[Studio] VRM load failed: {error}");

            // エラーをSSE配信
            var errorData = new
            {
                type = "vrm_load_error",
                progress = 0f,
                isLoading = false,
                error = error,
                timestamp = GetTimestamp(),
                filename = Path.GetFileName(_currentFilePath),
                applicationName = "VirgoMotionStudio"
            };

            _ = _server?.BroadcastMessage(errorData, "vrm_load_error");

            // 状態をクリア
            _currentClientId = null;
            _currentFilePath = null;
        }

        public void OnVRMLoadProgress(float progress)
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
                    timestamp = GetTimestamp(),
                    filename = Path.GetFileName(_currentFilePath),
                    applicationName = "VirgoMotionStudio"
                };
                
                _ = _server?.BroadcastMessage(progressData, "vrm_load_progress");
            }
        }
        
        #endregion
    }
}