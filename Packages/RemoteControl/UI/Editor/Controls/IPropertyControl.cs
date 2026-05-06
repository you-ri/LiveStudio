// Copyright (c) You-Ri, 2026

using UnityEngine.UIElements;

namespace Lilium.RemoteControl.UI.Editor
{
    public interface IPropertyControl
    {
        VisualElement CreateControl(PropertyControlContext context);
        void UpdateValue(VisualElement control, object value);
    }
}
