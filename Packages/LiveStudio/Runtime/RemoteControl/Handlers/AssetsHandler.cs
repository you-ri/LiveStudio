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
    /// Lists the assets that can be written to an asset reference slot. One endpoint, two filter axes that
    /// combine freely — the response envelope is the same either way, so a client never has to know which
    /// axis it used.
    ///
    /// <c>GET /live/assets?type=&lt;TypeName&gt;</c> — a flat catalog of every asset of that type, drawn
    /// from <see cref="AssetRegistry"/> so baked, built-in and external assets are unified. Pack members
    /// live inside files that are not opened until needed, and a pack declares no payload kind in its name,
    /// so which packs could hold the requested type is unknowable without opening them: every pack in the
    /// current project is opened once (cached, via <see cref="PackBundleLoader"/>) before the registry is
    /// read. The cache makes that a once-per-session cost regardless of how many types are queried.
    ///
    /// <c>GET /live/assets?pack=&lt;assetId&gt;</c> — the members of one specific pack: the second level of
    /// discovery, run only when a user drills into a pack (e.g. expanding its row on the project assets
    /// page). Only that pack is opened, so drilling in never pays the catalog-wide prewarm; listing it also
    /// registers its members, so selecting one afterwards resolves without re-reading the pack. An unknown
    /// id, or an id that is not a pack, is a 404 — unlike an unmatched <c>type</c>, which is simply an
    /// empty catalog.
    ///
    /// The response is <c>{ "type", "assets": [{ "key", "name", "type" }, ...] }</c>, where <c>key</c> is
    /// the unified asset reference — a bare GUID for a baked/in-app asset, or a
    /// <c>file:&lt;path&gt;#&lt;name&gt;</c> reference for a pack member — ready to write to a slot.
    /// </summary>
    public class AssetsHandler : BaseRemoteControlApiHandler
    {
        public AssetsHandler(RemoteControlServerCore server)
            : base(server, new RouteRule("/live/assets", RouteMatch.Exact))
        {
        }

        protected override bool SupportsGet() => true;

        protected override async Task HandleGetRequest(HttpListenerContext context)
        {
            var type = context.Request.QueryString["type"];
            var packId = context.Request.QueryString["pack"];

            JArray assets;
            if (!string.IsNullOrEmpty(packId))
            {
                // Resolve the pack on the main thread (ExternalAssetManager touches Unity state).
                var pack = await ExecuteOnMainThread<PackBundleAsset>(
                    () => ExternalAssetManager.current?.FindAsset(packId) as PackBundleAsset);

                if (pack == null)
                {
                    await WriteError(context, 404, "Asset pack not found.");
                    return;
                }

                UnityEngine.Object[] members;
                try
                {
                    // GetMembersAsync must START on the main thread (Unity AssetBundle API); post it there
                    // and await the returned Task off the main thread so the server does not stall.
                    var membersTask = await ExecuteOnMainThread<Task<UnityEngine.Object[]>>(
                        () => pack.GetMembersAsync());
                    members = await membersTask;
                }
                catch (Exception e)
                {
                    Debug.LogError($"[LiveStudio] Failed to list pack members for '{packId}': {e.Message}");
                    await WriteError(context, 500, "Failed to list pack members.");
                    return;
                }

                assets = await ExecuteOnMainThread<JArray>(() => _CollectPackMembers(members, type));
            }
            else
            {
                // Pack members only enter the registry once their pack is opened, and a pack's name says
                // nothing about what it holds — so there is no type for which this can be skipped. Open
                // every pack in the catalog once (cached) so its members are registered and listed.
                await _PrewarmPackBundles();

                // Snapshot the registry (touches UnityEngine.Object) on the main thread and build the list.
                assets = await ExecuteOnMainThread<JArray>(() =>
                {
                    // Register app-embedded built-in assets (Resources catalog) so they list alongside baked
                    // and external assets. Idempotent — a no-op once already registered (e.g. by play start).
                    BuiltinAssetRegistry.EnsureRegistered();

                    var list = new List<KeyValuePair<string, UnityEngine.Object>>();
                    AssetRegistry.CollectAssets(type, list);
                    return _BuildList(list);
                });
            }

            var info = new JObject
            {
                ["type"] = type ?? string.Empty,
                ["assets"] = assets,
            };

            context.Response.StatusCode = 200;
            await WriteResponse(context.Response, info.ToString(Formatting.None));
        }

        // Builds the listing for one pack's members. Loading them already registered each under its file
        // key, so the keys are read back from the registry rather than rebuilt here — the registry stays the
        // single source of truth, and this listing is byte-identical to the type-filtered one for the same
        // member. Main thread only (reads UnityEngine.Object type/state).
        static JArray _CollectPackMembers(UnityEngine.Object[] members, string typeName)
        {
            var list = new List<KeyValuePair<string, UnityEngine.Object>>();
            if (members == null) return _BuildList(list);

            bool all = string.IsNullOrEmpty(typeName);
            for (int i = 0; i < members.Length; i++)
            {
                var member = members[i];
                if (member == null) continue;
                // Same case-insensitive simple-name match AssetRegistry.CollectAssets uses, so ?type=
                // filters identically on both axes.
                if (!all && !string.Equals(member.GetType().Name, typeName, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!AssetRegistry.TryFindGuid(member, out var key)) continue;
                list.Add(new KeyValuePair<string, UnityEngine.Object>(key, member));
            }
            return _BuildList(list);
        }

        // Shared JSON shape for both axes. Main thread only (reads UnityEngine.Object type/state).
        static JArray _BuildList(List<KeyValuePair<string, UnityEngine.Object>> list)
        {
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
                // GetMembersAsync must START on the main thread (Unity AssetBundle API); post it there
                // and await the returned Task off the main thread so the server does not stall.
                var membersTask = await ExecuteOnMainThread<Task<UnityEngine.Object[]>>(
                    () => packs[i].GetMembersAsync());
                try { await membersTask; }
                catch { /* the loader logs pack-open failures; skip this pack */ }
            }
        }
    }
}
