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
        /// ExposedObjectContainer this server operates on.
        /// </summary>
        public ExposedObjectContainer objectContainer { get; }

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
        /// Create a RemoteControlContext.
        /// </summary>
        /// <param name="scope">Scope identifier (default: "default").</param>
        /// <param name="container">ExposedObjectContainer (optional).</param>
        public RemoteControlContext(string scope = "default", ExposedObjectContainer container = null)
        {
            this.objectContainer = container;
            this.scope = scope;
            this.eventQueue = new EventQueue();
            this.connectionManager = new RestApiConnectionManager();
        }
    }
}
