// Copyright (c) You-Ri, 2026

using System;
using System.IO;
using System.Threading.Tasks;

using UnityEngine;

using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Supplies asset preview pictures to the generic image endpoint (<c>GET /live/asset/{key}/@image</c>), which
    /// lives in RemoteControl and knows nothing about where a picture comes from. This is the LiveStudio
    /// half of that split, alongside the name/listing hooks <see cref="PackBundleLoader"/> registers:
    /// <list type="bullet">
    ///   <item>VRM (.vrm / .glb): the embedded thumbnail, extracted from the file via
    ///   <see cref="GlbThumbnailExtractor"/> on the first request and then cached.</item>
    ///   <item>Avatar/prop/set bundle (.avatar.lsb / .prop.lsb / .lsavatar …): the
    ///   <see cref="BundleThumbnail"/> packed into the bundle at export time, cached by the loader while
    ///   the bundle is open (a request never opens one — that would stall the server).</item>
    ///   <item>Built-in asset: no project file, so its picture is pre-warmed at startup under a synthetic
    ///   GUID key (see <see cref="BuiltinAssetRegistry"/>).</item>
    ///   <item>A kind whose picture is a file of its own beside the asset (a snapshot's
    ///   <c>*.snapshot.png</c>): read straight from disk, reported by
    ///   <see cref="AssetBase.thumbnailFilePath"/>.</item>
    /// </list>
    /// <see cref="ThumbnailCache"/> is backed by disk under the project (<see cref="ProjectPaths"/>), so VRM
    /// previews are available for every registered avatar and bundle previews survive restarts (once the
    /// bundle has been loaded at least once). Assets with no picture resolve to
    /// <see cref="AssetRegistry.Thumbnail.none"/>, which the endpoint reports as 404.
    /// </summary>
    internal static class AssetThumbnailProvider
    {
        // Wire the hook up for both play mode and the editor: the server also answers outside play mode,
        // and with domain reload disabled this must not depend on a previous session's registration.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#endif
        static void _Register()
        {
            AssetRegistry.SetThumbnailProvider(_ResolveAsync);
        }

        // Where one asset's picture is to be found: a ThumbnailCache key (the picture lives inside the
        // asset and was cached when it could be read), an image file of its own, or both — a kind is free
        // to have either. Resolved on the main thread, consumed off it.
        readonly struct Source
        {
            public readonly string cacheKey;
            public readonly string imageFilePath;

            public Source(string cacheKey, string imageFilePath)
            {
                this.cacheKey = cacheKey;
                this.imageFilePath = imageFilePath;
            }

            public bool isEmpty => string.IsNullOrEmpty(cacheKey) && string.IsNullOrEmpty(imageFilePath);
        }

        // Starts on the main thread (the caller awaits the returned task off it): the asset lookup touches
        // Unity state, everything after it is pure CPU/IO and runs off the main thread.
        static async Task<AssetRegistry.Thumbnail> _ResolveAsync(string assetId)
        {
            var source = _ResolveSource(assetId);
            if (source.isEmpty) return AssetRegistry.Thumbnail.none;

            var thumbnail = AssetRegistry.Thumbnail.none;
            // ConfigureAwait(false) matters here: this method starts on the main thread, so the await would
            // otherwise capture Unity's SynchronizationContext and hop back to it to finish — making the
            // reply depend on the main loop being pumped, and deadlocking any caller that blocks on the task
            // from the main thread. Nothing after the await touches Unity, so finish on the pool thread.
            await Task.Run(() => thumbnail = _Read(source)).ConfigureAwait(false);
            return thumbnail;
        }

        // Main thread only. For a file-backed asset (avatar / prop / set) the ThumbnailCache key is its file
        // path — the extension then decides how the picture is obtained. A built-in asset has no file, so it
        // resolves to the synthetic key its startup pre-warm stored the picture under. A kind whose picture
        // is a file of its own (a snapshot's screenshot) reports it through AssetBase.thumbnailFilePath. Any
        // asset kind is accepted; resolution is by reference, so both a runtime absolute id (the remote
        // app's session handle) and a persisted project-relative reference (a deck tile's
        // backgroundAssetId, a snapshot's reference) find it.
        static Source _ResolveSource(string assetId)
        {
            if (string.IsNullOrEmpty(assetId)) return default;
            var manager = ExternalAssetManager.current;
            if (manager == null) return default;
            var asset = manager.FindAssetByReference(assetId);
            if (asset == null) return default;

            var cacheKey = !string.IsNullOrEmpty(asset.filePath)
                ? asset.filePath
                : (asset.isBuiltin ? BuiltinAssetRegistry.ThumbnailCacheKey(asset.persistentId ?? asset.id) : null);
            return new Source(cacheKey, asset.thumbnailFilePath);
        }

        // Off the main thread (ThumbnailCache is thread-safe): the kind's own image file, or the cached
        // picture / a first-request VRM extraction that also fills the cache.
        //
        // The image file is tried FIRST because a kind that has one never has a cache entry: nothing stores
        // a picture under its key. Asking the cache first would pay a miss on every request — and a miss is
        // not free, it hashes the key and probes two files on disk before admitting it has nothing.
        static AssetRegistry.Thumbnail _Read(Source source)
        {
            if (!string.IsNullOrEmpty(source.imageFilePath))
            {
                return _ReadImageFile(source.imageFilePath);
            }

            if (!string.IsNullOrEmpty(source.cacheKey))
            {
                if (ThumbnailCache.TryGet(source.cacheKey, out var cachedBytes, out var cachedMime))
                {
                    return new AssetRegistry.Thumbnail(cachedBytes, cachedMime);
                }

                if (_IsVrmFile(source.cacheKey)
                    && GlbThumbnailExtractor.TryExtract(source.cacheKey, out var bytes, out var mime))
                {
                    ThumbnailCache.Store(source.cacheKey, bytes, mime);
                    return new AssetRegistry.Thumbnail(bytes, mime);
                }
            }

            return AssetRegistry.Thumbnail.none;
        }

        // A picture that is already a file on disk: read it as-is. Not cached — the file IS the cache, and
        // copying it into ThumbnailCache would duplicate it and add a staleness question for nothing.
        // A missing file is the ordinary "no picture yet" answer (a snapshot taken with no live camera).
        static AssetRegistry.Thumbnail _ReadImageFile(string imageFilePath)
        {
            if (string.IsNullOrEmpty(imageFilePath) || !File.Exists(imageFilePath))
            {
                return AssetRegistry.Thumbnail.none;
            }

            try
            {
                var bytes = File.ReadAllBytes(imageFilePath);
                if (bytes.Length == 0) return AssetRegistry.Thumbnail.none;
                return new AssetRegistry.Thumbnail(bytes, _MimeTypeOf(imageFilePath));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[LiveStudio] Failed to read thumbnail file '{imageFilePath}': {e.Message}");
                return AssetRegistry.Thumbnail.none;
            }
        }

        static string _MimeTypeOf(string filePath)
        {
            return filePath.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                || filePath.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
                ? "image/jpeg"
                : "image/png";
        }

        static bool _IsVrmFile(string filePath)
        {
            return filePath.EndsWith(".vrm", StringComparison.OrdinalIgnoreCase)
                || filePath.EndsWith(".glb", StringComparison.OrdinalIgnoreCase);
        }
    }
}
