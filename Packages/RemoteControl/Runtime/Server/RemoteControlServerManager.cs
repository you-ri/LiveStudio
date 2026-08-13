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
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
#endif

            _isInitialized = true;
        }

        // In a player build nothing above runs (all the teardown hooks are editor events), so the
        // servers and their background cleanup tasks would only die with the process. Hook the
        // runtime quit signal as well; in the editor this fires alongside ExitingPlayMode, where
        // the second RemoveAllServers is a no-op. Unsubscribe-first keeps this safe when Domain
        // Reload is disabled and the method runs again on the next play.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void _RegisterRuntimeQuitHook()
        {
            Application.quitting -= RemoveAllServers;
            Application.quitting += RemoveAllServers;
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

        /// <summary>
        /// Returns the server registered for <paramref name="port"/>, creating and registering one
        /// from <paramref name="serverConfig"/> when there is none. The config is pure data here;
        /// creation and registration are this manager's job.
        /// </summary>
        public static RemoteControlServerCore GetOrCreateServer(int port, RemoteControlServerConfig serverConfig, LiveObjectContainer container = null)
        {
            if (_servers.TryGetValue(port, out var existing))
            {
                return existing.server;
            }

            if (serverConfig == null) return null;

            var context = new RemoteControlContext($"port_{port}", container);
            var server = new RemoteControlServerCore(port, serverConfig.enableCors, context, serverConfig.allowExternalConnections);
            server.OnServerError += ex => Debug.LogError($"[RemoteControl] Server on port {port} error: {ex.Message}");

            AddServer(port, server, context);

            return server;
        }

        public static void RemoveServer(int port)
        {
            if (!_servers.TryGetValue(port, out var instance))
            {
                return;
            }

            // Dispose stops the server first, then tears down its handlers.
            instance.server?.Dispose();

            _servers.Remove(port);
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

        /// <summary>
        /// True while at least one registered server is listening. Answers "is the remote server up?"
        /// without the caller having to know which ports this app configured; the editor toolbar
        /// indicator polls it, so it walks the dictionary directly instead of allocating an enumerator.
        /// </summary>
        public static bool IsAnyServerRunning()
        {
            foreach (var pair in _servers)
            {
                if (pair.Value?.server?.IsRunning == true) return true;
            }
            return false;
        }

        public static bool HasServer(int port)
        {
            return _servers.ContainsKey(port);
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

        private static void OnBeforeAssemblyReload()
        {
            RemoveAllServers();
        }
#endif
    }
}
