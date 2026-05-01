// Copyright (c) You-Ri, 2026

using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Lilium.RemoteControl.WebUI.Editor
{
    public class IntPropertyControl : SimplePropertyControl<IntegerField, int>
    {
        protected override int ConvertFromObject(object value)
        {
            return value is int i ? i : 0;
        }
    }
}
