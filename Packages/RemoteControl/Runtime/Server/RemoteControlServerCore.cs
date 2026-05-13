using System;
using System.Collections.Generic;

using System.Net;
using System.Threading.Tasks;
using Lilium.RemoteControl.Core;
using Lilium.RemoteControl.RestApi.Controllers;
using Lilium.RemoteControl;


namespace Lilium.RemoteControl.Server
{
    public  class RemoteControlServerCore : HttpServerCore
    {
        public RemoteControlContext context { get; private set; }
        private EventQueue _eventQueue;
        private RestApiConnectionManager _connectionManager;
        private ExposedObjectHandler _exposedObjectHandler;
        private StatusHandler _statusHandler;
        private LanguageHandler _languageHandler;

        public event Action<RestApiClient> onClientConnected;
        public event Action<RestApiClient> onClientDisconnected;



        public RemoteControlServerCore(int port, bool enableCors, RemoteControlContext context) : base(port, enableCors)
        {
            this.context = context ?? throw new ArgumentNullException(nameof(context));
            InitializeRestApi();
            RegisterDefaultRoutes();
        }

        private void InitializeRestApi()
        {
            _eventQueue = context.eventQueue;
            _connectionManager = context.connectionManager;
            
            _connectionManager.OnClientConnected += HandleClientConnected;
            _connectionManager.OnClientDisconnected += HandleClientDisconnected;
        }
        
        public void RegisterDefaultRoutes()
        {
            RegisterRoute("/api/stream", new StreamHandler(this, "/api/stream"));
            _exposedObjectHandler = new ExposedObjectHandler(this);
            RegisterRoute("/exposed", _exposedObjectHandler);
            _statusHandler = new StatusHandler(this);
            RegisterRoute("/api/status", _statusHandler);
            _languageHandler = new LanguageHandler(this);
            RegisterRoute("/api/language", _languageHandler);
        }
        
        public void UnregisterDefaultRoutes()
        {
            UnregisterRoute("/api/stream");
            UnregisterRoute("/exposed");
            _exposedObjectHandler?.Cleanup();
            _exposedObjectHandler = null;
            UnregisterRoute("/api/status");
            _statusHandler?.Cleanup();
            _statusHandler = null;
            UnregisterRoute("/api/language");
            _languageHandler?.Cleanup();
            _languageHandler = null;
        }
        
        public override void StartServer()
        {
            base.StartServer();

            if (IsRunning)
            {
                _connectionManager?.RemoveAllClients();
                _ = BroadcastSystemNotification("Remote Control Server started", "info");
            }
        }

        public override void StopServer()
        {
            if (IsRunning && _eventQueue != null)
            {
                _ = BroadcastSystemNotification("Remote Control Server stopping", "warning");
            }

            base.StopServer();
        }

        public virtual void UpdateHandlers()
        {
        }

        private void HandleClientConnected(RestApiClient client)
        {
            onClientConnected?.Invoke(client);
            _ = _eventQueue?.NotifyClientConnected(client.ClientId);
        }

        private void HandleClientDisconnected(RestApiClient client)
        {
            onClientDisconnected?.Invoke(client);
            _ = _eventQueue?.NotifyClientDisconnected(client.ClientId);
        }
        
        public Task BroadcastMessage(object message, string eventType)
        {
            return _eventQueue != null
                ? _eventQueue.BroadcastAsync(message, eventType)
                : Task.CompletedTask;
        }

        public Task SendToClient(string clientId, object message)
        {
            return _eventQueue != null
                ? _eventQueue.SendToClientAsync(clientId, message)
                : Task.CompletedTask;
        }

        /// <summary>
        /// 特定クライアントにタイプ付きイベントを送信する。
        /// BroadcastMessageと同じ形式で、クライアントのキューが未作成でも安全に送信可能。
        /// </summary>
        public void SendEventToClient(string clientId, object message, string eventType)
        {
            _eventQueue?.AddEventToClient(clientId, message, eventType);
        }

        public Task BroadcastSystemNotification(string message, string type = "info", object data = null, string title = null, string icon = null)
        {
            return _eventQueue != null
                ? _eventQueue.BroadcastSystemNotificationAsync(message, type, data, title, icon)
                : Task.CompletedTask;
        }
        
        public int GetConnectionCount()
        {
            return _connectionManager?.ConnectionCount ?? 0;
        }
        
        protected void WriteErrorResponse(HttpListenerResponse response, string errorMessage)
        {
            try
            {
                response.ContentType = "application/json; charset=utf-8";
                var json = $"{{\"error\":\"{errorMessage}\"}}";
                var buffer = System.Text.Encoding.UTF8.GetBytes(json);
                response.ContentLength64 = buffer.Length;
                // GC削減: 小さいバッファなので同期書き込み
                response.OutputStream.Write(buffer, 0, buffer.Length);
                response.OutputStream.Close();
                response.Close();
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[RemoteControl] Error writing error response: {ex.Message}");
            }
        }
        
        public override void Dispose()
        {
            // バックグラウンドタスクを停止し、全クライアントを切断
            _eventQueue?.Shutdown();
            _connectionManager?.Shutdown();

            if (_connectionManager != null)
            {
                _connectionManager.OnClientConnected -= HandleClientConnected;
                _connectionManager.OnClientDisconnected -= HandleClientDisconnected;
            }

            UnregisterDefaultRoutes();
            base.Dispose();
        }
    }
}