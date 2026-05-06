// Copyright (c) You-Ri, 2026

using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Lilium.RemoteControl.UI.Editor
{
    public class FloatPropertyControl : SimplePropertyControl<FloatField, float>
    {
        protected override float ConvertFromObject(object value)
        {
            return value is float f ? f : 0f;
        }
    }
}
