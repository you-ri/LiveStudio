// Copyright (c) You-Ri, 2026

using System;
using UnityEngine;

namespace Lilium.RemoteControl.WebUI
{
    /// <summary>
    /// WebUIのページ定義。
    /// RemoteAppのScenePageに対応する。
    /// ExposedObjectContainer内の全オブジェクトをカテゴリ別にグルーピング表示する。
    /// Selectorは持たず、RemoteApp側でfetchAllにより全オブジェクトを取得する。
    /// </summary>
    [Serializable]
    [ExposedClass]
    public class ScenePage : IPage
    {
        public bool experimental = true;

        [SerializeReference, Select]
        public IObjectFactory factory = new StandardObjectFactory();
    }
}
