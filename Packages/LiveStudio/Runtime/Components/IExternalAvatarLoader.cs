// Copyright (c) You-Ri, 2026

using System.Threading.Tasks;

using UnityEngine;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// 外部アバターファイル（.vrm / .lsavatar など）を読み込むローダーの抽象。
    /// フォーマット固有の初期化・読込・破棄を内包し、<see cref="ExternalAvatarSource"/> から
    /// 拡張子に応じて選択される。
    /// </summary>
    internal interface IExternalAvatarLoader
    {
        /// <summary>
        /// ファイルを読み込み、生成したアバター root を返す。失敗時は null（実装側で根本原因をログ出力）。
        /// VRM 実装は完了通知を <see cref="IVRMLoadObserver"/> ブロードキャスト経由で行うため null を返す。
        /// </summary>
        Task<GameObject> LoadAsync(string filePath, Transform parent);

        /// <summary>
        /// ローダーが保有するネイティブ資源（AssetBundle ハンドル等）を解放する。
        /// 生成済みアバター GameObject の破棄は行わない（AvatarController.ReleaseAvatar が所有）。
        /// </summary>
        void Dispose();
    }
}
