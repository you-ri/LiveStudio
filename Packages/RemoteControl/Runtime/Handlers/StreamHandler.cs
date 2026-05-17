using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Profiling;
using Lilium.RemoteControl;
using Lilium.RemoteControl.Core;
using Lilium.RemoteControl.Server;
using Unity.Profiling;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Lilium.RemoteControl.RestApi.Controllers
{
    /// <summary>
    /// Server-Sent Events (SSE) ストリーミングハンドラー
    /// リアルタイム風の一方向通信を実現
    /// </summary>
    public class StreamHandler : BaseRemoteControlApiHandler
    {
        private readonly string _path;
        private CancellationTokenSource _shutdownCts = new CancellationTokenSource();
        private readonly List<Task> _activeConnections = new List<Task>();
        private readonly object _connectionLock = new object();

        // ProfilerMarkers for performance profiling
        private static readonly ProfilerMarker s_SendEventsMarker = new ProfilerMarker("StreamHandler.SendEvents");
        private static readonly ProfilerMarker s_KeepAliveMarker = new ProfilerMarker("StreamHandler.KeepAlive");
        private static readonly ProfilerMarker s_ConvertToUnifiedFormatMarker = new ProfilerMarker("StreamHandler.ConvertToUnifiedFormat");
        private static readonly ProfilerMarker s_JsonSerializeMarker = new ProfilerMarker("StreamHandler.JsonSerialize");
        private static readonly ProfilerMarker s_WriteDataMarker = new ProfilerMarker("StreamHandler.WriteData");

        private readonly RouteRule[] _routes;

        public StreamHandler(RemoteControlServerCore server, string path = "/api/stream")
            : base(server)
        {
            _path = path;
            _routes = new[] { new RouteRule(_path, RouteMatch.Exact) };
        }

        public override void Cleanup()
        {
            _shutdownCts?.Cancel();

            Task[] tasks;
            lock (_connectionLock)
            {
                tasks = _activeConnections.ToArray();
            }

            if (tasks.Length > 0)
            {
                try
                {
                    Task.WaitAll(tasks, TimeSpan.FromSeconds(3));
                }
                catch (AggregateException)
                {
                    // SSEタスクのキャンセル例外は無視
                }
            }

            _shutdownCts?.Dispose();
            _shutdownCts = null;
        }

        protected override IReadOnlyList<RouteRule> Routes => _routes;
        
        public override async Task HandleRequest(HttpListenerContext httpContext)
        {
            if (httpContext.Request.HttpMethod == "GET")
            {
                var task = HandleServerSentEvents(httpContext);
                lock (_connectionLock)
                {
                    _activeConnections.Add(task);
                }

                try
                {
                    await task;
                }
                finally
                {
                    lock (_connectionLock)
                    {
                        _activeConnections.Remove(task);
                    }
                }
            }
            else
            {
                httpContext.Response.StatusCode = 405;
                _ = WriteResponse(httpContext.Response, "{\"error\":\"Method not allowed\"}", "application/json");
            }
        }

        private async Task<bool> HandleServerSentEvents(HttpListenerContext httpContext)
        {
            var request = httpContext.Request;
            var response = httpContext.Response;
            var sseStarted = false;
            StreamWriter writer = null;
            CancellationTokenSource cancellationTokenSource = null;
            string clientId = null;

            try
            {
                // クライアントIDを取得
                clientId = GetClientId(request);

                // 最後のイベントIDを取得
                var lastEventIdStr = request.Headers["Last-Event-ID"] ?? request.QueryString["lastEventId"];
                var lastEventId = long.TryParse(lastEventIdStr, out var id) ? id : 0;

                // クライアントを登録
                var client = this._context.connectionManager.RegisterClient(clientId, request.UserAgent, request.RemoteEndPoint?.Address?.ToString());

                // SSE用のレスポンスヘッダーを設定
                response.StatusCode = 200;
                response.ContentType = "text/event-stream";
                response.Headers.Add("Cache-Control", "no-cache");
                response.Headers.Add("Connection", "keep-alive");

                // チャンク転送エンコーディングを有効化
                response.SendChunked = true;

                writer = new StreamWriter(response.OutputStream, Encoding.UTF8)
                {
                    AutoFlush = true
                };
                cancellationTokenSource = _shutdownCts != null
                    ? CancellationTokenSource.CreateLinkedTokenSource(_shutdownCts.Token)
                    : new CancellationTokenSource();

                // ログ出力でSSE接続開始を記録
                // 初期接続メッセージを送信
                SendSseEvent(writer, "connected", new
                {
                    clientId = clientId,
                    serverTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    message = "Connected to event stream"
                });

                // SSE開始をマーク
                sseStarted = true;

                // イベントストリーミングループ
                await StreamEvents(writer, clientId, lastEventId, cancellationTokenSource.Token);
            }
            catch (OperationCanceledException)
            {
                // シャットダウンによるキャンセルは正常終了
            }
            catch (Exception ex)
            {
                Debug.LogError($"[RemoteControl] SSE connection error for client {clientId}: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                // 確実にリソースをクリーンアップ
                cancellationTokenSource?.Cancel();
                cancellationTokenSource?.Dispose();

                writer?.Close();
                writer?.Dispose();

                response.OutputStream?.Close();

                // HTTP.sys カーネルリソースを確実に解放
                try { response.Close(); }
                catch (System.Exception) { /* listener停止後は例外の可能性あり */ }

                // SSE接続終了時にクライアントを確実に削除
                if (clientId != null)
                {
                    this._context.connectionManager.RemoveClient(clientId);
                }
            }

            return sseStarted;
        }
        
        private async Task StreamEvents(StreamWriter writer, string clientId, long lastEventId, CancellationToken cancellationToken)
        {
            var keepAliveInterval = TimeSpan.FromSeconds(30);
            var lastKeepAlive = DateTime.UtcNow;

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    // ストリームの状態を確認
                    if (!writer.BaseStream.CanWrite)
                    {
                        break;
                    }

                    // イベントを取得 (短いタイムアウトで、CancellationTokenを渡す)
                    List<EventItem> events = null;
                    try
                    {
                        events = await this._context.eventQueue.GetEventsAsync(clientId, lastEventId, TimeSpan.FromMilliseconds(500), cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        // キャンセル時は正常終了
                        break;
                    }

                    // イベントがある場合は送信 (送信前に再度ストリーム状態を確認)
                    if (events != null && events.Count > 0 && !cancellationToken.IsCancellationRequested)
                    {
                        if (!writer.BaseStream.CanWrite)
                            break;

                        using (s_SendEventsMarker.Auto())
                        {
                            foreach (var eventItem in events)
                            {
                                SendSseEvent(writer, eventItem.EventType, eventItem.Data, eventItem.Id);
                                lastEventId = eventItem.Id;
                                lastKeepAlive = DateTime.UtcNow;
                            }
                        }
                    }

                    // Keep-alive メッセージを送信 (送信前にストリーム状態を確認)
                    if (DateTime.UtcNow - lastKeepAlive > keepAliveInterval)
                    {
                        if (!writer.BaseStream.CanWrite)
                            break;

                        using (s_KeepAliveMarker.Auto())
                        {
                            SendSseComment(writer, $"keep-alive: {DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}");
                            lastKeepAlive = DateTime.UtcNow;
                        }
                    }

                    // クライアントアクティビティを更新
                    this._context.connectionManager.UpdateClientActivity(clientId);

                    // 短い待機 (約0.01秒)
                    await Task.Delay(10, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                // シャットダウンによるキャンセルは正常終了
            }
            catch (IOException ex)
            {
                // ネットワークエラー (クライアント切断を含む)
                Debug.Log($"[RemoteControl] Client {clientId} disconnected: {ex.Message}");
            }
            catch (ObjectDisposedException)
            {
                // ストリーム破棄済み (正常な切断処理)
                //Debug.Log($"[RemoteControl] Client {clientId} stream disposed");
            }
        }
        
        private void SendSseEvent(StreamWriter writer, string eventType, object data, long? eventId = null)
        {
            if (eventId.HasValue)
            {
                writer.WriteLine($"id: {eventId}");
            }

            // 統一形式への変換
            object unifiedData;
            string finalEventType;
            using (s_ConvertToUnifiedFormatMarker.Auto())
            {
                if (eventType == "data")
                {
                    // 既に統一形式の場合はそのまま送信
                    unifiedData = data;
                    finalEventType = "data";
                }
                else
                {
                    // 既存のイベントタイプを統一形式に変換
                    // dataオブジェクトにtypeとtimestampを追加
                    var dataObj = JObject.FromObject(data);

                    // typeとtimestampを追加（既存の値を上書きしないように）
                    if (dataObj["type"] == null)
                        dataObj["type"] = eventType;
                    if (dataObj["timestamp"] == null)
                        dataObj["timestamp"] = GetCurrentTimestamp();

                    unifiedData = new
                    {
                        type = "data",
                        data = dataObj
                    };
                    finalEventType = "data";
                }
            }

            writer.WriteLine($"event: {finalEventType}");

            string jsonData;
            using (s_JsonSerializeMarker.Auto())
            {
                jsonData = JsonConvert.SerializeObject(unifiedData);
            }
            //Debug.Log($"[RemoteControl] Send SSE {jsonData}");

            // データに改行が含まれる場合は複数行に分割
            using (s_WriteDataMarker.Auto())
            {
                var lines = jsonData.Split('\n');
                foreach (var line in lines)
                {
                    writer.WriteLine($"data: {line}");
                }

                // 空行でイベント終了を示す
                writer.WriteLine();
                writer.Flush();
            }
        }
        
        private string GetCurrentTimestamp()
        {
            return TimeUtility.GetISOTimestamp();
        }
        
        private void SendSseComment(StreamWriter writer, string comment)
        {
            writer.WriteLine($": {comment}");
            writer.WriteLine();
            writer.Flush();
        }
    }
}