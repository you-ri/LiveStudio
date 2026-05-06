// Copyright (c) You-Ri, 2026

using UnityEngine.UIElements;

namespace Lilium.RemoteControl.UI.Editor
{
    public class StringPropertyControl : SimplePropertyControl<TextField, string>
    {
        protected override string ConvertFromObject(object value)
        {
            return value as string ?? "";
        }
    }
}
