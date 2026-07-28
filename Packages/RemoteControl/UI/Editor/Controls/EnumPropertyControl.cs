// Copyright (c) You-Ri, 2026

using System;
using System.Linq;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Lilium.RemoteControl.UI.Editor
{
    public class EnumPropertyControl : IPropertyControl
    {
        public VisualElement CreateControl(PropertyControlContext ctx)
        {
            var valueType = ctx.propType.valueType;
            var liveEnum = LiveEnum.all.ContainsKey(valueType) ? LiveEnum.all[valueType] : null;

            if (liveEnum != null && liveEnum.values != null && liveEnum.values.Length > 0)
            {
                return _CreatePopupField(ctx, liveEnum, valueType);
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

        private VisualElement _CreatePopupField(PropertyControlContext ctx, LiveEnum liveEnum, Type valueType)
        {
            var names = liveEnum.values.Select(v => v.displayName).ToList();
            var currentIndex = 0;
            if (ctx.currentValue != null)
            {
                var intVal = Convert.ToInt32(ctx.currentValue);
                for (int i = 0; i < liveEnum.values.Length; i++)
                {
                    if (liveEnum.values[i].value == intVal) { currentIndex = i; break; }
                }
            }

            var popup = new PopupField<string>(names, currentIndex);
            popup.SetEnabled(!ctx.isReadOnly);
            if (!ctx.isReadOnly)
            {
                var prop = ctx.prop;
                var isUpdatingUI = ctx.isUpdatingUI;
                var capturedEnum = liveEnum;
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

            // PopupFieldからvalueTypeを特定するためLiveEnumを検索
            var valueType = value.GetType();
            var liveEnum = LiveEnum.all.ContainsKey(valueType) ? LiveEnum.all[valueType] : null;
            if (liveEnum == null) return;

            var intVal = Convert.ToInt32(value);
            for (int idx = 0; idx < liveEnum.values.Length; idx++)
            {
                if (liveEnum.values[idx].value == intVal)
                {
                    popup.index = idx;
                    break;
                }
            }
        }
    }
}
