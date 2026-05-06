// Copyright (c) You-Ri, 2026

using UnityEngine;
using UnityEngine.UIElements;

namespace Lilium.RemoteControl.UI.Editor
{
    public class Vector4PropertyControl : SimplePropertyControl<Vector4Field, Vector4>
    {
        protected override Vector4 ConvertFromObject(object value)
        {
            return value is Vector4 v ? v : Vector4.zero;
        }
    }
}
