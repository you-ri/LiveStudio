// Copyright (c) You-Ri, 2026
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using System.Text.RegularExpressions;
using Lilium.RemoteControl.Frames;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// Watching a frame must not change what happens to it. The reason the gate has a second opening
    /// at all is that the first one holds a recording: if a viewer had to take the sink, opening the
    /// viewer would stop the take.
    /// </summary>
    [TestFixture]
    public class FrameObserverTests
    {
        private sealed class RecordingObserver : IFrameObserver
        {
            public readonly List<long> frames = new List<long>();
            public readonly List<bool> hadEvents = new List<bool>();
            public FrameSymbolTable lastSymbols;

            public void OnFrameCompleted(in Frame frame, FrameSymbolTable symbols)
            {
                frames.Add(frame.frameNumber);
                hadEvents.Add(frame.events != null);
                lastSymbols = symbols;
            }
        }

        private sealed class ThrowingObserver : IFrameObserver
        {
            public int calls;

            public void OnFrameCompleted(in Frame frame, FrameSymbolTable symbols)
            {
                calls++;
                throw new System.InvalidOperationException("no");
            }
        }

        /// <summary>Removes itself while being notified, which must not disturb the walk.</summary>
        private sealed class SelfRemovingObserver : IFrameObserver
        {
            public int calls;

            public void OnFrameCompleted(in Frame frame, FrameSymbolTable symbols)
            {
                calls++;
                FrameGate.RemoveFrameObserver(this);
            }
        }

        private sealed class CountingSink : IFrameSink
        {
            public int frames;

            public void OnFrameCompleted(in Frame frame, FrameSymbolTable symbols) => frames++;
        }

        private readonly List<IFrameObserver> _attached = new List<IFrameObserver>();

        /// <summary>
        /// Watchers already attached when the test began. Observers survive
        /// <see cref="FrameGate.ResetState"/> by design, so an open LiveData Viewer is a legitimate
        /// extra watcher and the fixture counts its own on top of whatever was there.
        /// </summary>
        private int _otherObservers;

        private T Attach<T>(T observer) where T : IFrameObserver
        {
            FrameGate.AddFrameObserver(observer);
            _attached.Add(observer);
            return observer;
        }

        [SetUp]
        public void StartClean()
        {
            FrameGate.ResetState("[test] cleared");
            FrameGate.SetClock(new FrameCounterClock(FrameRate.FPS60));
            _otherObservers = FrameGate.observerCount;
        }

        [TearDown]
        public void Detach()
        {
            for (int i = 0; i < _attached.Count; i++) FrameGate.RemoveFrameObserver(_attached[i]);
            _attached.Clear();
            FrameGate.sink = null;
            FrameGate.ResetState("[test] cleared");
            FrameGate.RestoreDefaultClock();
        }

        [Test]
        public void AnObserverSeesEveryFrame()
        {
            var observer = Attach(new RecordingObserver());

            FrameGate.Pump();
            FrameGate.Pump();

            Assert.AreEqual(2, observer.frames.Count);
            Assert.AreEqual(observer.frames[0] + 1, observer.frames[1]);
        }

        [Test]
        public void TheInputsAreStillAttachedWhenAnObserverIsCalled()
        {
            // The gate drops the event slot right after this point, so an observer that wants the
            // events has only this window. Watching from the frame head instead would see them, but
            // beside the previous frame's state values.
            var observer = Attach(new RecordingObserver());

            FrameGate.Pump();

            CollectionAssert.AreEqual(new[] { true }, observer.hadEvents);
        }

        [Test]
        public void ManyObserversWatchTheSameFrames()
        {
            var a = Attach(new RecordingObserver());
            var b = Attach(new RecordingObserver());

            FrameGate.Pump();

            Assert.AreEqual(1, a.frames.Count);
            Assert.AreEqual(1, b.frames.Count);
        }

        [Test]
        public void WatchingDoesNotTakeTheSink()
        {
            // The whole reason for a second opening.
            var sink = new CountingSink();
            FrameGate.sink = sink;
            var observer = Attach(new RecordingObserver());

            FrameGate.Pump();

            Assert.AreEqual(1, sink.frames, "the recording keeps getting frames while something watches");
            Assert.AreEqual(1, observer.frames.Count);
        }

        [Test]
        public void AddingTheSameObserverTwice_DoesNotDoubleUpTheNotifications()
        {
            var observer = Attach(new RecordingObserver());
            FrameGate.AddFrameObserver(observer);

            FrameGate.Pump();

            Assert.AreEqual(1, observer.frames.Count);
            Assert.AreEqual(_otherObservers + 1, FrameGate.observerCount);
        }

        [Test]
        public void AnObserverThatThrows_IsDetachedAndCounted()
        {
            var observer = Attach(new ThrowingObserver());

            LogAssert.Expect(LogType.Error, new Regex("Frame observer failed and was detached"));
            FrameGate.Pump();
            FrameGate.Pump();

            Assert.AreEqual(1, observer.calls, "a broken observer must not throw once per frame");
            Assert.AreEqual(1, FrameGate.detachedObserverCount,
                "going quiet without saying so is the failure this whole thing is meant to find");
        }

        [Test]
        public void OneObserverThrowing_DoesNotStopTheOthers()
        {
            Attach(new ThrowingObserver());
            var survivor = Attach(new RecordingObserver());

            LogAssert.Expect(LogType.Error, new Regex("Frame observer failed and was detached"));
            FrameGate.Pump();

            Assert.AreEqual(1, survivor.frames.Count);
        }

        [Test]
        public void AnObserverCanDetachItselfWhileBeingNotified()
        {
            var observer = Attach(new SelfRemovingObserver());
            var other = Attach(new RecordingObserver());

            FrameGate.Pump();
            FrameGate.Pump();

            Assert.AreEqual(1, observer.calls);
            Assert.AreEqual(2, other.frames.Count, "the walk must survive a list edit inside it");
        }

        [Test]
        public void ObserversSurviveAReset()
        {
            // The sink and the source are cut on reset so a recording cannot span two runs. A
            // watcher has nothing to span -- and one that silently stopped at the start of every run
            // would be the exact kind of quiet failure it exists to catch.
            var observer = Attach(new RecordingObserver());

            FrameGate.ResetState("[test] cleared");
            FrameGate.SetClock(new FrameCounterClock(FrameRate.FPS60));
            FrameGate.Pump();

            Assert.AreEqual(1, observer.frames.Count);
        }
    }
}
