// Copyright (c) You-Ri, 2026

using UnityEngine;
using UnityEngine.UIElements;

namespace Lilium.RemoteControl.UI.Editor
{
    /// <summary>
    /// TransformValue (position / rotation / scale) 用のプロパティコントロール。
    /// rotation は euler 角で表示し、編集時に Quaternion に戻す
    /// (QuaternionPropertyControl と同戦略)。
    /// </summary>
    public class TransformValuePropertyControl : IPropertyControl
    {
        private const string kPositionLabel = "Position";
        private const string kRotationLabel = "Rotation";
        private const string kScaleLabel = "Scale";

        private class Controls
        {
            public Vector3Field positionField;
            public Vector3Field rotationField;
            public Vector3Field scaleField;
        }

        public VisualElement CreateControl(PropertyControlContext ctx)
        {
            var current = _FromObject(ctx.currentValue);

            var container = new VisualElement();
            container.style.flexDirection = FlexDirection.Column;

            var positionField = new Vector3Field(kPositionLabel) { value = current.position };
            var rotationField = new Vector3Field(kRotationLabel) { value = current.rotation.eulerAngles };
            var scaleField = new Vector3Field(kScaleLabel) { value = current.scale };

            positionField.SetEnabled(!ctx.isReadOnly);
            rotationField.SetEnabled(!ctx.isReadOnly);
            scaleField.SetEnabled(!ctx.isReadOnly);

            container.Add(positionField);
            container.Add(rotationField);
            container.Add(scaleField);

            var controls = new Controls
            {
                positionField = positionField,
                rotationField = rotationField,
                scaleField = scaleField,
            };
            container.userData = controls;

            if (!ctx.isReadOnly)
            {
                var prop = ctx.prop;
                var isUpdatingUI = ctx.isUpdatingUI;

                positionField.RegisterValueChangedCallback(evt =>
                {
                    if (isUpdatingUI()) return;
                    var v = _Current(controls);
                    v.position = evt.newValue;
                    prop.SetValue(v);
                });
                rotationField.RegisterValueChangedCallback(evt =>
                {
                    if (isUpdatingUI()) return;
                    var v = _Current(controls);
                    v.rotation = Quaternion.Euler(evt.newValue);
                    prop.SetValue(v);
                });
                scaleField.RegisterValueChangedCallback(evt =>
                {
                    if (isUpdatingUI()) return;
                    var v = _Current(controls);
                    v.scale = evt.newValue;
                    prop.SetValue(v);
                });
            }

            return container;
        }

        public void UpdateValue(VisualElement control, object value)
        {
            if (control?.userData is not Controls controls) return;
            var v = _FromObject(value);
            controls.positionField.SetValueWithoutNotify(v.position);
            controls.rotationField.SetValueWithoutNotify(v.rotation.eulerAngles);
            controls.scaleField.SetValueWithoutNotify(v.scale);
        }

        private static TransformValue _FromObject(object value)
            => value is TransformValue v ? v : TransformValue.identity;

        private static TransformValue _Current(Controls c)
            => new TransformValue(
                c.positionField.value,
                Quaternion.Euler(c.rotationField.value),
                c.scaleField.value);
    }
}
