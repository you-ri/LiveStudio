// Copyright (c) You-Ri, 2026

using System.Net;
using System.Threading.Tasks;

using Lilium.RemoteControl.Server;
using Lilium.RemoteControl.RestApi;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Serves a snapshot's camera screenshot as raw PNG bytes.
    ///
    /// <c>GET /api/snapshot/image?name=&lt;snapshotName&gt;</c> resolves
    /// "{projectPath}/Snapshots/&lt;name&gt;.snapshot.png" (written by
    /// <see cref="SnapshotManager.TakeSnapshot"/>) and streams it. The name is validated by
    /// <see cref="SnapshotManager"/> to be a plain file name, so a request can never escape the
    /// snapshot folder. Snapshots without a screenshot return 404.
    /// </summary>
    public class SnapshotImageHandler : BaseRemoteControlApiHandler
    {
        public SnapshotImageHandler(RemoteControlServerCore server)
            : base(server, new RouteRule("/api/snapshot/image", RouteMatch.Exact))
        {
        }

        protected override bool SupportsGet() => true;

        protected override async Task HandleGetRequest(HttpListenerContext context)
        {
            var name = context.Request.QueryString["name"];

            // Resolve on the main thread: the snapshot folder depends on ProjectManager.projectPath
            // (Unity state), while the file read below is plain IO and stays off the main thread.
            var filePath = await ExecuteOnMainThread(() =>
                SnapshotManager._ResolveSnapshotFile(name, SnapshotManager.kThumbnailFileExtension));

            byte[] imageBytes = null;
            if (!string.IsNullOrEmpty(filePath))
            {
                await Task.Run(() =>
                {
                    if (System.IO.File.Exists(filePath))
                        imageBytes = System.IO.File.ReadAllBytes(filePath);
                });
            }

            if (imageBytes != null && imageBytes.Length > 0)
            {
                context.Response.ContentType = "image/png";
                context.Response.StatusCode = 200;
                context.Response.ContentLength64 = imageBytes.Length;
                await context.Response.OutputStream.WriteAsync(imageBytes, 0, imageBytes.Length);
                context.Response.Close();
            }
            else
            {
                await WriteError(context, 404, "Snapshot thumbnail not available");
            }
        }
    }
}
