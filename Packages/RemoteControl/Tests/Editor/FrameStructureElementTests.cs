// Copyright (c) You-Ri, 2026
using System.Collections.Generic;

using NUnit.Framework;

using Lilium.RemoteControl.Frames;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>An element of a collection, identified by a key of its own.</summary>
    [LiveClass("ElementRow")]
    public partial class ElementRow
    {
        [LiveField, LiveKey]
        public string name;

        [LiveField(lane = FrameLane.State)]
        public bool on = true;
    }

    /// <summary>What a polymorphic collection holds: several concrete types under one base.</summary>
    [LiveClass("ElementShape")]
    public abstract class ElementShape
    {
        [LiveField, LiveKey]
        public string name;
    }

    [LiveClass("ElementCircle")]
    public partial class ElementCircle : ElementShape
    {
        [LiveField(lane = FrameLane.State)]
        public float radius;
    }

    [LiveClass("ElementSquare")]
    public partial class ElementSquare : ElementShape
    {
        [LiveField(lane = FrameLane.State)]
        public float side;
    }

    /// <summary>An element with no key. Nothing to match on but where it sits.</summary>
    [LiveClass("ElementSlot")]
    public partial class ElementSlot
    {
        [LiveField(lane = FrameLane.State)]
        public int weight;
    }

    [LiveClass("ElementOwner")]
    public class ElementOwner
    {
        [LiveField]
        public List<ElementRow> rows = new List<ElementRow>();

        [LiveField]
        public List<ElementShape> shapes = new List<ElementShape>();

        [LiveField]
        public List<ElementSlot> slots = new List<ElementSlot>();

        /// <summary>A view of something else: rebuilt on demand, so nothing may stand it back up.</summary>
        [LiveProperty]
        public ElementRow[] mirror => rows.ToArray();
    }

    /// <summary>
    /// The shape of a collection is the inventory's to carry.
    ///
    /// The state lane holds an element's values, addressed by a composed id, and holds them whether
    /// or not the element still exists -- a block keeps the last value written to a row rather than
    /// dropping it. So the row set is not the element set, and "what is in this collection" had to
    /// be written down somewhere else. This is that: the inventory carries one entry per element,
    /// naming the member holding it, its key, and where it sits, and a replay reconciles the real
    /// collection against it.
    /// </summary>
    [TestFixture]
    public class FrameStructureElementTests
    {
        private const string kOwnerId = "element-owner";

        private ElementOwner _owner;
        private LiveObjectHandle? _handle;

        [SetUp]
        public void StartClean()
        {
            LiveObjectRegistry.ClearAll();
            LiveClass.RegisterFromAttributes<ElementRow>();
            LiveClass.RegisterFromAttributes<ElementCircle>();
            LiveClass.RegisterFromAttributes<ElementSquare>();
            LiveClass.RegisterFromAttributes<ElementSlot>();
            LiveClass.RegisterFromAttributes<ElementOwner>();

            FrameGate.ResetState("[test] cleared");
            FrameGate.SetClock(new FrameCounterClock(FrameRate.FPS60));

            _owner = new ElementOwner();
            _handle = LiveObjectRegistry.Create(typeof(ElementOwner), _owner, kOwnerId);
        }

        [TearDown]
        public void Finish()
        {
            _handle?.Unregister();
            LiveObjectRegistry.ClearAll();
            FrameGate.ResetState("[test] cleared");
            FrameGate.RestoreDefaultClock();
        }

        /// <summary>Takes an inventory of the world as it stands.</summary>
        private static StructureBlock Capture(FrameSymbolTable symbols)
        {
            var structure = new StructureBlock();
            LiveStructureSystem.CaptureInto(structure, symbols);
            return structure;
        }

        private static List<string> Names(List<ElementRow> rows)
        {
            var names = new List<string>();
            for (int i = 0; i < rows.Count; i++) names.Add(rows[i].name);
            return names;
        }

        [Test]
        public void AnElement_IsInTheInventoryUnderItsMemberAndKey()
        {
            _owner.rows.Add(new ElementRow { name = "one" });

            var symbols = new FrameSymbolTable();
            using var structure = Capture(symbols);

            var found = false;
            for (int i = 0; i < structure.count; i++)
            {
                var entry = structure[i];
                if (!entry.isElement) continue;

                Assert.AreEqual("rows", symbols.Resolve(entry.memberId));
                Assert.AreEqual("one", symbols.Resolve(entry.keyId));
                Assert.AreEqual(kOwnerId, symbols.Resolve(entry.parentId));
                Assert.AreEqual(0, entry.ordinal);
                found = true;
            }

            Assert.IsTrue(found, "the element is nowhere in the inventory");
        }

        [Test]
        public void AViewOfSomethingElse_StaysOutOfTheInventory()
        {
            // `mirror` is a getter with no setter: the collection is rebuilt every time it is asked
            // for. Standing its elements back up would write into a copy nobody keeps.
            _owner.rows.Add(new ElementRow { name = "one" });

            var symbols = new FrameSymbolTable();
            using var structure = Capture(symbols);

            for (int i = 0; i < structure.count; i++)
            {
                Assert.AreNotEqual("mirror", symbols.Resolve(structure[i].memberId));
            }
        }

        [Test]
        public void AnElementTheInventoryNames_IsStoodBackUp()
        {
            _owner.rows.Add(new ElementRow { name = "one" });
            _owner.rows.Add(new ElementRow { name = "two" });

            var symbols = new FrameSymbolTable();
            using var recorded = Capture(symbols);

            _owner.rows.Clear();

            LiveStructureSystem.ApplyFrom(recorded, symbols);

            CollectionAssert.AreEqual(new[] { "one", "two" }, Names(_owner.rows));
            Assert.AreEqual(2, LiveStructureSystem.elementsCreated);
        }

        [Test]
        public void AnElementTheInventoryDoesNotName_IsTakenAway()
        {
            // ⚠ Unlike a registered object, an element is taken away even though this system did not
            // put it there. A collection whose owner opted in has its whole shape recorded, or
            // "the operator deleted a row mid-take" would not come back on a scrub.
            _owner.rows.Add(new ElementRow { name = "one" });

            var symbols = new FrameSymbolTable();
            using var recorded = Capture(symbols);

            _owner.rows.Add(new ElementRow { name = "two" });

            LiveStructureSystem.ApplyFrom(recorded, symbols);

            CollectionAssert.AreEqual(new[] { "one" }, Names(_owner.rows));
            Assert.AreEqual(1, LiveStructureSystem.elementsRemoved);
        }

        [Test]
        public void ApplyingTheSameInventoryTwice_ChangesNothingTheSecondTime()
        {
            // Scrubbing goes back and forth over the same keyframe. Standing the same elements up
            // again would leave the collection growing for as long as someone drags.
            _owner.rows.Add(new ElementRow { name = "one" });
            _owner.rows.Add(new ElementRow { name = "two" });

            var symbols = new FrameSymbolTable();
            using var recorded = Capture(symbols);

            LiveStructureSystem.ApplyFrom(recorded, symbols);
            LiveStructureSystem.ApplyFrom(recorded, symbols);

            CollectionAssert.AreEqual(new[] { "one", "two" }, Names(_owner.rows));
            Assert.AreEqual(0, LiveStructureSystem.elementsCreated);
            Assert.AreEqual(0, LiveStructureSystem.elementsRemoved);
            Assert.AreEqual(0, LiveStructureSystem.elementsMoved);
        }

        [Test]
        public void AReorderedCollection_IsPutBackInTheRecordedOrder()
        {
            _owner.rows.Add(new ElementRow { name = "one" });
            _owner.rows.Add(new ElementRow { name = "two" });
            _owner.rows.Add(new ElementRow { name = "three" });

            var symbols = new FrameSymbolTable();
            using var recorded = Capture(symbols);

            var moved = _owner.rows[2];
            _owner.rows.RemoveAt(2);
            _owner.rows.Insert(0, moved);

            LiveStructureSystem.ApplyFrom(recorded, symbols);

            CollectionAssert.AreEqual(new[] { "one", "two", "three" }, Names(_owner.rows));
        }

        [Test]
        public void ReorderingACollection_MovesTheEpochAndSoWritesAKeyframe()
        {
            // Order is part of the recorded world, so a move is a structural change like any other.
            _owner.rows.Add(new ElementRow { name = "one" });
            _owner.rows.Add(new ElementRow { name = "two" });

            var symbols = new FrameSymbolTable();
            using var structure = Capture(symbols);
            var before = structure.epoch;

            var moved = _owner.rows[1];
            _owner.rows.RemoveAt(1);
            _owner.rows.Insert(0, moved);

            LiveStructureSystem.CaptureInto(structure, symbols);

            Assert.Greater(structure.epoch, before);
        }

        [Test]
        public void APolymorphicElement_IsStoodUpAsTheTypeThatWasRecorded()
        {
            // The declared element type is abstract, so making it would stand up nothing. What was
            // recorded is the concrete type, and that is what the entry carries.
            _owner.shapes.Add(new ElementCircle { name = "round" });
            _owner.shapes.Add(new ElementSquare { name = "boxy" });

            var symbols = new FrameSymbolTable();
            using var recorded = Capture(symbols);

            _owner.shapes.Clear();

            LiveStructureSystem.ApplyFrom(recorded, symbols);

            Assert.AreEqual(2, _owner.shapes.Count);
            Assert.IsInstanceOf<ElementCircle>(_owner.shapes[0]);
            Assert.IsInstanceOf<ElementSquare>(_owner.shapes[1]);
            Assert.AreEqual("round", _owner.shapes[0].name);
            Assert.AreEqual("boxy", _owner.shapes[1].name);
        }

        [Test]
        public void AKeylessElement_IsMatchedByWhereItSits()
        {
            // Nothing to identify one of these but its position, so the count is what is restored.
            _owner.slots.Add(new ElementSlot { weight = 1 });
            _owner.slots.Add(new ElementSlot { weight = 2 });

            var symbols = new FrameSymbolTable();
            using var recorded = Capture(symbols);

            _owner.slots.Clear();

            LiveStructureSystem.ApplyFrom(recorded, symbols);

            Assert.AreEqual(2, _owner.slots.Count);
        }

        [Test]
        public void AnEmptyCollection_IsStillInTheInventory()
        {
            // "recorded and empty" has to be a thing the file can say. Without it a replay cannot
            // tell an empty collection from one nothing looked at, and leaves it alone either way.
            var symbols = new FrameSymbolTable();
            using var structure = Capture(symbols);

            var found = false;
            for (int i = 0; i < structure.count; i++)
            {
                var entry = structure[i];
                if (!entry.isCollection) continue;
                if (symbols.Resolve(entry.memberId) != "rows") continue;

                Assert.AreEqual("ElementRow", symbols.Resolve(entry.typeId),
                    "an empty collection still says what it holds");
                found = true;
            }

            Assert.IsTrue(found, "the empty collection is nowhere in the inventory");
        }

        [Test]
        public void ACollectionRecordedEmpty_IsEmptiedOnApply()
        {
            // The hole the entry above closes: the take ends with the last row deleted, and a scrub
            // back to it has to take the row away again.
            var symbols = new FrameSymbolTable();
            using var recorded = Capture(symbols);

            _owner.rows.Add(new ElementRow { name = "late" });

            LiveStructureSystem.ApplyFrom(recorded, symbols);

            Assert.AreEqual(0, _owner.rows.Count, "a collection recorded empty was left holding a row");
            Assert.AreEqual(1, LiveStructureSystem.elementsRemoved);
        }

        [Test]
        public void AnElementInsideAnElement_IsReachedToo()
        {
            // The walk goes down: a collection can sit on something that is itself an element.
            _owner.rows.Add(new ElementRow { name = "outer" });

            var symbols = new FrameSymbolTable();
            using var structure = Capture(symbols);

            var elements = 0;
            for (int i = 0; i < structure.count; i++)
            {
                if (structure[i].isElement) elements++;
            }

            Assert.AreEqual(1, elements, "the walk reached something it should not have, or missed one");
        }
    }
}
