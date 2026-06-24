// Copyright (c) You-Ri, 2026

using System;
using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Base of an action set's receiving side: an operation run when the set fires. Concrete kinds set an
    /// expression, switch the avatar/stage, toggle a GameObject, etc. Each kind decides how it reads the
    /// <see cref="ActionContext"/> (continuous <c>value</c> vs the <c>pressed</c> edge vs <c>active</c>).
    ///
    /// Named <c>ActionBase</c> (not <c>Action</c>) to avoid colliding with <see cref="System.Action"/>;
    /// the user-facing concept is "Action". <c>[ExposedClass]</c> mirrors <see cref="InputSource"/> /
    /// <see cref="ICameraController"/>; only the concrete kinds are ever instantiated.
    /// </summary>
    [Serializable]
    [ExposedClass]
    public abstract class ActionBase
    {
        /// <summary>Runs the action for the current frame's input state.</summary>
        public abstract void Apply(in ActionContext context);
    }
}
