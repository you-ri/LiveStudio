using System;
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

        public override bool CanHandle(HttpListenerRequest request)
        {
            return request.Url.AbsolutePath.Equals("/api/status", StringComparison.OrdinalIgnoreCase) &&
                   request.HttpMethod == "GET";
        }

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

            var json = JsonUtility.ToJson(status);
            context.Response.StatusCode = 200;
            return WriteResponse(context.Response, json);
        }

    }

}
