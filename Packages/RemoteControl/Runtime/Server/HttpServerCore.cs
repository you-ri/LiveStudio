using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Profiling;
using Lilium.RemoteControl.Core;

namespace Lilium.RemoteControl.Server
{
    public class HttpServerCore : System.IDisposable
    {
        private HttpListener _listener;
        private CancellationTokenSource _cancellationTokenSource;
        private Dictionary<string, IRequestHandler> _routes = new Dictionary<string, IRequestHandler>();
        private bool _isRunning = false;
        private bool _isManualStop = false;
        private bool _isDisposed = false;
        private readonly int _port;
        private readonly bool _enableCors;

        // CORS header constants for optimization
        private const string kCorsOrigin = "*";
        private const string kCorsMethods = "GET, POST, PUT, DELETE, PATCH, OPTIONS";
        private const string kCorsHeaders = "Content-Type, Accept, X-Requested-With, x-client-id";
        private const string kCorsMaxAge = "86400";

        public int Port => _port;
        public bool IsRunning => _isRunning && !_isDisposed;

        public event System.Action OnServerStarted;
        public event System.Action<string> OnServerStopped;
        public event System.Action<System.Exception> OnServerError;

        private static readonly ProfilerMarker _processRequestMarker = new ProfilerMarker("HttpServerCore.ProcessRequest");

        public HttpServerCore(int port = 3002, bool enableCors = true)
        {
            _port = port;
            _enableCors = enableCors;
        }

        /// <summary>
        /// ポートが使用可能かどうかをチェックします（TCPレベル、診断用）
        /// </summary>
        public static bool IsPortAvailable(int port)
        {
            try
            {
                using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
                {
                    socket.Bind(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, port));
                    return true;
                }
            }
            catch (SocketException)
            {
                return false;
            }
        }

        public virtual void StartServer()
        {
            if (_isDisposed)
            {
                OnServerError?.Invoke(new System.ObjectDisposedException(nameof(HttpServerCore)));
                return;
            }

            if (_isRunning)
            {
                Debug.LogWarning("[RemoteControl] Server is already running");
                return;
            }

            _isManualStop = false;

            try
            {
                _listener = new HttpListener();

                // localhostがIPv6(::1)に解決される環境でも
                // 127.0.0.1(IPv4)でアクセスできるよう両方登録する
                _listener.Prefixes.Add($"http://localhost:{_port}/");
                _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");

                _cancellationTokenSource = new CancellationTokenSource();

                _listener.Start();
                _isRunning = true;

                Task.Run(() => ListenForRequests(_cancellationTokenSource.Token));

                OnServerStarted?.Invoke();
            }
            catch (HttpListenerException ex)
            {
                _isRunning = false;
                Debug.LogError($"[RemoteControl] Port {_port} is already in use.");
                OnServerError?.Invoke(ex);
                CloseServer();
            }
            catch (System.Exception ex)
            {
                _isRunning = false;
                Debug.LogError($"[RemoteControl] Server failed to start on port {_port}.");
                OnServerError?.Invoke(ex);
                CloseServer();
            }
        }

        public virtual void StopServer()
        {
            if (_isDisposed || !_isRunning)
            {
                return;
            }

            _isManualStop = true;
            _isRunning = false;

            // まずリスナーを停止（GetContextAsyncを例外終了させる）
            if (_listener != null)
            {
                try
                {
                    if (_listener.IsListening)
                    {
                        _listener.Stop();
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[RemoteControl] Error stopping listener: {ex.Message}");
                }
            }

            // その後キャンセルトークンを発行
            try
            {
                _cancellationTokenSource?.Cancel();
            }
            catch (System.ObjectDisposedException)
            {
                // Already disposed, ignore
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[RemoteControl] Error canceling token: {ex.Message}");
            }

            CloseServer();

            OnServerStopped?.Invoke("Manual stop requested");
        }

        protected virtual async Task ListenForRequests(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && _listener != null && _listener.IsListening)
            {
                try
                {
                    var context = await _listener.GetContextAsync().ConfigureAwait(false);

                    using (_processRequestMarker.Auto())
                    {
                        _ = _SafeProcessRequest(context);
                    }
                }
                catch (HttpListenerException)
                {
                    // Listener stopped - normal shutdown
                    break;
                }
                catch (System.ObjectDisposedException)
                {
                    // Listener disposed - normal shutdown
                    break;
                }
                catch (System.InvalidOperationException)
                {
                    // Listener not started or stopped - normal shutdown
                    break;
                }
            }

            _isRunning = false;

            if (!_isManualStop)
            {
                OnServerStopped?.Invoke("Listener loop ended");
            }
        }

