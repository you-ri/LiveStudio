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

        // Routes supplied through the params constructor. Null when a handler
        // overrides Routes itself (e.g. LiveObjectHandler, whose route table
        // is a static shared with its endpoint dispatch).
        private readonly RouteRule[] _routesField;


        protected BaseRemoteControlApiHandler(RemoteControlServerCore server)
        {
            _server = server;
            this._context = server?.context;
            _mainThreadContext = SynchronizationContext.Current;
        }

        /// <summary>
        /// Convenience constructor: declare the handler's routes inline so trivial
        /// handlers no longer need a static route array plus a Routes override.
        /// </summary>
        protected BaseRemoteControlApiHandler(RemoteControlServerCore server, params RouteRule[] routes)
            : this(server)
        {
            _routesField = routes;
        }

        /// <summary>
        /// コンテキストからLiveObjectContainerを取得
        /// </summary>
        protected LiveObjectContainer GetObjectContainer()
        {
            return _context?.objectContainer;
        }

        /// <summary>
        /// LiveObjectHandle 解決用リゾルバを取得。
        /// ObjectContainer があればそれを、無ければ既定リゾルバを返す。
        /// </summary>
        protected ILiveObjectResolver GetResolver()
        {
            return GetObjectContainer() ?? (ILiveObjectResolver)DefaultLiveObjectResolver.Instance;
        }

        // Virtual no-op so handlers without teardown work don't need an empty override.
        public virtual void Cleanup() { }

        // ---- Declarative routing (opt-in; backward compatible) ----
        // 既存ハンドラは CanHandle を override しているため挙動は不変。
        // Routes を返すよう移行したハンドラは CanHandle を消してこの既定実装を使う。

        /// <summary>
        /// パスの照合方法。
        /// <para>
        /// <see cref="ExactOrQuery"/> は「完全一致、ただし直後に <c>?</c> が続いてもよい」。
        /// batch のサブリクエストはパスをクエリ込みの 1 本の文字列で運ぶ
        /// (<c>/live/events?since=5</c>) ため、単発 (HTTP) と同じ 1 つの宣言で両方から
        /// 引けるようにするために要る。HTTP 経路の <c>Url.AbsolutePath</c> にクエリは
        /// 含まれないので、そちらでは <see cref="Exact"/> と同じ挙動になる。
        /// </para>
        /// </summary>
        protected enum RouteMatch { Exact, Prefix, Wildcard, ExactOrQuery }

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
        protected virtual IReadOnlyList<RouteRule> Routes => _routesField;

        /// <summary>
        /// パスがパターンに一致するか判定する共通ロジック。
        /// CanHandle(外側ゲート)と DispatchEndpoints(内側ディスパッチ)で共用する。
        /// 一致判定はすべて大文字小文字非依存。
        /// </summary>
        protected static bool MatchPattern(string path, string pattern, RouteMatch match)
        {
            switch (match)
            {
                case RouteMatch.Exact:
                    return path.Equals(pattern, StringComparison.OrdinalIgnoreCase);
                case RouteMatch.Prefix:
                    return path.StartsWith(pattern, StringComparison.OrdinalIgnoreCase);
                case RouteMatch.Wildcard:
                    return PathParser.IsMatchIgnoreCase(path, pattern);
                case RouteMatch.ExactOrQuery:
                    // 部分文字列を作らずに判定する: このルートはポーリングのたびに引かれる。
                    return path.Length >= pattern.Length
                        && (path.Length == pattern.Length || path[pattern.Length] == '?')
                        && string.Compare(path, 0, pattern, 0, pattern.Length,
                            StringComparison.OrdinalIgnoreCase) == 0;
                default:
                    return false;
            }
        }

        /// <summary>
        /// リクエスト行のパスを**エスケープされたまま**返す (クエリは落とす)。
        ///
        /// <c>Url.AbsolutePath</c> は .NET の Uri 正規化を通った後の値で、URL に載せた文字列と
        /// 一致しない: <c>%23</c> は <c>#</c> に戻された結果そこから後ろが fragment として切り捨てられ、
        /// 連続した <c>/</c> は 1 つに潰れる (実測)。パスセグメントが識別子そのものであるルート
        /// (アセットキー) では、この正規化が別のキーへの書き換えになる。
        ///
        /// 使うのはその必要があるハンドラだけでよい。id が GUID のルートは正規化の影響を受けない。
        /// </summary>
        protected static string GetRawPath(HttpListenerRequest request)
        {
            var raw = request.RawUrl;
            if (string.IsNullOrEmpty(raw)) return request.Url.AbsolutePath;
            int query = raw.IndexOf('?');
            return query >= 0 ? raw.Substring(0, query) : raw;
        }

        /// <summary>
        /// ルート一致に使うパス。既定は正規化済みの <c>Url.AbsolutePath</c>。
        /// パスセグメントが識別子そのものになるハンドラは <see cref="GetRawPath"/> を返すよう
        /// override する (理由は GetRawPath 参照)。
        /// </summary>
        protected virtual string GetMatchPath(HttpListenerRequest request) => request.Url.AbsolutePath;

        public virtual bool CanHandle(HttpListenerRequest request)
        {
            var routes = Routes;
            if (routes == null) return false;

            var path = GetMatchPath(request);
            for (int i = 0; i < routes.Count; i++)
            {
                var r = routes[i];
                if (MatchPattern(path, r.pattern, r.match)) return true;
            }
            return false;
        }

        /// <summary>
        /// 内側ディスパッチ用ルート: HTTP メソッド内でパスパターンと
        /// 個別ハンドラを対応付ける。<see cref="DispatchEndpoints"/> で先頭から
        /// 走査し最初に一致したハンドラを実行する(元の if/else 連鎖と同じ評価順)。
        /// </summary>
        protected readonly struct EndpointRoute
        {
            public readonly string pattern;
            public readonly RouteMatch match;
            public readonly Func<HttpListenerContext, Task> handler;
            public EndpointRoute(string pattern, RouteMatch match, Func<HttpListenerContext, Task> handler)
            {
                this.pattern = pattern;
                this.match = match;
                this.handler = handler;
            }
        }

        /// <summary>
        /// <paramref name="routes"/> を宣言順に走査し、最初に一致した
        /// ハンドラを実行する。無一致なら 400 + <paramref name="fallbackMessage"/>。
        /// 各 Handle{Method}Request 内の手動 if/else 連鎖を置き換える。
        /// </summary>
        protected async Task DispatchEndpoints(HttpListenerContext context,
            IReadOnlyList<EndpointRoute> routes, string fallbackMessage)
        {
            var path = context.Request.Url.AbsolutePath;
            for (int i = 0; i < routes.Count; i++)
            {
                var r = routes[i];
                if (MatchPattern(path, r.pattern, r.match))
                {
                    await r.handler(context);
                    return;
                }
            }

            await WriteError(context, 400, fallbackMessage);
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
        /// クライアントIDを取得。
        /// クライアントが名乗る X-Client-ID を優先する。受信箱 (<see cref="EventQueue"/>) の
        /// 宛先はポーリングの往復をまたいで同一である必要があり、TCP の送信元ポートは
        /// 接続が張り直されるたびに変わってしまうため、それだけでは identity にならない。
        /// ヘッダーが無いクライアント (curl 等) には従来どおり接続元エンドポイントを使う。
        /// </summary>
        protected string GetClientId(HttpListenerRequest request)
        {
            var declared = request.Headers["X-Client-ID"];
            if (!string.IsNullOrEmpty(declared)) return declared;

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
        /// ISO形式のタイムスタンプを取得
        /// </summary>
        protected string GetISOTimestamp()
        {
            return TimeUtility.GetISOTimestamp();
        }

        // サポートメソッド集合はハンドラ毎に不変なので初回のみ構築してキャッシュする
        // (OPTIONS/405 のたびに List + string.Join を確保しない)。
        private string _supportedMethodsCache;

        /// <summary>
        /// サポートするHTTPメソッドの文字列を取得
        /// </summary>
        private string GetSupportedMethods()
        {
            if (_supportedMethodsCache != null) return _supportedMethodsCache;

            var methods = new System.Collections.Generic.List<string>();
            if (SupportsGet()) methods.Add("GET");
            if (SupportsPost()) methods.Add("POST");
            if (SupportsPut()) methods.Add("PUT");
            if (SupportsDelete()) methods.Add("DELETE");
            if (SupportsPatch()) methods.Add("PATCH");
            methods.Add("OPTIONS");
            _supportedMethodsCache = string.Join(", ", methods);
            return _supportedMethodsCache;
        }

        /// <summary>
        /// Method Not Allowedレスポンスを送信
        /// </summary>
        protected Task SendMethodNotAllowed(HttpListenerContext context)
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

    }
}