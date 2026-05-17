using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;
using Lilium.RemoteControl;
using Lilium.RemoteControl.Core;
using Lilium.RemoteControl.Server;
using Lilium.RemoteControl.Utility;

namespace Lilium.RemoteControl.RestApi
{
    /// <summary>
    /// REST APIハンドラーの基底クラス
    /// </summary>
    public abstract class BaseRemoteControlApiHandler : IRequestHandler
    {
        protected readonly RemoteControlServerCore _server;

        protected readonly RemoteControlContext _context;

        protected readonly SynchronizationContext _mainThreadContext;


        protected BaseRemoteControlApiHandler(RemoteControlServerCore server)
        {
            _server = server;
            this._context = server?.context;
            _mainThreadContext = SynchronizationContext.Current;
        }

        /// <summary>
        /// コンテキストからExposedObjectContainerを取得
        /// </summary>
        protected ExposedObjectContainer GetObjectContainer()
        {
            return _context?.objectContainer;
        }

        public abstract void Cleanup();

        // ---- Declarative routing (opt-in; backward compatible) ----
        // 既存ハンドラは CanHandle を override しているため挙動は不変。
        // Routes を返すよう移行したハンドラは CanHandle を消してこの既定実装を使う。

        protected enum RouteMatch { Exact, Prefix, Wildcard }

        protected readonly struct RouteRule
        {
            public readonly string pattern;
            public readonly RouteMatch match;
            public RouteRule(string pattern, RouteMatch match)
            {
                this.pattern = pattern;
                this.match = match;
            }
        }

        /// <summary>
        /// 宣言的ルート定義。null（既定）を返すハンドラは従来どおり CanHandle の
        /// override 実装が使われる。Routes を返すと共通の一致判定が使われる。
        /// </summary>
        protected virtual IReadOnlyList<RouteRule> Routes => null;

        public virtual bool CanHandle(HttpListenerRequest request)
        {
            var routes = Routes;
            if (routes == null) return false;

            var path = request.Url.AbsolutePath;
            for (int i = 0; i < routes.Count; i++)
            {
                var r = routes[i];
                bool m;
                switch (r.match)
                {
                    case RouteMatch.Exact:
                        m = path.Equals(r.pattern, StringComparison.OrdinalIgnoreCase);
                        break;
                    case RouteMatch.Prefix:
                        m = path.StartsWith(r.pattern, StringComparison.OrdinalIgnoreCase);
                        break;
                    case RouteMatch.Wildcard:
                        m = PathParser.IsMatchIgnoreCase(path, r.pattern);
                        break;
                    default:
                        m = false;
                        break;
                }
                if (m) return true;
            }
            return false;
        }

        /// <summary>
        /// HTTPリクエストを処理（共通実装）
        /// </summary>
        public virtual Task HandleRequest(HttpListenerContext context)
        {
            switch (context.Request.HttpMethod)
            {
                case "GET":
                    return SupportsGet() ? HandleGetRequest(context) : SendMethodNotAllowed(context);

                case "PUT":
                    return SupportsPut() ? HandlePutRequest(context) : SendMethodNotAllowed(context);

                case "POST":
                    return SupportsPost() ? HandlePostRequest(context) : SendMethodNotAllowed(context);

                case "DELETE":
                    return SupportsDelete() ? HandleDeleteRequest(context) : SendMethodNotAllowed(context);

                case "PATCH":
                    return SupportsPatch() ? HandlePatchRequest(context) : SendMethodNotAllowed(context);

                case "OPTIONS":
                    return HandleOptionsRequest(context, GetSupportedMethods(), "Content-Type");

                default:
                    return SendMethodNotAllowed(context);
            }
        }

        /// <summary>
        /// GETメソッドをサポートするかどうか
        /// </summary>
        protected virtual bool SupportsGet() => false;

        /// <summary>
        /// PUTメソッドをサポートするかどうか
        /// </summary>
        protected virtual bool SupportsPut() => false;

