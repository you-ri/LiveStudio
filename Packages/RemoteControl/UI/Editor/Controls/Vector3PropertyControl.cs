// Copyright (c) You-Ri, 2026

using UnityEngine;
using UnityEngine.UIElements;

namespace Lilium.RemoteControl.UI.Editor
{
    public class Vector3PropertyControl : SimplePropertyControl<Vector3Field, Vector3>
    {
        protected override Vector3 ConvertFromObject(object value)
        {
            return value is Vector3 v ? v : Vector3.zero;
        }
    }
}
