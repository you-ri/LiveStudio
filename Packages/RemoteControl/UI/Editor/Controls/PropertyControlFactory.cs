// Copyright (c) You-Ri, 2026

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Lilium.RemoteControl.UI.Editor
{
    public static class PropertyControlFactory
    {
        private static readonly Dictionary<Type, IPropertyControl> _controls = new Dictionary<Type, IPropertyControl>
        {
            { typeof(bool), new BoolPropertyControl() },
            { typeof(int), new IntPropertyControl() },
            { typeof(float), new FloatPropertyControl() },
            { typeof(double), new DoublePropertyControl() },
            { typeof(string), new StringPropertyControl() },
            { typeof(Vector2), new Vector2PropertyControl() },
            { typeof(Vector3), new Vector3PropertyControl() },
            { typeof(Vector4), new Vector4PropertyControl() },
            { typeof(Vector2Int), new Vector2IntPropertyControl() },
            { typeof(Vector3Int), new Vector3IntPropertyControl() },
            { typeof(Color), new ColorPropertyControl() },
            { typeof(Color32), new Color32PropertyControl() },
            { typeof(Quaternion), new QuaternionPropertyControl() },
            { typeof(TransformValue), new TransformValuePropertyControl() },
        };

        private static readonly EnumPropertyControl _enumControl = new EnumPropertyControl();
        private static readonly SliderPropertyControl _sliderControl = new SliderPropertyControl();
        private static readonly ReferencePropertyControl _referenceControl = new ReferencePropertyControl();
        private static readonly TypeSelectorPropertyControl _typeSelectorControl = new TypeSelectorPropertyControl();
        private static readonly CameraControlPropertyControl _cameraControlControl = new CameraControlPropertyControl();
        private static readonly ReadOnlyLabelControl _readOnlyLabelControl = new ReadOnlyLabelControl();

        public static IPropertyControl GetControl(ExposedPropertyType propType, bool hasProperty, Type valueType)
        {
            if (!hasProperty || valueType == null)
                return _readOnlyLabelControl;

            // Enum型
            if (valueType.IsEnum)
                return _enumControl;

            // TypeSelector属性
            if (propType.controlAttribute is TypeSelectorAttribute)
                return _typeSelectorControl;

            // CameraController属性
            if (propType.controlAttribute is CameraControllerAttribute)
                return _cameraControlControl;

            // Slider属性
            if (propType.controlAttribute is SliderAttribute && !propType.isReadOnly)
                return _sliderControl;

            // 型別コントロール
            if (_controls.TryGetValue(valueType, out var control))
                return control;

            // 参照型/構造体の子プロパティ展開
            if (propType.exposedValueClass != null && !propType.isExposedObjectReference)
                return _referenceControl;

            // フォールバック
            return _readOnlyLabelControl;
        }
    }
}
