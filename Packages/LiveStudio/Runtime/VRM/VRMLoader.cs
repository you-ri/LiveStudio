using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    public interface IVRMLoadObserver
    {
        void OnVRMLoadStarted(string filePath);

        void OnVRMLoaded(GameObject vrm);

        void OnVRMLoadError(string error);

        void OnVRMLoadProgress(float progress);
    }


    public static partial class VRMLoadObserver
    {
        public static void NoticeOnVRMLoadStarted(string filePath)
        {
            Service<IVRMLoadObserver>.subjects.ForEach(s => s.OnVRMLoadStarted(filePath));
        }

        public static void NoticeOnVRMLoaded(GameObject vrm)
        {
            Service<IVRMLoadObserver>.subjects.ForEach(s => s.OnVRMLoaded(vrm));
        }

        public static void NoticeOnVRMLoadError(string error)
        {
            Service<IVRMLoadObserver>.subjects.ForEach(s => s.OnVRMLoadError(error));
        }

        public static void NoticeOnVRMLoadProgress(float progress)
        {
            Service<IVRMLoadObserver>.subjects.ForEach(s => s.OnVRMLoadProgress(progress));
        }
    }


    /// <summary>
    /// VRMファイルを非同期でロードするクラス
    /// VRMLoadProviderを使用してVRMファイルを読み込み、シグナルで結果を通知します。
    /// </summary>
    /// TODO: テストを追加する
    public static class VRMLoader
    {
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
            VRMLoadObserver.NoticeOnVRMLoadStarted(filePath);

            if (string.IsNullOrEmpty(filePath))
            {
                Debug.LogError("[LiveStudio] VRM file path is null or empty");
                IsLoading = false;
                CurrentLoadingFilePath = null;
                VRMLoadObserver.NoticeOnVRMLoadError("VRM file path is null or empty");
                return;
            }

            if (!System.IO.File.Exists(filePath))
            {
                IsLoading = false;
                CurrentLoadingFilePath = null;
                VRMLoadObserver.NoticeOnVRMLoadError($"VRM file not found: {filePath}");
                return;
            }

            // 新しいCancellationTokenSourceを作成
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = new CancellationTokenSource();
            var cancellationToken = _cancellationTokenSource.Token;

            try
            {
                // プログレス通知: 開始
                VRMLoadObserver.NoticeOnVRMLoadProgress(0.0f);

                // キャンセル状態をチェック
                cancellationToken.ThrowIfCancellationRequested();

                // プログレス通知: ファイル読み込み開始
                VRMLoadObserver.NoticeOnVRMLoadProgress(0.1f);

                // UniVRM 1.0のローダーを使用
                var vrm10Instance = await UniVRM10.Vrm10.LoadPathAsync(filePath, controlRigGenerationOption: UniVRM10.ControlRigGenerationOption.Generate, ct: cancellationToken);

                VRMLoadObserver.NoticeOnVRMLoadProgress(0.7f);

                // ロード後もキャンセル状態をチェック
                cancellationToken.ThrowIfCancellationRequested();

                if (vrm10Instance != null)
                {
                    // プログレス通知: 初期化開始
                    VRMLoadObserver.NoticeOnVRMLoadProgress(0.8f);

                    var gameObject = vrm10Instance.gameObject;
                    gameObject.name = vrm10Instance.Vrm.Meta.Name;

                    if (parent != null)
                    {
                        gameObject.transform.SetParent(parent, worldPositionStays: false);
                    }

                    // 最終的なキャンセル状態をチェック
                    cancellationToken.ThrowIfCancellationRequested();

                    // プログレス通知: 完了
                    VRMLoadObserver.NoticeOnVRMLoadProgress(1.0f);

                    Debug.Log($"[LiveStudio] VRM loaded successfully: {filePath}");

                    IsLoading = false;
                    CurrentLoadingFilePath = null;
                    VRMLoadObserver.NoticeOnVRMLoaded(gameObject);
                }
                else
                {
                    Debug.LogError($"[LiveStudio] Failed to load VRM from path: {filePath}");
                    IsLoading = false;
                    CurrentLoadingFilePath = null;
                    VRMLoadObserver.NoticeOnVRMLoadError($"Failed to load VRM from path: {filePath}");
                }

            }
            catch (OperationCanceledException)
            {
                Debug.Log("[LiveStudio] VRM loading was cancelled.");
                IsLoading = false;
                CurrentLoadingFilePath = null;
                VRMLoadObserver.NoticeOnVRMLoadError("VRM loading was cancelled.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LiveStudio] Exception during VRM loading: {ex.Message}");
                IsLoading = false;
                CurrentLoadingFilePath = null;
                VRMLoadObserver.NoticeOnVRMLoadError($"Exception during VRM loading: {ex.Message}");
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

