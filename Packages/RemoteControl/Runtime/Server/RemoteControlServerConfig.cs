using System;
using UnityEngine;


namespace Lilium.RemoteControl.Server
{
    /// <summary>
    /// Server configuration stored as a ScriptableObject.
    /// Each server instance is configured by its own asset file.
    /// Concrete: instantiates a generic <see cref="RemoteControlServerCore"/>.
    /// Application-specific routes are registered by external components
    /// (e.g. MonoBehaviours next to <see cref="RemoteControlServerRunner"/>),
    /// not by subclassing this config.
    /// </summary>
    [CreateAssetMenu(fileName = "RemoteControlServerConfig", menuName = "Remote Control/Server Config")]
    public class RemoteControlServerConfig : ScriptableObject
    {
        [Tooltip("Server port number")]
        public int port = 3002;

        [Tooltip("Enable CORS for cross-origin requests")]
        public bool enableCors = true;

        [Tooltip("Keep this server running in Unity Editor")]
        public bool runningInEditor = false;

        public RemoteControlServerCore CreateServer()
        {
            return CreateServer(null);
        }

        public RemoteControlServerCore CreateServer(ExposedObjectContainer container)
        {
            if (RemoteControlServerManager.servers.TryGetValue(port, out var existing))
            {
                Debug.LogWarning($"[RemoteControl] Server already exists on port {port}");
                return existing.server;
            }

            var context = new RemoteControlContext($"port_{port}", container);
            var server = new RemoteControlServerCore(port, enableCors, context);
            server.OnServerError += ex => Debug.LogError($"[RemoteControl] Server on port {port} error: {ex.Message}");

            RemoteControlServerManager.AddServer(port, server, context);

            return server;
        }
    }
}
