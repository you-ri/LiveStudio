// Copyright (c) You-Ri, 2026

using UnityEngine;
using UnityEngine.UIElements;

namespace Lilium.RemoteControl.UI.Editor
{
    public class Vector2IntPropertyControl : SimplePropertyControl<Vector2IntField, Vector2Int>
    {
        protected override Vector2Int ConvertFromObject(object value)
        {
            return value is Vector2Int v ? v : Vector2Int.zero;
        }
    }
}
