// Copyright (c) You-Ri, 2026

using System;
using System.Net;
using System.Threading.Tasks;

using UnityEngine;

using Lilium.RemoteControl.Server;
using Lilium.RemoteControl.RestApi;

namespace Lilium.RemoteControl
{
    /// <summary>
    /// Serves an asset's preview image as raw bytes.
    ///
    /// <c>GET /live/asset/&lt;key&gt;/@image</c> — the picture counterpart of <see cref="AssetHandler"/>
    /// (name and type) for the same asset: a client that can label an asset can also show it. Any asset
    /// kind is accepted; the key is passed through untouched, so an app-specific reference works as well as
    /// a registry key.
    ///
    /// The key runs to the end of the URL on the info route, so the picture is told apart by a trailing
    /// pseudo-member rather than a sub-path — <c>@image</c>, spelled like the <c>@parent</c> pseudo-property
    /// on objects. A real key would have to end in a segment that is literally <c>@image</c> to collide.
    ///
    /// ⚠ This handler must be registered BEFORE <see cref="AssetHandler"/>: that one claims the whole
    /// <c>/live/asset/</c> prefix, and the server takes the first handler that accepts the path. Same
    /// ordering requirement as reset-before-append in <see cref="LiveObjectHandler"/>.
    ///
    /// Where the picture comes from is the host app's business, reached through
    /// <see cref="AssetRegistry.ResolveThumbnailAsync"/> (LiveStudio, for instance, returns a VRM's embedded
    /// thumbnail or a bundle's packed one from its disk cache). Without a registered provider — or for an
    /// asset that simply has no picture — the answer is 404, which clients already treat as "fall back to a
    /// placeholder".
    /// </summary>
    public class AssetImageHandler : BaseRemoteControlApiHandler
    {
        public AssetImageHandler(RemoteControlServerCore server)
            : base(server, new RouteRule("/live/asset/*" + AssetHandler.kImageSuffix, RouteMatch.Wildcard))
        {
        }

        // Same reason as AssetHandler: match on the escaped path, or a key containing '#' never reaches
        // this route (the normalized path has been cut at the '#', so the @image suffix is gone).
        protected override string GetMatchPath(HttpListenerRequest request) => GetRawPath(request);

        protected override bool SupportsGet() => true;

        protected override async Task HandleGetRequest(HttpListenerContext context)
        {
            // The key is the tail minus the pseudo-member; AssetHandler.ParseKey keeps its inner slashes.
            var tail = AssetHandler.ParseKey(GetRawPath(context.Request));
            var id = tail != null && tail.EndsWith(AssetHandler.kImageSuffix, StringComparison.Ordinal)
                ? tail.Substring(0, tail.Length - AssetHandler.kImageSuffix.Length)
                : null;
            if (string.IsNullOrEmpty(id))
            {
                await WriteError(context, 400, "Asset key is required.");
                return;
            }

            AssetRegistry.Thumbnail thumbnail;
            try
            {
                // The provider looks the asset up through Unity state, so it must START on the main thread;
                // post it there and await the returned Task off it (cache lookups and file reads happen
                // behind it) so the server does not stall. Same rule as AssetsHandler.
                var resolveTask = await ExecuteOnMainThread<Task<AssetRegistry.Thumbnail>>(
                    () => AssetRegistry.ResolveThumbnailAsync(id));
                thumbnail = await resolveTask;
            }
            catch (Exception e)
            {
                Debug.LogError($"[RemoteControl] Failed to resolve thumbnail for asset '{id}': {e.Message}");
                await WriteError(context, 500, "Failed to resolve asset thumbnail.");
                return;
            }

            if (!thumbnail.isValid)
            {
                await WriteError(context, 404, "Asset thumbnail not available");
                return;
            }

            context.Response.ContentType = string.IsNullOrEmpty(thumbnail.mimeType) ? "image/png" : thumbnail.mimeType;
            context.Response.StatusCode = 200;
            context.Response.ContentLength64 = thumbnail.bytes.Length;
            await context.Response.OutputStream.WriteAsync(thumbnail.bytes, 0, thumbnail.bytes.Length);
            context.Response.Close();
        }
    }
}
