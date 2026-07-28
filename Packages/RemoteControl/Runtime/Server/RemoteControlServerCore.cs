using System;
using System.Collections.Generic;

using System.Threading.Tasks;
using Lilium.RemoteControl.Core;
using Lilium.RemoteControl.RestApi;
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
        private EventsHandler _eventsHandler;
        private StatusHandler _statusHandler;
        private PerformanceHandler _performanceHandler;
        private LanguageHandler _languageHandler;
        private AssetHandler _assetHandler;
        private ConfirmApiHandler _confirmHandler;

        public event Action<RestApiClient> onClientConnected;
        public event Action<RestApiClient> onClientDisconnected;



        public RemoteControlServerCore(int port, bool enableCors, RemoteControlContext context, bool allowExternalConnections = false)
            : base(port, enableCors, allowExternalConnections)
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
            _exposedObjectHandler = new ExposedObjectHandler(this);
            RegisterRoute(_exposedObjectHandler);
            // Standalone route for the per-client event inbox. The remote app collects it through
            // the batch endpoint instead, but a non-batching client needs somewhere to ask.
            _eventsHandler = new EventsHandler(this);
            RegisterRoute(_eventsHandler);
            _statusHandler = new StatusHandler(this);
            RegisterRoute(_statusHandler);
            _performanceHandler = new PerformanceHandler(this);
            RegisterRoute(_performanceHandler);
            _languageHandler = new LanguageHandler(this);
            RegisterRoute(_languageHandler);
            _assetHandler = new AssetHandler(this);
            RegisterRoute(_assetHandler);
            // Carries a remote app's answer to a RemoteConfirmSystem prompt. A default route because
            // the prompt can be raised by the framework itself (unsaved changes), not just by an app.
            _confirmHandler = new ConfirmApiHandler(this);
            RegisterRoute(_confirmHandler);
        }

        public void UnregisterDefaultRoutes()
        {
            // UnregisterRoute calls handler.Cleanup() internally.
            UnregisterRoute(_exposedObjectHandler);
            _exposedObjectHandler = null;
            UnregisterRoute(_eventsHandler);
            _eventsHandler = null;
            UnregisterRoute(_statusHandler);
            _statusHandler = null;
            UnregisterRoute(_performanceHandler);
            _performanceHandler = null;
            UnregisterRoute(_languageHandler);
            _languageHandler = null;
            UnregisterRoute(_assetHandler);
            _assetHandler = null;
            UnregisterRoute(_confirmHandler);
            _confirmHandler = null;
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
            // Do not broadcast a "stopping" notification here: the server is shutting
            // down, so the message would only race the disconnect and surface a
            // misleading warning on the RemoteApp side.
            base.StopServer();
        }

        private void HandleClientConnected(RestApiClient client)
        {
            onClientConnected?.Invoke(client);
        }

        private void HandleClientDisconnected(RestApiClient client)
        {
            onClientDisconnected?.Invoke(client);
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
        
        /// <summary>
        /// How many remote apps are polling this server right now. Backed by each client's last
        /// request time (see <see cref="RestApiConnectionManager"/>), which is the only liveness
        /// signal there is once nothing holds a connection open.
        /// </summary>
        public int GetConnectionCount()
        {
            return _connectionManager?.ConnectionCount ?? 0;
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