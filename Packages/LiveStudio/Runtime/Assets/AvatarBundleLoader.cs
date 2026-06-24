// Copyright (c) You-Ri, 2026

using System.IO;
using System.Threading.Tasks;

using UnityEngine;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// .lsavatar（AssetBundle）ファイル用の外部アバターローダー。
    /// バンドルをロードして単一の root プレハブを取り出し、インスタンス化したアバターを返す。
    /// 生成済みアセットを生かしたままバンドルコンテナのみ即解放するため、同一ファイルの再ロードも可能。
    /// </summary>
    internal sealed class AvatarBundleLoader : IExternalAvatarLoader
    {
        public async Task<GameObject> LoadAsync(string filePath, Transform parent)
        {
            if (!File.Exists(filePath))
            {
                Debug.LogError($"[LiveStudio] .lsavatar file not found: {filePath}");
                return null;
            }

            var bundleRequest = AssetBundle.LoadFromFileAsync(filePath);
            await _AwaitOperation(bundleRequest);

            var bundle = bundleRequest.assetBundle;
            if (bundle == null)
            {
                Debug.LogError($"[LiveStudio] Failed to load AssetBundle: {filePath}");
                return null;
            }

            // バンドルは finally で確実に解放する。Unload(false) なので生成済みアセットは生存する。
            try
            {
                var assetNames = bundle.GetAllAssetNames();
                if (assetNames == null || assetNames.Length == 0)
                {
                    Debug.LogError($"[LiveStudio] .lsavatar contains no assets: {filePath}");
                    return null;
                }

                // The bundle may also contain a BundleThumbnail (a ScriptableObject), so load the
                // GameObject explicitly instead of assuming assetNames[0] is the root prefab.
                var assetRequest = bundle.LoadAllAssetsAsync<GameObject>();
                await _AwaitOperation(assetRequest);

                var allAssets = assetRequest.allAssets;
                var prefab = (allAssets != null && allAssets.Length > 0) ? allAssets[0] as GameObject : null;
                if (prefab == null)
                {
                    Debug.LogError($"[LiveStudio] .lsavatar root asset is not a GameObject: {filePath}");
                    return null;
                }

                // Cache the packed BundleThumbnail while the bundle is open so the REST handler can serve
                // it without re-opening the bundle (the remote app shows it as the avatar's preview).
                _CacheBundleThumbnail(bundle, filePath);

                var instance = Object.Instantiate(prefab, parent, worldPositionStays: false);
                instance.name = prefab.name;
                return instance;
            }
            finally
            {
                // false: ロード済みアセット（メッシュ/マテリアル/プレハブ）は維持し、バンドルコンテナのみ解放。
                // これにより同一 .lsavatar の再ロード時に "already loaded" にならない。
                bundle.Unload(false);
            }
        }

        public void Dispose()
        {
            // バンドルは LoadAsync 内で即時解放済み。アバター GameObject の破棄は AvatarController が所有。
        }

        // 既に開いているバンドルから BundleThumbnail を読み、ファイルパスをキーにキャッシュする。
        static void _CacheBundleThumbnail(AssetBundle bundle, string filePath)
        {
            var thumbnails = bundle.LoadAllAssets<BundleThumbnail>();
            if (thumbnails == null || thumbnails.Length == 0) return;
            var thumbnail = thumbnails[0];
            if (thumbnail == null || !thumbnail.HasImage) return;
            // Unload(false) で生成済みアセットは生存するが、UnityEngine.Object 参照を持ち越さないよう複製する。
            ThumbnailCache.Store(filePath, (byte[])thumbnail.ImageData.Clone(), thumbnail.MimeType);
        }

        /// <summary>
        /// Unity の <see cref="AsyncOperation"/> を await 可能にする。
        /// （本リポジトリでは AsyncOperation の直接 await パターンが無いため TaskCompletionSource で橋渡しする）
        /// </summary>
        static Task _AwaitOperation(AsyncOperation operation)
        {
            if (operation.isDone)
            {
                return Task.CompletedTask;
            }

            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            operation.completed += _ => tcs.TrySetResult(true);
            return tcs.Task;
        }
    }
}
