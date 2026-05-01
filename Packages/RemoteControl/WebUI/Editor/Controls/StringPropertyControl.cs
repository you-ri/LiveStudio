// Copyright (c) You-Ri, 2026

using UnityEngine.UIElements;

namespace Lilium.RemoteControl.WebUI.Editor
{
    public class StringPropertyControl : SimplePropertyControl<TextField, string>
    {
        protected override string ConvertFromObject(object value)
        {
            return value as string ?? "";
        }
    }
}
