// Copyright (c) You-Ri, 2026
using NUnit.Framework;
using Lilium.RemoteControl.Frames;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// The inventory says what the values belong to.
    ///
    /// Without it a recording holds state addressed to objects it never mentions, and a replay has
    /// nothing to tell it whether the world it is writing into is the world that was recorded.
    /// </summary>
    [TestFixture]
    public class FrameStructureTests
    {
        [LiveClass]
        public class Thing
        {
            [LiveField] public float value;
        }

        private StructureBlock _structure;
        private FrameSymbolTable _symbols;

        [SetUp]
        public void StartClean()
        {
            LiveObjectRegistry.ClearAll();
            LiveClass.RegisterFromAttributes<Thing>();

            _structure = new StructureBlock();
            _symbols = new FrameSymbolTable();
        }

        [TearDown]
        public void Finish()
        {
            LiveObjectRegistry.ClearAll();
            _structure.Dispose();
        }

        [Test]
        public void EveryRegisteredObject_IsInTheInventory()
        {
            LiveObjectRegistry.Create<Thing>(new Thing(), "thing-a");
            LiveObjectRegistry.Create<Thing>(new Thing(), "thing-b");

            Assert.AreEqual(2, LiveStructureSystem.CaptureInto(_structure, _symbols));

            Assert.GreaterOrEqual(_structure.IndexOf(_symbols.Intern("thing-a")), 0);
            Assert.GreaterOrEqual(_structure.IndexOf(_symbols.Intern("thing-b")), 0);
        }

        [Test]
        public void AnObjectThatIsGone_IsTakenOut()
        {
            var a = LiveObjectRegistry.Create<Thing>(new Thing(), "thing-a");
            LiveObjectRegistry.Create<Thing>(new Thing(), "thing-b");
            LiveStructureSystem.CaptureInto(_structure, _symbols);

            a.Value.Unregister();

            // Destroying is as much a part of the inventory as creating: scrub back past a spawn and
            // what it spawned has to disappear again.
            Assert.AreEqual(1, LiveStructureSystem.CaptureInto(_structure, _symbols));
            Assert.AreEqual(-1, _structure.IndexOf(_symbols.Intern("thing-a")));
            Assert.GreaterOrEqual(_structure.IndexOf(_symbols.Intern("thing-b")), 0);
        }

        [Test]
        public void CapturingTwiceWithNothingChanged_DoesNotAdvanceTheEpoch()
        {
            // State is only readable against the structure it was written for, so an epoch that
            // moved for no reason would invalidate values that are still perfectly good.
            LiveObjectRegistry.Create<Thing>(new Thing(), "thing-a");

            LiveStructureSystem.CaptureInto(_structure, _symbols);
            var epoch = _structure.epoch;

            LiveStructureSystem.CaptureInto(_structure, _symbols);

            Assert.AreEqual(epoch, _structure.epoch);
        }

        [Test]
        public void AddingAnObject_AdvancesTheEpoch()
        {
            LiveObjectRegistry.Create<Thing>(new Thing(), "thing-a");
            LiveStructureSystem.CaptureInto(_structure, _symbols);
            var epoch = _structure.epoch;

            LiveObjectRegistry.Create<Thing>(new Thing(), "thing-b");
            LiveStructureSystem.CaptureInto(_structure, _symbols);

            Assert.Greater(_structure.epoch, epoch);
        }

        [Test]
        public void TheOrderIsKept_SoTwoMachinesLayTheWorldOutTheSameWay()
        {
            LiveObjectRegistry.Create<Thing>(new Thing(), "a");
            LiveObjectRegistry.Create<Thing>(new Thing(), "b");
            LiveObjectRegistry.Create<Thing>(new Thing(), "c");
            LiveStructureSystem.CaptureInto(_structure, _symbols);

            var first = _structure[0].id;
            var second = _structure[1].id;

            // Removing the middle one must not pull the last into its place: the array is the order
            // of record, and reordering it behind everyone's back is how two machines diverge.
            LiveObjectRegistry.FindById("b")?.Unregister();
            LiveStructureSystem.CaptureInto(_structure, _symbols);

            Assert.AreEqual(first, _structure[0].id);
            Assert.AreNotEqual(second, _structure[1].id);
            Assert.AreEqual(2, _structure.count);
        }

        [Test]
        public void AnEntrySomeoneElsePutThere_IsLeftAlone()
        {
            // The block is shared. Something else may have stood an entry up in it -- a spawned
            // prop, a test -- and taking it out because the registry has never heard of it would be
            // this system deciding it is the only one allowed to say what exists. It also moves the
            // epoch every frame, which turns every frame into a keyframe.
            LiveObjectRegistry.Create<Thing>(new Thing(), "thing-a");
            LiveStructureSystem.CaptureInto(_structure, _symbols);

            _structure.AddOrUpdate(_symbols.Intern("someone-else"), _symbols.Intern("Other"),
                FrameSymbolTable.kNone);
            var epoch = _structure.epoch;

            LiveStructureSystem.CaptureInto(_structure, _symbols);

            Assert.GreaterOrEqual(_structure.IndexOf(_symbols.Intern("someone-else")), 0,
                "the other producer's entry survives");
            Assert.AreEqual(epoch, _structure.epoch, "and nothing moved, so nothing was written");
        }

        [Test]
        public void TwoThingsWantingTheCapture_BothGetIt_AndTheFirstToFinishDoesNotTakeItAway()
        {
            // A recording and an open viewer both want this running. A plain on/off let whichever
            // stopped first turn it off under the other, which reads as a recording that quietly
            // stopped carrying state.
            //
            // Measured against whatever already holds it rather than against zero: an open LiveData
            // Viewer is a legitimate holder, and a test that assumed it started from nothing would
            // release someone else's claim -- which is exactly the drift this counting prevents.
            var before = LiveStructureSystem.isRunning;

            LiveStructureSystem.Retain();
            LiveStructureSystem.Retain();
            Assert.IsTrue(LiveStructureSystem.isRunning);

            LiveStructureSystem.Release();
            Assert.IsTrue(LiveStructureSystem.isRunning, "the other one still wants it");

            LiveStructureSystem.Release();
            Assert.AreEqual(before, LiveStructureSystem.isRunning,
                "put back exactly as it was found");
        }

        [Test]
        public void AnObjectWithNoId_IsLeftOut()
        {
            // The inventory exists to be addressed. Something nothing can name has no place in it.
            LiveObjectRegistry.GetOrCreateWithoutId(LiveClass.Find(typeof(Thing)), new Thing());

            Assert.AreEqual(0, LiveStructureSystem.CaptureInto(_structure, _symbols));
        }
    }
}
