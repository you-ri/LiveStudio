// Copyright (c) You-Ri, 2026
using System;
using System.Net;
using System.Threading.Tasks;
using UnityEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Lilium.RemoteControl.Server;
using Lilium.RemoteControl.RestApi;

namespace Lilium.RemoteControl
{
    public class LanguageHandler : BaseRemoteControlApiHandler
    {
        public LanguageHandler(RemoteControlServerCore server) : base(server)
        {
        }

        public override void Cleanup()
        {
        }

        public override bool CanHandle(HttpListenerRequest request)
        {
            return request.Url.AbsolutePath.Equals("/api/language", StringComparison.OrdinalIgnoreCase);
        }

        protected override bool SupportsGet() => true;
        protected override bool SupportsPut() => true;

        protected override Task HandleGetRequest(HttpListenerContext context)
        {
            var available = LocalizationSystem.availableLanguages;
            var jObject = new JObject
            {
                ["current"] = LocalizationSystem.currentLanguage,
                ["available"] = new JArray(available)
            };

            var json = jObject.ToString(Formatting.None);
            context.Response.StatusCode = 200;
            return WriteResponse(context.Response, json);
        }

        protected override async Task HandlePutRequest(HttpListenerContext context)
        {
            try
            {
                var body = await ReadRequestBody(context.Request).ConfigureAwait(false);
                var jObject = JObject.Parse(body);
                var language = jObject["language"]?.ToString();

                if (string.IsNullOrEmpty(language))
                {
                    context.Response.StatusCode = 400;
                    await WriteResponse(context.Response, "{\"error\":\"'language' field is required.\"}");
                    return;
                }

                await ExecuteOnMainThread(() =>
                {
                    LocalizationSystem.currentLanguage = language;
                });

                var responseObj = new JObject
                {
                    ["success"] = true,
                    ["current"] = language
                };

                context.Response.StatusCode = 200;
                await WriteResponse(context.Response, responseObj.ToString(Formatting.None));
            }
            catch (JsonException)
            {
                context.Response.StatusCode = 400;
                await WriteResponse(context.Response, "{\"error\":\"Invalid JSON body.\"}");
            }
        }
    }
}
