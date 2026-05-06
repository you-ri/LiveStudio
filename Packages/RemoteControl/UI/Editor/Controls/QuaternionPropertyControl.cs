// Copyright (c) You-Ri, 2026

using UnityEngine;
using UnityEngine.UIElements;

namespace Lilium.RemoteControl.UI.Editor
{
    public class QuaternionPropertyControl : SimplePropertyControl<Vector3Field, Vector3>
    {
        protected override Vector3 ConvertFromObject(object value)
        {
            return value is Quaternion q ? q.eulerAngles : Vector3.zero;
        }

        protected override object ConvertToSetValue(Vector3 value)
        {
            return Quaternion.Euler(value);
        }
    }
}
