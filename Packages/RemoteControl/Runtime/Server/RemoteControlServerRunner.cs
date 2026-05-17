// Copyright (c) You-Ri, 2026
using System;
using System.Threading;

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

        // Per-runner update thread. Drives UpdateHandlers() at high frequency. Coexists with
        // the lower-frequency manager thread for compatibility.
        private Thread _updateThread;
        private volatile bool _isRunning = false;

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

            StartUpdateThread();
        }

        public void ShutdownServer()
        {
            StopUpdateThread();

            if (_isServerOwner && _server != null)
            {
                _server.StopServer();
                RemoteControlServerManager.RemoveServer(GetPort());
            }
            _server = null;
            _isServerOwner = false;
        }

        private void StartUpdateThread()
        {
            if (_updateThread != null && _updateThread.IsAlive)
                return;

            _isRunning = true;
            _updateThread = new Thread(() =>
            {
                while (_isRunning)
                {
                    try
                    {
                        if (_server != null && _server.IsRunning)
                            _server.UpdateHandlers();
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[RemoteControl] Error in UpdateHandlers: {ex.Message}");
                    }

                    Thread.Sleep(1);
                }
            })
            {
                IsBackground = true,
                Name = $"RemoteControlServerUpdate_{GetPort()}"
            };

            _updateThread.Start();
        }

        private void StopUpdateThread()
        {
            if (_updateThread != null && _updateThread.IsAlive)
            {
                _isRunning = false;

                if (!_updateThread.Join(TimeSpan.FromSeconds(1)))
                {
                    Debug.LogWarning($"[RemoteControl] UpdateHandlers thread did not stop in time");
                }

                _updateThread = null;
            }
        }
    }
}
