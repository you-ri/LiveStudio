// Copyright (c) You-Ri, 2026

using System;
using System.Linq;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Lilium.RemoteControl.WebUI.Editor
{
    public class EnumPropertyControl : IPropertyControl
    {
        public VisualElement CreateControl(PropertyControlContext ctx)
        {
            var valueType = ctx.propType.valueType;
            var exposedEnum = ExposedEnum.all.ContainsKey(valueType) ? ExposedEnum.all[valueType] : null;

            if (exposedEnum != null && exposedEnum.values != null && exposedEnum.values.Length > 0)
            {
                return _CreatePopupField(ctx, exposedEnum, valueType);
            }

            if (ctx.currentValue is Enum enumVal)
            {
                return _CreateEnumField(ctx, enumVal);
            }

            return new ReadOnlyLabelControl().CreateControl(ctx);
        }

        public void UpdateValue(VisualElement control, object value)
        {
            if (control is PopupField<string> popup)
            {
                _UpdatePopupField(popup, value);
            }
            else if (control is EnumField enumField && value is Enum enumVal)
            {
                enumField.SetValueWithoutNotify(enumVal);
            }
        }

        private VisualElement _CreatePopupField(PropertyControlContext ctx, ExposedEnum exposedEnum, Type valueType)
        {
            var names = exposedEnum.values.Select(v => v.displayName).ToList();
            var currentIndex = 0;
            if (ctx.currentValue != null)
            {
                var intVal = Convert.ToInt32(ctx.currentValue);
                for (int i = 0; i < exposedEnum.values.Length; i++)
                {
                    if (exposedEnum.values[i].value == intVal) { currentIndex = i; break; }
                }
            }

            var popup = new PopupField<string>(names, currentIndex);
            popup.SetEnabled(!ctx.isReadOnly);
            if (!ctx.isReadOnly)
            {
                var prop = ctx.prop;
                var isUpdatingUI = ctx.isUpdatingUI;
                var capturedEnum = exposedEnum;
                var capturedType = valueType;
                popup.RegisterValueChangedCallback(evt =>
                {
                    if (isUpdatingUI()) return;
                    var idx = popup.index;
                    if (idx >= 0 && idx < capturedEnum.values.Length)
                    {
                        var enumValue = Enum.ToObject(capturedType, capturedEnum.values[idx].value);
                        prop.SetValue(enumValue);
                    }
                });
            }
            return popup;
        }

        private VisualElement _CreateEnumField(PropertyControlContext ctx, Enum enumVal)
        {
            var field = new EnumField(enumVal);
            field.SetEnabled(!ctx.isReadOnly);
            if (!ctx.isReadOnly)
            {
                var prop = ctx.prop;
                var isUpdatingUI = ctx.isUpdatingUI;
                field.RegisterValueChangedCallback(evt =>
                {
                    if (isUpdatingUI()) return;
                    prop.SetValue(evt.newValue);
                });
            }
            return field;
        }

        private void _UpdatePopupField(PopupField<string> popup, object value)
        {
            if (value == null) return;

            // PopupFieldからvalueTypeを特定するためExposedEnumを検索
            var valueType = value.GetType();
            var exposedEnum = ExposedEnum.all.ContainsKey(valueType) ? ExposedEnum.all[valueType] : null;
            if (exposedEnum == null) return;

            var intVal = Convert.ToInt32(value);
            for (int idx = 0; idx < exposedEnum.values.Length; idx++)
            {
                if (exposedEnum.values[idx].value == intVal)
                {
                    popup.index = idx;
                    break;
                }
            }
        }
    }
}
