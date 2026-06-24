// Copyright (c) You-Ri, 2026

using System;
using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Switches the active avatar on the rising edge. Drives the same exclusive (radio) selection as
    /// toggling an <see cref="AvatarAsset"/>'s enabled flag, via <see cref="ExternalAssetManager"/>.
    /// </summary>
    [Serializable]
    [ExposedClass(Category = "Action", Icon = "face")]
    public class SwitchAvatarAction : ActionBase
    {
        [ExposedField, StringSelector(nameof(avatarNames))]
        public string avatar = string.Empty;

        /// <summary>Registered avatar names (empty entry = default avatar) — the dropdown source.</summary>
        [ExposedProperty, Hide]
        public string[] avatarNames
            => ExternalAssetManager.current?.GetAvatarNames() ?? Array.Empty<string>();

        public override void Apply(in ActionContext context)
        {
            if (!context.pressed) return;
            ExternalAssetManager.current?.SelectAvatarByName(avatar);
        }
    }
}
