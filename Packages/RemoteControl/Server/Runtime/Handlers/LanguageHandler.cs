// Copyright (c) You-Ri, 2026
using Lilium.RemoteControl.Frames;
using System;
using System.Collections.Generic;
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
        public LanguageHandler(RemoteControlServerCore server)
            : base(server, new RouteRule("/live/language", RouteMatch.Exact))
        {
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
            var (ok, jObject, error) = await TryReadRequest<JObject>(context.Request,
                emptyMessage: "Invalid JSON body.", invalidMessage: "Invalid JSON body.");
            if (!ok)
            {
                await WriteError(context, 400, error);
                return;
            }

            var language = jObject["language"]?.ToString();
            if (string.IsNullOrEmpty(language))
            {
                await WriteError(context, 400, "'language' field is required.");
                return;
            }

            await ExecuteAsInput(InputKind.PropertyWrite,
                context.Request.Url.AbsolutePath, language, () =>
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
    }
}
