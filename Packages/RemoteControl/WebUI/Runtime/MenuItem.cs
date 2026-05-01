// Copyright (c) You-Ri, 2026

using System;
using UnityEngine;

namespace Lilium.RemoteControl.WebUI
{
    /// <summary>
    /// メニュー項目の表示位置
    /// </summary>
    public enum MenuItemPosition
    {
        Main,
        Bottom,
    }

    /// <summary>
    /// WebUIのサイドメニュー項目定義。
    /// RemoteAppの FeatureMenuItem に対応する。
    /// </summary>
    [Serializable]
    public class MenuItem
    {
        /// <summary>
        /// メニュー項目のID。ページルーティングに使用する。
        /// </summary>
        public string id;

        /// <summary>
        /// Material Icon名
        /// </summary>
        public string icon;

        /// <summary>
        /// エディタ用アイコン
        /// </summary>
        public Texture2D editorIcon;

        /// <summary>
        /// 表示ラベル
        /// </summary>
        public string label;

        [SerializeReference, Select]
        public IPage page;

        /// <summary>
        /// 表示位置
        /// </summary>
        public MenuItemPosition position;

        /// <summary>
        /// 表示順序（小さいほど上）
        /// </summary>
        public int order;

        /// <summary>
        /// 開発ビルドでのみ表示する項目
        /// </summary>
        public bool development;

        /// <summary>
        /// 実験的機能としてバッジを表示する
        /// </summary>
        public bool experimental;
    }
}
