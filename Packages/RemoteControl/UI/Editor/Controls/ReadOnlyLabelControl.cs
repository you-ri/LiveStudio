// Copyright (c) You-Ri, 2026

using UnityEngine;
using UnityEngine.UIElements;

namespace Lilium.RemoteControl.UI.Editor
{
    public class ReadOnlyLabelControl : IPropertyControl
    {
        public VisualElement CreateControl(PropertyControlContext ctx)
        {
            var label = new Label(ctx.currentValue != null ? ctx.currentValue.ToString() : "null");
            label.AddToClassList("uid-readonly-value");
            return label;
        }

        public void UpdateValue(VisualElement control, object value)
        {
            if (control is Label label)
            {
                label.text = value != null ? value.ToString() : "null";
            }
        }
    }
}
