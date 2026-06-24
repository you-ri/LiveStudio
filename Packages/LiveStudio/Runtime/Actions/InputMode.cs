// Copyright (c) You-Ri, 2026

using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// How an <see cref="InputSource"/> turns its raw input into the output value its actions read.
    /// </summary>
    [ExposedEnum]
    public enum InputMode
    {
        /// <summary>Output follows the raw input: 1 (or the analog value) while held, 0 when released.</summary>
        Button,

        /// <summary>Each press flips the latched output between 0 and 1.</summary>
        Toggle,

        /// <summary>Output is the continuous analog value (0..1), e.g. a gamepad trigger or axis.</summary>
        Value,
    }
}
