using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// VRMファイルを非同期でロードするクラス
    /// VRMLoadProviderを使用してVRMファイルを読み込み、シグナルで結果を通知します。
    /// </summary>
    /// TODO: テストを追加する
    public static class VRMLoader
    {
        /// <summary>Raised when a VRM load starts. Argument is the file path.</summary>
        public static event Action<string> onLoadStarted;

        /// <summary>Raised when a VRM load completes successfully. Argument is the loaded root GameObject.</summary>
        public static event Action<GameObject> onLoaded;

        /// <summary>Raised when a VRM load fails or is cancelled. Argument is the error message.</summary>
        public static event Action<string> onLoadError;

        /// <summary>Raised as the load progresses. Argument is the normalized progress [0, 1].</summary>
        public static event Action<float> onLoadProgress;

        /// <summary>
        /// VRMの読み込み中かどうか
        /// </summary>
        public static bool IsLoading { get; private set; }

        /// <summary>
        /// 現在読み込み中のVRMファイルパス
        /// </summary>
        public static string CurrentLoadingFilePath { get; private set; }

        private static CancellationTokenSource _cancellationTokenSource;


        /// <summary>
        /// VRMを非同期でロードする
        /// </summary>
        // VRM 1.0のローダーでVRM 0.xも読み込み可能
        public static async Task LoadVRMModel(string filePath, Transform parent = null)
        {
#if VRMC_VRM10
            Debug.Log($"[LiveStudio] Starting VRM load from path: {filePath}");

            // 読み込み状態を設定
            IsLoading = true;
            CurrentLoadingFilePath = filePath;

            // 読み込み開始を通知
            onLoadStarted?.Invoke(filePath);

            if (string.IsNullOrEmpty(filePath))
            {
                Debug.LogError("[LiveStudio] VRM file path is null or empty");
                IsLoading = false;
                CurrentLoadingFilePath = null;
                onLoadError?.Invoke("VRM file path is null or empty");
                return;
            }

            if (!System.IO.File.Exists(filePath))
            {
                IsLoading = false;
                CurrentLoadingFilePath = null;
                onLoadError?.Invoke($"VRM file not found: {filePath}");
                return;
            }

            // 新しいCancellationTokenSourceを作成
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = new CancellationTokenSource();
            var cancellationToken = _cancellationTokenSource.Token;

            try
            {
                // プログレス通知: 開始
                onLoadProgress?.Invoke(0.0f);

                // キャンセル状態をチェック
                cancellationToken.ThrowIfCancellationRequested();

                // プログレス通知: ファイル読み込み開始
                onLoadProgress?.Invoke(0.1f);

                // UniVRM 1.0のローダーを使用
                var vrm10Instance = await UniVRM10.Vrm10.LoadPathAsync(filePath, controlRigGenerationOption: UniVRM10.ControlRigGenerationOption.Generate, ct: cancellationToken);

                onLoadProgress?.Invoke(0.7f);

                // ロード後もキャンセル状態をチェック
                cancellationToken.ThrowIfCancellationRequested();

                if (vrm10Instance != null)
                {
                    // プログレス通知: 初期化開始
                    onLoadProgress?.Invoke(0.8f);

                    var gameObject = vrm10Instance.gameObject;
                    gameObject.name = vrm10Instance.Vrm.Meta.Name;

                    if (parent != null)
                    {
                        gameObject.transform.SetParent(parent, worldPositionStays: false);
                    }

                    // 最終的なキャンセル状態をチェック
                    cancellationToken.ThrowIfCancellationRequested();

                    // プログレス通知: 完了
                    onLoadProgress?.Invoke(1.0f);

                    Debug.Log($"[LiveStudio] VRM loaded successfully: {filePath}");

                    IsLoading = false;
                    CurrentLoadingFilePath = null;
                    onLoaded?.Invoke(gameObject);
                }
                else
                {
                    Debug.LogError($"[LiveStudio] Failed to load VRM from path: {filePath}");
                    IsLoading = false;
                    CurrentLoadingFilePath = null;
                    onLoadError?.Invoke($"Failed to load VRM from path: {filePath}");
                }

            }
            catch (OperationCanceledException)
            {
                Debug.Log("[LiveStudio] VRM loading was cancelled.");
                IsLoading = false;
                CurrentLoadingFilePath = null;
                onLoadError?.Invoke("VRM loading was cancelled.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LiveStudio] Exception during VRM loading: {ex.Message}");
                IsLoading = false;
                CurrentLoadingFilePath = null;
                onLoadError?.Invoke($"Exception during VRM loading: {ex.Message}");
            }
#endif
        }



        /// <summary>
        /// 現在のロード処理をキャンセルする
        /// </summary>
        public static void CancelLoading()
        {
            if (_cancellationTokenSource != null && !_cancellationTokenSource.IsCancellationRequested)
            {
                Debug.Log("[LiveStudio] Cancelling VRM loading.");
                _cancellationTokenSource.Cancel();
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void Initialize()
        {
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = new CancellationTokenSource();
            IsLoading = false;
            CurrentLoadingFilePath = null;
        }


    }
}
