// Copyright (c) You-Ri, 2026
using System.IO;
using NUnit.Framework;
using Lilium.RemoteControl.Frames;
using Lilium.RemoteControl.Frames.Recording;
using Lilium.RemoteControl.Editor.LiveDataViewer;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// Reading a recording back for a person to look at.
    ///
    /// The viewer parses the format by hand rather than replaying the file, so that a recording made
    /// by another build can still be opened. The price is a second reader of the same bytes, and
    /// these are what keep it from drifting away from the writer -- a drift that would show up as a
    /// recording that looks wrong when it is fine, which is worse than no viewer at all.
    /// </summary>
    [TestFixture]
    public class LiveDataFileFeedTests
    {
        private struct Beam
        {
            public float intensity;
        }

        private string _path;

        [SetUp]
        public void StartClean()
        {
            FrameGate.sink = null;
            FrameGate.ResetState("[test] cleared");
            FrameGate.SetClock(new FrameCounterClock(FrameRate.FPS60));
            _path = Path.Combine(Path.GetTempPath(), $"livedata-feed-{System.Guid.NewGuid():N}.livedata");
        }

        [TearDown]
        public void Finish()
        {
            FrameGate.sink = null;
            FrameGate.ResetState("[test] cleared");
            FrameGate.RestoreDefaultClock();

            if (File.Exists(_path)) File.Delete(_path);
        }

        /// <summary>Records frames through the real gate and leaves them on disk.</summary>
        private void Record(int frames, FrameHeadDelegate producer, bool close = true)
        {
            var recorder = new FrameRecorder { keyframeInterval = 4 };
            var stream = new FileStream(_path, FileMode.Create, FileAccess.Write);

            FrameGate.AddFrameHeadHandler(producer);
            recorder.Start(stream, leaveOpen: false);
            FrameGate.sink = recorder;

            try
            {
                for (int i = 0; i < frames; i++) FrameGate.Pump();
            }
            finally
            {
                FrameGate.sink = null;

                // A file that was never closed carries no tail. Left open on purpose in one test:
                // that is what a recording looks like when the application died holding it.
                if (close) recorder.Stop();
                else stream.Dispose();

                FrameGate.RemoveFrameHeadHandler(producer);
            }
        }

        private static void Beams(ref Frame frame)
        {
            frame.structure.AddOrUpdate(
                FrameGate.symbols.Intern("lamp"), FrameGate.symbols.Intern("Beam"),
                FrameSymbolTable.kNone);

            var block = frame.state.GetOrCreate<Beam>();
            ref var element = ref block.GetOrCreate(FrameGate.symbols.Intern("lamp"));
            element.source = FrameGate.ResolveSource("test");
            element.time = frame.frameNumber;
            element.value.intensity = frame.frameNumber;
        }

        [Test]
        public void EveryRecordedFrame_CanBeMovedTo()
        {
            Record(6, Beams);

            using var feed = new LiveDataFileFeed();
            feed.Open(_path);

            Assert.AreEqual(6, feed.frameCount);
            Assert.IsTrue(feed.isComplete, "the recorder closed it, so it carries its tail");
            Assert.AreEqual(0, feed.frameIndex, "opens on the first frame");

            feed.Seek(5);
            Assert.AreEqual(5, feed.snapshot.frameNumber);
        }

        [Test]
        public void AFrameCarriesItsStateLane_WithOwnersNamed()
        {
            Record(3, Beams);

            using var feed = new LiveDataFileFeed();
            feed.Open(_path);
            feed.Seek(2);

            Assert.AreEqual(1, feed.snapshot.types.Count);

            var type = feed.snapshot.types[0];
            Assert.AreEqual(typeof(Beam).FullName, type.typeName);
            Assert.AreEqual(1, type.elements.Count);

            // Ids mean nothing without the recording's own table, which is the whole reason it is
            // written into the file.
            Assert.AreEqual("lamp", type.elements[0].owner);
            Assert.AreEqual(2, type.elements[0].time);
        }

        [Test]
        public void SelectingAnElement_ReadsItsValueBack()
        {
            Record(5, Beams);

            using var feed = new LiveDataFileFeed();
            feed.Open(_path);

            feed.Select(typeof(Beam).FullName, feed.snapshot.types[0].elements[0].ownerId);
            feed.Seek(4);

            // The value sits after the element's metadata, and finding it is the one piece of the
            // layout this reader works out for itself. Taken as the whole remainder of the element,
            // trailing padding included, which is what the live path hands over too -- the reader of
            // these bytes goes by field offset, so the padding costs nothing and matching the live
            // path costs nothing either.
            Assert.GreaterOrEqual(feed.snapshot.selectedValueLength, 4);
            Assert.AreEqual(4f, System.BitConverter.ToSingle(feed.snapshot.selectedValue, 0),
                "reading the element's size minus its value's lands in the padding instead");
        }

        [Test]
        public void TheInventoryShows_OnFramesThatDoNotCarryIt()
        {
            // Only a keyframe carries the inventory. A frame in between showing an empty one would
            // read as a world with nothing in it, which is what this guards.
            Record(6, Beams);

            using var feed = new LiveDataFileFeed();
            feed.Open(_path);
            feed.Seek(5);

            Assert.AreEqual(1, feed.snapshot.structure.Count);
            Assert.AreEqual("lamp", feed.snapshot.structure[0].objectName);
            Assert.AreEqual("Beam", feed.snapshot.structure[0].typeName);
        }

        [Test]
        public void InputsAreListedForTheFrameTheyLandedOn()
        {
            void Producer(ref Frame frame)
            {
                Beams(ref frame);

                if (frame.frameNumber != 1) return;

                FrameGate._Enqueue(EventKind.PropertyWrite, "test", "/live/object/lamp/intensity",
                    "{\"value\":1}", () => true, verb: "PUT");
            }

            Record(4, Producer);

            using var feed = new LiveDataFileFeed();
            feed.Open(_path);

            feed.Seek(0);
            Assert.AreEqual(0, feed.eventCount);

            // Enqueued during frame 1's head, so it is applied at the head of frame 2.
            feed.Seek(2);
            Assert.AreEqual(1, feed.eventCount);
            Assert.AreEqual("/live/object/lamp/intensity", feed.GetEvent(0).target);
            Assert.AreEqual("PUT", feed.GetEvent(0).verb);
        }

        [Test]
        public void ARecordingThatWasNeverClosed_IsStillReadable()
        {
            // Everything before the cut is intact, and that is usually the part worth looking at:
            // a recording with no tail is what a crash leaves behind.
            Record(4, Beams, close: false);

            using var feed = new LiveDataFileFeed();
            feed.Open(_path);

            Assert.IsFalse(feed.isComplete);
            Assert.GreaterOrEqual(feed.frameCount, 1);
            Assert.AreEqual("lamp", feed.snapshot.structure[0].objectName,
                "the mapping table was rebuilt from the entries, since there is no tail to take it from");
        }

        [Test]
        public void SomethingThatIsNotARecording_SaysSoRatherThanOpening()
        {
            File.WriteAllText(_path, "not a recording");

            using var feed = new LiveDataFileFeed();

            Assert.Throws<InvalidDataException>(() => feed.Open(_path));
        }
    }
}
