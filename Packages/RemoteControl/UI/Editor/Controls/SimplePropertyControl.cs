// Copyright (c) You-Ri, 2026

using UnityEngine.UIElements;

namespace Lilium.RemoteControl.UI.Editor
{
    public abstract class SimplePropertyControl<TField, TValue> : IPropertyControl
        where TField : BaseField<TValue>, new()
    {
        protected abstract TValue ConvertFromObject(object value);

        protected virtual object ConvertToSetValue(TValue value)
        {
            return value;
        }

        public VisualElement CreateControl(PropertyControlContext ctx)
        {
            var field = new TField();
            field.value = ConvertFromObject(ctx.currentValue);
            field.SetEnabled(!ctx.isReadOnly);
            if (!ctx.isReadOnly)
            {
                var prop = ctx.prop;
                var isUpdatingUI = ctx.isUpdatingUI;
                field.RegisterValueChangedCallback(evt =>
                {
                    if (isUpdatingUI()) return;
                    prop.SetValue(ConvertToSetValue(evt.newValue));
                });
            }
            return field;
        }

        public void UpdateValue(VisualElement control, object value)
        {
            if (control is TField field)
            {
                field.SetValueWithoutNotify(ConvertFromObject(value));
            }
        }
    }
}
