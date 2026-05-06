// Copyright (c) You-Ri, 2026

using System;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace Lilium.RemoteControl.UI
{
    /// <summary>
    /// Display position of a menu item.
    /// </summary>
    [MovedFrom(true, "Lilium.RemoteControl.WebUI", "Lilium.RemoteControl.WebUI")]
    public enum MenuItemPosition
    {
        Main,
        Bottom,
    }

    /// <summary>
    /// Side menu item definition for the Remote Control UI.
    /// Corresponds to the FeatureMenuItem on the RemoteApp side.
    /// </summary>
    [Serializable]
    [MovedFrom(true, "Lilium.RemoteControl.WebUI", "Lilium.RemoteControl.WebUI")]
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
        /// アクセスレベル。Public は常に表示、Experimental はバッジ付きで表示、
        /// Development は開発ビルドでのみ表示しバッジを付ける。
        /// </summary>
        public AccessLevel accessLevel = AccessLevel.Public;
    }
}
