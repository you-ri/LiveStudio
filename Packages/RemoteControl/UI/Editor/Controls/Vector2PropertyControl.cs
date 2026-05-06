// Copyright (c) You-Ri, 2026

using UnityEngine;
using UnityEngine.UIElements;

namespace Lilium.RemoteControl.UI.Editor
{
    public class Vector2PropertyControl : SimplePropertyControl<Vector2Field, Vector2>
    {
        protected override Vector2 ConvertFromObject(object value)
        {
            return value is Vector2 v ? v : Vector2.zero;
        }
    }
}
