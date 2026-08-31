// Copyright (c) You-Ri, 2026

using System;
using UnityEngine.Scripting.APIUpdating;
using Lilium.RemoteControl;
using Lilium.RemoteControl.Frames;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Switches the active avatar when the input triggers (on release in Button mode, on press otherwise).
    /// Drives the same exclusive (radio) selection as toggling an <see cref="AvatarAsset"/>'s enabled flag,
    /// via <see cref="ExternalAssetManager"/>.
    /// </summary>
    [Serializable]
    [LiveClass(Category = "Operation", Icon = "face")]
    [MovedFrom(false, null, null, "SwitchAvatarAction")]
    [FormerlyNamedAs("SwitchAvatarAction")]
    public class SwitchAvatarOperation : OperationBase
    {
        [LiveField, StringSelector(nameof(avatarNames))]
        public string avatar = string.Empty;

        /// <summary>Registered avatar names (empty entry = default avatar) — the dropdown source.</summary>
        [LiveProperty, Hide]
        public string[] avatarNames
            => AvatarSelection.GetNames(ExternalAssetManager.current);

        /// <summary>The operator's own controls, as a producer. See the assembly declaration.</summary>
        private static readonly FrameSource _source = FrameGate.ResolveSource("operation");

        public override void Apply(in OperationContext context)
        {
            if (!context.triggered) return;

            var manager = ExternalAssetManager.current;
            if (manager == null) return;

            FrameGate.Post(EventKind.FunctionCall, _source, "POST",
                $"/live/function/{manager.id}/selectavatarbyname",
                () => AvatarSelection.SelectByName(ExternalAssetManager.current, avatar),
                OperationRequest.FromArguments(avatar));
        }
    }
}
