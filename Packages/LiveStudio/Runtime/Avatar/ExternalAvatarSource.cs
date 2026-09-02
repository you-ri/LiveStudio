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
    public partial class ExternalAvatarSource : MonoBehaviour, IAvatarSource
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
                ? AvatarSelection.GetNames(ExternalAssetManager.current)
                : Array.Empty<string>();

        // ライブシーンページ等のインスペクタから、ExternalAssetManager に登録済みのアバターを
        // ドロップダウンで選択する。get/set とも manager に委譲する（backing field なし = 非永続）。
        //
        // State lane rather than the event lane, which is not a choice about how often it changes --
        // it changes a few times a take -- but about what is the source of truth. The value is the
        // intent ("this avatar is out"); loading it is the effect. Carried as state, a replay puts
        // the intent back and the setter's reconcile produces the effect, so any frame of the
        // recording is enough to say which avatar should be standing there. Carried as an event, the
        // recording holds the moment someone switched and nothing else, and every reader has to
        // reconstruct the intent by replaying history from the beginning.
        //
        // The write is only made when the value actually changed (the generated apply compares
        // first), so the reconcile does not run sixty times a second for an avatar standing still.
        // ⚠ Its width is a ceiling on the display name: a longer one is not carried at all rather
        // than shortened, which for this member means a recording that does not say which avatar
        // was out. 256 bytes is 85 kanji or 256 ASCII characters.
        [LiveProperty(label = "AVATAR_SELECT", lane = FrameLane.State, textCapacity = 256)]
        [StringSelector(nameof(avatarOptions))]
        [Help("AVATAR_SELECT_HELP")]
        public string selectedAvatar
        {
            get =>
                ExternalAssetManager.current != null
                    ? AvatarSelection.GetSelectedName(ExternalAssetManager.current)
                    : string.Empty;
            set
            {
                Debug.Log($"[LiveStudio] ExternalAvatarSource.selectedAvatar = {value}");
                AvatarSelection.SelectByName(ExternalAssetManager.current, value);
            }
        }

        public void RequestLoad(string filepath)
        {
            _modelFilePath = filepath;
            _LoadIfFileExists();
        }

        void OnEnable()
        {
            VRMLoader.onLoaded += _OnVRMLoaded;
            VRMLoader.onLoadError += _OnVRMLoadError;
        }

        void OnDisable()
        {
            VRMLoader.onLoaded -= _OnVRMLoaded;
            VRMLoader.onLoadError -= _OnVRMLoadError;
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
                // VRM の完了通知は VRMLoader.onLoaded イベント経由で _OnVRMLoaded に届く。
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

        void _OnVRMLoaded(GameObject newTarget)
        {
            Debug.Assert(newTarget != null);
            onAvatarReady?.Invoke(newTarget);
        }

        void _OnVRMLoadError(string error)
        {
            Debug.LogError($"[LiveStudio] VRM Load Error: {error}");
        }
    }
}
