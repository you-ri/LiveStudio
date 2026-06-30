// Copyright (c) You-Ri, 2026
using UnityEngine;
using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Implemented by an exposed object whose manipulated <see cref="TransformValue"/> property is expressed
    /// relative to a dynamic parent (e.g. an avatar socket) rather than its own GameObject hierarchy. The
    /// Transform manipulator stays feature-agnostic: it asks the target for the parent world transform, the
    /// current local value, and a world pivot, then drives the same gizmo it uses for ordinary transforms.
    /// </summary>
    public interface IManipulatorTarget
    {
        /// <summary>
        /// Provides the manipulator with the parent world TRS, the current edited local TRS, and a world-space
        /// pivot to frame the camera on. Returns false when the state is not yet available (e.g. the parent has
        /// not resolved), in which case the out values are left at identity / zero.
        /// </summary>
        bool TryGetManipulatorState(out TransformValue parentWorld, out TransformValue targetLocal, out Vector3 pivotWorld);
    }
}
