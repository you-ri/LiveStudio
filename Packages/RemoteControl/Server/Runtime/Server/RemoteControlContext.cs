using System;

using Lilium.RemoteControl.Core;

namespace Lilium.RemoteControl.Server
{
    /// <summary>
    /// Per-server context shared by handlers running under one RemoteControlServerCore.
    /// Encapsulates the dependencies needed to run multiple server instances independently.
    /// </summary>
    public class RemoteControlContext
    {
        /// <summary>
        /// LiveObjectContainer this server operates on.
        /// </summary>
        public LiveObjectContainer objectContainer { get; }

        /// <summary>
        /// Event queue dedicated to this server instance.
        /// </summary>
        public EventQueue eventQueue { get; }

        /// <summary>
        /// Connection manager dedicated to this server instance.
        /// </summary>
        public RestApiConnectionManager connectionManager { get; }

        /// <summary>
        /// Scope identifier for this context (scene name, port number, etc.).
        /// </summary>
        public string scope { get; }

        /// <summary>
        /// Identifies this run of the server. A new value means the event queue and the exposed
        /// object registry were rebuilt, so every cursor a remote app is holding (inbox read
        /// position, change-feed revision) refers to a state that no longer exists. Clients watch
        /// this on <c>/live/status</c> and resync when it changes — polling alone cannot notice a
        /// restart it happened to poll straight through.
        /// </summary>
        public string instanceId { get; }

        /// <summary>
        /// Create a RemoteControlContext.
        /// </summary>
        /// <param name="scope">Scope identifier (default: "default").</param>
        /// <param name="container">LiveObjectContainer (optional).</param>
        public RemoteControlContext(string scope = "default", LiveObjectContainer container = null)
        {
            this.objectContainer = container;
            this.scope = scope;
            this.eventQueue = new EventQueue();
            this.connectionManager = new RestApiConnectionManager();
            this.instanceId = Guid.NewGuid().ToString("N");
        }
    }
}
