// Copyright (c) You-Ri, 2026

using System;
using System.IO;
using System.Threading.Tasks;

using UnityEngine;

using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// 外部アバターファイルを読み込むアバターソース。拡張子で .vrm / .avatar.lsb (旧 .lsavatar) を判別し、
    /// フォーマット固有処理は <see cref="IExternalAvatarLoader"/> 実装へ委譲する。
    ///
    /// ロードは <see cref="ExternalAssetManager"/>（<see cref="AvatarAsset"/> 経由 → <see cref="AvatarService"/>
    /// → <see cref="AvatarController.RequestLoad"/>）からのみ駆動される純粋なローダー実行役。
    /// 「どのアバターを読むか」の選択状態と永続化は ExternalAssetManager の assets 配列が唯一の source of truth。
    /// このコンポーネント自身は exposed なモデルファイルプロパティを持たない。
    /// </summary>
    [DefaultExecutionOrder(250)]
    [LiveClass("ExternalAvatarSource", Category = "Avatar", Icon = "deployed_code")]
    [FormerlyNamedAs("VRMAvatarSource")]
    public class ExternalAvatarSource : MonoBehaviour, IAvatarSource, IVRMLoadObserver
    {
        public event Action<GameObject> onAvatarReady;

        // 現在ロード中（または直近に要求された）アバターファイルパス。RequestLoad でのみ設定される
        // ランタイム状態で、永続化も exposed もしない（永続化は ExternalAssetManager の assets が担う）。
        // アバターバンドルは複合拡張子 ".avatar.lsb" で絞る（".set.lsb" と区別するため）。
        // 旧 ".lsavatar" も後方互換のため受理する。
        string _modelFilePath;

        public string modelFilePath => _modelFilePath;

        // 登録済みアバター名の一覧（先頭の空文字 = 既定アバター）。selectedAvatar の選択肢ソース。
        // ExternalAssetManager のアバター選択への view であり、ここには永続化状態を持たない。
        [LiveProperty, Hide]
        public string[] avatarOptions =>
            ExternalAssetManager.current != null
                ? ExternalAssetManager.current.GetAvatarNames()
                : Array.Empty<string>();

        // ライブシーンページ等のインスペクタから、ExternalAssetManager に登録済みのアバターを
        // ドロップダウンで選択する。get/set とも manager に委譲する（backing field なし = 非永続）。
        [LiveProperty(label = "AVATAR_SELECT"), StringSelector(nameof(avatarOptions))]
        [Help("AVATAR_SELECT_HELP")]
        public string selectedAvatar
        {
            get =>
                ExternalAssetManager.current != null
                    ? ExternalAssetManager.current.GetSelectedAvatarName()
                    : string.Empty;
            set => ExternalAssetManager.current?.SelectAvatarByName(value);
        }

        public void RequestLoad(string filepath)
        {
            _modelFilePath = filepath;
            _LoadIfFileExists();
        }

        void OnEnable()
        {
            Service<IVRMLoadObserver>.Register(this);
        }

        void OnDisable()
        {
            Service<IVRMLoadObserver>.Unregister(this);
        }

        void _LoadIfFileExists()
        {
            // パスが空（リセット要求）の場合は AvatarController の既定アバターに戻す。
            if (string.IsNullOrEmpty(_modelFilePath))
            {
                GetComponent<AvatarController>()?.ResetAvatar();
                return;
            }

            if (!File.Exists(_modelFilePath))
            {
                return;
            }

            // .set.lsb と .avatar.lsb はどちらも Path.GetExtension では ".lsb" になるため、
            // 複合サフィックスで判別する。
            if (_modelFilePath.EndsWith(".vrm", StringComparison.OrdinalIgnoreCase))
            {
                // VRM の完了通知は IVRMLoadObserver ブロードキャスト経由で OnVRMLoaded に届く。
                _ = new VrmExternalAvatarLoader().LoadAsync(_modelFilePath, this.transform);
            }
            else if (LiveStudioBundle.IsAvatarBundle(_modelFilePath))
            {
                _ = _LoadLsAvatarAsync(_modelFilePath);
            }
            else
            {
                Debug.LogError($"[LiveStudio] Unsupported avatar file: {_modelFilePath}");
            }
        }

        async Task _LoadLsAvatarAsync(string path)
        {
            var loader = new AvatarBundleLoader();
            var instance = await loader.LoadAsync(path, this.transform);
            loader.Dispose();
            if (instance != null)
            {
                onAvatarReady?.Invoke(instance);
            }
        }

        void IVRMLoadObserver.OnVRMLoadStarted(string filePath)
        {
        }

        void IVRMLoadObserver.OnVRMLoaded(GameObject newTarget)
        {
            Debug.Assert(newTarget != null);
            onAvatarReady?.Invoke(newTarget);
        }

        void IVRMLoadObserver.OnVRMLoadError(string error)
        {
            Debug.LogError($"[LiveStudio] VRM Load Error: {error}");
        }

        void IVRMLoadObserver.OnVRMLoadProgress(float progress)
        {
        }
    }
}
