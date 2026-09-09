// Copyright (c) You-Ri, 2026

using System;

using UnityEngine;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Obsolete. The screen's settings moved to <see cref="ScreenManager"/>, a persistent object in the
    /// <c>Live Studio System</c> prefab, so that the camera they apply to can be whichever one the
    /// standing set brought rather than one fixed component's own GameObject.
    ///
    /// The class is kept as an empty component so that a scene or prefab still referencing it opens
    /// without a missing script — the reference resolves, the component does nothing, and it can be
    /// removed at leisure. It exposes nothing: <c>Screen</c> as a live class is
    /// <see cref="ScreenManager"/>'s name now, and two types claiming it would fight over the settings
    /// file. Settings saved while this component owned them are carried over on load; see
    /// <c>ProjectSettingsSerializer</c>.
    /// </summary>
    [Obsolete("Screen settings moved to ScreenManager, which finds the output camera instead of living on one. This component does nothing; remove it from your scenes.")]
    [AddComponentMenu("")]
    public class ScreenController : MonoBehaviour
    {
    }
}
