// Copyright (c) You-Ri, 2026

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace Lilium.RemoteControl.UI
{
    /// <summary>
    /// UI page definition.
    /// Corresponds to the RemoteApp NavigatePage.
    /// Displays ExposedObjects of static classes with sections.
    /// </summary>
    [Serializable]
    [MovedFrom(true, "Lilium.RemoteControl.WebUI", "Lilium.RemoteControl.WebUI")]
    public class NavigatePage : IPage
    {
        /// <summary>
        /// ページ内のオブジェクトを選択するためのセレクタ。
        /// </summary>
        [SerializeReference, Select]
        public IObjectSelector selector = new NavigateObjectSelector();
    }

    /// <summary>
    /// NavigatePage用のオブジェクトセレクタ。
    /// 指定されたIDのExposedObjectを参照として返す。
    /// 静的クラスのExposedObjectはtargetがnullのため、ExposedObjectインスタンス自体を返し、
    /// シリアライザが@refとして解決する。
    /// </summary>
    [Serializable]
    [MovedFrom(true, "Lilium.RemoteControl.WebUI", "Lilium.RemoteControl.WebUI")]
    public class NavigateObjectSelector : ObjectSelectorBase
    {
        /// <summary>
        /// 表示するExposedObjectのID一覧。
        /// 各IDはExposedObjectRegistry上の静的クラスのtypeNameに対応する。
        /// </summary>
        public string[] objectIds = new string[0];

        protected override object[] GetObjects()
        {
            if (objectIds == null || objectIds.Length == 0)
                return new object[0];

            var result = new List<object>();
            for (int i = 0; i < objectIds.Length; i++)
            {
                var exposedObject = ExposedObjectRegistry.FindById(objectIds[i]);
                if (exposedObject == null)
                {
                    // RuntimeInitializeOnLoad による static 登録が走る前や、
                    // ExposedObjectRegistry のクリア後にアクセスされたケースを救済する。
                    var exposedClass = ExposedClass.Find(objectIds[i]);
                    if (exposedClass != null && exposedClass.isStatic)
                    {
                        exposedObject = ExposedObjectRegistry.GetOrCreate(exposedClass.typeName, exposedClass, null);
                    }
                }
                if (exposedObject != null)
                    result.Add(exposedObject);
            }
            return result.ToArray();
        }
    }
}
