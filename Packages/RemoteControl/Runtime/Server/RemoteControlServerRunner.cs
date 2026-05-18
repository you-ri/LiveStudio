// Copyright (c) You-Ri, 2026
using UnityEngine;
using Lilium.RemoteControl;

namespace Lilium.RemoteControl.Server
{
    /// <summary>
    /// Pure C# helper that owns the lifetime of a <see cref="RemoteControlServerCore"/> for a
    /// given port. Used to be a MonoBehaviour; the host
    /// <see cref="Lilium.RemoteControl.Scene.RemoteControlBehaviour"/> now drives Unity lifecycle.
    /// </summary>
    public class RemoteControlServerRunner
    {
        private const int kDefaultPort = 3002;

        public RemoteControlServerConfig serverConfig => _serverConfig;

        /// <summary>
        /// The running server instance, or null if not started yet (or start failed).
        /// </summary>
        public RemoteControlServerCore server => _server;

        private readonly RemoteControlServerConfig _serverConfig;
        private readonly ExposedObjectContainer _container;

        private RemoteControlServerCore _server;

        // True when this runner is the one that successfully started the server. Only the owner
        // is allowed to shut it down.
        private bool _isServerOwner;

        public RemoteControlServerRunner(RemoteControlServerConfig serverConfig, ExposedObjectContainer container)
        {
            _serverConfig = serverConfig;
            _container = container;
        }

        public int GetPort()
        {
            return _serverConfig?.port ?? kDefaultPort;
        }

        public void StartServer()
        {
            if (_server != null) return;

            var port = GetPort();

            _server = RemoteControlServerManager.GetOrCreateServer(port, _serverConfig, _container);

            if (_server == null)
            {
                Debug.LogWarning($"[RemoteControl] No server configuration found for port {port}. Please configure server in RemoteControlServerWindow.");
                return;
            }

            if (!_server.IsRunning)
            {
                _server.StartServer();

                if (!_server.IsRunning)
                {
                    Debug.LogWarning($"[RemoteControl] Server failed to start on port {port}. Cleaning up.");
                    RemoteControlServerManager.RemoveServer(port);
                    _server = null;
                    return;
                }

                _isServerOwner = true;
            }
        }

        public void ShutdownServer()
        {
            if (_isServerOwner && _server != null)
            {
                _server.StopServer();
                RemoteControlServerManager.RemoveServer(GetPort());
            }
            _server = null;
            _isServerOwner = false;
        }
    }
}
