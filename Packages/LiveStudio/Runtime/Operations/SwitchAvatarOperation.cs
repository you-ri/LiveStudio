// Copyright (c) You-Ri, 2026

using System;
using UnityEngine.Scripting.APIUpdating;
using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Switches the active avatar when the input triggers (on release in Button mode, on press otherwise).
    /// Drives the same exclusive (radio) selection as toggling an <see cref="AvatarAsset"/>'s enabled flag,
    /// via <see cref="ExternalAssetManager"/>.
    /// </summary>
    [Serializable]
    [ExposedClass(Category = "Operation", Icon = "face")]
    [MovedFrom(false, null, null, "SwitchAvatarAction")]
    [FormerlyExposedAs("SwitchAvatarAction")]
    public class SwitchAvatarOperation : OperationBase
    {
        [ExposedField, StringSelector(nameof(avatarNames))]
        public string avatar = string.Empty;

        /// <summary>Registered avatar names (empty entry = default avatar) — the dropdown source.</summary>
        [ExposedProperty, Hide]
        public string[] avatarNames
            => ExternalAssetManager.current?.GetAvatarNames() ?? Array.Empty<string>();

        public override void Apply(in OperationContext context)
        {
            if (!context.triggered) return;
            ExternalAssetManager.current?.SelectAvatarByName(avatar);
        }
    }
}
