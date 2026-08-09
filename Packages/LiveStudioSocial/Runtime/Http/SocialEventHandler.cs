// Copyright (c) You-Ri, 2026

using System.Net;
using System.Threading.Tasks;
using Lilium.RemoteControl.RestApi;
using Lilium.RemoteControl.Server;

namespace Lilium.LiveStudio.Social
{
    /// <summary>
    /// The HTTP intake for streaming events, bolted onto a running RemoteControl server.
    ///
    /// <code>
    /// POST /social/event   one event         → 200 {"accepted":true}
    /// POST /social/events  array of events   → 200 {"accepted":N,"rejected":M}
    /// GET  /social/status  intake counters   → 200 {"formatVersion":1,...}
    /// </code>
    ///
    /// Request bodies are read as UTF-8 regardless of what the Content-Type claims.
    ///
    /// This is the public contract external feeders speak, so the paths, the accepted shapes and the
    /// status codes may only ever grow — see Documentation~ for the frozen wire spec.
    ///
    /// Everything happens on the server's worker thread. There is deliberately no hop to the main thread:
    /// the handler only parses JSON and calls <see cref="SocialEventHub.Enqueue"/>, both of which are
    /// thread-safe and touch no Unity API, so a feeder's POST returns without waiting for a frame.
    /// </summary>
    public class SocialEventHandler : BaseRemoteControlApiHandler
    {
        public const string kEventPath = "/social/event";
        public const string kEventsPath = "/social/events";
        public const string kStatusPath = "/social/status";

        /// <summary>Version of the wire contract, reported by <c>GET /social/status</c> so a feeder can
        /// tell what it is talking to. Bumped only if a change cannot be expressed as an added field.</summary>
        public const int kFormatVersion = 1;

        /// <summary>
        /// Header carrying <see cref="authToken"/>. Named rather than reusing Authorization because
        /// feeders like OneComme and Streamer.bot set plain headers, not auth schemes.
        ///
        /// Note for browser-based feeders: the server answers CORS preflight itself, before any handler
        /// runs, using its own allow-list — which does not include this header. Feeders that run in a
        /// browser therefore cannot send it. The intended feeders (a Node.js plugin, Streamer.bot) are
        /// not browsers and are unaffected; CORS does not apply to them at all.
        /// </summary>
        public const string kTokenHeader = "X-Social-Token";

        // Written from the main thread (the gateway), read on server worker threads. A reference
        // assignment is atomic and volatile makes the new value visible without a lock, which matters
        // because the token can be edited from the remote app while feeders are posting.
        private volatile string _authToken = string.Empty;

        /// <summary>
        /// Shared secret required in the <see cref="kTokenHeader"/> header. Empty (the default) leaves the
        /// endpoint open, which is safe while the server is bound to loopback; set it when the app is
        /// configured to accept external connections.
        /// </summary>
        public string authToken
        {
            get => _authToken;
            set => _authToken = value ?? string.Empty;
        }

        public SocialEventHandler(RemoteControlServerCore server)
            : base(server,
                new RouteRule(kEventPath, RouteMatch.Exact),
                new RouteRule(kEventsPath, RouteMatch.Exact),
                new RouteRule(kStatusPath, RouteMatch.Exact))
        {
        }

        protected override bool SupportsGet() => true;

        protected override bool SupportsPost() => true;

        public override Task HandleRequest(HttpListenerContext context)
        {
            // Guard every route, status included: the counters describe traffic on a protected endpoint.
            // OPTIONS needs no exemption here — the server short-circuits preflight before handlers run.
            if (!SocialEventIntake.IsAuthorized(_authToken, context.Request.Headers[kTokenHeader]))
            {
                return WriteError(context, (int)HttpStatusCode.Unauthorized,
                    "[Social] Missing or incorrect " + kTokenHeader + " header.");
            }

            return base.HandleRequest(context);
        }

        protected override Task HandleGetRequest(HttpListenerContext context)
        {
            if (!MatchPattern(context.Request.Url.AbsolutePath, kStatusPath, RouteMatch.Exact))
            {
                return SendMethodNotAllowed(context);
            }

            return WriteJson(context, new SocialStatusResponse
            {
                formatVersion = kFormatVersion,
                totalReceived = SocialEventHub.totalReceived,
                totalDropped = SocialEventHub.totalDropped,
                queueCapacity = SocialEventHub.kQueueCapacity,
            });
        }

        protected override Task HandlePostRequest(HttpListenerContext context)
        {
            var request = context.Request;
            var path = request.Url.AbsolutePath;

            // Decide the route before touching the body: POSTing to the status route is a 405 whatever
            // it carries, and answering that without reading avoids a pointless copy.
            bool batch = MatchPattern(path, kEventsPath, RouteMatch.Exact);
            if (!batch && !MatchPattern(path, kEventPath, RouteMatch.Exact))
            {
                return SendMethodNotAllowed(context);
            }

            if (!SocialEventIntake.TryReadBody(request.InputStream, request.ContentLength64, out var body))
            {
                return WriteError(context, (int)HttpStatusCode.RequestEntityTooLarge,
                    $"[Social] Request body exceeds the {SocialEventIntake.kMaxBodyBytes} byte limit.");
            }

            if (batch)
            {
                if (!SocialEventIntake.TrySubmitBatch(body, out int accepted, out int rejected, out var batchError))
                {
                    return WriteError(context, (int)HttpStatusCode.BadRequest, batchError);
                }

                return WriteJson(context, new SocialBatchResponse { accepted = accepted, rejected = rejected });
            }

            if (!SocialEventIntake.TrySubmitOne(body, out var error))
            {
                return WriteError(context, (int)HttpStatusCode.BadRequest, error);
            }

            return WriteJson(context, new SocialAcceptedResponse { accepted = true });
        }

        private class SocialAcceptedResponse
        {
            public bool accepted;
        }

        private class SocialBatchResponse
        {
            public int accepted;
            // Entries in this request that never reached the hub because source or type was missing.
            // Deliberately not called "dropped": that word is already taken by the hub's queue overflow
            // (/social/status totalDropped), where the event did arrive and was displaced by newer traffic.
            public int rejected;
        }

        private class SocialStatusResponse
        {
            public int formatVersion;
            public long totalReceived;
            public long totalDropped;
            public int queueCapacity;
        }
    }
}
