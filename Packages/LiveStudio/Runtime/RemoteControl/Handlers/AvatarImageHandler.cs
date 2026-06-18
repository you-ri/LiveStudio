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
    /// in <see cref="ExternalAssetManager"/> and, for VRM (.vrm / .glb) files, returns the embedded
    /// VRM thumbnail extracted straight from the file via <see cref="GlbThumbnailExtractor"/>.
    /// Because it reads the file (not the loaded instance), previews are available for every
    /// registered VRM avatar, not just the currently active one. Non-VRM assets (AssetBundle
    /// .avatar.lsb / .lsavatar) and avatars without a thumbnail return 404.
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
            string filePath = await ExecuteOnMainThread<string>(() =>
            {
                if (string.IsNullOrEmpty(assetId)) return null;
                var manager = ExternalAssetManager.current;
                if (manager == null) return null;
                return manager.FindAsset(assetId) is AvatarAsset avatar ? avatar.filePath : null;
            });

            if (string.IsNullOrEmpty(filePath) || !IsVrmFile(filePath))
            {
                await WriteError(context, 404, "Avatar thumbnail not available");
                return;
            }

            // File read + glb parse is pure CPU/IO; keep it off the main thread.
            byte[] imageBytes = null;
            string mimeType = null;
            await Task.Run(() =>
            {
                if (GlbThumbnailExtractor.TryExtract(filePath, out var bytes, out var mime))
                {
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
