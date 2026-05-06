// Copyright (c) You-Ri, 2026

using System;
using UnityEngine;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// アバター制御サービスの抽象。
    /// 実装の登録・切替は <c>SelectableService&lt;IAvatarService&gt;</c> 経由で行う。
    /// </summary>
    public interface IAvatarService
    {
        /// <summary>
        /// 現在のアバター GameObject。
        /// </summary>
        GameObject target { get; }

        /// <summary>
        /// アバターが切り替わった際に呼び出される。
        /// </summary>
        event Action onAvatarChanged;

        /// <summary>
        /// VRM モデルのロードを要求する。
        /// </summary>
        void RequestLoadVRM(string filepath);

        void ResetAvatar();
    }
}
