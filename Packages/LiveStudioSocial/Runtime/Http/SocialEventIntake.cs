// Copyright (c) You-Ri, 2026

using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace Lilium.LiveStudio.Social
{
    /// <summary>
    /// The wire-facing half of the intake endpoint: body reading, JSON parsing, required-field checks
    /// and token comparison. Kept apart from <see cref="SocialEventHandler"/> because everything here is
    /// pure with respect to HTTP — no <c>HttpListenerContext</c>, which cannot be constructed in a test —
    /// so the rules that decide 200 / 400 / 401 / 413 can be exercised directly.
    ///
    /// Every method here runs on a worker thread and touches no Unity API.
    /// </summary>
    internal static class SocialEventIntake
    {
        /// <summary>Largest request body accepted. A single event is a few hundred bytes and a realistic
        /// batch is a few thousand, so this only stops something pathological.</summary>
        public const int kMaxBodyBytes = 64 * 1024;

        private const int kReadChunkBytes = 8 * 1024;

        /// <summary>
        /// True when the request may proceed. An empty configured token means the endpoint is open, which
        /// is the default: the server binds to loopback, so the token only starts to matter once the app
        /// is set to accept external connections.
        ///
        /// The comparison runs in time proportional to the token length rather than to how far the two
        /// agree. Ordinary string equality short-circuits at the first difference, which lets a caller on
        /// the LAN recover the token one character at a time from response timings — and on the LAN the
        /// token is the only thing standing between a stranger and the event stream. Length is allowed to
        /// leak; it is not worth padding for.
        /// </summary>
        public static bool IsAuthorized(string configuredToken, string presentedToken)
        {
            if (string.IsNullOrEmpty(configuredToken)) return true;
            if (presentedToken == null || presentedToken.Length != configuredToken.Length) return false;

            int difference = 0;
            for (int i = 0; i < configuredToken.Length; i++)
            {
                difference |= configuredToken[i] ^ presentedToken[i];
            }
            return difference == 0;
        }

        /// <summary>
        /// Reads the request body as UTF-8, refusing anything over <see cref="kMaxBodyBytes"/>.
        /// <paramref name="declaredLength"/> is the request's Content-Length, or a negative value when the
        /// sender used chunked encoding — in which case the cap has to be enforced while reading, since an
        /// unbounded read is exactly what the limit exists to prevent.
        /// </summary>
        public static bool TryReadBody(Stream input, long declaredLength, out string body)
        {
            body = null;
            if (declaredLength > kMaxBodyBytes) return false;

            if (declaredLength >= 0)
            {
                var exact = new byte[declaredLength];
                int filled = _ReadFully(input, exact);
                body = Encoding.UTF8.GetString(exact, 0, filled);
                return true;
            }

            using (var grown = new MemoryStream())
            {
                var chunk = new byte[kReadChunkBytes];
                int read;
                while ((read = input.Read(chunk, 0, chunk.Length)) > 0)
                {
                    if (grown.Length + read > kMaxBodyBytes) return false;
                    grown.Write(chunk, 0, read);
                }
                body = Encoding.UTF8.GetString(grown.GetBuffer(), 0, (int)grown.Length);
                return true;
            }
        }

        /// <summary>
        /// Parses one event and hands it to the hub. Returns false with an English <c>[Social]</c> message
        /// on anything the contract calls invalid; the caller turns that into a 400.
        /// </summary>
        public static bool TrySubmitOne(string body, out string error)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                error = "[Social] Request body is empty.";
                return false;
            }

            SocialEvent e;
            try
            {
                e = JsonConvert.DeserializeObject<SocialEvent>(body);
            }
            catch (JsonException ex)
            {
                error = "[Social] Invalid event JSON: " + ex.Message;
                return false;
            }

            if (e == null)
            {
                error = "[Social] Request body must be a JSON object describing one event.";
                return false;
            }

            if (!_HasRequiredFields(e, out error)) return false;

            SocialEventHub.Enqueue(e);
            error = null;
            return true;
        }

        /// <summary>
        /// Parses an array of events. A malformed entry is counted in <paramref name="rejected"/> rather
        /// than failing the whole request: a feeder batching 50 comments should not lose 49 good ones to a
        /// single bad entry. Only a body that is not a JSON array at all is an error.
        ///
        /// Rejection here is not the same thing as the hub's queue overflow — an entry counted as rejected
        /// never reached the hub, whereas a dropped one did and was displaced by newer traffic.
        /// </summary>
        public static bool TrySubmitBatch(string body, out int accepted, out int rejected, out string error)
        {
            accepted = 0;
            rejected = 0;

            if (string.IsNullOrWhiteSpace(body))
            {
                error = "[Social] Request body is empty.";
                return false;
            }

            List<SocialEvent> events;
            try
            {
                events = JsonConvert.DeserializeObject<List<SocialEvent>>(body);
            }
            catch (JsonException ex)
            {
                error = "[Social] Invalid batch JSON: " + ex.Message;
                return false;
            }

            if (events == null)
            {
                error = "[Social] Request body must be a JSON array of events.";
                return false;
            }

            for (int i = 0; i < events.Count; i++)
            {
                var e = events[i];
                if (e == null || !_HasRequiredFields(e, out _))
                {
                    rejected++;
                    continue;
                }

                SocialEventHub.Enqueue(e);
                accepted++;
            }

            error = null;
            return true;
        }

        // source and type are the only required fields; everything else is optional and gets normalized
        // by the hub. Whitespace counts as missing so a feeder cannot smuggle a blank platform through.
        private static bool _HasRequiredFields(SocialEvent e, out string error)
        {
            if (string.IsNullOrWhiteSpace(e.source))
            {
                error = "[Social] Event is missing the required field 'source'.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(e.type))
            {
                error = "[Social] Event is missing the required field 'type'.";
                return false;
            }
            error = null;
            return true;
        }

        // Stream.Read may return short reads; keep going until the buffer is full or the peer stops.
        private static int _ReadFully(Stream input, byte[] buffer)
        {
            int total = 0;
            while (total < buffer.Length)
            {
                int read = input.Read(buffer, total, buffer.Length - total);
                if (read <= 0) break;
                total += read;
            }
            return total;
        }
    }
}
