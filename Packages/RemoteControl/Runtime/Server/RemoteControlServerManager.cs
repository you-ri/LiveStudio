using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Lilium.RemoteControl;

#if UNITY_EDITOR
using UnityEditor;
#endif


namespace Lilium.RemoteControl.Server
{
#if UNITY_EDITOR
    [InitializeOnLoad]
#endif
    public static class RemoteControlServerManager
    {
        public class ServerInstance
        {
            public RemoteControlServerCore server;
            public RemoteControlContext context;
        }

        public static IReadOnlyDictionary<int, ServerInstance> servers => _servers;

        private static readonly Dictionary<int, ServerInstance> _servers = new Dictionary<int, ServerInstance>();

        private static bool _isInitialized = false;

        static RemoteControlServerManager()
        {
            Initialize();
        }

        private static void Initialize()
        {
            if (_isInitialized) return;

#if UNITY_EDITOR
            EditorApplication.quitting += OnEditorQuitting;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            AssemblyReloadEvents.beforeAssemblyReload += () =>
            {
                RemoveAllServers();
            };
#endif
            
            _isInitialized = true;
        }

        public static void AddServer(int port, RemoteControlServerCore server, RemoteControlContext context)
        {
            var instance = new ServerInstance
            {
                server = server,
                context = context
            };

            _servers[port] = instance;
        }

        public static RemoteControlServerCore GetServer(int port)
        {
            return _servers.TryGetValue(port, out var instance) ? instance.server : null;
        }

        public static RemoteControlServerCore GetOrCreateServer(int port, RemoteControlServerConfig serverConfig, ExposedObjectContainer container = null)
        {
            if (_servers.ContainsKey(port))
            {
                return _servers[port].server;
            }

            if (container != null)
            {
                return serverConfig.CreateServer(container);
            }

            return serverConfig.CreateServer();
        }

        public static void RemoveServer(int port)
        {
            if (!_servers.TryGetValue(port, out var instance))
            {
                return;
            }

            if (instance.server != null)
            {
                instance.server.StopServer();
                instance.server.Dispose();
            }

            _servers.Remove(port);
            //Debug.Log($"[Studio] Removed server on port {port}");
        }

        public static void RemoveAllServers()
        {
            var ports = _servers.Keys.ToList();
            foreach (var port in ports)
            {
                RemoveServer(port);
            }
        }

        public static void StartServer(int port)
        {
            if (_servers.TryGetValue(port, out var instance))
            {
                instance.server?.StartServer();
            }
        }

        public static void StopServer(int port)
        {
            if (_servers.TryGetValue(port, out var instance))
            {
                instance.server?.StopServer();
            }
        }

        public static bool IsServerRunning(int port)
        {
            return _servers.TryGetValue(port, out var instance) && instance.server?.IsRunning == true;
        }

        public static bool HasServer(int port)
        {
            return _servers.ContainsKey(port);
        }

        public static IEnumerable<int> GetAllPorts()
        {
            return _servers.Keys;
        }

#if UNITY_EDITOR
        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                RemoveAllServers();
            }
        }

        private static void OnEditorQuitting()
        {
            RemoveAllServers();
        }
#endif
    }
}