        /// <summary>
        /// POSTメソッドをサポートするかどうか
        /// </summary>
        protected virtual bool SupportsPost() => false;

        /// <summary>
        /// DELETEメソッドをサポートするかどうか
        /// </summary>
        protected virtual bool SupportsDelete() => false;

        /// <summary>
        /// PATCHメソッドをサポートするかどうか
        /// </summary>
        protected virtual bool SupportsPatch() => false;

        /// <summary>
        /// GETリクエストを処理
        /// </summary>
        protected virtual Task HandleGetRequest(HttpListenerContext context) => Task.CompletedTask;

        /// <summary>
        /// POSTリクエストを処理
        /// </summary>
        protected virtual Task HandlePostRequest(HttpListenerContext context) => Task.CompletedTask;


        /// <summary>
        /// PUTリクエストを処理
        /// </summary>
        protected virtual Task HandlePutRequest(HttpListenerContext context) => Task.CompletedTask;

        /// <summary>
        /// DELETEリクエストを処理
        /// </summary>
        protected virtual Task HandleDeleteRequest(HttpListenerContext context) => Task.CompletedTask;

        /// <summary>
        /// PATCHリクエストを処理
        /// </summary>
        protected virtual Task HandlePatchRequest(HttpListenerContext context) => Task.CompletedTask;


        /// <summary>
        /// CORSプリフライトリクエストを処理
        /// </summary>
        protected Task HandleOptionsRequest(HttpListenerContext context, string allowedMethods = "POST, OPTIONS", string allowedHeaders = "Content-Type")
        {
            var response = context.Response;
            response.StatusCode = 200;
            response.Headers.Add("Access-Control-Allow-Origin", "*");
            response.Headers.Add("Access-Control-Allow-Methods", allowedMethods);
            response.Headers.Add("Access-Control-Allow-Headers", allowedHeaders);
            return WriteResponse(response, "", "text/plain");
        }

        /// <summary>
        /// HTTPレスポンスを書き込み
        /// </summary>
        protected Task WriteResponse(int statusCode, HttpListenerResponse response, string content, string contentType = "application/json")
        {
            response.StatusCode = statusCode;
            return _WriteResponseBody(response, content, contentType);
        }

        // WriteResponse の共通本体。StatusCode は一切触らない
        // （statusCode 付きオーバーロードのみが事前に設定する。no-status 版は
        //  呼び出し側が事前に設定した StatusCode をそのまま使う既存挙動を保つ）。
        private Task _WriteResponseBody(HttpListenerResponse response, string content, string contentType)
        {
            response.ContentType = contentType;

            // CORSヘッダーが未設定の場合のみ追加
            if (string.IsNullOrEmpty(response.Headers["Access-Control-Allow-Origin"]))
            {
                response.Headers.Add("Access-Control-Allow-Origin", "*");
            }

            if (!string.IsNullOrEmpty(content))
            {
                byte[] buffer = Encoding.UTF8.GetBytes(content);
                response.ContentLength64 = buffer.Length;
                response.OutputStream.Write(buffer, 0, buffer.Length);
            }
            else
            {
                response.ContentLength64 = 0;
            }

            response.Close();
            return Task.CompletedTask;
        }

        /// <summary>
        /// HTTPレスポンスを書き込み
        /// </summary>
        protected Task WriteResponse(HttpListenerResponse response, string content, string contentType = "application/json")
        {
            return _WriteResponseBody(response, content, contentType);
        }

        /// <summary>
        /// クライアントIDを取得
        /// </summary>
        protected string GetClientId(HttpListenerRequest request)
        {
            var clientEndpoint = request.RemoteEndPoint;
            return clientEndpoint != null ? $"{clientEndpoint.Address}:{clientEndpoint.Port}" : "unknown";
        }



