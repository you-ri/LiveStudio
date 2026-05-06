// Copyright (c) You-Ri, 2026

using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Lilium.RemoteControl.UI.Editor
{
    public class DoublePropertyControl : SimplePropertyControl<DoubleField, double>
    {
        protected override double ConvertFromObject(object value)
        {
            return value is double d ? d : 0.0;
        }
    }
}
