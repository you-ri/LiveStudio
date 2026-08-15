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
    /// <c>GET /live/asset?guid=&lt;key&gt;</c> looks up <see cref="AssetRegistry"/> and returns
    /// <c>{ "guid", "name", "type" }</c>. Clients use this to label AssetSelector options,
    /// whose choice lists carry keys only. The key may be a baked GUID or an external
    /// <c>file:&lt;path&gt;#&lt;clip&gt;</c> reference: a loaded target resolves to its real name/type,
    /// and an unloaded external key still resolves to a derived name via the registry's name fallback
    /// (with an empty type). Keys the registry cannot resolve at all return 404.
    /// </summary>
    public class AssetHandler : BaseRemoteControlApiHandler
    {
        public AssetHandler(RemoteControlServerCore server)
            : base(server, new RouteRule("/live/asset", RouteMatch.Exact))
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
                // Loaded target: report its real name and type.
                if (AssetRegistry.TryFind(guid, out var asset))
                {
                    return new JObject
                    {
                        ["guid"] = guid,
                        ["name"] = asset.name,
                        ["type"] = asset.GetType().Name,
                    };
                }

                // Not loaded: a scheme-aware fallback may still derive a name (e.g. an external file:
                // key's clip name), so the resolver stays complete for unloaded references. Type is
                // unknown here, so it is left empty.
                var fallbackName = AssetRegistry.ResolveDisplayName(guid);
                if (string.IsNullOrEmpty(fallbackName)) return null;
                return new JObject
                {
                    ["guid"] = guid,
                    ["name"] = fallbackName,
                    ["type"] = string.Empty,
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
