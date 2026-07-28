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
            : base(server, new RouteRule("/api/status", RouteMatch.Exact))
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
            };

            return WriteJson(context, status);
        }

    }

}
