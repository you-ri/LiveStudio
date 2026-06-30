// Copyright (c) You-Ri, 2026

using System;
using UnityEngine.Scripting.APIUpdating;
using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Makes the named set the active stage when the input triggers (on release in Button mode, on press
    /// otherwise), via <see cref="StageManager"/>. This is a complete switch: any other loaded set is
    /// unloaded, and the named set is loaded on demand and activated once its scene is ready.
    /// </summary>
    [Serializable]
    [ExposedClass(Category = "Operation", Icon = "public")]
    [MovedFrom(false, null, null, "SwitchStageAction")]
    [FormerlyExposedAs("SwitchStageAction")]
    public class SwitchStageOperation : OperationBase
    {
        [ExposedField, StringSelector(nameof(stageNames))]
        public string stage = string.Empty;

        /// <summary>Names of the known sets (including the bootstrap set) — the dropdown source.</summary>
        [ExposedProperty, Hide]
        public string[] stageNames
            => StageManager.current?.GetSetNames() ?? Array.Empty<string>();

        public override void Apply(in OperationContext context)
        {
            if (!context.triggered) return;
            StageManager.current?.SwitchToSetByName(stage);
        }
    }
}
