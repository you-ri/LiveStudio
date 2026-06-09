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
    internal sealed class LsAvatarLoader : IExternalAvatarLoader
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

                var assetRequest = bundle.LoadAssetAsync<GameObject>(assetNames[0]);
                await _AwaitOperation(assetRequest);

                var prefab = assetRequest.asset as GameObject;
                if (prefab == null)
                {
                    Debug.LogError($"[LiveStudio] .lsavatar root asset is not a GameObject: {filePath}");
                    return null;
                }

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
