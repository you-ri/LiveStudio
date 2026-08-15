// Copyright (c) You-Ri, 2026

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;

using UnityEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Lilium.RemoteControl.Server;
using Lilium.RemoteControl.RestApi;

namespace Lilium.RemoteControl
{
    /// <summary>
    /// Lists the assets that can be written to an asset reference slot. The plural counterpart of
    /// <see cref="AssetHandler"/>: that one resolves a key a client already holds, this one is how it
    /// discovers which keys exist. Both read <see cref="AssetRegistry"/>, so both belong here — the
    /// registry is this package's, even though the key schemes in it are not.
    ///
    /// Two filter axes that combine freely; the response envelope is the same either way, so a client
    /// never has to know which axis it used.
    ///
    /// <c>GET /live/assets?type=&lt;TypeName&gt;</c> — a flat catalog of every registered asset of that
    /// type. Assets that are only registered once something opens the file holding them would be missing
    /// from it, so <see cref="AssetRegistry.PrewarmCatalogAsync"/> runs first and gives the owning layer a
    /// chance to populate (in LiveStudio that means opening every asset pack once, cached).
    ///
    /// <c>GET /live/assets?group=&lt;key&gt;</c> — the members of one group: a file or bundle holding
    /// several assets (a LiveStudio asset pack, say). The second level of discovery, run when a user drills
    /// into one group, so it never pays the catalog-wide prewarm. Expanding also registers the members, so
    /// selecting one afterwards resolves without reopening. A key no expander recognizes is a 404 — unlike
    /// an unmatched <c>type</c>, which is simply an empty catalog.
    ///
    /// The response is <c>{ "type", "assets": [{ "key", "name", "type" }, ...] }</c>, where <c>key</c> is
    /// the value to write to a slot.
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
            var group = context.Request.QueryString["group"];

            JArray assets;
            if (!string.IsNullOrEmpty(group))
            {
                UnityEngine.Object[] members;
                try
                {
                    // The expander opens a file, so it must START on the main thread (Unity APIs); post it
                    // there and await the returned Task off the main thread so the server does not stall.
                    var membersTask = await ExecuteOnMainThread<Task<UnityEngine.Object[]>>(
                        () => AssetRegistry.ExpandGroupAsync(group));
                    members = await membersTask;
                }
                catch (Exception e)
                {
                    Debug.LogError($"[RemoteControl] Failed to expand asset group '{group}': {e.Message}");
                    await WriteError(context, 500, "Failed to list group members.");
                    return;
                }

                if (members == null)
                {
                    await WriteError(context, 404, "Asset group not found.");
                    return;
                }

                assets = await ExecuteOnMainThread<JArray>(() => _CollectGroupMembers(members, type));
            }
            else
            {
                // Same threading rule as the expander above.
                var prewarmTask = await ExecuteOnMainThread<Task>(() => AssetRegistry.PrewarmCatalogAsync());
                await prewarmTask;

                // Snapshot the registry (touches UnityEngine.Object) on the main thread and build the list.
                assets = await ExecuteOnMainThread<JArray>(() =>
                {
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

        // Builds the listing for one group's members. Expanding the group already registered each under its
        // own key, so the keys are read back from the registry rather than rebuilt here — the registry stays
        // the single source of truth (and this package does not know the key grammar anyway), and the
        // listing is byte-identical to the type-filtered one for the same member. Main thread only.
        static JArray _CollectGroupMembers(UnityEngine.Object[] members, string typeName)
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
    }
}
