// Copyright (c) You-Ri, 2026

using System;
using System.Net;
using System.Threading.Tasks;

using UnityEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Lilium.RemoteControl.Server;
using Lilium.RemoteControl.RestApi;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Lists the clip names inside an animation bundle — the second level of the two-level animation
    /// selector, run only when a user drills into a specific bundle.
    ///
    /// <c>GET /api/animation/clips?id=&lt;assetId&gt;</c> resolves the matching
    /// <see cref="AnimationBundleAsset"/> in <see cref="ExternalAssetManager"/> and returns
    /// <c>{ "id", "clips": [{ "name", "key" }, ...] }</c>, where <c>key</c> is the ready-to-use
    /// <c>file:&lt;path&gt;#&lt;clipName&gt;</c> reference (built server-side from the bundle's
    /// authoritative project-relative path) that the remote app writes to the avatar body-override slot.
    ///
    /// The bundle is opened through the async <see cref="AnimationBundleLoader"/> (not a blocking
    /// <c>LoadFromFile</c>), so listing a bundle does not stall the server; the same call also caches and
    /// registers the clips, so selecting one afterwards resolves without re-reading the bundle. Unknown
    /// ids or non-animation assets return 404.
    /// </summary>
    public class AnimationClipsHandler : BaseRemoteControlApiHandler
    {
        public AnimationClipsHandler(RemoteControlServerCore server)
            : base(server, new RouteRule("/api/animation/clips", RouteMatch.Exact))
        {
        }

        protected override bool SupportsGet() => true;

        protected override async Task HandleGetRequest(HttpListenerContext context)
        {
            var assetId = context.Request.QueryString["id"];

            // Resolve the bundle asset on the main thread (ExternalAssetManager touches Unity state).
            var bundle = await ExecuteOnMainThread<AnimationBundleAsset>(() =>
            {
                if (string.IsNullOrEmpty(assetId)) return null;
                return ExternalAssetManager.current?.FindAsset(assetId) as AnimationBundleAsset;
            });

            if (bundle == null)
            {
                await WriteError(context, 404, "Animation bundle not found.");
                return;
            }

            // The bundle's project-relative path is the authoritative base for the clip keys; read it on
            // the main thread (touches Unity/project state) so the remote app never has to build paths.
            var relativePath = await ExecuteOnMainThread<string>(() => bundle.relativePath);

            // Enumerate clip names via the async loader. GetClipNamesAsync must START on the main thread
            // (Unity AssetBundle API), so post it there and await the returned Task off the main thread.
            string[] names;
            try
            {
                var namesTask = await ExecuteOnMainThread<Task<string[]>>(() => bundle.GetClipNamesAsync());
                names = await namesTask;
            }
            catch (Exception e)
            {
                Debug.LogError($"[LiveStudio] Failed to list animation clips for '{assetId}': {e.Message}");
                await WriteError(context, 500, "Failed to list animation clips.");
                return;
            }

            var clips = new JArray();
            if (names != null)
            {
                for (int i = 0; i < names.Length; i++)
                {
                    var clipName = names[i];
                    if (string.IsNullOrEmpty(clipName)) continue;
                    clips.Add(new JObject
                    {
                        ["name"] = clipName,
                        ["key"] = ExternalAssetKey.BuildClipKey(relativePath, clipName),
                    });
                }
            }

            var info = new JObject
            {
                ["id"] = assetId,
                ["clips"] = clips,
            };

            context.Response.StatusCode = 200;
            await WriteResponse(context.Response, info.ToString(Formatting.None));
        }
    }
}
