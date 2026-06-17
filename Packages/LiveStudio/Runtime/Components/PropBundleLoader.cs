// Copyright (c) You-Ri, 2026

using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Loads a <c>*.prop.lsb</c> AssetBundle (a single root prefab) at runtime and
    /// instantiates it. Mirrors <see cref="LsAvatarLoader"/>: the bundle is unloaded with
    /// <c>Unload(false)</c> right after instantiation so the same file can be reloaded later
    /// while the instantiated assets stay alive.
    ///
    /// A prop bundle is either an avatar prop (attached under an avatar via
    /// <see cref="AvatarProp"/>) or a free-standing stage prop. The loader itself does
    /// not parent or wire the instance; that is the caller's (source/manager) responsibility.
    /// </summary>
    internal sealed class PropBundleLoader
    {
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

                var instance = Object.Instantiate(prefab, parent, worldPositionStays: false);
                instance.name = prefab.name;
                return instance;
            }
            finally
            {
                // false = keep instantiated assets (meshes, materials, prefab) alive,
                // free only the bundle container so the same file can be reloaded.
                bundle.Unload(false);
            }
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
