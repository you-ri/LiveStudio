// Copyright (c) You-Ri, 2026

using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using UnityEngine;

using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Loads the <see cref="AnimationClip"/> assets packed in a <c>*.anim.lsb</c> AssetBundle at runtime
    /// and registers each clip in <see cref="AssetRegistry"/> under its
    /// <c>file:&lt;relative-path&gt;#&lt;clipName&gt;</c> key (see <see cref="ExternalAssetKey"/>), so an
    /// <see cref="AssetSelectorAttribute"/> reference (e.g. the avatar body-override slot) can resolve the
    /// clip back where AssetDatabase is unavailable.
    ///
    /// Clips are cached per bundle file for the session: the first load reads the bundle, caches the
    /// clips, and frees the container with <c>Unload(false)</c> (which keeps the loaded clips alive);
    /// later requests return the cached clips without re-reading. Per the asset lifetime policy the clips
    /// are held until the live scene / project is switched, when <see cref="Clear"/> drops the cache (the
    /// clips, being unreferenced managed assets, are then reclaimed by Unity). AnimationClips carry no GPU
    /// resources, so keeping a session's worth resident is cheap and avoids re-reading a bundle every time
    /// the selection moves between clips in the same pack.
    ///
    /// The open→read→unload window is serialized through <see cref="BundleLoadGate"/>: like the prop /
    /// avatar bundles, individually-built LiveStudio bundles can share an internal id (CAB-...), so two
    /// must never be open at once.
    /// </summary>
    internal static class AnimationBundleLoader
    {
        // Cached clips per bundle file path (absolute). Entries become null Unity objects after the assets
        // are unloaded (leaving play mode), which is treated as a cache miss below.
        static readonly Dictionary<string, AnimationClip[]> _clipCache = new Dictionary<string, AnimationClip[]>();

        // In-flight loads per file path. Concurrent requests for the same file share one load so the
        // bundle is never opened twice at once.
        static readonly Dictionary<string, Task<AnimationClip[]>> _inflight = new Dictionary<string, Task<AnimationClip[]>>();

        // Reset the session caches at the start of each play session so stale references from a previous
        // run do not survive when domain reload is disabled.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#endif
        static void _ResetState()
        {
            _clipCache.Clear();
            _inflight.Clear();

            // Teach the generic asset-name resolver (GET /api/asset) how to label an external clip key
            // whose bundle is not loaded yet: derive the name from the key's clip segment. Keeps the file:
            // scheme's semantics in this (LiveStudio) layer, not in RemoteControl core.
            AssetRegistry.SetNameFallback(_DeriveExternalClipName);
        }

        // The display name for a file: clip key is its clip segment; other keys are not ours (null).
        static string _DeriveExternalClipName(string key)
            => ExternalAssetKey.TryParseClipKey(key, out _, out var clipName) ? clipName : null;

        /// <summary>
        /// Drops the session clip cache. Called when the live scene or project is switched, so the next
        /// scene starts with no resident clips. The unreferenced clips are reclaimed by Unity's asset
        /// cleanup; stale <see cref="AssetRegistry"/> entries pointing at them resolve to a null Unity
        /// object and are treated as absent (self-healing on the next load).
        /// </summary>
        public static void Clear()
        {
            _clipCache.Clear();
            _inflight.Clear();
        }

        /// <summary>
        /// Resolves an external clip key (<c>file:&lt;relative-path&gt;#&lt;clipName&gt;</c>) to its
        /// <see cref="AnimationClip"/>, loading the bundle if needed. Returns null when the key is not a
        /// file key, the bundle cannot be resolved / read, or it holds no clip of that name.
        /// </summary>
        public static async Task<AnimationClip> ResolveClipAsync(string fileKey)
        {
            if (!ExternalAssetKey.TryParseClipKey(fileKey, out var relativePath, out _)) return null;

            var absolutePath = PropPreset.ResolveSource(relativePath, ProjectManager.projectPath);
            await LoadClipsAsync(absolutePath, relativePath);

            // Registration used this same key, so the registry is the single resolution point.
            return AssetRegistry.TryFind(fileKey, out var asset) ? asset as AnimationClip : null;
        }

        /// <summary>
        /// Loads (and caches) every clip in the bundle at <paramref name="absolutePath"/>, registering
        /// each under its <c>file:&lt;relativePath&gt;#&lt;clipName&gt;</c> key. Returns the clips (empty
        /// on failure). <paramref name="relativePath"/> is the project-relative bundle path used to build
        /// the keys, so registration matches the keys stored in the live scene.
        /// </summary>
        public static Task<AnimationClip[]> LoadClipsAsync(string absolutePath, string relativePath)
        {
            if (string.IsNullOrEmpty(absolutePath)) return Task.FromResult(System.Array.Empty<AnimationClip>());

            if (_clipCache.TryGetValue(absolutePath, out var cached) && _IsAlive(cached))
            {
                _RegisterClips(cached, relativePath);
                return Task.FromResult(cached);
            }
            if (_inflight.TryGetValue(absolutePath, out var pending)) return pending;

            var task = _LoadClipsAsync(absolutePath, relativePath);
            _inflight[absolutePath] = task;
            return task;
        }

        static async Task<AnimationClip[]> _LoadClipsAsync(string absolutePath, string relativePath)
        {
            try
            {
                if (!File.Exists(absolutePath))
                {
                    Debug.LogError($"[LiveStudio] Animation bundle not found: {absolutePath}");
                    return System.Array.Empty<AnimationClip>();
                }

                var clips = await BundleLoadGate.RunExclusiveAsync(async () =>
                {
                    var bundleRequest = AssetBundle.LoadFromFileAsync(absolutePath);
                    await _AwaitOperation(bundleRequest);

                    var bundle = bundleRequest.assetBundle;
                    if (bundle == null)
                    {
                        Debug.LogError($"[LiveStudio] Failed to load animation bundle: {absolutePath}");
                        return System.Array.Empty<AnimationClip>();
                    }

                    try
                    {
                        var assetRequest = bundle.LoadAllAssetsAsync<AnimationClip>();
                        await _AwaitOperation(assetRequest);

                        var all = assetRequest.allAssets;
                        if (all == null || all.Length == 0)
                        {
                            Debug.LogError($"[LiveStudio] Animation bundle has no clips: {absolutePath}");
                            return System.Array.Empty<AnimationClip>();
                        }

                        var result = new AnimationClip[all.Length];
                        for (int i = 0; i < all.Length; i++) result[i] = all[i] as AnimationClip;
                        return result;
                    }
                    finally
                    {
                        // false = keep the loaded clips alive, free only the bundle container so the same
                        // file can be reloaded (and colliding CABs never overlap).
                        bundle.Unload(false);
                    }
                });

                _clipCache[absolutePath] = clips;
                _RegisterClips(clips, relativePath);
                return clips;
            }
            finally
            {
                _inflight.Remove(absolutePath);
            }
        }

        // Registers each clip under its file key so AssetRegistry resolves the selector reference. Idempotent.
        static void _RegisterClips(AnimationClip[] clips, string relativePath)
        {
            if (clips == null || string.IsNullOrEmpty(relativePath)) return;
            for (int i = 0; i < clips.Length; i++)
            {
                var clip = clips[i];
                if (clip == null) continue;
                var key = ExternalAssetKey.BuildClipKey(relativePath, clip.name);
                if (!string.IsNullOrEmpty(key)) AssetRegistry.Register(key, clip);
            }
        }

        // True when the cached array still holds live Unity objects (survives leaving play mode nulling them).
        static bool _IsAlive(AnimationClip[] clips)
        {
            if (clips == null || clips.Length == 0) return false;
            for (int i = 0; i < clips.Length; i++)
            {
                if (clips[i] == null) return false;
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
