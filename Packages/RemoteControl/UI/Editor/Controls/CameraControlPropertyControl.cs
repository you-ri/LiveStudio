// Copyright (c) You-Ri, 2026

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Lilium.RemoteControl.UI.Editor
{
    /// <summary>
    /// CameraController属性付きSerializeReferenceフィールド用コントロール。
    /// ドロップダウンで派生型を切り替え、選択した型のプロパティをフラットに表示する。
    /// </summary>
    public class CameraControlPropertyControl : IPropertyControl
    {
        private const float kPropertyNameWidth = 160f;

        /// <summary>
        /// ネストプロパティの配置先。設定されている場合、型切り替え時にここへ再構築する。
        /// 未設定の場合はcamera-control-container内に配置する。
        /// </summary>
        public VisualElement nestedPropsTarget { get; set; }

        public VisualElement CreateControl(PropertyControlContext ctx)
        {
            var cameraController = ctx.propType.controlAttribute as CameraControllerAttribute;
            if (cameraController == null || cameraController.options == null || cameraController.options.Length == 0)
                return new ReadOnlyLabelControl().CreateControl(ctx);

            // 縦並びコンテナ（ドロップダウン + ネストプロパティ）
            var container = new VisualElement();
            container.name = "camera-control-container";
            container.style.flexDirection = FlexDirection.Column;
            container.style.flexGrow = 1;
            container.style.flexShrink = 1;

            // 現在の型名を取得
            var currentTypeName = _GetCurrentTypeName(ctx.currentValue);
            var options = cameraController.options.ToList();

            var currentIndex = 0;
            if (!string.IsNullOrEmpty(currentTypeName))
            {
                var idx = options.IndexOf(currentTypeName);
                if (idx >= 0) currentIndex = idx;
            }

            // ドロップダウン
            var popup = new PopupField<string>(options, currentIndex);
            popup.name = "camera-control-popup";
            popup.SetEnabled(!ctx.isReadOnly);
            container.Add(popup);

            // ネストプロパティ（フラット表示、インデントなし）
            var propsContainer = _CreateNestedProperties(ctx, currentTypeName);
            propsContainer.name = "camera-control-props";
            container.Add(propsContainer);

            if (!ctx.isReadOnly)
            {
                var prop = ctx.prop;
                var obj = ctx.obj;
                var propType = ctx.propType;
                var isUpdatingUI = ctx.isUpdatingUI;

                popup.RegisterValueChangedCallback(evt =>
                {
                    if (isUpdatingUI()) return;

                    var selectedTypeName = evt.newValue;
                    var exposedClass = ExposedClass.Find(selectedTypeName);
                    if (exposedClass == null) return;

                    var newInstance = Activator.CreateInstance(exposedClass.type);
                    prop.SetValue(newInstance);

                    // プロパティ領域を再構築（外部ターゲットがあればそちらに配置）
                    var propsParent = nestedPropsTarget ?? container;
                    var oldProps = propsParent.Q("camera-control-props");
                    if (oldProps != null)
                        oldProps.RemoveFromHierarchy();

                    var newCtx = new PropertyControlContext
                    {
                        obj = obj,
                        propType = propType,
                        prop = prop,
                        currentValue = newInstance,
                        isReadOnly = false,
                        isUpdatingUI = isUpdatingUI
                    };
                    var newProps = _CreateNestedProperties(newCtx, selectedTypeName);
                    newProps.name = "camera-control-props";
                    propsParent.Add(newProps);
                });
            }

            return container;
        }

        public void UpdateValue(VisualElement control, object value)
        {
            var popup = control.Q<PopupField<string>>("camera-control-popup");
            if (popup == null) return;

            var currentTypeName = _GetCurrentTypeName(value);

            // 型が変わった場合はpopupを更新
            if (!string.IsNullOrEmpty(currentTypeName) && popup.value != currentTypeName)
            {
                if (popup.choices.Contains(currentTypeName))
                    popup.SetValueWithoutNotify(currentTypeName);
            }

            // 子プロパティの値を更新（外部ターゲットにある場合はそちらを検索）
            var propsContainer = (nestedPropsTarget ?? control).Q("camera-control-props");
            if (propsContainer == null) return;

            var parentProp = propsContainer.userData as ExposedProperty?;
            if (parentProp == null) return;

            foreach (var child in propsContainer.Children())
            {
                var childPath = child.userData as string;
                if (childPath == null) continue;

                var lastDot = childPath.LastIndexOf('.');
                if (lastDot < 0) continue;
                var childName = childPath.Substring(lastDot + 1);

                var childProp = parentProp.Value.GetProperty(childName);
                object childValue = null;
                try { if (childProp.HasValue) childValue = childProp.Value.GetValue(); } catch { }

                var propControl = child.Q(name: "prop-control");
                if (propControl is Foldout nestedFoldout)
                {
                    ReferencePropertyControl.UpdateNestedPropertyValues(nestedFoldout, childProp);
                }
                else if (propControl != null && childProp.HasValue)
                {
                    var propertyControl = PropertyControlFactory.GetControl(childProp.Value.type, true, childProp.Value.type.valueType);
                    propertyControl.UpdateValue(propControl, childValue);
                }
            }
        }

        private string _GetCurrentTypeName(object value)
        {
            if (value == null) return null;
            var exposedClass = ExposedClass.Find(value.GetType());
            return exposedClass != null ? exposedClass.typeName : null;
        }

        private VisualElement _CreateNestedProperties(PropertyControlContext ctx, string typeName)
        {
            var container = new VisualElement();

            if (string.IsNullOrEmpty(typeName) || !ctx.prop.isValid)
                return container;

            var exposedClass = ExposedClass.Find(typeName);
            if (exposedClass == null)
                return container;

            // UpdateValueで使用するためにpropを保持
            container.userData = (ExposedProperty?)ctx.prop;

            var childPropTypes = exposedClass.propertyTypes;
            if (childPropTypes == null || childPropTypes.Length == 0)
                return container;

            var sorted = childPropTypes.OrderBy(p => p.order).ToArray();
            foreach (var childPropType in sorted)
            {
                var childProp = ctx.prop.GetProperty(childPropType.name);
                object childValue = null;
                try { if (childProp.HasValue) childValue = childProp.Value.GetValue(); } catch { }

                var childPath = ctx.propType.name + "." + childPropType.name;

                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.marginBottom = 1;
                row.style.paddingTop = 1;
                row.style.paddingBottom = 1;
                row.style.alignItems = Align.Center;
                row.style.minHeight = 20;
                row.userData = childPath;

                var nameLabel = new Label(ObjectNames.NicifyVariableName(childPropType.name));
                nameLabel.style.width = kPropertyNameWidth;
                nameLabel.style.minWidth = kPropertyNameWidth;
                nameLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
                nameLabel.name = "prop-name";
                row.Add(nameLabel);

                var childCtx = new PropertyControlContext
                {
                    obj = ctx.obj,
                    propType = childPropType,
                    prop = childProp.HasValue ? childProp.Value : default,
                    currentValue = childValue,
                    isReadOnly = childPropType.isReadOnly,
                    isUpdatingUI = ctx.isUpdatingUI
                };

                if (childPropType.exposedValueClass != null && !childPropType.isExposedObjectReference)
                {
                    var refControl = new ReferencePropertyControl();
                    var childControl = refControl.CreateControl(childCtx);
                    childControl.name = "prop-control";
                    childControl.style.flexGrow = 1;
                    childControl.style.flexShrink = 1;
                    row.Add(childControl);
                }
                else
                {
                    var propertyControl = PropertyControlFactory.GetControl(childPropType, childProp.HasValue, childPropType.valueType);
                    var childControl = propertyControl.CreateControl(childCtx);
                    childControl.name = "prop-control";
                    childControl.style.flexGrow = 1;
                    childControl.style.flexShrink = 1;
                    row.Add(childControl);
                }

                container.Add(row);
            }

            return container;
        }
    }
}
