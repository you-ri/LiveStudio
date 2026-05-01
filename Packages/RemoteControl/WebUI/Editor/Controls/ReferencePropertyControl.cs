// Copyright (c) You-Ri, 2026

using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Lilium.RemoteControl.WebUI.Editor
{
    public class ReferencePropertyControl : IPropertyControl
    {
        private const int kMaxReferenceDepth = 5;
        private const float kPropertyNameWidth = 160f;

        public VisualElement CreateControl(PropertyControlContext ctx)
        {
            return _CreateFoldout(ctx, 0, null);
        }

        public void UpdateValue(VisualElement control, object value)
        {
            // Foldout内のネストされたプロパティは_UpdateNestedで更新される
        }

        public static void UpdateNestedPropertyValues(Foldout foldout, ExposedProperty? parentProp)
        {
            if (parentProp == null || !parentProp.HasValue) return;

            foreach (var child in foldout.Children())
            {
                var childPath = child.userData as string;
                if (childPath == null) continue;

                var lastDot = childPath.LastIndexOf('.');
                if (lastDot < 0) continue;
                var childName = childPath.Substring(lastDot + 1);

                var childProp = parentProp.Value.GetProperty(childName);
                object childValue = null;
                try { if (childProp.HasValue) childValue = childProp.Value.GetValue(); } catch { }

                var control = child.Q(name: "prop-control");
                if (control is Foldout nestedFoldout)
                {
                    UpdateNestedPropertyValues(nestedFoldout, childProp);
                }
                else if (control != null && childProp.HasValue)
                {
                    var propertyControl = PropertyControlFactory.GetControl(childProp.Value.type, true, childProp.Value.type.valueType);
                    propertyControl.UpdateValue(control, childValue);
                }
            }
        }

        private VisualElement _CreateFoldout(PropertyControlContext ctx, int depth, string parentPath)
        {
            var foldout = new Foldout();
            foldout.text = ctx.propType.exposedValueClass.typeName ?? ctx.propType.valueType.Name;
            foldout.value = false;
            foldout.name = "prop-foldout";

            if (depth >= kMaxReferenceDepth || !ctx.prop.isValid)
            {
                var depthLabel = new Label(depth >= kMaxReferenceDepth ? "(max depth)" : "(null)");
                depthLabel.style.color = new Color(0.5f, 0.5f, 0.5f);
                foldout.Add(depthLabel);
                return foldout;
            }

            var childPropTypes = ctx.propType.exposedValueClass.propertyTypes;
            if (childPropTypes == null || childPropTypes.Length == 0)
                return foldout;

            var sorted = childPropTypes.OrderBy(p => p.order).ToArray();
            foreach (var childPropType in sorted)
            {
                var childProp = ctx.prop.GetProperty(childPropType.name);
                object childValue = null;
                try { if (childProp.HasValue) childValue = childProp.Value.GetValue(); } catch { }

                var childPath = string.IsNullOrEmpty(parentPath)
                    ? ctx.propType.name + "." + childPropType.name
                    : parentPath + "." + childPropType.name;

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

                if (childPropType.exposedValueClass != null && !childPropType.isExposedObjectReference)
                {
                    var childCtx = new PropertyControlContext
                    {
                        obj = ctx.obj,
                        propType = childPropType,
                        prop = childProp.HasValue ? childProp.Value : default,
                        currentValue = childValue,
                        isReadOnly = childPropType.isReadOnly,
                        isUpdatingUI = ctx.isUpdatingUI
                    };
                    var childFoldout = _CreateFoldout(childCtx, depth + 1, childPath);
                    childFoldout.name = "prop-control";
                    childFoldout.style.flexGrow = 1;
                    childFoldout.style.flexShrink = 1;
                    row.Add(childFoldout);
                }
                else
                {
                    var propertyControl = PropertyControlFactory.GetControl(childPropType, childProp.HasValue, childPropType.valueType);
                    var childCtx = new PropertyControlContext
                    {
                        obj = ctx.obj,
                        propType = childPropType,
                        prop = childProp.HasValue ? childProp.Value : default,
                        currentValue = childValue,
                        isReadOnly = childPropType.isReadOnly,
                        isUpdatingUI = ctx.isUpdatingUI
                    };
                    var control = propertyControl.CreateControl(childCtx);
                    control.name = "prop-control";
                    control.style.flexGrow = 1;
                    control.style.flexShrink = 1;
                    row.Add(control);
                }

                foldout.Add(row);
            }

            return foldout;
        }
    }
}
