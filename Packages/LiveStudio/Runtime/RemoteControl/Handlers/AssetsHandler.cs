// Copyright (c) You-Ri, 2026

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;

using UnityEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Lilium.RemoteControl;
using Lilium.RemoteControl.Server;
using Lilium.RemoteControl.RestApi;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Lists the selectable assets of a given type as one flat catalog for an ExternalAssetRef control.
    ///
    /// <c>GET /api/assets?type=&lt;TypeName&gt;</c> returns
    /// <c>{ "type", "assets": [{ "key", "name", "type" }, ...] }</c>, where <c>key</c> is the unified
    /// asset reference — a bare GUID for a baked/in-app asset, or a <c>file:&lt;path&gt;#&lt;clip&gt;</c>
    /// reference for a clip inside an external <c>*.anim.lsb</c> bundle. The list is drawn from
    /// <see cref="AssetRegistry"/>, so baked and external assets are unified behind one endpoint.
    ///
    /// AnimationClips live inside bundles that are not opened until needed, so for that type every
    /// animation bundle in the current project is opened once (cached, via <see cref="AnimationBundleLoader"/>)
    /// first, registering its clips so they appear. Other types read straight from the registry.
    /// </summary>
    public class AssetsHandler : BaseRemoteControlApiHandler
    {
        const string kAnimationClipType = "AnimationClip";

        public AssetsHandler(RemoteControlServerCore server)
            : base(server, new RouteRule("/api/assets", RouteMatch.Exact))
        {
        }

        protected override bool SupportsGet() => true;

        protected override async Task HandleGetRequest(HttpListenerContext context)
        {
            var type = context.Request.QueryString["type"];

            // AnimationClips only enter the registry once their bundle is opened; open every animation
            // bundle in the catalog once so its clips are registered and listed. Skipped for other types.
            if (string.IsNullOrEmpty(type) ||
                string.Equals(type, kAnimationClipType, StringComparison.OrdinalIgnoreCase))
            {
                await _PrewarmAnimationBundles();
            }

            // Snapshot the registry (touches UnityEngine.Object) on the main thread and build the list.
            var assets = await ExecuteOnMainThread<JArray>(() =>
            {
                // Register app-embedded built-in assets (Resources catalog) so they list alongside baked
                // and external assets. Idempotent — a no-op once already registered (e.g. by play start).
                BuiltinAssetRegistry.EnsureRegistered();

                var list = new List<KeyValuePair<string, UnityEngine.Object>>();
                AssetRegistry.CollectAssets(type, list);
                var arr = new JArray();
                for (int i = 0; i < list.Count; i++)
                {
                    var asset = list[i].Value;
                    if (asset == null) continue;
                    arr.Add(new JObject
                    {
                        ["key"] = list[i].Key,
                        ["name"] = asset.name,
                        ["type"] = asset.GetType().Name,
                    });
                }
                return arr;
            });

            var info = new JObject
            {
                ["type"] = type ?? string.Empty,
                ["assets"] = assets,
            };

            context.Response.StatusCode = 200;
            await WriteResponse(context.Response, info.ToString(Formatting.None));
        }

        // Opens every animation bundle in the current catalog once (cached), so LoadClipsAsync registers
        // its clips in AssetRegistry and they appear in the listing. A per-bundle open failure is logged by
        // the loader and skipped, so one bad bundle does not fail the whole listing.
        async Task _PrewarmAnimationBundles()
        {
            var bundles = await ExecuteOnMainThread<List<AnimationBundleAsset>>(() =>
            {
                var result = new List<AnimationBundleAsset>();
                var mgr = ExternalAssetManager.current;
                if (mgr != null)
                {
                    var view = mgr.assetsView;
                    for (int i = 0; i < view.Count; i++)
                    {
                        if (view[i] is AnimationBundleAsset ab) result.Add(ab);
                    }
                }
                return result;
            });

            for (int i = 0; i < bundles.Count; i++)
            {
                // GetClipNamesAsync must START on the main thread (Unity AssetBundle API); post it there and
                // await the returned Task off the main thread so the server does not stall.
                var namesTask = await ExecuteOnMainThread<Task<string[]>>(() => bundles[i].GetClipNamesAsync());
                try { await namesTask; }
                catch { /* the loader logs bundle-open failures; skip this bundle */ }
            }
        }
    }
}
