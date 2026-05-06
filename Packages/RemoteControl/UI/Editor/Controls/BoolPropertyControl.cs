// Copyright (c) You-Ri, 2026

using UnityEngine.UIElements;

namespace Lilium.RemoteControl.UI.Editor
{
    public class BoolPropertyControl : SimplePropertyControl<Toggle, bool>
    {
        protected override bool ConvertFromObject(object value)
        {
            return value is bool b && b;
        }
    }
}
