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
    /// <c>GET /live/asset/image?id=&lt;key&gt;</c> — the picture counterpart of <see cref="AssetHandler"/>
    /// (name and type) for the same asset: a client that can label an asset can also show it. Any asset
    /// kind is accepted; the id is passed through untouched, so an app-specific reference works as well as
    /// a registry key.
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
            : base(server, new RouteRule("/live/asset/image", RouteMatch.Exact))
        {
        }

        protected override bool SupportsGet() => true;

        protected override async Task HandleGetRequest(HttpListenerContext context)
        {
            var id = context.Request.QueryString["id"];
            if (string.IsNullOrEmpty(id))
            {
                await WriteError(context, 400, "'id' query parameter is required.");
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
