// Copyright (c) You-Ri, 2026
using System.Net;
using System.Threading.Tasks;

using Newtonsoft.Json.Linq;

using Lilium.RemoteControl.Notification;
using Lilium.RemoteControl.Server;

namespace Lilium.RemoteControl.RestApi
{
    /// <summary>
    /// Carries a remote app's answer to a confirmation raised by
    /// <see cref="RemoteConfirmSystem"/> back to the app.
    ///
    /// POST /live/confirm  { "id": "confirm-3", "choice": "yes" | "no" | "cancel" }
    ///
    /// Answering is a race between the surfaces showing the same prompt, so a POST for an id that is
    /// already resolved (or never existed) is not an error: it reports <c>settled: false</c> and the
    /// remote app just closes its modal.
    /// </summary>
    public class ConfirmApiHandler : BaseRemoteControlApiHandler
    {
        public ConfirmApiHandler(RemoteControlServerCore server)
            : base(server, new RouteRule("/live/confirm", RouteMatch.Exact))
        {
        }

        protected override bool SupportsPost() => true;

        protected override async Task HandlePostRequest(HttpListenerContext context)
        {
            var body = await ReadRequestBody(context.Request);

            string id;
            string choice;
            try
            {
                var root = JObject.Parse(body);
                id = (string)root["id"];
                choice = (string)root["choice"];
            }
            catch (Newtonsoft.Json.JsonException ex)
            {
                await WriteError(context, (int)HttpStatusCode.BadRequest, $"[RemoteControl] Invalid confirm body: {ex.Message}");
                return;
            }

            if (string.IsNullOrEmpty(id))
            {
                await WriteError(context, (int)HttpStatusCode.BadRequest, "[RemoteControl] Confirm request id is required.");
                return;
            }

            // No main-thread hop: settling is thread-safe and hands the caller's callback to the main
            // thread itself. Going through ExecuteOnMainThread would instead make this response wait
            // on a frame, and the answer is time-critical — the OS dialog is still up until it lands.
            bool settled = RemoteConfirmSystem.Answer(id, RemoteConfirmSystem.ParseChoice(choice));

            await WriteJson(context, new ConfirmAnswerResponse
            {
                success = true,
                settled = settled,
                timestamp = GetISOTimestamp(),
            });
        }

        private class ConfirmAnswerResponse
        {
            public bool success;
            // False when another surface answered first, or the prompt no longer exists.
            public bool settled;
            public string timestamp;
        }
    }
}
