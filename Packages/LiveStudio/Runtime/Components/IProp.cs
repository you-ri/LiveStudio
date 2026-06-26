// Copyright (c) You-Ri, 2026

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Marker for a prop behavior component — the single, mutually-exclusive component on a prop
    /// GameObject loaded from a <c>*.prop.lsb</c>: <see cref="Prop"/> (plain socket follow),
    /// <see cref="AvatarItem"/> (avatar parameter bridge + expressions) or <see cref="AvatarChair"/>
    /// (chair controls + damped follow). Exactly one <see cref="IProp"/> lives on a prop root; each owns
    /// its own lifecycle and a shared <see cref="PropAttachment"/> (socket name + offsets + follow math),
    /// so there is no inter-component coordination (mirrors the avatar main axis, where one
    /// <c>IAvatar</c> component owns the GameObject).
    /// </summary>
    public interface IProp { }
}
