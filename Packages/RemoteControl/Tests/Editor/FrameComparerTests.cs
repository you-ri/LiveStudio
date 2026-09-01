// Copyright (c) You-Ri, 2026
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Lilium.RemoteControl.Frames;
using Lilium.RemoteControl.Frames.Recording;

namespace Lilium.RemoteControl.Tests
{
    public class FrameComparerTests
    {
        private struct Beam
        {
            public float intensity;
        }

        private struct Pose
        {
            public float x;
        }

        private readonly FrameComparer _comparer = new FrameComparer();

        // Everything built by a test, released after it. The blocks hold native storage, and a leak
        // here would surface at the next domain reload rather than in the test that caused it.
        private readonly List<System.IDisposable> _created = new List<System.IDisposable>();

        [TearDown]
        public void ReleaseBlocks()
        {
            for (int i = 0; i < _created.Count; i++) _created[i].Dispose();

            _created.Clear();
            FrameGate.RestoreDefaultClock();
        }

        private StateBlockSet WithBeams(params (int owner, float intensity)[] beams)
        {
            var set = new StateBlockSet();
            _created.Add(set);

            var block = set.GetOrCreate<Beam>();

            foreach (var (owner, intensity) in beams)
            {
                block.GetOrCreate(owner).value.intensity = intensity;
            }

            return set;
        }

        private StructureBlock NewStructure()
        {
            var structure = new StructureBlock();
            _created.Add(structure);
            return structure;
        }

        private StateBlockSet NewState()
        {
            var set = new StateBlockSet();
            _created.Add(set);
            return set;
        }

        [Test]
        public void IdenticalWorlds_MatchCompletely()
        {
            var report = _comparer.Compare(null, WithBeams((1, 0.5f), (2, 1f)),
                                           null, WithBeams((1, 0.5f), (2, 1f)));

            Assert.IsTrue(report.isClean);
            Assert.AreEqual(2, report.comparedElements);
            Assert.AreEqual(2, report.matchedElements);
            Assert.AreEqual(1f, report.matchRate);
        }

        [Test]
        public void OneDifferentValue_IsReportedByTypeAndOwner()
        {
            var report = _comparer.Compare(null, WithBeams((1, 0.5f), (2, 1f)),
                                           null, WithBeams((1, 0.5f), (2, 2f)));

            Assert.IsFalse(report.isClean);
            Assert.AreEqual(1, report.mismatchCount);
            Assert.AreEqual(0.5f, report.matchRate);

            var mismatch = report.mismatches[0];
            Assert.AreEqual(typeof(Beam).FullName, mismatch.typeName);
            Assert.AreEqual(2, mismatch.ownerId);
            Assert.AreEqual(MismatchReason.ValueDiffers, mismatch.reason);
        }

        [Test]
        public void TheSmallestPossibleDifference_IsCaught()
        {
            // Exact on purpose. On one machine a deterministic run reproduces its floats bit for
            // bit, so a tolerance here would hide exactly what this is for.
            var report = _comparer.Compare(null, WithBeams((1, 1f)),
                                           null, WithBeams((1, 1.0000001f)));

            Assert.AreEqual(1, report.mismatchCount);
        }

        [Test]
        public void ElementsAreMatchedByOwnerNotByPosition()
        {
            // The same two beams written in the other order. Comparing positionally would call both
            // of them wrong and bury whatever actually changed.
            var report = _comparer.Compare(null, WithBeams((1, 0.5f), (2, 1f)),
                                           null, WithBeams((2, 1f), (1, 0.5f)));

            Assert.IsTrue(report.isClean);
        }

        [Test]
        public void AnElementOnlyOneSideHas_IsAMismatchEitherWay()
        {
            var missingOnTheRight = _comparer.Compare(null, WithBeams((1, 1f), (2, 1f)),
                                                      null, WithBeams((1, 1f)));
            Assert.AreEqual(1, missingOnTheRight.mismatchCount);
            Assert.AreEqual(MismatchReason.Missing, missingOnTheRight.mismatches[0].reason);

            var extraOnTheRight = _comparer.Compare(null, WithBeams((1, 1f)),
                                                    null, WithBeams((1, 1f), (2, 1f)));
            Assert.AreEqual(1, extraOnTheRight.mismatchCount);
            Assert.AreEqual(2, extraOnTheRight.mismatches[0].ownerId);
        }

