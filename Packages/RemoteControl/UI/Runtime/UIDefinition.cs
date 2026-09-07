// Copyright (c) You-Ri, 2026

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace Lilium.RemoteControl.UI
{
    /// <summary>
    /// ScriptableObject defining the page structure of the Remote Control UI.
    /// Declares the side menu items and their associated LiveObjectHandle categories.
    /// </summary>
    [CreateAssetMenu(fileName = "UIDefinition", menuName = "Live Studio/Remote Control/UI Definition")]
    [MovedFrom(true, "Lilium.RemoteControl.WebUI", "Lilium.RemoteControl.WebUI", "WebUIDefinition")]
    public class UIDefinition : ScriptableObject
    {
        /// <summary>
        /// サイドメニュー項目リスト
        /// </summary>
        public List<MenuItem> menuItems = new List<MenuItem>();
    }
}
