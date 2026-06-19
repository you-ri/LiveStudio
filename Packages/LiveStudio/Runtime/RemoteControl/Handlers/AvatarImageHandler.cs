// Copyright (c) You-Ri, 2026

using System;
using System.Net;
using System.Threading.Tasks;

using Lilium.RemoteControl.Server;
using Lilium.RemoteControl.RestApi;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Serves an avatar's preview thumbnail as raw image bytes.
    ///
    /// <c>GET /api/avatar/image?id=&lt;assetId&gt;</c> looks up the matching <see cref="AvatarAsset"/>
    /// in <see cref="ExternalAssetManager"/> and returns its thumbnail:
    /// <list type="bullet">
    ///   <item>VRM (.vrm / .glb): the embedded VRM thumbnail, extracted from the file via
    ///   <see cref="GlbThumbnailExtractor"/>.</item>
    ///   <item>Avatar bundle (.avatar.lsb / .lsavatar): the <see cref="BundleThumbnail"/> packed into
    ///   the bundle at export time, cached by the loader and served via <see cref="BundleThumbnailCache"/>.</item>
    /// </list>
    /// VRM previews are available for every registered avatar (read from the file). Bundle previews are
    /// available after the avatar has been loaded at least once. Avatars without a thumbnail return 404.
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

            if (IsVrmFile(filePath))
            {
                // VRM: extract the embedded thumbnail from the file. Pure CPU/IO, keep off the main thread.
                await Task.Run(() =>
                {
                    if (GlbThumbnailExtractor.TryExtract(filePath, out var bytes, out var mime))
                    {
                        imageBytes = bytes;
                        mimeType = mime;
                    }
                });
            }
            else if (LiveStudioBundle.IsAvatarBundle(filePath) || LiveStudioBundle.IsPropBundle(filePath))
            {
                // Avatar/prop bundle: serve the BundleThumbnail cached when the bundle was loaded. Available
                // only after the avatar has been loaded at least once (reading the thumbnail re-opens no
                // bundle here, which would stall the server). Dictionary read; main thread for safety.
                var result = await ExecuteOnMainThread<(byte[] bytes, string mime)>(() =>
                    BundleThumbnailCache.TryGet(filePath, out var bytes, out var mime)
                        ? (bytes, mime)
                        : (null, null));
                imageBytes = result.bytes;
                mimeType = result.mime;
            }

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