        /// <summary>
        /// メインスレッドで処理を実行
        /// </summary>
        protected async Task<T> ExecuteOnMainThread<T>(Func<T> action)
        {
            var taskCompletionSource = new TaskCompletionSource<T>();

            if (_mainThreadContext == null)
            {
                taskCompletionSource.SetException(new InvalidOperationException("[RemoteControl] MainThread SynchronizationContext is null"));
                return await taskCompletionSource.Task;
            }

            _mainThreadContext.Post(_ =>
            {
                try
                {
                    var result = action();
                    taskCompletionSource.SetResult(result);
                }
                catch (Exception ex)
                {
                    taskCompletionSource.SetException(ex);
                }
            }, null);

            return await taskCompletionSource.Task;
        }

        /// <summary>
        /// メインスレッドで非同期処理を実行
        /// </summary>
        protected async Task ExecuteOnMainThread(Action action)
        {
            var taskCompletionSource = new TaskCompletionSource<bool>();

            _mainThreadContext?.Post(_ =>
            {
                try
                {
                    action();
                    taskCompletionSource.SetResult(true);
                }
                catch (Exception ex)
                {
                    taskCompletionSource.SetException(ex);
                }
            }, null);

            await taskCompletionSource.Task;
        }

        /// <summary>
        /// リクエストボディを読み取り
        /// </summary>
        protected async Task<string> ReadRequestBody(HttpListenerRequest request)
        {
            using (var reader = new StreamReader(request.InputStream, Encoding.UTF8))
            {
                return await reader.ReadToEndAsync().ConfigureAwait(false);
            }
        }

        // ---- Consolidation helpers (additive; behavior-preserving) ----

        /// <summary>
        /// リクエストボディを読み取り <typeparamref name="T"/> にデシリアライズする共通ヘルパ。
        /// 空 body / null / JSON 例外を (ok=false, error) で返す。エラー文言は呼び出し元が
        /// 従来の文言と一致させられるよう差し替え可能。
        /// </summary>
        protected async Task<(bool ok, T data, string error)> TryReadRequest<T>(
            HttpListenerRequest request,
            string emptyMessage = "Empty request body",
            string invalidMessage = "Invalid request format") where T : class
        {
            var body = await ReadRequestBody(request).ConfigureAwait(false);
            if (string.IsNullOrEmpty(body))
                return (false, null, emptyMessage);
            try
            {
                var data = JsonConvert.DeserializeObject<T>(body);
                if (data == null)
                    return (false, null, invalidMessage);
                return (true, data, null);
            }
            catch (JsonException)
            {
                return (false, null, invalidMessage);
            }
        }

        /// <summary>
        /// {"error": "..."} 形式のエラーレスポンスを書き込む共通ヘルパ。
        /// 単純な文字列メッセージの場合、従来の手組み JSON とバイト一致する。
        /// </summary>
        protected Task WriteError(HttpListenerContext context, int statusCode, string message)
        {
            context.Response.StatusCode = statusCode;
            var json = JsonConvert.SerializeObject(new { error = message });
            return WriteResponse(context.Response, json, "application/json");
        }

        /// <summary>
        /// DTO を JSON 直列化してレスポンスを書き込む共通ヘルパ。
        /// 従来 Formatting.Indented を使っていたハンドラは formatting を明示して
        /// 出力バイトを維持する。
        /// </summary>
        protected Task WriteJson<T>(HttpListenerContext context, T data,
            int statusCode = 200, Formatting formatting = Formatting.None)
        {
            context.Response.StatusCode = statusCode;
            var json = JsonConvert.SerializeObject(data, formatting);
            return WriteResponse(context.Response, json, "application/json");
        }

        /// <summary>
        /// タイムスタンプを取得
        /// </summary>
        protected long GetTimestamp()
        {
            return TimeUtility.GetUnixTimeMilliseconds();
        }

        /// <summary>
        /// ISO形式のタイムスタンプを取得
        /// </summary>
        protected string GetISOTimestamp()
        {
            return TimeUtility.GetISOTimestamp();
        }

