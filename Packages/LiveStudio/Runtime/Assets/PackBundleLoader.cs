// Copyright (c) You-Ri, 2026

using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using UnityEngine;

using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Loads the assets packed in a <c>*.pack.lsb</c> AssetBundle at runtime and registers each member in
    /// <see cref="AssetRegistry"/> under its <c>file:&lt;relative-path&gt;#&lt;assetName&gt;</c> key (see
    /// <see cref="ExternalAssetKey"/>), so an <see cref="AssetSelectorAttribute"/> reference (e.g. the
    /// avatar body-override slot) can resolve the member back where AssetDatabase is unavailable.
    ///
    /// Members are loaded untyped: a pack encodes no payload kind, so whatever it holds is registered under
    /// its name and the runtime type of each object decides which selectors offer it (see
    /// <c>AssetRegistry.CollectAssets</c>, which filters by type name). Adding a new selectable asset type
    /// therefore needs no change here.
    ///
    /// Members are cached per pack file for the session: the first load reads the pack, caches the members,
    /// and frees the container with <c>Unload(false)</c> (which keeps the loaded assets alive); later
    /// requests return the cached members without re-reading. Per the asset lifetime policy the members are
    /// held until the live scene / project is switched, when <see cref="Clear"/> drops the cache (the
    /// members, being unreferenced managed assets, are then reclaimed by Unity). Keeping a session's worth
    /// resident avoids re-reading a pack every time the selection moves between its members.
    ///
    /// The open→read→unload window is serialized through <see cref="BundleLoadGate"/>: like the prop /
    /// avatar bundles, individually-built LiveStudio bundles can share an internal id (CAB-...), so two
    /// must never be open at once.
    /// </summary>
    internal static class PackBundleLoader
    {
        // Cached members per pack file path (absolute). Entries become null Unity objects after the assets
        // are unloaded (leaving play mode), which is treated as a cache miss below.
        static readonly Dictionary<string, UnityEngine.Object[]> _memberCache = new Dictionary<string, UnityEngine.Object[]>();

        // In-flight loads per file path. Concurrent requests for the same file share one load so the pack
        // is never opened twice at once.
        static readonly Dictionary<string, Task<UnityEngine.Object[]>> _inflight = new Dictionary<string, Task<UnityEngine.Object[]>>();

        // Reset the session caches at the start of each play session so stale references from a previous
        // run do not survive when domain reload is disabled.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#endif
        static void _ResetState()
        {
            _memberCache.Clear();
            _inflight.Clear();

            // Teach the generic asset-name resolver (GET /live/asset) how to label an external member key
            // whose pack is not loaded yet: derive the name from the key's member segment. Keeps the file:
            // scheme's semantics in this (LiveStudio) layer, not in RemoteControl core.
            AssetRegistry.SetNameFallback(_DeriveExternalMemberName);

            // Same split for the listing (GET /live/assets). A pack's members are not in the registry until
            // the pack is opened, so the generic listing hands the work back here: the catalog prewarm opens
            // every pack once, and the group expander opens just the one a client drilled into. RemoteControl
            // stays free of the pack concept; it only knows "ask the owner, then read the registry".
            AssetRegistry.SetCatalogPrewarm(_PrewarmCatalogAsync);
            AssetRegistry.SetGroupExpander(_ExpandPackAsync);
        }

        // Opens every asset pack in the current catalog once (cached), so their members are registered and
        // appear in a type-filtered listing. A pack's name says nothing about what it holds, so there is no
        // requested type for which this can be skipped.
        static Task _PrewarmCatalogAsync()
        {
            // App-embedded built-in assets (Resources catalog) list alongside baked and external ones.
            // Idempotent — a no-op once already registered (e.g. by play start).
            BuiltinAssetRegistry.EnsureRegistered();

            var manager = ExternalAssetManager.current;
            if (manager == null) return Task.CompletedTask;

            // Start every open in this one pass, on the caller's thread. GetMembersAsync must be *started*
            // on the main thread (Unity AssetBundle API) and the caller guarantees we are on it; doing the
            // starts up front means we never depend on which thread an await resumes on. The actual
            // open→read→unload windows are still serialized behind BundleLoadGate.
            var view = manager.assetsView;
            List<Task> opens = null;
            for (int i = 0; i < view.Count; i++)
            {
                if (!(view[i] is PackBundleAsset pack)) continue;
                if (opens == null) opens = new List<Task>(view.Count);
                // A pack that fails to open is logged by the loader and skipped here, so one bad pack does
                // not fail the whole listing.
                opens.Add(pack.GetMembersAsync().ContinueWith(
                    _ => { }, TaskContinuationOptions.ExecuteSynchronously));
            }

            return opens == null ? Task.CompletedTask : Task.WhenAll(opens);
        }

        // Opens one pack by its asset id and returns its members. Null means "not ours" — an unknown id, or
        // an asset that is not a pack — which the generic listing reports as 404.
        static Task<UnityEngine.Object[]> _ExpandPackAsync(string groupKey)
        {
            var pack = ExternalAssetManager.current?.FindAsset(groupKey) as PackBundleAsset;
            if (pack == null) return Task.FromResult<UnityEngine.Object[]>(null);
            return pack.GetMembersAsync();
        }

        // The display name for a file: member key is its member segment; other keys are not ours (null).
        static string _DeriveExternalMemberName(string key)
            => ExternalAssetKey.TryParseMemberKey(key, out _, out var memberName) ? memberName : null;

        /// <summary>
        /// Drops the session member cache. Called when the live scene or project is switched, so the next
        /// scene starts with no resident pack members. The unreferenced members are reclaimed by Unity's
        /// asset cleanup; stale <see cref="AssetRegistry"/> entries pointing at them resolve to a null Unity
        /// object and are treated as absent (self-healing on the next load).
        /// </summary>
        public static void Clear()
        {
            _memberCache.Clear();
            _inflight.Clear();
        }

        /// <summary>
        /// Resolves an external member key (<c>file:&lt;relative-path&gt;#&lt;assetName&gt;</c>) to its
        /// asset, loading the pack if needed. Returns null when the key is not a file key, the pack cannot
        /// be resolved / read, it holds no member of that name, or the member is not a
        /// <typeparamref name="T"/>.
        /// </summary>
        public static async Task<T> ResolveAsync<T>(string fileKey) where T : UnityEngine.Object
        {
            if (!ExternalAssetKey.TryParseMemberKey(fileKey, out var relativePath, out _)) return null;

            var absolutePath = PropPreset.ResolveSource(relativePath, ProjectManager.projectPath);
            await LoadMembersAsync(absolutePath, relativePath);

            // Registration used this same key, so the registry is the single resolution point.
            return AssetRegistry.TryFind(fileKey, out var asset) ? asset as T : null;
        }

        /// <summary>
        /// Loads (and caches) every asset in the pack at <paramref name="absolutePath"/>, registering each
        /// under its <c>file:&lt;relativePath&gt;#&lt;assetName&gt;</c> key. Returns the members (empty on
        /// failure). <paramref name="relativePath"/> is the project-relative pack path used to build the
        /// keys, so registration matches the keys stored in the live scene.
        /// </summary>
        public static Task<UnityEngine.Object[]> LoadMembersAsync(string absolutePath, string relativePath)
        {
            if (string.IsNullOrEmpty(absolutePath)) return Task.FromResult(System.Array.Empty<UnityEngine.Object>());

            if (_memberCache.TryGetValue(absolutePath, out var cached) && _IsAlive(cached))
            {
                _RegisterMembers(cached, relativePath);
                return Task.FromResult(cached);
            }
            if (_inflight.TryGetValue(absolutePath, out var pending)) return pending;

            var task = _LoadMembersAsync(absolutePath, relativePath);
            _inflight[absolutePath] = task;
            return task;
        }

        static async Task<UnityEngine.Object[]> _LoadMembersAsync(string absolutePath, string relativePath)
        {
            try
            {
                if (!File.Exists(absolutePath))
                {
                    Debug.LogError($"[LiveStudio] Asset pack not found: {absolutePath}");
                    return System.Array.Empty<UnityEngine.Object>();
                }

                var members = await BundleLoadGate.RunExclusiveAsync(async () =>
                {
                    var bundleRequest = AssetBundle.LoadFromFileAsync(absolutePath);
                    await _AwaitOperation(bundleRequest);

                    var bundle = bundleRequest.assetBundle;
                    if (bundle == null)
                    {
                        Debug.LogError($"[LiveStudio] Failed to load asset pack: {absolutePath}");
                        return System.Array.Empty<UnityEngine.Object>();
                    }

                    try
                    {
                        // Untyped: a pack declares no payload kind, so load whatever it holds and let the
                        // runtime type of each member decide where it is selectable.
                        var assetRequest = bundle.LoadAllAssetsAsync();
                        await _AwaitOperation(assetRequest);

                        var all = assetRequest.allAssets;
                        if (all == null || all.Length == 0)
                        {
                            Debug.LogError($"[LiveStudio] Asset pack is empty: {absolutePath}");
                            return System.Array.Empty<UnityEngine.Object>();
                        }

                        var result = new UnityEngine.Object[all.Length];
                        for (int i = 0; i < all.Length; i++) result[i] = all[i];
                        return result;
                    }
                    finally
                    {
                        // false = keep the loaded members alive, free only the bundle container so the same
                        // file can be reloaded (and colliding CABs never overlap).
                        bundle.Unload(false);
                    }
                });

                _memberCache[absolutePath] = members;
                _RegisterMembers(members, relativePath);
                return members;
            }
            finally
            {
                _inflight.Remove(absolutePath);
            }
        }

        // Registers each member under its file key so AssetRegistry resolves the selector reference. Idempotent.
        static void _RegisterMembers(UnityEngine.Object[] members, string relativePath)
        {
            if (members == null || string.IsNullOrEmpty(relativePath)) return;
            for (int i = 0; i < members.Length; i++)
            {
                var member = members[i];
                if (member == null) continue;
                var key = ExternalAssetKey.BuildMemberKey(relativePath, member.name);
                if (!string.IsNullOrEmpty(key)) AssetRegistry.Register(key, member);
            }
        }

        // True when the cached array still holds live Unity objects (survives leaving play mode nulling them).
        static bool _IsAlive(UnityEngine.Object[] members)
        {
            if (members == null || members.Length == 0) return false;
            for (int i = 0; i < members.Length; i++)
            {
                if (members[i] == null) return false;
            }
            return true;
        }

        static Task _AwaitOperation(AsyncOperation operation)
        {
            if (operation.isDone) return Task.CompletedTask;

            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            operation.completed += _ => tcs.TrySetResult(true);
            return tcs.Task;
        }
    }
}
