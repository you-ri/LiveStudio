// Copyright (c) You-Ri, 2026

using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.UIElements;

namespace Lilium.RemoteControl.UI.Editor
{
    public class SliderPropertyControl : IPropertyControl
    {
        public VisualElement CreateControl(PropertyControlContext ctx)
        {
            var slider = ctx.propType.controlAttribute as SliderAttribute;
            if (slider == null) return new ReadOnlyLabelControl().CreateControl(ctx);

            var container = new VisualElement();
            container.style.flexDirection = FlexDirection.Row;
            container.style.flexGrow = 1;
            container.style.alignItems = Align.Center;

            var sliderField = new Slider(slider.minValue, slider.maxValue);
            sliderField.style.flexGrow = 1;
            sliderField.style.flexShrink = 1;
            sliderField.name = "prop-slider";

            if (ctx.currentValue is float fv)
                sliderField.value = fv;
            else if (ctx.currentValue is int iv)
                sliderField.value = iv;

            var valueField = new FloatField();
            valueField.style.width = 60;
            valueField.style.minWidth = 60;
            valueField.style.flexShrink = 0;
            valueField.style.marginLeft = 4;
            valueField.name = "prop-slider-value";
            valueField.value = sliderField.value;

            var prop = ctx.prop;
            var isUpdatingUI = ctx.isUpdatingUI;
            var isInt = ctx.propType.valueType == typeof(int);

            sliderField.RegisterValueChangedCallback(evt =>
            {
                if (isUpdatingUI()) return;
                valueField.SetValueWithoutNotify(evt.newValue);
                if (isInt)
                    prop.SetValue((int)evt.newValue);
                else
                    prop.SetValue(evt.newValue);
            });

            valueField.RegisterValueChangedCallback(evt =>
            {
                if (isUpdatingUI()) return;
                var clamped = Mathf.Clamp(evt.newValue, slider.minValue, slider.maxValue);
                sliderField.SetValueWithoutNotify(clamped);
                if (isInt)
                    prop.SetValue((int)clamped);
                else
                    prop.SetValue(clamped);
            });

            container.Add(sliderField);
            container.Add(valueField);
            return container;
        }

        public void UpdateValue(VisualElement control, object value)
        {
            var sliderField = control.Q<Slider>("prop-slider");
            if (sliderField == null) return;

            var fv = value is float f ? f : (value is int iv ? (float)iv : 0f);
            sliderField.SetValueWithoutNotify(fv);

            var sliderValueField = control.Q<FloatField>("prop-slider-value");
            if (sliderValueField != null)
                sliderValueField.SetValueWithoutNotify(fv);
        }
    }
}
