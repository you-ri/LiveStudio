// Copyright (c) You-Ri, 2026

using System;
using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Sets a facial expression's weight to the input's continuous value (0..1). Reuses the active
    /// avatar's <see cref="IAvatarExpression"/>, so it follows whatever avatar is currently loaded.
    /// </summary>
    [Serializable]
    [ExposedClass(Category = "Action", Icon = "mood")]
    public class SetExpressionAction : ActionBase
    {
        [ExposedField, StringSelector(nameof(expressionNames))]
        public string expression = string.Empty;

        /// <summary>Available expression names on the current avatar — the dropdown source.</summary>
        [ExposedProperty, Hide]
        public string[] expressionNames
        {
            get
            {
                var expr = _Expression();
                if (expr == null) return Array.Empty<string>();
                var keys = expr.GetAvailableExpressions();
                var names = new string[keys.Length];
                for (int i = 0; i < keys.Length; i++) names[i] = keys[i].name;
                return names;
            }
        }

        public override void Apply(in ActionContext context)
        {
            if (string.IsNullOrEmpty(expression)) return;
            _Expression()?.SetExpressionWeight(FacialKey.CreateCustom(expression), context.value);
        }

        // The active avatar's expression service, or null when no avatar is loaded.
        private static IAvatarExpression _Expression()
        {
            var list = Service<IAvatarExpression>.subjects;
            return (list != null && list.Count > 0) ? list[0] : null;
        }
    }
}
