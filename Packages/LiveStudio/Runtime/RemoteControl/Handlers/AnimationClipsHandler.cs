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
    /// Lists the member names inside an asset pack — the second level of discovery, run only when a user
    /// drills into a specific pack (e.g. expanding it on the project assets page).
    ///
    /// <c>GET /api/animation/clips?id=&lt;assetId&gt;</c> resolves the matching
    /// <see cref="PackBundleAsset"/> in <see cref="ExternalAssetManager"/> and returns
    /// <c>{ "id", "clips": [{ "name", "key" }, ...] }</c>, where <c>key</c> is the ready-to-use
    /// <c>file:&lt;path&gt;#&lt;assetName&gt;</c> reference (built server-side from the pack's
    /// authoritative project-relative path) that the remote app writes to an asset reference slot.
    /// The route and the <c>clips</c> field name predate generic packs and are kept verbatim so an existing
    /// remote app build keeps working; the array now lists every member, whatever its type.
    ///
    /// The pack is opened through the async <see cref="PackBundleLoader"/> (not a blocking
    /// <c>LoadFromFile</c>), so listing a pack does not stall the server; the same call also caches and
    /// registers the members, so selecting one afterwards resolves without re-reading the pack. Unknown ids
    /// or assets that are not packs return 404.
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

            // Resolve the pack asset on the main thread (ExternalAssetManager touches Unity state).
            var pack = await ExecuteOnMainThread<PackBundleAsset>(() =>
            {
                if (string.IsNullOrEmpty(assetId)) return null;
                return ExternalAssetManager.current?.FindAsset(assetId) as PackBundleAsset;
            });

            if (pack == null)
            {
                await WriteError(context, 404, "Asset pack not found.");
                return;
            }

            // The pack's project-relative path is the authoritative base for the member keys; read it on
            // the main thread (touches Unity/project state) so the remote app never has to build paths.
            var relativePath = await ExecuteOnMainThread<string>(() => pack.relativePath);

            // Enumerate member names via the async loader. GetMemberNamesAsync must START on the main thread
            // (Unity AssetBundle API), so post it there and await the returned Task off the main thread.
            string[] names;
            try
            {
                var namesTask = await ExecuteOnMainThread<Task<string[]>>(() => pack.GetMemberNamesAsync());
                names = await namesTask;
            }
            catch (Exception e)
            {
                Debug.LogError($"[LiveStudio] Failed to list pack members for '{assetId}': {e.Message}");
                await WriteError(context, 500, "Failed to list pack members.");
                return;
            }

            var clips = new JArray();
            if (names != null)
            {
                for (int i = 0; i < names.Length; i++)
                {
                    var memberName = names[i];
                    if (string.IsNullOrEmpty(memberName)) continue;
                    clips.Add(new JObject
                    {
                        ["name"] = memberName,
                        ["key"] = ExternalAssetKey.BuildMemberKey(relativePath, memberName),
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
