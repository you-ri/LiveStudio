// Copyright (c) You-Ri, 2026

using System;

using UnityEngine;

using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// A catalog entry for an app-embedded (built-in) <see cref="AnimationClip"/>. Registered as a built-in
    /// kind by <see cref="BuiltinAssetTypeRegistry"/>, so every standalone clip under a <c>Resources</c>
    /// folder is baked into <see cref="BuiltinAssetCatalog"/> and listed here — selectable e.g. for the
    /// avatar body-override slot. All behavior is the default reference-resource behavior of
    /// <see cref="BuiltinAssetBase"/>; this type exists to give the kind its own exposed <c>@type</c>, which
    /// the remote app renders with the animation icon.
    /// </summary>
    [Serializable]
    [ExposedClass("BuiltinAnimationAsset", Category = "Asset", Icon = "animation")]
    public class BuiltinAnimationAsset : BuiltinAssetBase
    {
    }
}
