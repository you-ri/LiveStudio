// Copyright (c) You-Ri, 2026
using System.Net;
using System.Threading.Tasks;

using Lilium.RemoteControl.Server;
using Lilium.RemoteControl.RestApi;


namespace Lilium.RemoteControl
{
    [System.Serializable]
    public class PerformanceResponse
    {
        public bool success;

        /// <summary>Rolling one-second average frame rate.</summary>
        public float fps;

        /// <summary>Process memory usage (working set) in bytes.</summary>
        public long memory;
    }


    /// <summary>
    /// Dedicated endpoint for runtime performance metrics (FPS / memory). Kept separate from
    /// <see cref="StatusHandler"/> so clients only pay for these samples when they actually display
    /// them (e.g. the RemoteApp performance overlay polls this only while visible).
    /// </summary>
    public class PerformanceHandler : BaseRemoteControlApiHandler
    {
        public PerformanceHandler(RemoteControlServerCore server)
            : base(server, new RouteRule("/api/performance", RouteMatch.Exact))
        {
        }

        protected override bool SupportsGet() => true;

        protected override Task HandleGetRequest(HttpListenerContext context)
        {
            var response = new PerformanceResponse
            {
                success = true,
                fps = PerformanceMonitor.fps,
                memory = PerformanceMonitor.memoryBytes,
            };

            return WriteJson(context, response);
        }
    }
}
