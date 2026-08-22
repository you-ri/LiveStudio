// Copyright (c) You-Ri, 2026

using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Lilium.RemoteControl.UI.Editor
{
    public static class FunctionRowControl
    {
        public static VisualElement CreateFunctionRow(LiveObjectHandle obj, LiveFunctionType funcType)
        {
            var row = new VisualElement();
            row.AddToClassList("uid-function-row");

            var paramText = "";
            if (funcType.parameters != null && funcType.parameters.Length > 0)
            {
                paramText = string.Join(", ", funcType.parameters.Select(p => $"{p.ParameterType.Name} {p.Name}"));
            }
            var returnTypeName = funcType.returnType != null && funcType.returnType != typeof(void) ? funcType.returnType.Name : "void";
            var displayName = $"{returnTypeName}  {ObjectNames.NicifyVariableName(funcType.name)}({paramText})";

            var nameLabel = new Label(displayName);
            nameLabel.AddToClassList("uid-function-name");
            row.Add(nameLabel);

            if (funcType.isStatic)
            {
                var badge = new Label("[S]");
                badge.AddToClassList("uid-badge");
                badge.AddToClassList("uid-badge--static");
                row.Add(badge);
            }

            if (funcType.parameters == null || funcType.parameters.Length == 0)
            {
                var invokeButton = new Button(() =>
                {
                    obj.InvokeFunction(funcType.apiName, null);
                });
                invokeButton.text = "Invoke";
                invokeButton.AddToClassList("uid-invoke-button");
                row.Add(invokeButton);
            }

            return row;
        }
    }
}