        /// <summary>
        /// サポートするHTTPメソッドの文字列を取得
        /// </summary>
        private string GetSupportedMethods()
        {
            var methods = new System.Collections.Generic.List<string>();
            if (SupportsGet()) methods.Add("GET");
            if (SupportsPost()) methods.Add("POST");
            if (SupportsPut()) methods.Add("PUT");
            if (SupportsDelete()) methods.Add("DELETE");
            if (SupportsPatch()) methods.Add("PATCH");
            methods.Add("OPTIONS");
            return string.Join(", ", methods);
        }

        /// <summary>
        /// Method Not Allowedレスポンスを送信
        /// </summary>
        private Task SendMethodNotAllowed(HttpListenerContext context)
        {
            context.Response.StatusCode = 405;
            return WriteResponse(context.Response, "{\"error\":\"Method not allowed\"}", "application/json");
        }

        /// <summary>
        /// Internal Server Errorレスポンスを送信
        /// </summary>
        private Task SendInternalServerError(HttpListenerContext context)
        {
            context.Response.StatusCode = 500;
            return WriteResponse(context.Response, "{\"error\":\"Internal server error\"}", "application/json");
        }

        /// <summary>
        /// Not Foundレスポンスを送信
        /// </summary>
        protected Task SendNotFound(HttpListenerContext context)
        {
            context.Response.StatusCode = 404;
            return WriteResponse(context.Response, "{\"error\":\"Not found\"}", "application/json");
        }

        /// <summary>
        /// コマンドレスポンスの共通クラス
        /// </summary>
        [System.Serializable]
        protected class CommandResponse
        {
            public bool success;
            public string message;
            public string timestamp;
        }

        /// <summary>
        /// エンドポイント設定
        /// </summary>
        protected class EndpointConfig
        {
            public string Path { get; set; }
            public string[] SupportedMethods { get; set; }
            public System.Func<HttpListenerContext, Task> GetHandler { get; set; }
            public System.Func<HttpListenerContext, Task> PostHandler { get; set; }
            public System.Func<HttpListenerContext, Task> PutHandler { get; set; }
            public System.Func<HttpListenerContext, Task> DeleteHandler { get; set; }
        }

        /// <summary>
        /// 複数エンドポイントを処理
        /// </summary>
        protected async Task HandleMultipleEndpoints(HttpListenerContext context, EndpointConfig[] endpoints)
        {
            var path = context.Request.Url.AbsolutePath;
            var method = context.Request.HttpMethod;

            var endpoint = System.Array.Find(endpoints, e =>
                path.Equals(e.Path, StringComparison.OrdinalIgnoreCase));

            if (endpoint == null)
            {
                await SendNotFound(context);
                return;
            }

            try
            {
                switch (method)
                {
                    case "GET" when System.Array.IndexOf(endpoint.SupportedMethods, "GET") >= 0:
                        if (endpoint.GetHandler != null)
                            await endpoint.GetHandler(context).ConfigureAwait(false);
                        else
                            await SendMethodNotAllowed(context);
                        break;

                    case "POST" when System.Array.IndexOf(endpoint.SupportedMethods, "POST") >= 0:
                        if (endpoint.PostHandler != null)
                            await endpoint.PostHandler(context).ConfigureAwait(false);
                        else
                            await SendMethodNotAllowed(context);
                        break;

                    case "PUT" when System.Array.IndexOf(endpoint.SupportedMethods, "PUT") >= 0:
                        if (endpoint.PutHandler != null)
                            await endpoint.PutHandler(context).ConfigureAwait(false);
                        else
                            await SendMethodNotAllowed(context);
                        break;

                    case "DELETE" when System.Array.IndexOf(endpoint.SupportedMethods, "DELETE") >= 0:
                        if (endpoint.DeleteHandler != null)
                            await endpoint.DeleteHandler(context).ConfigureAwait(false);
                        else
                            await SendMethodNotAllowed(context);
                        break;

                    case "OPTIONS":
                        await HandleOptionsRequest(context, string.Join(", ", endpoint.SupportedMethods), "Content-Type");
                        break;

                    default:
                        await SendMethodNotAllowed(context);
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[RemoteControl] Error handling endpoint request: {ex.Message}");
                await SendInternalServerError(context);
            }
        }

    }
}