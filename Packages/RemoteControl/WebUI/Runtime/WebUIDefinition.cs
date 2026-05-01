// Copyright (c) You-Ri, 2026

using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using System.Reflection;
#endif

namespace Lilium.RemoteControl.WebUI
{
    /// <summary>
    /// WebUIのページ構成を定義するScriptableObject。
    /// サイドメニューの項目とそれに対応するExposedObjectカテゴリを定義する。
    /// </summary>
    [CreateAssetMenu(fileName = "WebUIDefinition", menuName = "Remote Control/WebUI Definition")]
    public class WebUIDefinition : ScriptableObject
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
        public static void _RefreshPrefabKeysInternal(WebUIDefinition def)
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
