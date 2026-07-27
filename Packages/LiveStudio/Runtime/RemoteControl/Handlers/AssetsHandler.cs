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
    /// asset reference — a bare GUID for a baked/in-app asset, or a <c>file:&lt;path&gt;#&lt;name&gt;</c>
    /// reference for a member of an external <c>*.pack.lsb</c> pack. The list is drawn from
    /// <see cref="AssetRegistry"/>, so baked and external assets are unified behind one endpoint.
    ///
    /// Pack members live inside files that are not opened until needed, and a pack declares no payload kind
    /// in its name, so which packs could hold the requested type is unknowable without opening them: every
    /// pack in the current project is opened once (cached, via <see cref="PackBundleLoader"/>) before the
    /// registry is read, registering its members so they appear. The cache makes that a once-per-session
    /// cost regardless of how many types are queried.
    /// </summary>
    public class AssetsHandler : BaseRemoteControlApiHandler
    {
        public AssetsHandler(RemoteControlServerCore server)
            : base(server, new RouteRule("/api/assets", RouteMatch.Exact))
        {
        }

        protected override bool SupportsGet() => true;

        protected override async Task HandleGetRequest(HttpListenerContext context)
        {
            var type = context.Request.QueryString["type"];

            // Pack members only enter the registry once their pack is opened, and a pack's name says nothing
            // about what it holds — so there is no type for which this can be skipped. Open every pack in
            // the catalog once (cached) so its members are registered and listed.
            await _PrewarmPackBundles();

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

        // Opens every asset pack in the current catalog once (cached), so LoadMembersAsync registers its
        // members in AssetRegistry and they appear in the listing. A per-pack open failure is logged by the
        // loader and skipped, so one bad pack does not fail the whole listing.
        async Task _PrewarmPackBundles()
        {
            var packs = await ExecuteOnMainThread<List<PackBundleAsset>>(() =>
            {
                var result = new List<PackBundleAsset>();
                var mgr = ExternalAssetManager.current;
                if (mgr != null)
                {
                    var view = mgr.assetsView;
                    for (int i = 0; i < view.Count; i++)
                    {
                        if (view[i] is PackBundleAsset pack) result.Add(pack);
                    }
                }
                return result;
            });

            for (int i = 0; i < packs.Count; i++)
            {
                // GetMemberNamesAsync must START on the main thread (Unity AssetBundle API); post it there
                // and await the returned Task off the main thread so the server does not stall.
                var namesTask = await ExecuteOnMainThread<Task<string[]>>(() => packs[i].GetMemberNamesAsync());
                try { await namesTask; }
                catch { /* the loader logs pack-open failures; skip this pack */ }
            }
        }
    }
}
