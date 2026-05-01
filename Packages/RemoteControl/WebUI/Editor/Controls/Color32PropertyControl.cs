// Copyright (c) You-Ri, 2026

using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Lilium.RemoteControl.WebUI.Editor
{
    public class Color32PropertyControl : SimplePropertyControl<ColorField, Color>
    {
        protected override Color ConvertFromObject(object value)
        {
            return value is Color32 c32 ? (Color)c32 : Color.white;
        }

        protected override object ConvertToSetValue(Color value)
        {
            return (Color32)value;
        }
    }
}
