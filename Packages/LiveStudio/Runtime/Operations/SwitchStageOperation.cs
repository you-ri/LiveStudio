// Copyright (c) You-Ri, 2026

using System;
using UnityEngine.Scripting.APIUpdating;
using Lilium.RemoteControl;
using Lilium.RemoteControl.Frames;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Makes the named set the active stage when the input triggers (on release in Button mode, on press
    /// otherwise), via <see cref="StageManager"/>. This is a complete switch: any other loaded set is
    /// unloaded, and the named set is loaded on demand and activated once its scene is ready.
    /// </summary>
    [Serializable]
    [LiveClass(Category = "Operation", Icon = "public")]
    [MovedFrom(false, null, null, "SwitchStageAction")]
    [FormerlyNamedAs("SwitchStageAction")]
    public class SwitchStageOperation : OperationBase
    {
        [LiveField, StringSelector(nameof(stageNames))]
        public string stage = string.Empty;

        /// <summary>Names of the known sets (including the bootstrap set) — the dropdown source.</summary>
        [LiveProperty, Hide]
        public string[] stageNames
            => StageManager.current?.GetSetNames() ?? Array.Empty<string>();

        /// <summary>The operator's own controls, as a producer. See the assembly declaration.</summary>
        private static readonly FrameSource _source = FrameGate.ResolveSource("operation");

        public override void Apply(in OperationContext context)
        {
            if (!context.triggered) return;

            var manager = StageManager.current;
            if (manager == null) return;

            // Sent through the gate under the same address a remote call would use, so the recorded
            // operation replays through exactly that path.
            FrameGate.Post(EventKind.FunctionCall, _source, "POST",
                $"/live/function/{manager.id}/switchtosetbyname",
                () => StageManager.current?.SwitchToSetByName(stage),
                OperationRequest.FromArguments(stage));
        }
    }
}
