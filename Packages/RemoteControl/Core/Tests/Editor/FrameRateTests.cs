// Copyright (c) You-Ri, 2026

using NUnit.Framework;

namespace Lilium.RemoteControl.Tests
{
    public class FrameRateTests
    {
        [Test]
        public void AsDecimal_WithWholeRate_ReturnsFramesPerSecond()
        {
            Assert.AreEqual(60.0, new FrameRate(1, 60).AsDecimal(), 1e-9);
            Assert.AreEqual(30.0, new FrameRate(1, 30).AsDecimal(), 1e-9);
        }

        [Test]
        public void AsDecimal_WithFractionalRate_KeepsTheFraction()
        {
            // Dividing the two uints directly is integer division, which returned 59 here and made
            // anything scaled by the rate drift by about a frame a second.
            Assert.AreEqual(59.94, new FrameRate(1001, 60000).AsDecimal(), 1e-3);
            Assert.AreEqual(23.976, new FrameRate(1001, 24000).AsDecimal(), 1e-3);
        }
    }
}
