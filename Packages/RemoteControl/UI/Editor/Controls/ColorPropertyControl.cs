// Copyright (c) You-Ri, 2026

using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Lilium.RemoteControl.UI.Editor
{
    public class ColorPropertyControl : SimplePropertyControl<ColorField, Color>
    {
        protected override Color ConvertFromObject(object value)
        {
            return value is Color c ? c : Color.white;
        }
    }
}
