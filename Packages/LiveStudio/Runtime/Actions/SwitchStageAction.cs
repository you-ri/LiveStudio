// Copyright (c) You-Ri, 2026

using System;
using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Makes the named set the active stage on the rising edge, via <see cref="StageManager"/>.
    /// Only loaded sets can be activated; activating an unloaded set is a no-op (logged by the manager).
    /// </summary>
    [Serializable]
    [ExposedClass(Category = "Action", Icon = "public")]
    public class SwitchStageAction : ActionBase
    {
        [ExposedField, StringSelector(nameof(stageNames))]
        public string stage = string.Empty;

        /// <summary>Names of the known sets (including the bootstrap set) — the dropdown source.</summary>
        [ExposedProperty, Hide]
        public string[] stageNames
            => StageManager.current?.GetSetNames() ?? Array.Empty<string>();

        public override void Apply(in ActionContext context)
        {
            if (!context.pressed) return;
            StageManager.current?.SetActiveSetByName(stage);
        }
    }
}
