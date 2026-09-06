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
    public partial class ExternalAvatarSource : MonoBehaviour, IAvatarSource, ILiveDeserializeCallback
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

        // 「どのアバターが出ているか」の意図。シーンに保存され、状態レーンで運ばれる
        // (`selectedAvatar` のシャドウフィールド)。
        //
        // ⚠ カタログ (`ExternalAssetManager.assets`) はこの機械のディスクにある物なので保存も収録も
        // しない。舞台に出ているものはショーなので、こちら側が持つ。保存と収録が同じ 1 つの宣言から
        // 決まる (FrameLaneRules) ので、「ファイルには残るがテイクには残らない」は起こらない。
        //
        // ⚠ 実体との同期は _SyncFromManager / _TryApplySelection が持つ。アセットページから
        // 直接 enabled を触られてもここが実体に追従し、復元直後でカタログがまだ無い間は
        // 意図を保持したまま到着を待つ。
        [SerializeField, LiveField(lane = FrameLane.State), Hide]
        [FormerlyNamedAs("selectedAvatar")]
        private string _selectedAvatar = string.Empty;

        // 復元 (または再生の適用) で受け取った意図が、まだカタログに無くて適用できていない状態。
        // 立っている間は実体からの同期を止める — 空のカタログが意図を消してしまうため。
        [NonSerialized]
        private bool _selectionPending;

        [NonSerialized]
        private ExternalAssetManager _subscribedManager;

        // ライブシーンページ等のインスペクタから、ExternalAssetManager に登録済みのアバターを
        // ドロップダウンで選択する。値は意図、ロードは効果。
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
        // Carried as the id the frame's table gives the name rather than as text, because the
        // choice comes from a list (see the selector below). A width here would be a ceiling on the
        // display name -- a longer one is not carried at all rather than shortened, which for this
        // member means a recording that does not say which avatar was out.
        [LiveProperty(label = "AVATAR_SELECT", lane = FrameLane.State)]
        [StringSelector(nameof(avatarOptions))]
        [Help("AVATAR_SELECT_HELP")]
        public string selectedAvatar
        {
            // 意図を返す。実体を直接読まないのは、選択の反映が 1 フレーム遅れる (SelectByName は
            // 選んだ方を上げるだけで、他を下ろすのは後のリコンサイル) 間に「ひとつ前のアバター」を
            // 答えてしまうため。その値が記録に載ると、切り替えたのと違うアバターへの切り替えとして
            // 残る。実体が別経路で動いたときは _SyncFromManager がこの値を追従させる。
            get => _selectedAvatar ?? string.Empty;
            set
            {
                var name = value ?? string.Empty;
                if (string.Equals(name, _selectedAvatar, StringComparison.Ordinal)) return;

                Debug.Log($"[LiveStudio] ExternalAvatarSource.selectedAvatar = {name}");
                _selectedAvatar = name;
                _selectionPending = !_TryApplySelection();
            }
        }

        /// <summary>
        /// 意図をカタログへ適用する。まだそのアバターがカタログに無ければ false を返し、
        /// 到着を待つ (<see cref="_OnAssetsChanged"/> が再試行する)。
        /// </summary>
        private bool _TryApplySelection()
        {
            var manager = ExternalAssetManager.current;
            if (manager == null) return false;

            // 空 = 既定アバターへ戻す。カタログの中身に関わらず必ず適用できる。
            if (string.IsNullOrEmpty(_selectedAvatar))
            {
                AvatarSelection.SelectByName(manager, string.Empty);
                return true;
            }

            if (!AvatarSelection.Contains(manager, _selectedAvatar)) return false;

            AvatarSelection.SelectByName(manager, _selectedAvatar);
            return true;
        }

        // 実体が別経路で動いたとき (アセットページの enabled トグル、排他リコンサイル) に意図を
        // 追従させる。⚠ 待ち状態の間は動かさない — 起動直後のカタログはまだ空で、そこから
        // 同期すると復元した意図をその場で消す。
        private void _SyncFromManager()
        {
            var manager = ExternalAssetManager.current;
            if (manager == null) return;

            _selectedAvatar = AvatarSelection.GetSelectedName(manager);
        }

        private void _OnAssetsChanged()
        {
            if (_selectionPending)
            {
                if (!_TryApplySelection()) return;
                _selectionPending = false;
                return;
            }

            _SyncFromManager();
        }

        /// <summary>
        /// ライブシーンの復元後。シャドウフィールドへ直接書かれるのでセッターを通らず、意図が
        /// 適用されないまま残る。⚠ このコールバックはプロパティ書き込みでも発火するので、
        /// 実体と食い違っているときだけ動く。
        /// </summary>
        public void OnAfterLiveDeserialize()
        {
            if (!Application.isPlaying) return;

            var manager = ExternalAssetManager.current;
            if (manager != null
                && string.Equals(AvatarSelection.GetSelectedName(manager), selectedAvatar, StringComparison.Ordinal))
            {
                _selectionPending = false;
                return;
            }

            _selectionPending = !_TryApplySelection();
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
            _EnsureSubscribed();
        }

        void OnDisable()
        {
            VRMLoader.onLoaded -= _OnVRMLoaded;
            VRMLoader.onLoadError -= _OnVRMLoadError;

            if (_subscribedManager != null)
            {
                _subscribedManager.onAssetsChanged -= _OnAssetsChanged;
                _subscribedManager = null;
            }
        }

        void Update()
        {
            // マネージャは OnEnable の時点ではまだ居ないことがある (ロード順)。購読を遅らせる。
            // 掴んだインスタンスを持つのは、先に壊されても確実に解除するため (StageManager と同形)。
            _EnsureSubscribed();
        }

        private void _EnsureSubscribed()
        {
            if (_subscribedManager != null) return;

            var manager = ExternalAssetManager.current;
            if (manager == null) return;

            _subscribedManager = manager;
            manager.onAssetsChanged += _OnAssetsChanged;

            // 購読した時点のカタログで 1 度確かめる。復元がこれより先に走っていれば待ち状態の
            // 意図がここで通り、そうでなければ実体から意図を採る。
            _OnAssetsChanged();
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
