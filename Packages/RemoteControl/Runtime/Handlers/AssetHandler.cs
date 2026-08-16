// Copyright (c) You-Ri, 2026
using System.Net;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Lilium.RemoteControl.Server;
using Lilium.RemoteControl.RestApi;
using Lilium.RemoteControl.Utility;

namespace Lilium.RemoteControl
{
    /// <summary>
    /// Resolves an asset key to the registered asset's display info.
    ///
    /// <c>GET /live/asset/&lt;key&gt;</c> looks up <see cref="AssetRegistry"/> and returns
    /// <c>{ "guid", "name", "type" }</c>. Clients use this to label AssetSelector options,
    /// whose choice lists carry keys only. The key may be a baked GUID or an external
    /// <c>file:&lt;path&gt;#&lt;clip&gt;</c> reference: a loaded target resolves to its real name/type,
    /// and an unloaded external key still resolves to a derived name via the registry's name fallback
    /// (with an empty type). Keys the registry cannot resolve at all return 404.
    ///
    /// The key occupies the whole tail of the URL — slashes inside it are kept verbatim, the way
    /// <c>/live/object/{id}/{path}</c> keeps a property path — because a key is a file path or an
    /// engine asset path as often as it is a GUID. Two consequences a client must live with: reserved
    /// characters (<c>#</c>, <c>?</c>, <c>%</c>) have to be percent-encoded or the URL is cut short, and
    /// a key containing <c>..</c> is rewritten by URL normalization before it ever reaches here.
    ///
    /// Because the key runs to the end, a sub-resource can only be told apart by a name no key ends
    /// with: the picture lives at <c>/live/asset/{key}/@image</c> (<see cref="AssetImageHandler"/>),
    /// spelled like the <c>@parent</c> pseudo-property on objects.
    /// </summary>
    public class AssetHandler : BaseRemoteControlApiHandler
    {
        /// <summary>Route prefix both this handler and <see cref="AssetImageHandler"/> parse keys out of.</summary>
        internal const string kRoutePrefix = "/live/asset/";

        /// <summary>Trailing pseudo-member that marks the picture instead of the asset's own info.</summary>
        internal const string kImageSuffix = "/@image";

        public AssetHandler(RemoteControlServerCore server)
            : base(server, new RouteRule(kRoutePrefix, RouteMatch.Prefix))
        {
        }

        /// <summary>
        /// Reads the asset key out of a <c>/live/asset/...</c> path. Everything after the prefix is the
        /// key, slashes included, so a leading-slash key (an engine asset path) survives the round trip.
        /// Returns null when there is nothing after the prefix.
        ///
        /// ⚠ Pass the RAW request path (<see cref="BaseRemoteControlApiHandler.GetRawPath"/>), not
        /// <c>Url.AbsolutePath</c>: the latter has already lost a percent-escaped <c>#</c> (and everything
        /// after it) and collapsed doubled slashes, both of which turn one key into a different one.
        /// </summary>
        internal static string ParseKey(string rawPath)
        {
            // Segment 2 onwards, joined back with "/" — the same read /live/object/{id}/{path} does for
            // its property path, so the two routes agree on what "the rest of the URL" means. It also
            // unescapes per segment, which is what turns the raw path back into the client's key.
            var key = PathParser.GetPathSegmentFrom(rawPath, 2);
            return string.IsNullOrEmpty(key) ? null : key;
        }

        // 経路一致もエスケープ済みのパスで行う。'#' を含むキーは AbsolutePath だとそこで切れており、
        // 絵のルート (末尾 @image) に一致しなくなる = 絵の要求が名前で返る。
        protected override string GetMatchPath(HttpListenerRequest request) => GetRawPath(request);

        protected override bool SupportsGet() => true;

        protected override async Task HandleGetRequest(HttpListenerContext context)
        {
            var guid = ParseKey(GetRawPath(context.Request));
            if (string.IsNullOrEmpty(guid))
            {
                await WriteError(context, 400, "Asset key is required.");
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
