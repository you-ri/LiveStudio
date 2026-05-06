// Copyright (c) You-Ri, 2026

using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Lilium.RemoteControl.UI.Editor
{
    public static class FunctionRowControl
    {
        public static VisualElement CreateFunctionRow(ExposedObject obj, ExposedFunctionType funcType)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom = 2;
            row.style.paddingTop = 2;
            row.style.paddingBottom = 2;
            row.style.alignItems = Align.Center;

            var paramText = "";
            if (funcType.parameters != null && funcType.parameters.Length > 0)
            {
                paramText = string.Join(", ", funcType.parameters.Select(p => $"{p.ParameterType.Name} {p.Name}"));
            }
            var returnTypeName = funcType.returnType != null && funcType.returnType != typeof(void) ? funcType.returnType.Name : "void";
            var displayName = $"{returnTypeName}  {ObjectNames.NicifyVariableName(funcType.name)}({paramText})";

            var nameLabel = new Label(displayName);
            nameLabel.style.flexGrow = 1;
            nameLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            row.Add(nameLabel);

            if (funcType.isStatic)
            {
                var badge = new Label("[S]");
                badge.style.color = new Color(0.4f, 0.6f, 0.4f);
                badge.style.marginLeft = 4;
                badge.style.flexShrink = 0;
                row.Add(badge);
            }

            if (funcType.parameters == null || funcType.parameters.Length == 0)
            {
                var invokeButton = new Button(() =>
                {
                    obj.InvokeFunction(funcType.apiName, null);
                });
                invokeButton.text = "Invoke";
                invokeButton.style.width = 60;
                invokeButton.style.flexShrink = 0;
                row.Add(invokeButton);
            }

            return row;
        }
    }
}
