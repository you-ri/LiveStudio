// Copyright (c) You-Ri, 2026
using System;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Lilium.RemoteControl.Frames;
using Lilium.RemoteControl.Frames.Recording;

namespace Lilium.RemoteControl.Tests
{
    public class FrameRecorderTests
    {
        private MemoryStream _stream;
        private FrameRecorder _recorder;

        [SetUp]
        public void StartClean()
        {
            FrameGate.ResetState("[test] cleared");
            _stream = new MemoryStream();
            _recorder = new FrameRecorder();
        }

        [TearDown]
        public void Detach()
        {
            FrameGate.sink = null;
            _recorder?.Dispose();
            _stream?.Dispose();
            FrameGate.ResetState("[test] cleared");
        }

        private byte[] RecordFrames(int count, Action beforeEachPump = null)
        {
            _recorder.Start(_stream, leaveOpen: true);
            FrameGate.sink = _recorder;

            for (int i = 0; i < count; i++)
            {
                beforeEachPump?.Invoke();
                FrameGate.Pump();
            }

            FrameGate.sink = null;
            _recorder.Stop();
            return _stream.ToArray();
        }

        [Test]
        public void EveryPumpedFrame_ReachesTheRecording()
        {
            var bytes = RecordFrames(5);

            using (var reader = new FrameRecordReader(new MemoryStream(bytes)))
            {
                Assert.IsTrue(reader.hasIndex);
                Assert.AreEqual(5, reader.indexedFrameCount);
            }
        }

        [Test]
        public void InputsApplied_AtAFrameHead_AreInTheRecording()
        {
            var bytes = RecordFrames(1, () =>
                FrameGate._Enqueue(InputKind.PropertyWrite, "test", "/live/object/cam/fov", "35.0",
                    () => true));

            using (var reader = new FrameRecordReader(new MemoryStream(bytes)))
            {
                var inputs = 0;
                while (reader.TryReadEntry(out var entry))
                {
                    if (entry.kind == FrameEntryKind.Input) inputs++;
                }

                Assert.AreEqual(1, inputs);
                CollectionAssert.Contains(reader.symbols, "/live/object/cam/fov");
                CollectionAssert.Contains(reader.symbols, "test");
            }
        }

        [Test]
        public void StateWrittenByAProducer_IsInTheRecording()
        {
            var source = FrameGate.ResolveSource("test");

            void Producer(ref Frame frame)
            {
                ref var element = ref frame.state.GetOrCreate<Beam>().GetOrCreate(1);
                element.source = source;
                element.time = 99;
                element.value.intensity = 0.5f;
            }

            FrameGate.AddFrameHeadHandler(Producer);

            try
            {
                var bytes = RecordFrames(2);

                using (var reader = new FrameRecordReader(new MemoryStream(bytes)))
                {
                    var states = 0;
                    while (reader.TryReadEntry(out var entry))
                    {
                        if (entry.kind == FrameEntryKind.State) states++;
                    }

                    // Once per frame: state is dense and written every frame, not on change.
                    Assert.AreEqual(2, states);
                    CollectionAssert.Contains(reader.symbols, typeof(Beam).FullName);
                }
            }
            finally
            {
                FrameGate.RemoveFrameHeadHandler(Producer);
            }
        }

        [Test]
        public void Sink_SeesTheFrameAfterTheProducersHaveWritten()
        {
            // The order the whole thing rests on: inputs, then state, then whoever is recording it.
            var order = new System.Collections.Generic.List<string>();

            void Producer(ref Frame frame) => order.Add("state");
            var sink = new OrderProbe(order);

            FrameGate.AddFrameHeadHandler(Producer);
            FrameGate.sink = sink;

            try
            {
                FrameGate._Enqueue(InputKind.PropertyWrite, "test", "/live/a", "1",
                    () => { order.Add("input"); return true; });

                FrameGate.Pump();
            }
            finally
            {
                FrameGate.RemoveFrameHeadHandler(Producer);
                FrameGate.sink = null;
            }

            CollectionAssert.AreEqual(new[] { "input", "state", "sink" }, order);
        }

        [Test]
        public void SinkThatThrows_IsDetachedRatherThanFailingEveryFrame()
        {
            LogAssert.Expect(LogType.Error, new Regex("Frame sink failed"));

            FrameGate.sink = new ThrowingSink();

            FrameGate.Pump();
            Assert.IsNull(FrameGate.sink, "a sink that failed should not be asked again");

            // The run carries on: the frame still commits and callers waiting on it are not stranded.
            using var frame = new InputFrame();
            Assert.AreEqual(FrameLookup.Found, FrameGate.buffer.TryReadLatest(frame));

            FrameGate.Pump();
        }

        [Test]
        public void RecordingNotStopped_IsStillReadableFromTheTop()
        {
            // The crash case, driven through the real gate rather than the writer directly.
            _recorder.Start(_stream, leaveOpen: true);
            FrameGate.sink = _recorder;

            for (int i = 0; i < 3; i++) FrameGate.Pump();

            FrameGate.sink = null;
            var bytes = _stream.ToArray();

            using (var reader = new FrameRecordReader(new MemoryStream(bytes)))
            {
                Assert.IsFalse(reader.hasIndex);

                var frames = 0;
                while (reader.TryReadEntry(out var entry))
                {
                    if (entry.kind == FrameEntryKind.FrameBoundary) frames++;
                }

                Assert.AreEqual(3, frames);
            }
        }

        [Test]
        public void ResetState_DetachesTheSink()
        {
            // Otherwise a recording would quietly span two runs, whose frame numbers both start over.
            FrameGate.sink = _recorder;
            FrameGate.ResetState("[test] cleared");

            Assert.IsNull(FrameGate.sink);
        }

        [Test]
        public void Start_WhileAlreadyRecording_IsRefused()
        {
            _recorder.Start(_stream, leaveOpen: true);

            Assert.Throws<InvalidOperationException>(() => _recorder.Start(new MemoryStream()));
        }

        private struct Beam
        {
            public float intensity;
        }

        private sealed class OrderProbe : IFrameSink
        {
            private readonly System.Collections.Generic.List<string> _order;

            public OrderProbe(System.Collections.Generic.List<string> order) => _order = order;

            public void OnFrameCompleted(in Frame frame, InputSymbolTable symbols) => _order.Add("sink");
        }

        private sealed class ThrowingSink : IFrameSink
        {
            public void OnFrameCompleted(in Frame frame, InputSymbolTable symbols)
                => throw new InvalidOperationException("no disk");
        }
    }
}
