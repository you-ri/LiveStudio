// Copyright (c) You-Ri, 2026

namespace Lilium.LiveStudio
{
    /// <summary>
    /// The per-frame evaluation result of an <see cref="InputSource"/>, passed to each
    /// <see cref="OperationBase"/>. A readonly struct passed by <c>in</c> so the per-frame action
    /// dispatch allocates nothing.
    /// </summary>
    public readonly struct OperationContext
    {
        /// <summary>Output value after the source's <see cref="InputMode"/> is applied (0..1).</summary>
        public readonly float value;

        /// <summary>True only on the frame the raw input crossed from inactive to active (rising edge).</summary>
        public readonly bool pressed;

        /// <summary>True only on the frame the raw input crossed from active to inactive (falling edge).</summary>
        public readonly bool released;

        /// <summary>True while <see cref="value"/> is above the activation threshold.</summary>
        public readonly bool active;

        /// <summary>One-shot trigger pulse for discrete ("switch once") operations. Fires on release (key-up)
        /// in <see cref="InputMode.Button"/> so a button commits when let go, and on press (rising edge) in
        /// every other mode. Operations that act once per activation read this instead of <see cref="pressed"/>.</summary>
        public readonly bool triggered;

        public OperationContext(float value, bool pressed, bool released, bool active, bool triggered)
        {
            this.value = value;
            this.pressed = pressed;
            this.released = released;
            this.active = active;
            this.triggered = triggered;
        }
    }
}
