// Copyright (c) You-Ri, 2026

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
#if UNITY_EDITOR
using System.Reflection;
#endif

namespace Lilium.RemoteControl.UI
{
    /// <summary>
    /// ScriptableObject defining the page structure of the Remote Control UI.
    /// Declares the side menu items and their associated ExposedObject categories.
    /// </summary>
    [CreateAssetMenu(fileName = "UIDefinition", menuName = "Remote Control/UI Definition")]
    [MovedFrom(true, "Lilium.RemoteControl.WebUI", "Lilium.RemoteControl.WebUI", "WebUIDefinition")]
    public class UIDefinition : ScriptableObject
    {
        /// <summary>
        /// サイドメニュー項目リスト
        /// </summary>
        public List<MenuItem> menuItems = new List<MenuItem>();

#if UNITY_EDITOR
        private void OnValidate()
        {
            _RefreshPrefabKeysInternal(this);
        }

        /// <summary>
        /// MenuItem.page 配下の StandardObjectFactory を再帰的に探し出し、
        /// 各 IExposedObjectFactory の prefab Asset GUID を再解決する。
        /// CategoryPage だけでなく ScenePage など、factory フィールドを持つ任意の IPage 型に対応する。
        /// </summary>
        public static void _RefreshPrefabKeysInternal(UIDefinition def)
        {
            if (def == null) return;
            if (def.menuItems == null) return;

            for (int i = 0; i < def.menuItems.Count; i++)
            {
                var item = def.menuItems[i];
                if (item == null || item.page == null) continue;
                _RefreshStandardFactoriesInPage(item.page);
            }
        }

        static void _RefreshStandardFactoriesInPage(IPage page)
        {
            var type = page.GetType();
            while (type != null && type != typeof(object))
            {
                var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                for (int i = 0; i < fields.Length; i++)
                {
                    var val = fields[i].GetValue(page);
                    if (val is StandardObjectFactory sf)
                    {
                        sf.RefreshPrefabKeys();
                        // Play 中の Inspector 変更や Reset でも PrefabRegistry を追従させる。
                        sf.RegisterPrefabs();
                    }
                }
                type = type.BaseType;
            }
        }
#endif
    }
}
