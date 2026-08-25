using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using UnityEngine;

using Lilium.RemoteControl.Server;
using Lilium.RemoteControl.RestApi;


namespace Lilium.RemoteControl
{
    [System.Serializable]
    public class StatusResponse
    {
        public bool success;

        public string applicationName;
        public string version;

        public float fps;

        /// <summary>Identifies this run of the server. See <see cref="RemoteControlContext.instanceId"/>.</summary>
        public string instanceId;

        /// <summary>
        /// <c>"editor"</c> while the editor is not playing, <c>"play"</c> otherwise.
        /// </summary>
        /// <remarks>
        /// Remote apps use it to decide what to show and what to offer: an editor session has no
        /// live scene to save, open, or snapshot (see <see cref="LiveEditorSession"/>).
        /// <para/>
        /// ⚠ Read on every poll, not once per connection. The same server moves between the two as
        /// play mode starts and stops, and the connection stays up across it.
        /// </remarks>
        public string environment;
    }



    /// <summary>
    /// Connection check, polled by every remote app about once a second.
    ///
    /// It doubles as the presence signal: with nothing holding a connection open, "is a remote app
    /// there" can only mean "did one ask us something recently", and this is the request every
    /// connected remote sends on a fixed cadence whatever page it is on.
    /// </summary>
    public class StatusHandler : BaseRemoteControlApiHandler
    {
        private readonly string _applicationName;
        private readonly string _applicationVersion;

        public StatusHandler(RemoteControlServerCore server)
            : base(server, new RouteRule("/live/status", RouteMatch.Exact))
        {
            _applicationName = Application.productName;
            _applicationVersion = Application.version;
        }

        protected override bool SupportsGet() => true;

        protected override Task HandleGetRequest(HttpListenerContext context)
        {
            // Registering on every poll is what keeps the client counted as present, which is how a
            // confirmation prompt knows whether it has anywhere to show (see RemoteConfirmSystem).
            _context?.connectionManager?.RegisterClient(
                GetClientId(context.Request),
                context.Request.UserAgent,
                context.Request.RemoteEndPoint?.Address?.ToString());

            var status = new StatusResponse
            {
                success = true,
                applicationName = _applicationName,
                version = _applicationVersion,
                fps = 60,//TimeService.fps,
                instanceId = _context?.instanceId,
                environment = LiveEditorSession.isEditorSession ? "editor" : "play",
            };

            return WriteJson(context, status);
        }

    }

}
