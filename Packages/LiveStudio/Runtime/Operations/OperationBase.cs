// Copyright (c) You-Ri, 2026

using System;
using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Base of an operation set's receiving side: an operation run when the set fires. Concrete kinds set an
    /// expression, switch the avatar/stage, toggle a GameObject, etc. Each kind decides how it reads the
    /// <see cref="OperationContext"/> (continuous <c>value</c> vs the <c>pressed</c> edge vs <c>active</c>).
    ///
    /// Named <c>OperationBase</c> (not bare <c>Operation</c>) so the abstract base and its concrete kinds read
    /// distinctly, mirroring <see cref="InputSource"/> / <see cref="CameraControllerBase"/>; the user-facing
    /// concept is "Operation". <c>[LiveClass]</c> on the base lets the owning field's <c>[TypeSelector]</c>
    /// enumerate the concrete kinds; only the concrete kinds are ever instantiated.
    /// </summary>
    [Serializable]
    [LiveClass]
    public abstract class OperationBase
    {
        /// <summary>Runs the operation for the current frame's input state.</summary>
        public abstract void Apply(in OperationContext context);
    }
}
