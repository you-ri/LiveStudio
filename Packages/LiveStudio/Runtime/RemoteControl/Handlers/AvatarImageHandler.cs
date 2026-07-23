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

            // Resolve the ThumbnailCache key on the main thread (ExternalAssetManager touches Unity state).
            // For a file-backed asset (avatar / prop) the key is its file path — the file extension below then
            // decides how the thumbnail is obtained. A built-in asset has no project file: its preview is
            // pre-warmed at startup under a synthetic GUID key (see BuiltinAssetRegistry), so it resolves to
            // that key instead. Any asset kind is accepted.
            string cacheKey = await ExecuteOnMainThread<string>(() =>
            {
                if (string.IsNullOrEmpty(assetId)) return null;
                var manager = ExternalAssetManager.current;
                if (manager == null) return null;
                // Resolve by reference so both a runtime absolute id (the remote app's session handle) and a
                // persisted project-relative reference (e.g. a deck tile's backgroundAssetId) find the asset.
                var asset = manager.FindAssetByReference(assetId);
                if (asset == null) return null;
                if (!string.IsNullOrEmpty(asset.filePath)) return asset.filePath;
                // Built-in (Resources) asset: key by its stable GUID, matching the startup pre-warm.
                if (asset.isBuiltin) return BuiltinAssetRegistry.ThumbnailCacheKey(asset.persistentId ?? asset.id);
                return null;
            });

            if (string.IsNullOrEmpty(cacheKey))
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
                // Cached (memory or disk) for any asset kind — survives restarts, available for bundles once
                // loaded at least once, and for built-in assets once pre-warmed at startup.
                if (ThumbnailCache.TryGet(cacheKey, out var cachedBytes, out var cachedMime))
                {
                    imageBytes = cachedBytes;
                    mimeType = cachedMime;
                    return;
                }

                // VRM/glb: extract the embedded thumbnail from the file and cache it for next time (the key is
                // the file path here). Bundle thumbnails cannot be read here (opening a bundle in a request
                // handler would stall the server); they are cached by the loaders when the asset is loaded.
                // A built-in key is synthetic (not a VRM path), so this branch is skipped for it.
                if (IsVrmFile(cacheKey)
                    && GlbThumbnailExtractor.TryExtract(cacheKey, out var bytes, out var mime))
                {
                    ThumbnailCache.Store(cacheKey, bytes, mime);
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
