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
    /// Displays LiveObjects of static classes with sections.
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
    /// 指定されたIDのLiveObjectを参照として返す。
    /// 静的クラスのLiveObjectはtargetがnullのため、LiveObjectインスタンス自体を返し、
    /// シリアライザが@refとして解決する。
    /// </summary>
    [Serializable]
    [MovedFrom(true, "Lilium.RemoteControl.WebUI", "Lilium.RemoteControl.WebUI")]
    public class NavigateObjectSelector : ObjectSelectorBase
    {
        /// <summary>
        /// 表示するLiveObjectのID一覧。
        /// 各IDはLiveObjectRegistry上の静的クラスのtypeNameに対応する。
        /// </summary>
        public string[] objectIds = new string[0];

        protected override object[] GetObjects()
        {
            if (objectIds == null || objectIds.Length == 0)
                return new object[0];

            var result = new List<object>();
            for (int i = 0; i < objectIds.Length; i++)
            {
                var liveObject = LiveObjectRegistry.FindById(objectIds[i]);
                if (liveObject == null)
                {
                    // RuntimeInitializeOnLoad による static 登録が走る前や、
                    // LiveObjectRegistry のクリア後にアクセスされたケースを救済する。
                    var liveClass = LiveClass.Find(objectIds[i]);
                    if (liveClass != null && liveClass.isStatic)
                    {
                        liveObject = LiveObjectRegistry.GetOrCreate(liveClass.typeName, liveClass, null);
                    }
                }
                if (liveObject != null)
                    result.Add(liveObject);
            }
            return result.ToArray();
        }
    }
}
