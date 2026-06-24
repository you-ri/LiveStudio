// Copyright (c) You-Ri, 2026

using NUnit.Framework;

namespace Lilium.LiveStudio.EditorTests
{
    // An input source whose raw value is set by the test, bypassing any real input device. Top-level and
    // non-private because the RemoteControl source generator enumerates every concrete subclass of an
    // [ExposedClass] base (InputSource) in declaration-order code and cannot reference a private type.
    internal sealed class FakeInputSource : InputSource
    {
        public float raw;
        protected override float ReadRawValue() => raw;
    }

    /// <summary>
    /// Tests <see cref="InputSource.Evaluate"/>: the mode folding (Button / Toggle / Value) and the
    /// rising/falling edge detection that the actions consume. Uses a fake source whose raw input is set
    /// directly, so the logic is verified without the Input System.
    /// </summary>
    public class InputSourceTests
    {
        [Test]
        public void Button_FollowsRawAndFiresEdges()
        {
            var source = new FakeInputSource { mode = InputMode.Button };

            // Rising edge: value follows the raw input, pressed fires once.
            source.raw = 1f;
            var c = source.Evaluate();
            Assert.AreEqual(1f, c.value, 1e-4f);
            Assert.IsTrue(c.pressed);
            Assert.IsFalse(c.released);
            Assert.IsTrue(c.active);

            // Held: no edge.
            c = source.Evaluate();
            Assert.IsFalse(c.pressed);
            Assert.IsFalse(c.released);
            Assert.IsTrue(c.active);

            // Falling edge: value drops, released fires once.
            source.raw = 0f;
            c = source.Evaluate();
            Assert.AreEqual(0f, c.value, 1e-4f);
            Assert.IsFalse(c.pressed);
            Assert.IsTrue(c.released);
            Assert.IsFalse(c.active);
        }

        [Test]
        public void Value_PassesThroughAnalogAndThresholdsActive()
        {
            var source = new FakeInputSource { mode = InputMode.Value };

            source.raw = 0.3f;
            var c = source.Evaluate();
            Assert.AreEqual(0.3f, c.value, 1e-4f);
            Assert.IsFalse(c.active, "0.3 is below the 0.5 activation threshold");

            source.raw = 0.7f;
            c = source.Evaluate();
            Assert.AreEqual(0.7f, c.value, 1e-4f);
            Assert.IsTrue(c.active, "0.7 is above the 0.5 activation threshold");
        }

        [Test]
        public void Toggle_FlipsOnEachRisingEdge()
        {
            var source = new FakeInputSource { mode = InputMode.Toggle };

            // First press latches on.
            source.raw = 1f;
            Assert.AreEqual(1f, source.Evaluate().value, 1e-4f);

            // Release keeps it on (no flip on falling edge).
            source.raw = 0f;
            Assert.AreEqual(1f, source.Evaluate().value, 1e-4f);

            // Second press latches off.
            source.raw = 1f;
            Assert.AreEqual(0f, source.Evaluate().value, 1e-4f);

            // Release keeps it off.
            source.raw = 0f;
            Assert.AreEqual(0f, source.Evaluate().value, 1e-4f);

            // Third press latches on again.
            source.raw = 1f;
            Assert.AreEqual(1f, source.Evaluate().value, 1e-4f);
        }

        [Test]
        public void ResetState_ClearsToggleAndEdgeLatch()
        {
            var source = new FakeInputSource { mode = InputMode.Toggle };

            source.raw = 1f;
            Assert.AreEqual(1f, source.Evaluate().value, 1e-4f);

            source.ResetState();

            // After reset the latch is cleared and the still-held input reads as a fresh rising edge.
            var c = source.Evaluate();
            Assert.AreEqual(1f, c.value, 1e-4f);
            Assert.IsTrue(c.pressed, "held input after reset is treated as a new press");
        }
    }
}
