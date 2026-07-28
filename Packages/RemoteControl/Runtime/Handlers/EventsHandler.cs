// Copyright (c) You-Ri, 2026
using System.Net;
using System.Threading.Tasks;

using Lilium.RemoteControl.Server;

namespace Lilium.RemoteControl.RestApi.Controllers
{
    /// <summary>
    /// <c>GET /api/events?since=N</c> — collects the one-shot notices queued for the calling client.
    ///
    /// The remote app normally picks these up as a sub-request of <c>POST /live/batch</c> so it
    /// spends no extra round trip on them; this standalone route exists for clients that do not
    /// batch, and to make the inbox inspectable with a plain GET. Both resolve to the same body
    /// (see <see cref="EventInbox"/>).
    /// </summary>
    public class EventsHandler : BaseRemoteControlApiHandler
    {
        public EventsHandler(RemoteControlServerCore server)
            : base(server, new RouteRule(EventInbox.kPath, RouteMatch.Exact))
        {
        }

        protected override bool SupportsGet() => true;

        protected override Task HandleGetRequest(HttpListenerContext context)
        {
            var clientId = GetClientId(context.Request);
            long.TryParse(context.Request.QueryString["since"], out var since);

            var json = EventInbox.BuildJson(_context?.eventQueue, clientId, since);
            return WriteResponse(200, context.Response, json);
        }
    }
}
