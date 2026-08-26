// Copyright (c) You-Ri, 2026
using System.IO;
using NUnit.Framework;
using Lilium.RemoteControl.Frames;
using Lilium.RemoteControl.Frames.Recording;

namespace Lilium.RemoteControl.Tests
{
    public class FrameKeyframeTests
    {
        private struct Beam
        {
            public float intensity;
        }

        [SetUp]
        [TearDown]
        public void ClearGate()
        {
            FrameGate.sink = null;
            FrameGate.ResetState("[test] cleared");
        }

        /// <summary>Records frames through the real gate at a given keyframe interval.</summary>
        private static byte[] Record(int frames, int keyframeInterval, FrameHeadDelegate producer)
        {
            var stream = new MemoryStream();
            var recorder = new FrameRecorder { keyframeInterval = keyframeInterval };

            FrameGate.AddFrameHeadHandler(producer);
            recorder.Start(stream, leaveOpen: true);
            FrameGate.sink = recorder;

            try
            {
                for (int i = 0; i < frames; i++) FrameGate.Pump();
            }
            finally
            {
                FrameGate.sink = null;
                recorder.Stop();
                FrameGate.RemoveFrameHeadHandler(producer);
            }

            return stream.ToArray();
        }

        [Test]
        public void Keyframes_ArePutAtTheInterval()
        {
            void Producer(ref Frame frame) => frame.structure.AddOrUpdate(1, 10, InputSymbolTable.kNone);

            var bytes = Record(20, keyframeInterval: 5, Producer);

            using (var player = new FrameRecordPlayer(new MemoryStream(bytes)))
            {
                // One at the start plus one every five frames after it.
                CollectionAssert.AreEqual(new long[] { 0, 5, 10, 15 }, player.keyframes);
            }
        }

        [Test]
        public void AStructuralChange_IsAlwaysAKeyframe()
        {
            var frames = 0;

            void Producer(ref Frame frame)
            {
                frame.structure.AddOrUpdate(1, 10, InputSymbolTable.kNone);

                // Something appears well away from the interval.
                if (frames == 7) frame.structure.AddOrUpdate(2, 10, InputSymbolTable.kNone);
                frames++;
            }

            var bytes = Record(20, keyframeInterval: 100, Producer);

            using (var player = new FrameRecordPlayer(new MemoryStream(bytes)))
            {
                // A seek must never land after a change without a keyframe to pick the shape up
                // from, so a change writes one regardless of the interval.
                CollectionAssert.Contains(player.keyframes, 7L);
            }
        }

        [Test]
        public void WithNoInterval_OnlyChangesWriteAKeyframe()
        {
            void Producer(ref Frame frame) => frame.structure.AddOrUpdate(1, 10, InputSymbolTable.kNone);

            var bytes = Record(20, keyframeInterval: 0, Producer);

            using (var player = new FrameRecordPlayer(new MemoryStream(bytes)))
            {
                // One: the inventory is written the first time it is seen, even when it is empty,
                // because "nothing exists" is itself a shape a seek has to be able to land on.
                Assert.AreEqual(1, player.keyframes.Count, "nothing changed after the first frame");
            }
        }

        [Test]
        public void SeekWithStructure_PicksUpTheShapeFromTheKeyframeBeforeIt()
        {
            var frames = 0;

            void Producer(ref Frame frame)
            {
                frame.structure.AddOrUpdate(1, 10, InputSymbolTable.kNone);
                if (frames >= 8) frame.structure.AddOrUpdate(2, 10, InputSymbolTable.kNone);

                frame.state.GetOrCreate<Beam>().GetOrCreate(1).value.intensity = frames;
                frames++;
            }

            var bytes = Record(20, keyframeInterval: 4, Producer);

            using (var player = new FrameRecordPlayer(new MemoryStream(bytes)))
            {
                var block = player.state.GetOrCreate<Beam>();

                // Land after the spawn: two objects, and the value of that exact frame.
                Assert.IsTrue(player.TrySeekWithStructure(15));
                Assert.AreEqual(15, player.frameNumber);
                Assert.AreEqual(2, player.structure.count);
                Assert.AreEqual(15f, block[0].value.intensity);

                // Land before it: back to one, without having replayed the whole run.
                Assert.IsTrue(player.TrySeekWithStructure(3));
                Assert.AreEqual(3, player.frameNumber);
                Assert.AreEqual(1, player.structure.count);
                Assert.AreEqual(3f, block[0].value.intensity);
            }
        }

        [Test]
        public void EveryFrameCarriesItsValuesEvenWithoutAKeyframe()
        {
            // The reason a keyframe only needs the inventory: the state lane is dense and written in
            // full every frame, so a plain seek already restores every value.
            void Producer(ref Frame frame)
                => frame.state.GetOrCreate<Beam>().GetOrCreate(1).value.intensity = frame.frameNumber;

            var bytes = Record(12, keyframeInterval: 0, Producer);

            using (var player = new FrameRecordPlayer(new MemoryStream(bytes)))
            {
                var block = player.state.GetOrCreate<Beam>();

                // Only the one at the start, and frame 9 is nowhere near it.
                Assert.AreEqual(1, player.keyframes.Count);
                Assert.IsTrue(player.TrySeek(9));
                Assert.AreEqual(9f, block[0].value.intensity);
            }
        }

        [Test]
        public void AKeyframeCostsTheInventoryAndNothingElse()
        {
            void Producer(ref Frame frame)
            {
                for (int i = 0; i < 10; i++) frame.structure.AddOrUpdate(i, 10, InputSymbolTable.kNone);
                frame.state.GetOrCreate<Beam>().GetOrCreate(1).value.intensity = frame.frameNumber;
            }

            var sparse = Record(60, keyframeInterval: 0, Producer).Length;
            var everySecond = Record(60, keyframeInterval: 60, Producer).Length;
            var everyFrame = Record(60, keyframeInterval: 1, Producer).Length;

            // 12 of block header plus 12 per object plus 13 of entry header: about 145 bytes for ten
            // objects. Cheap enough that the interval is a responsiveness decision, not a size one.
            var perKeyframe = (everyFrame - sparse) / 59.0;
            Assert.Less(perKeyframe, 200, $"a keyframe cost {perKeyframe:F0} bytes");

            Assert.AreEqual(sparse, everySecond, "one a second over a second of frames is just the first");
        }
    }
}
