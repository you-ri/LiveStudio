// Copyright (c) You-Ri, 2026
using NUnit.Framework;
using Lilium.RemoteControl.Frames;

// The blocks hold native storage, so every one created here is disposed. A test that leaks one is
// reported at the next domain reload, far from the test that caused it.

namespace Lilium.RemoteControl.Tests
{
    public class StructureBlockTests
    {
        [Test]
        public void AddOrUpdate_AppendsAndAdvancesTheEpoch()
        {
            using var structure = new StructureBlock();

            Assert.IsTrue(structure.AddOrUpdate(1, 10, InputSymbolTable.kNone));
            Assert.IsTrue(structure.AddOrUpdate(2, 10, 1));

            Assert.AreEqual(2, structure.count);
            Assert.AreEqual(2, structure.epoch);
            Assert.AreEqual(1, structure[1].parentId);
        }

        [Test]
        public void AddOrUpdate_WithNothingNew_DoesNotAdvanceTheEpoch()
        {
            // State is written against an epoch, so re-declaring what is already known must not
            // invalidate it.
            using var structure = new StructureBlock();
            structure.AddOrUpdate(1, 10, InputSymbolTable.kNone);
            var epoch = structure.epoch;

            Assert.IsFalse(structure.AddOrUpdate(1, 10, InputSymbolTable.kNone));
            Assert.AreEqual(epoch, structure.epoch);
        }

        [Test]
        public void AddOrUpdate_WithADifferentParent_IsAChange()
        {
            using var structure = new StructureBlock();
            structure.AddOrUpdate(1, 10, InputSymbolTable.kNone);
            var epoch = structure.epoch;

            Assert.IsTrue(structure.AddOrUpdate(1, 10, 7));
            Assert.AreEqual(epoch + 1, structure.epoch);
            Assert.AreEqual(7, structure[0].parentId);
        }

        [Test]
        public void Remove_KeepsTheRelativeOrderOfWhatIsLeft()
        {
            // The array is the order of record. Filling the hole with the last entry would reorder
            // the inventory, and two machines fed the same inputs would lay state out differently.
            using var structure = new StructureBlock();
            structure.AddOrUpdate(1, 10, InputSymbolTable.kNone);
            structure.AddOrUpdate(2, 10, InputSymbolTable.kNone);
            structure.AddOrUpdate(3, 10, InputSymbolTable.kNone);

            Assert.IsTrue(structure.Remove(2));

            Assert.AreEqual(2, structure.count);
            Assert.AreEqual(1, structure[0].id);
            Assert.AreEqual(3, structure[1].id);
        }

        [Test]
        public void Remove_OfSomethingAbsent_ChangesNothing()
        {
            using var structure = new StructureBlock();
            structure.AddOrUpdate(1, 10, InputSymbolTable.kNone);
            var epoch = structure.epoch;

            Assert.IsFalse(structure.Remove(99));
            Assert.AreEqual(epoch, structure.epoch);
            Assert.AreEqual(1, structure.count);
        }
    }

    public class StateBlockTests
    {
        private struct Pose
        {
            public float x;
            public float y;
        }

        [Test]
        public void GetOrCreate_AppendsOnceAndReturnsTheSameElement()
        {
            using var block = new StateBlock<Pose>();

            block.GetOrCreate(1).value.x = 5f;
            block.GetOrCreate(1).value.y = 6f;

            Assert.AreEqual(1, block.count);
            Assert.AreEqual(5f, block[0].value.x);
            Assert.AreEqual(6f, block[0].value.y);
        }

        [Test]
        public void GetOrCreate_ReturnsByReferenceSoAProducerWritesInPlace()
        {
            using var block = new StateBlock<Pose>();

            var source = FrameGate.ResolveSource("test");

            ref var element = ref block.GetOrCreate(42);
            element.source = source;
            element.time = 1234;
            element.value.x = 1f;

            Assert.AreEqual(source, block[0].source);
            Assert.AreEqual(1234, block[0].time);
            Assert.AreEqual(1f, block[0].value.x);
        }

        [Test]
        public void NewElement_HasNoSourceUntilAProducerClaimsIt()
        {
            using var block = new StateBlock<Pose>();

            Assert.IsFalse(block.GetOrCreate(1).source.isValid);
        }

        [Test]
        public void Remove_KeepsTheRelativeOrderOfWhatIsLeft()
        {
            using var block = new StateBlock<Pose>();
            block.GetOrCreate(1);
            block.GetOrCreate(2);
            block.GetOrCreate(3);

            Assert.IsTrue(block.Remove(2));

            Assert.AreEqual(2, block.count);
            Assert.AreEqual(1, block[0].ownerId);
            Assert.AreEqual(3, block[1].ownerId);
        }

        [Test]
        public void ElementSize_CoversTheMetaAndTheValue()
        {
            using var block = new StateBlock<Pose>();

            // 4 (owner) + 4 (source handle) + 8 (time) + 8 (two floats). The exact number matters
            // because a recording writes elements at a fixed stride.
            Assert.AreEqual(24, block.elementSize);
        }

        [Test]
        public void Growth_KeepsEveryElementAndItsOrder()
        {
            using var block = new StateBlock<Pose>();

            for (int i = 0; i < 100; i++) block.GetOrCreate(i).value.x = i;

            Assert.AreEqual(100, block.count);
            for (int i = 0; i < 100; i++)
            {
                Assert.AreEqual(i, block[i].ownerId, $"element {i} did not survive growth");
                Assert.AreEqual(i, block[i].value.x);
            }
        }
    }

    public class StateBlockSetTests
    {
        private struct Pose { public float x; }

        private struct Beam { public float intensity; }

        [Test]
        public void GetOrCreate_ReturnsOneBlockPerElementType()
        {
            using var set = new StateBlockSet();

            var pose = set.GetOrCreate<Pose>();
            var beam = set.GetOrCreate<Beam>();

            Assert.AreSame(pose, set.GetOrCreate<Pose>());
            Assert.AreNotSame(pose, (object)beam);
            Assert.AreEqual(2, set.blocks.Count);
        }

        [Test]
        public void Blocks_AreListedInTheOrderTheyWereFirstCreated()
        {
            // Iterated instead of the map, so a recording lays its types out the same way each run.
            using var set = new StateBlockSet();
            set.GetOrCreate<Beam>();
            set.GetOrCreate<Pose>();

            Assert.AreEqual(typeof(Beam), set.blocks[0].elementType);
            Assert.AreEqual(typeof(Pose), set.blocks[1].elementType);
        }

        [Test]
        public void Find_ReturnsNothingUntilSomethingHasWrittenThatType()
        {
            using var set = new StateBlockSet();

            Assert.IsNull(set.Find<Pose>());
            set.GetOrCreate<Pose>();
            Assert.IsNotNull(set.Find<Pose>());
        }

        [Test]
        public void Reset_EmptiesTheBlocksButKeepsTheTypeLayout()
        {
            using var set = new StateBlockSet();
            set.GetOrCreate<Pose>().GetOrCreate(1);
            set.GetOrCreate<Beam>().GetOrCreate(1);

            set.Reset();

            Assert.AreEqual(2, set.blocks.Count, "the type layout of a run stays put");
            Assert.AreEqual(0, set.blocks[0].count);
            Assert.AreEqual(0, set.blocks[1].count);
        }
    }
}
