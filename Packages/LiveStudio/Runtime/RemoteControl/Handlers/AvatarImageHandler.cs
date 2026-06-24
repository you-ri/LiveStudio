// Copyright (c) You-Ri, 2026

using System;
using System.Net;
using System.Threading.Tasks;

using Lilium.RemoteControl.Server;
using Lilium.RemoteControl.RestApi;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Serves an avatar's (or prop's) preview thumbnail as raw image bytes.
    ///
    /// <c>GET /api/avatar/image?id=&lt;assetId&gt;</c> looks up the matching asset in
    /// <see cref="ExternalAssetManager"/> and returns its thumbnail via <see cref="ThumbnailCache"/>:
    /// <list type="bullet">
    ///   <item>VRM (.vrm / .glb): the embedded VRM thumbnail, extracted from the file via
    ///   <see cref="GlbThumbnailExtractor"/> on the first request and then cached.</item>
    ///   <item>Avatar/prop bundle (.avatar.lsb / .prop.lsb / .lsavatar): the <see cref="BundleThumbnail"/>
    ///   packed into the bundle at export time, cached by the loader.</item>
    /// </list>
    /// The cache is backed by disk under the project (<see cref="ProjectPaths"/>), so VRM previews are
    /// available for every registered avatar and bundle previews survive restarts (available once the
    /// bundle has been loaded at least once). Assets without a thumbnail return 404.
    /// </summary>
    public class AvatarImageHandler : BaseRemoteControlApiHandler
    {
        public AvatarImageHandler(RemoteControlServerCore server)
            : base(server, new RouteRule("/api/avatar/image", RouteMatch.Exact))
        {
        }

        protected override bool SupportsGet() => true;

        protected override async Task HandleGetRequest(HttpListenerContext context)
        {
            var assetId = context.Request.QueryString["id"];

            // Resolve the asset's file path on the main thread (ExternalAssetManager touches Unity state).
            // Any asset kind is accepted (avatar / prop); the file extension below decides how the
            // thumbnail is obtained.
            string filePath = await ExecuteOnMainThread<string>(() =>
            {
                if (string.IsNullOrEmpty(assetId)) return null;
                var manager = ExternalAssetManager.current;
                if (manager == null) return null;
                return manager.FindAsset(assetId)?.filePath;
            });

            if (string.IsNullOrEmpty(filePath))
            {
                await WriteError(context, 404, "Avatar thumbnail not available");
                return;
            }

            byte[] imageBytes = null;
            string mimeType = null;

            // Resolve the thumbnail off the main thread (cache lookup, disk IO and VRM extraction are
            // pure CPU/IO; ThumbnailCache is thread-safe).
            await Task.Run(() =>
            {
                // Cached (memory or disk) for any asset kind — survives restarts and is available for
                // bundles once they have been loaded at least once.
                if (ThumbnailCache.TryGet(filePath, out var cachedBytes, out var cachedMime))
                {
                    imageBytes = cachedBytes;
                    mimeType = cachedMime;
                    return;
                }

                // VRM/glb: extract the embedded thumbnail from the file and cache it for next time.
                // Bundle thumbnails cannot be read here (opening a bundle in a request handler would
                // stall the server); they are cached by the loaders when the asset is loaded.
                if (IsVrmFile(filePath)
                    && GlbThumbnailExtractor.TryExtract(filePath, out var bytes, out var mime))
                {
                    ThumbnailCache.Store(filePath, bytes, mime);
                    imageBytes = bytes;
                    mimeType = mime;
                }
            });

            if (imageBytes != null && imageBytes.Length > 0)
            {
                context.Response.ContentType = string.IsNullOrEmpty(mimeType) ? "image/png" : mimeType;
                context.Response.StatusCode = 200;
                context.Response.ContentLength64 = imageBytes.Length;
                await context.Response.OutputStream.WriteAsync(imageBytes, 0, imageBytes.Length);
                context.Response.Close();
            }
            else
            {
                await WriteError(context, 404, "Avatar thumbnail not available");
            }
        }

        private static bool IsVrmFile(string filePath)
        {
            return filePath.EndsWith(".vrm", StringComparison.OrdinalIgnoreCase)
                || filePath.EndsWith(".glb", StringComparison.OrdinalIgnoreCase);
        }
    }
}
