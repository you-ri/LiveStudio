// Copyright (c) You-Ri, 2026
using System;
using NUnit.Framework;
using Lilium.RemoteControl.Frames;

[assembly: Lilium.RemoteControl.FrameSource("test-producer")]

namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// A producer inside the frame -- a deck tile, a bound key, a gamepad axis -- is an input like
    /// any other and has to take its place in the order.
    ///
    /// It cannot go in the way a request does: a request is submitted from a worker thread that
    /// then waits for the frame head, and the main thread cannot wait for a frame head it is
    /// itself supposed to run. Posting is the same queue without the waiting.
    /// </summary>
    [TestFixture]
    public class FramePostTests
    {
        private FrameSource _source;

        [SetUp]
        public void StartClean()
        {
            FrameGate.ResetState("[test] cleared");
            _source = FrameGate.ResolveSource("test-producer");
        }

        [TearDown]
        public void Finish() => FrameGate.ResetState("[test] cleared");

        [Test]
        public void APostedInput_AppliesAtTheNextFrameHead_NotWhereItWasPosted()
        {
            var applied = 0;

            FrameGate._Post(InputKind.PropertyWrite, _source, "PUT", "/live/object/cam/fov",
                () => applied++);

            Assert.AreEqual(0, applied, "posting is not applying");

            FrameGate.Pump();

            Assert.AreEqual(1, applied);
        }

        [Test]
        public void APostedInput_IsRecordedLikeAnyOther()
        {
            FrameGate._Post(InputKind.FunctionCall, _source, "POST", "/live/function/deck/Fire",
                () => { });

            FrameGate.Pump();

            using var frame = new InputFrame();
            Assert.AreEqual(FrameLookup.Found, FrameGate.buffer.TryReadLatest(frame));
            Assert.AreEqual(1, frame.inputCount);

            var record = frame[0];
            Assert.AreEqual(InputKind.FunctionCall, record.kind);
            Assert.AreEqual("test-producer", FrameGate.symbols.Resolve(record.sourceId));
            Assert.AreEqual("POST", FrameGate.symbols.Resolve(record.verbId));
            Assert.AreEqual("/live/function/deck/Fire", FrameGate.symbols.Resolve(record.targetId));
        }

        [Test]
        public void APostedWriteThatSaysWhatItApplied_KeepsTheValue()
        {
            const string target = "/live/object/cam/fov";

            FrameGate._Post(InputKind.PropertyWrite, _source, "PUT", target,
                () => FrameGate.StampAppliedPayload(target, typeof(float), 35f));

            FrameGate.Pump();

            using var frame = new InputFrame();
            Assert.AreEqual(FrameLookup.Found, FrameGate.buffer.TryReadLatest(frame));

            var record = frame[0];
            Assert.AreEqual("System.Single", FrameGate.symbols.Resolve(record.payloadTypeId));

            var bytes = new byte[record.payloadLength];
            record.CopyPayloadTo(bytes);
            Assert.AreEqual(35f, BitConverter.ToSingle(bytes, 0), 0f);
        }

        [Test]
        public void PostedInputs_KeepTheOrderTheyWerePostedIn()
        {
            var order = new System.Collections.Generic.List<int>();

            FrameGate._Post(InputKind.PropertyWrite, _source, "PUT", "/live/a", () => order.Add(1));
            FrameGate._Post(InputKind.PropertyWrite, _source, "PUT", "/live/b", () => order.Add(2));
            FrameGate._Post(InputKind.PropertyWrite, _source, "PUT", "/live/c", () => order.Add(3));

            FrameGate.Pump();

            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, order);

            using var frame = new InputFrame();
            FrameGate.buffer.TryReadLatest(frame);
            Assert.AreEqual(frame[0].sequence + 1, frame[1].sequence, "no gaps in the numbering");
            Assert.AreEqual(frame[1].sequence + 1, frame[2].sequence);
        }

        [Test]
        public void PostedAndRequestedInputs_ShareOneOrdering()
        {
            // The point of routing a deck button through here: an operation and a remote write in
            // the same frame land in a decided order rather than in whichever ran first.
            var order = new System.Collections.Generic.List<string>();

            FrameGate._Post(InputKind.PropertyWrite, _source, "PUT", "/live/a",
                () => order.Add("operation"));

            FrameGate._Enqueue(InputKind.PropertyWrite, "test", "/live/b", null,
                () => { order.Add("request"); return true; });

            FrameGate.Pump();

            CollectionAssert.AreEqual(new[] { "operation", "request" }, order);
        }

        [Test]
        public void PostingAnUndeclaredSource_IsRefusedRatherThanRecordedAsUnknown()
        {
            Assert.Throws<ArgumentException>(() => FrameGate.Post(
                InputKind.PropertyWrite, default, "PUT", "/live/a", () => { }));
        }
    }
}
