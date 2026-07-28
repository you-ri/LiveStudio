// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;
using System.Text;

using Newtonsoft.Json;

namespace Lilium.RemoteControl
{
    /// <summary>
    /// The wire side of <see cref="EventQueue"/>: turns a client's queued one-shot notices into the
    /// response it collects them with.
    ///
    /// Nothing is pushed to a remote app any more, so a notice has to survive until its client asks
    /// for it. Each client keeps a read position and sends it back as <c>?since=</c>; the server
    /// hands back everything after it plus the new position. Miss a poll, or a hundred, and nothing
    /// is lost — the queue is per client and only advances when that client acknowledges by asking
    /// for what comes next.
    ///
    /// Reachable two ways, both resolving to the same body: on its own as
    /// <c>GET /api/events</c>, and as a sub-request of <c>POST /live/batch</c>, which is how the
    /// remote app actually collects it — riding the poll it already sends for displayed values
    /// costs no extra round trip.
    /// </summary>
    public static class EventInbox
    {
        /// <summary>The inbox path, without query. Shared by the single and batch routes.</summary>
        public const string kPath = "/api/events";

        // Per-thread scratch. The endpoint is polled continuously by every connected remote app, so
        // the steady state (an empty inbox) must not allocate beyond the response string itself.
        [ThreadStatic] private static List<EventItem> _drainBuffer;
        [ThreadStatic] private static StringBuilder _json;

        /// <summary>
        /// True when <paramref name="absolutePath"/> targets the inbox (with or without a query).
        /// </summary>
        public static bool IsInboxPath(string absolutePath)
        {
            if (absolutePath == null) return false;
            if (!absolutePath.StartsWith(kPath, StringComparison.OrdinalIgnoreCase)) return false;
            return absolutePath.Length == kPath.Length
                || absolutePath[kPath.Length] == '?';
        }

        /// <summary>
        /// Reads <c>?since=N</c> off a raw path. Batch sub-requests carry the path as a string rather
        /// than a parsed query collection, and this runs on every poll of every client, so it scans
        /// in place instead of splitting the string.
        /// </summary>
        public static bool TryParseSince(string absolutePath, out long since)
        {
            since = 0;
            if (absolutePath == null) return false;

            int query = absolutePath.IndexOf('?');
            if (query < 0) return false;
            int at = absolutePath.IndexOf("since=", query, StringComparison.Ordinal);
            if (at < 0) return false;

            at += "since=".Length;
            long value = 0;
            bool anyDigit = false;
            for (; at < absolutePath.Length; at++)
            {
                char c = absolutePath[at];
                if (c < '0' || c > '9') break;
                value = value * 10 + (c - '0');
                anyDigit = true;
            }
            if (!anyDigit) return false;
            since = value;
            return true;
        }

        /// <summary>
        /// Builds <c>{"lastEventId":N,"events":[...]}</c> for <paramref name="clientId"/>.
        /// An empty inbox echoes <paramref name="since"/> back so the client's read position holds
        /// steady. A null queue yields the same empty shape rather than an error — a server without
        /// an event queue simply has nothing to hand out.
        /// </summary>
        public static string BuildJson(EventQueue queue, string clientId, long since)
        {
            if (since < 0) since = 0;

            int count = 0;
            List<EventItem> buffer = null;
            if (queue != null)
            {
                buffer = _drainBuffer ?? (_drainBuffer = new List<EventItem>(8));
                count = queue.DrainEvents(clientId, since, buffer);
            }

            var sb = _json ?? (_json = new StringBuilder(256));
            sb.Clear();
            sb.Append("{\"lastEventId\":")
              .Append(count > 0 ? buffer[count - 1].Id : since)
              .Append(",\"events\":[");
            for (int i = 0; i < count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(buffer[i].GetPayload().ToString(Formatting.None));
            }
            sb.Append("]}");
            return sb.ToString();
        }
    }
}
