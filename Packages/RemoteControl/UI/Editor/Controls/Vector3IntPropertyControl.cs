// Copyright (c) You-Ri, 2026

using UnityEngine;
using UnityEngine.UIElements;

namespace Lilium.RemoteControl.UI.Editor
{
    public class Vector3IntPropertyControl : SimplePropertyControl<Vector3IntField, Vector3Int>
    {
        protected override Vector3Int ConvertFromObject(object value)
        {
            return value is Vector3Int v ? v : Vector3Int.zero;
        }
    }
}
