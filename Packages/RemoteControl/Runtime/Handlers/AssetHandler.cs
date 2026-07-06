// Copyright (c) You-Ri, 2026
using System.Net;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Lilium.RemoteControl.Server;
using Lilium.RemoteControl.RestApi;

namespace Lilium.RemoteControl
{
    /// <summary>
    /// Resolves an asset GUID to the registered asset's display info.
    ///
    /// <c>GET /api/asset?guid=&lt;guid&gt;</c> looks up <see cref="AssetRegistry"/> and returns
    /// <c>{ "guid", "name", "type" }</c>. Clients use this to label AssetSelector options,
    /// whose choice lists carry GUIDs only. Unknown GUIDs return 404.
    /// </summary>
    public class AssetHandler : BaseRemoteControlApiHandler
    {
        public AssetHandler(RemoteControlServerCore server)
            : base(server, new RouteRule("/api/asset", RouteMatch.Exact))
        {
        }

        protected override bool SupportsGet() => true;

        protected override async Task HandleGetRequest(HttpListenerContext context)
        {
            var guid = context.Request.QueryString["guid"];
            if (string.IsNullOrEmpty(guid))
            {
                await WriteError(context, 400, "'guid' query parameter is required.");
                return;
            }

            // Asset name/type must be read on the main thread (UnityEngine.Object access).
            var info = await ExecuteOnMainThread<JObject>(() =>
            {
                if (!AssetRegistry.TryFind(guid, out var asset)) return null;
                return new JObject
                {
                    ["guid"] = guid,
                    ["name"] = asset.name,
                    ["type"] = asset.GetType().Name,
                };
            });

            if (info == null)
            {
                await WriteError(context, 404, "Asset not found.");
                return;
            }

            context.Response.StatusCode = 200;
            await WriteResponse(context.Response, info.ToString(Formatting.None));
        }
    }
}
