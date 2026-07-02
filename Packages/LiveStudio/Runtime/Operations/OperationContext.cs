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

        /// <summary>Low end of the value range the control maps its normalized <see cref="value"/> onto
        /// (a <see cref="DeckSlider"/>'s min; defaults to 0 for the identity range). See <see cref="MappedValue"/>.</summary>
        public readonly float valueMin;

        /// <summary>High end of the value range (a <see cref="DeckSlider"/>'s max; defaults to 1). See
        /// <see cref="MappedValue"/>.</summary>
        public readonly float valueMax;

        public OperationContext(float value, bool pressed, bool released, bool active, bool triggered)
            : this(value, pressed, released, active, triggered, 0f, 1f)
        {
        }

        public OperationContext(float value, bool pressed, bool released, bool active, bool triggered,
            float valueMin, float valueMax)
        {
            this.value = value;
            this.pressed = pressed;
            this.released = released;
            this.active = active;
            this.triggered = triggered;
            this.valueMin = valueMin;
            this.valueMax = valueMax;
        }

        /// <summary><see cref="value"/> mapped onto the control's <see cref="valueMin"/>..<see cref="valueMax"/>
        /// range. Equals <see cref="value"/> for the default 0..1 range, so continuous operations can write this
        /// unconditionally. <see cref="value"/> itself stays normalized so gauges and edge tests are unaffected.</summary>
        public float MappedValue => valueMin + (valueMax - valueMin) * value;

        /// <summary>Returns a copy carrying the given value range while keeping every other field, so a context
        /// produced without range awareness (e.g. from an <see cref="InputSource"/>) can adopt the control's
        /// range at the point the owning <see cref="OperationSet"/> is known.</summary>
        public OperationContext WithRange(float min, float max)
            => new OperationContext(value, pressed, released, active, triggered, min, max);
    }
}
