// Copyright (c) You-Ri, 2026

using System;
using UnityEngine;
using UnityEngine.InputSystem;
using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Base of an action set's firing side: produces a 0..1 value each frame that the set's actions read.
    /// Concrete kinds (e.g. <see cref="KeyInputSource"/>) supply the raw input; this base applies the
    /// <see cref="mode"/> and derives the rising/falling edges into an <see cref="ActionContext"/>.
    ///
    /// Marked <c>[ExposedClass]</c> on the abstract base (like <see cref="ICameraController"/>) so the
    /// <c>[TypeSelector]</c> on the owning field can enumerate the concrete kinds; the base itself is
    /// never instantiated (the field always defaults to a concrete kind and @type only ever names one).
    /// </summary>
    [Serializable]
    [ExposedClass]
    public abstract class InputSource
    {
        protected const float kThreshold = 0.5f;

        [ExposedField]
        public InputMode mode = InputMode.Button;

        [NonSerialized] private bool _toggleState;
        [NonSerialized] private bool _prevActive;

        /// <summary>
        /// Builds whatever runtime input the source needs into the shared map. Called by
        /// <see cref="ActionManager"/> when the source is bound (and rebuilt on a type swap), mirroring
        /// <see cref="ICameraController.Setup"/>. <paramref name="actionName"/> is unique per action set.
        /// </summary>
        public virtual void Setup(InputActionMap map, string actionName) { }

        /// <summary>Tears down what <see cref="Setup"/> built. Idempotent.</summary>
        public virtual void Teardown(InputActionMap map) { }

        /// <summary>Current raw input in 0..1 (e.g. button 0/1, analog axis). 0 when unbound.</summary>
        protected abstract float ReadRawValue();

        /// <summary>
        /// Reads the raw input and folds in <see cref="mode"/> and edge detection. Allocation-free; safe
        /// to call every frame. Edges track the raw input so a press fires on key-down regardless of mode.
        /// </summary>
        public ActionContext Evaluate()
        {
            float raw = ReadRawValue();
            bool rawActive = raw > kThreshold;

            float output;
            switch (mode)
            {
                case InputMode.Toggle:
                    if (rawActive && !_prevActive) _toggleState = !_toggleState;
                    output = _toggleState ? 1f : 0f;
                    break;
                case InputMode.Value:
                case InputMode.Button:
                default:
                    output = Mathf.Clamp01(raw);
                    break;
            }

            bool pressed = rawActive && !_prevActive;
            bool released = !rawActive && _prevActive;
            _prevActive = rawActive;

            return new ActionContext(output, pressed, released, output > kThreshold);
        }

        /// <summary>Resets latched runtime state (toggle / edge), e.g. when (re)bound.</summary>
        public void ResetState()
        {
            _toggleState = false;
            _prevActive = false;
        }
    }
}