        [Test]
        public void AWholeTypeAppearingOutOfNowhere_IsNotCalledClean()
        {
            var expected = NewState();
            var actual = NewState();
            actual.GetOrCreate<Pose>().GetOrCreate(1).value.x = 1f;

            var report = _comparer.Compare(null, expected, null, actual);

            Assert.IsFalse(report.isClean);
            Assert.AreEqual(MismatchReason.BlockMissing, report.mismatches[0].reason);
            Assert.AreEqual(typeof(Pose).FullName, report.mismatches[0].typeName);
        }

        [Test]
        public void Structure_MustAgreeOnContentsAndOrder()
        {
            var a = NewStructure();
            a.AddOrUpdate(1, 10, FrameSymbolTable.kNone);
            a.AddOrUpdate(2, 10, FrameSymbolTable.kNone);

            var same = NewStructure();
            same.AddOrUpdate(1, 10, FrameSymbolTable.kNone);
            same.AddOrUpdate(2, 10, FrameSymbolTable.kNone);

            Assert.IsTrue(_comparer.Compare(a, null, same, null).structureMatches);

            // The order is part of the recording, so the same objects in a different order have
            // already come apart.
            var reordered = NewStructure();
            reordered.AddOrUpdate(2, 10, FrameSymbolTable.kNone);
            reordered.AddOrUpdate(1, 10, FrameSymbolTable.kNone);

            Assert.IsFalse(_comparer.Compare(a, null, reordered, null).structureMatches);
        }

        [Test]
        public void Structure_DifferingByParent_IsNotAMatch()
        {
            var a = NewStructure();
            a.AddOrUpdate(1, 10, FrameSymbolTable.kNone);

            var reparented = NewStructure();
            reparented.AddOrUpdate(1, 10, 7);

            Assert.IsFalse(_comparer.Compare(a, null, reparented, null).structureMatches);
        }

        [Test]
        public void RecordedRun_ReplaysBackToTheSameStateOnTheSameMachine()
        {
            // The vertical slice in miniature: run it, record it, play it back, compare. This is the
            // number the whole design is judged on.
            FrameGate.sink = null;
            FrameGate.ResetState("[test] cleared");
            FrameGate.SetClock(new FrameCounterClock(FrameRate.FPS60));

            var live = NewState();
            var liveBlock = live.GetOrCreate<Beam>();
            var intensity = 0f;

            void Producer(ref Frame frame)
            {
                frame.state.GetOrCreate<Beam>().GetOrCreate(1).value.intensity = intensity;
                liveBlock.GetOrCreate(1).value.intensity = intensity;
                intensity += 0.25f;
            }

            var stream = new MemoryStream();
            var recorder = new FrameRecorder();
            // These tests are about the events they submit, so the recorder is asked not to add the
            // values it restates into each keyframe (see LiveEventRestateSystem): the editor session
            // this runs in has live objects of its own, and their values would show up as events
            // nothing in the test wrote.
            recorder.restateValues = false;

            FrameGate.AddFrameHeadHandler(Producer);
            recorder.Start(stream, leaveOpen: true);
            FrameGate.sink = recorder;

            try
            {
                for (int i = 0; i < 8; i++) FrameGate.Pump();
            }
            finally
            {
                FrameGate.sink = null;
                recorder.Stop();
                FrameGate.RemoveFrameHeadHandler(Producer);
            }

            using (var player = new FrameRecordPlayer(new MemoryStream(stream.ToArray())))
            {
                player.state.GetOrCreate<Beam>();
                while (player.Advance()) { }

                var report = _comparer.Compare(null, live, null, player.state);

                Assert.IsTrue(report.isClean, report.ToString());
                Assert.AreEqual(1f, report.matchRate);
            }

            FrameGate.ResetState("[test] cleared");
            FrameGate.SetClock(new FrameCounterClock(FrameRate.FPS60));
        }
    }
}
