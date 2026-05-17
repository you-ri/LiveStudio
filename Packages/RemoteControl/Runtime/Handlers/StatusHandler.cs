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
    }



    public class StatusHandler : BaseRemoteControlApiHandler
    {
        private readonly string _applicationName;
        private readonly string _applicationVersion;

        public StatusHandler(RemoteControlServerCore server) : base(server)
        {
            _applicationName = Application.productName;
            _applicationVersion = Application.version;
        }

        public override void Cleanup()
        {
        }

        private static readonly RouteRule[] _kRoutes =
        {
            new RouteRule("/api/status", RouteMatch.Exact)
        };

        protected override IReadOnlyList<RouteRule> Routes => _kRoutes;

        protected override bool SupportsGet() => true;

        protected override Task HandleGetRequest(HttpListenerContext context)
        {
            var status = new StatusResponse
            {
                success = true,
                applicationName = _applicationName,
                version = _applicationVersion,
                fps = 60,//TimeService.fps,
            };

            return WriteJson(context, status);
        }

    }

}
