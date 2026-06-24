// Copyright (c) You-Ri, 2026

namespace Lilium.LiveStudio
{
    /// <summary>
    /// The per-frame evaluation result of an <see cref="InputSource"/>, passed to each
    /// <see cref="ActionBase"/>. A readonly struct passed by <c>in</c> so the per-frame action
    /// dispatch allocates nothing.
    /// </summary>
    public readonly struct ActionContext
    {
        /// <summary>Output value after the source's <see cref="InputMode"/> is applied (0..1).</summary>
        public readonly float value;

        /// <summary>True only on the frame the raw input crossed from inactive to active (rising edge).</summary>
        public readonly bool pressed;

        /// <summary>True only on the frame the raw input crossed from active to inactive (falling edge).</summary>
        public readonly bool released;

        /// <summary>True while <see cref="value"/> is above the activation threshold.</summary>
        public readonly bool active;

        public ActionContext(float value, bool pressed, bool released, bool active)
        {
            this.value = value;
            this.pressed = pressed;
            this.released = released;
            this.active = active;
        }
    }
}
