// Copyright (c) You-Ri, 2026

using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Loads a <c>*.prop.lsb</c> AssetBundle (a single root prefab) at runtime and
    /// instantiates it. The root prefab is cached per file path for the session, so a prop can be
    /// unloaded and loaded again without re-reading the bundle from disk: the first load reads the
    /// bundle, caches the prefab, and frees the bundle container with <c>Unload(false)</c> (which
    /// keeps the prefab and its assets alive); subsequent loads instantiate the cached prefab
    /// directly. This also avoids the "another AssetBundle with the same files is already loaded"
    /// error that occurs when the same bundle file is loaded again before its container is freed.
    ///
    /// A prop bundle is either an avatar prop (attached under an avatar via
    /// <see cref="AvatarProp"/>) or a free-standing stage prop. The loader itself does
    /// not parent or wire the instance; that is the caller's (source/manager) responsibility.
    /// </summary>
    internal sealed class PropBundleLoader
    {
        // Cached root prefab per bundle file path. Entries become null Unity objects after the
        // assets are unloaded (e.g. leaving play mode), which is treated as a cache miss below.
        static readonly Dictionary<string, GameObject> _prefabCache = new Dictionary<string, GameObject>();

        // In-flight prefab loads per file path. Concurrent requests for the same file share one
        // load so the bundle is never opened twice at once (which would raise "another AssetBundle
        // with the same files is already loaded"); each caller still instantiates its own instance.
        static readonly Dictionary<string, Task<GameObject>> _inflight = new Dictionary<string, Task<GameObject>>();

        // Reset the session caches at the start of each play session so stale references from a
        // previous run do not survive when domain reload is disabled.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void _ResetState()
        {
            _prefabCache.Clear();
            _inflight.Clear();
        }

        /// <summary>
        /// Loads the bundle at <paramref name="filePath"/>, instantiates its root prefab under
        /// <paramref name="parent"/>, and returns the instance (null on failure).
        /// </summary>
        public async Task<GameObject> LoadAsync(string filePath, Transform parent)
        {
            if (!File.Exists(filePath))
            {
                Debug.LogError($"[LiveStudio] Prop bundle not found: {filePath}");
                return null;
            }

            var prefab = await _GetPrefabAsync(filePath);
            if (prefab == null) return null;
            return _Instantiate(prefab, parent);
        }

        // Returns the cached root prefab for a bundle path, loading it once if needed. Concurrent
        // callers for the same path await the same load instead of opening the bundle in parallel.
        static Task<GameObject> _GetPrefabAsync(string filePath)
        {
            if (_prefabCache.TryGetValue(filePath, out var cached) && cached != null)
            {
                return Task.FromResult(cached);
            }
            if (_inflight.TryGetValue(filePath, out var pending))
            {
                return pending;
            }
            var task = _LoadPrefabAsync(filePath);
            _inflight[filePath] = task;
            return task;
        }

        static async Task<GameObject> _LoadPrefabAsync(string filePath)
        {
            try
            {
                var bundleRequest = AssetBundle.LoadFromFileAsync(filePath);
                await _AwaitOperation(bundleRequest);

                var bundle = bundleRequest.assetBundle;
                if (bundle == null)
                {
                    Debug.LogError($"[LiveStudio] Failed to load prop bundle: {filePath}");
                    return null;
                }

                try
                {
                    var assetNames = bundle.GetAllAssetNames();
                    if (assetNames == null || assetNames.Length == 0)
                    {
                        Debug.LogError($"[LiveStudio] Prop bundle has no assets: {filePath}");
                        return null;
                    }

                    var assetRequest = bundle.LoadAssetAsync<GameObject>(assetNames[0]);
                    await _AwaitOperation(assetRequest);

                    var prefab = assetRequest.asset as GameObject;
                    if (prefab == null)
                    {
                        Debug.LogError($"[LiveStudio] Prop bundle root is not a GameObject: {filePath}");
                        return null;
                    }

                    _prefabCache[filePath] = prefab;
                    return prefab;
                }
                finally
                {
                    // false = keep the cached prefab and instantiated assets alive,
                    // free only the bundle container so the same file can be reloaded.
                    bundle.Unload(false);
                }
            }
            finally
            {
                _inflight.Remove(filePath);
            }
        }

        static GameObject _Instantiate(GameObject prefab, Transform parent)
        {
            var instance = Object.Instantiate(prefab, parent, worldPositionStays: false);
            instance.name = prefab.name;
            return instance;
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