        private async Task _SafeProcessRequest(HttpListenerContext context)
        {
            try
            {
                await ProcessRequest(context).ConfigureAwait(false);
            }
            catch (System.ObjectDisposedException)
            {
                // The listener / response was torn down (e.g. scene switch or server shutdown)
                // while this request was still in flight. Nothing to write back to.
                var url = context?.Request?.Url?.ToString() ?? "(null)";
                Debug.LogWarning($"[RemoteControl] Request {url} aborted: response was disposed (likely shutdown / scene switch).");
            }
            catch (System.Exception ex)
            {
                var url = context?.Request?.Url?.ToString() ?? "(null)";
                Debug.LogError($"[RemoteControl] Unhandled exception while processing request {url}: {ex}");
                try
                {
                    if (context?.Response != null)
                    {
                        context.Response.StatusCode = 500;
                        context.Response.Close();
                    }
                }
                catch (System.ObjectDisposedException)
                {
                    // Response already disposed; nothing to clean up.
                }
                catch (System.Exception closeEx)
                {
                    Debug.LogError($"[RemoteControl] Failed to close response after unhandled exception: {closeEx.Message}");
                }
            }
        }

        protected virtual Task ProcessRequest(HttpListenerContext context)
        {
            // [Debug] quit リクエスト受信の可視化
            if (context.Request.Url != null &&
                context.Request.Url.AbsolutePath.IndexOf("/api/commands/quit", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Debug.Log($"[Debug][RemoteControl] quit request received at HttpServerCore: " +
                          $"method={context.Request.HttpMethod} from={context.Request.RemoteEndPoint}");
            }

            if (_enableCors)
            {
                AddCorsHeaders(context.Response);
            }

            if (context.Request.HttpMethod == "OPTIONS")
            {
                context.Response.StatusCode = 200;
                context.Response.Close();
                return Task.CompletedTask;
            }

            // Route search phase - find matching handler
            foreach (var route in _routes)
            {
                if (route.Value.CanHandle(context.Request))
                {
                    // [Debug] quit ルートに到達したか可視化
                    if (context.Request.Url != null &&
                        context.Request.Url.AbsolutePath.IndexOf("/api/commands/quit", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        Debug.Log($"[Debug][RemoteControl] quit route matched: handler={route.Value.GetType().Name}");
                    }
                    return route.Value.HandleRequest(context);
                }
            }

            // Handler not found
            Debug.LogWarning($"[RemoteControl] No handler found for: {context.Request.HttpMethod} {context.Request.Url}");
            context.Response.StatusCode = 404;
            context.Response.Close();
            return Task.CompletedTask;
        }

        protected virtual void AddCorsHeaders(HttpListenerResponse response)
        {
            response.Headers.Add("Access-Control-Allow-Origin", kCorsOrigin);
            response.Headers.Add("Access-Control-Allow-Methods", kCorsMethods);
            response.Headers.Add("Access-Control-Allow-Headers", kCorsHeaders);
            response.Headers.Add("Access-Control-Max-Age", kCorsMaxAge);
        }

        public virtual void RegisterRoute(string pattern, IRequestHandler handler)
        {
            if (_routes.ContainsKey(pattern))
            {
                Debug.LogError($"[RemoteControl] Route already registered: {pattern}");
                return;
            }
            if (handler == null)
            {
                Debug.LogError($"[RemoteControl] Handler cannot be null for route: {pattern}");
                return;
            }

            _routes[pattern] = handler;
        }

        public virtual void UnregisterRoute(string pattern)
        {
            if (_routes.TryGetValue(pattern, out var handler))
            {
                handler.Cleanup();
                _routes.Remove(pattern);
            }
        }

        private void DisposeCancellationToken()
        {
            if (_cancellationTokenSource == null) return;

            _cancellationTokenSource.Dispose();
            _cancellationTokenSource = null;
        }

        private void CloseServer()
        {
            // ルートのクリーンアップ
            foreach (var route in _routes.Values)
            {
                route?.Cleanup();
            }
            _routes.Clear();

            // キャンセレーショントークンの解放
            DisposeCancellationToken();

            if (_listener != null)
            {
                try
                {
                    if (_listener.IsListening)
                    {
                        _listener.Stop();
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[RemoteControl] Error stopping listener: {ex.Message}");
                }

                try
                {
                    _listener.Close();
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[RemoteControl] Error closing listener: {ex.Message}");
                }
                finally
                {
                    _listener = null;
                }
            }
        }

        public virtual void Dispose()
        {
            if (_isDisposed) return;

            _isDisposed = true;
            StopServer();
        }
    }
}