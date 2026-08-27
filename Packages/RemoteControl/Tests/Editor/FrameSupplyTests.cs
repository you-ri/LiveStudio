// Copyright (c) You-Ri, 2026
using System.Collections.Generic;
using NUnit.Framework;
using Lilium.RemoteControl.Frames;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// A frame comes either from what is running or from a source, and the producers have to be able
    /// to tell which. Before this, replay was one more frame-head handler among the producers, so
    /// whether it ran before or after them came down to registration order -- and the producer
    /// overwrote what the replay had just put in either way.
    /// </summary>
    [TestFixture]
    public class FrameSupplyTests
    {
        /// <summary>A source that supplies a fixed number of frames and then runs out.</summary>
        private sealed class CountingSource : IFrameSource
        {
            private readonly int _frames;

            public int filled;

            public CountingSource(int frames) => _frames = frames;

            public bool FillFrame(ref Frame frame)
            {
                if (filled >= _frames) return false;

                filled++;
                return true;
            }
        }

        private sealed class ThrowingSource : IFrameSource
        {
            public bool FillFrame(ref Frame frame) => throw new System.InvalidOperationException("no");
        }

        [SetUp]
        public void StartClean() => FrameGate.ResetState("[test] cleared");

        [TearDown]
        public void Detach()
        {
            FrameGate.source = null;
            FrameGate.ResetState("[test] cleared");
        }

        [Test]
        public void WithNoSource_TheFrameIsNotMarkedSupplied()
        {
            var seen = new List<bool>();
            FrameHeadDelegate handler = (ref Frame frame) => seen.Add(frame.isSupplied);
            FrameGate.AddFrameHeadHandler(handler);

            FrameGate.Pump();

            FrameGate.RemoveFrameHeadHandler(handler);
            CollectionAssert.AreEqual(new[] { false }, seen);
        }

        [Test]
        public void WithASource_ProducersSeeASuppliedFrame()
        {
            // This is the signal the whole thing hangs on: a producer writes the state lane on a live
            // frame and reads it on a supplied one, and it has only this to go by.
            var seen = new List<bool>();
            FrameHeadDelegate handler = (ref Frame frame) => seen.Add(frame.isSupplied);
            FrameGate.AddFrameHeadHandler(handler);
            FrameGate.source = new CountingSource(2);

            FrameGate.Pump();
            FrameGate.Pump();

            FrameGate.RemoveFrameHeadHandler(handler);
            CollectionAssert.AreEqual(new[] { true, true }, seen);
        }

        [Test]
        public void TheSourceFillsBeforeTheHandlersRun()
        {
            // Ordering is the reason the source is not just another handler. A handler registered
            // first still sees a frame the source has already filled.
            var order = new List<string>();
            var source = new OrderRecordingSource(order);

            FrameHeadDelegate handler = (ref Frame frame) => order.Add("handler");
            FrameGate.AddFrameHeadHandler(handler);
            FrameGate.source = source;

            FrameGate.Pump();

            FrameGate.RemoveFrameHeadHandler(handler);
            CollectionAssert.AreEqual(new[] { "source", "handler" }, order);
        }

        private sealed class OrderRecordingSource : IFrameSource
        {
            private readonly List<string> _order;

            public OrderRecordingSource(List<string> order) => _order = order;

            public bool FillFrame(ref Frame frame)
            {
                _order.Add("source");
                return true;
            }
        }

        [Test]
        public void WhenTheSourceRunsOut_ItIsDetachedAndAnnounced()
        {
            var ended = 0;
            System.Action onEnded = () => ended++;
            FrameGate.onSourceEnded += onEnded;

            var source = new CountingSource(1);
            FrameGate.source = source;

            FrameGate.Pump();     // supplied
            FrameGate.Pump();     // runs out here

            FrameGate.onSourceEnded -= onEnded;

            Assert.AreEqual(1, source.filled);
            Assert.IsNull(FrameGate.source, "a spent source must not be asked again every frame");
            Assert.AreEqual(1, ended, "whoever attached it has to be told, once");
        }

        [Test]
        public void TheFrameThatRunsOut_FallsBackToTheLiveLanes()
        {
            // The frame the source refused is an ordinary live frame, not a half-supplied one.
            var seen = new List<bool>();
            FrameHeadDelegate handler = (ref Frame frame) => seen.Add(frame.isSupplied);
            FrameGate.AddFrameHeadHandler(handler);
            FrameGate.source = new CountingSource(1);

            FrameGate.Pump();
            FrameGate.Pump();

            FrameGate.RemoveFrameHeadHandler(handler);
            CollectionAssert.AreEqual(new[] { true, false }, seen);
        }

        [Test]
        public void ASourceThatThrows_IsDetachedRatherThanLeftToThrowEveryFrame()
        {
            var ended = 0;
            System.Action onEnded = () => ended++;
            FrameGate.onSourceEnded += onEnded;
            FrameGate.source = new ThrowingSource();

            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Error,
                new System.Text.RegularExpressions.Regex("Frame source failed and was detached"));

            FrameGate.Pump();

            FrameGate.onSourceEnded -= onEnded;

            Assert.IsNull(FrameGate.source);
            Assert.AreEqual(1, ended);
        }

        [Test]
        public void ResetState_DetachesTheSource()
        {
            FrameGate.source = new CountingSource(10);

            FrameGate.ResetState("[test] cleared");

            Assert.IsNull(FrameGate.source,
                "a source left attached across a reset would supply frames into the next run");
        }
    }
}
