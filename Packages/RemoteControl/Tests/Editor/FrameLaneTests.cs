// Copyright (c) You-Ri, 2026
using NUnit.Framework;
using Lilium.RemoteControl.Frames;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// A value belongs to one lane.
    ///
    /// The two lanes are of equal standing, so carrying the same value in both is not redundancy
    /// that costs nothing: the state lane copies it every frame regardless, and the input record
    /// pays its full width to repeat what the state lane already said.
    /// </summary>
    [TestFixture]
    public class FrameLaneTests
    {
        [LiveClass]
        public class Fixture
        {
            [LiveField(lane = FrameLane.State)] public float carried;

            [LiveField] public float requested;

            // The convention this codebase uses for a property with side effects: the value lives
            // in a hidden field and the property pushes it somewhere on write.
            [LiveField(lane = FrameLane.State), Hide]
            [FormerlyNamedAs("shadowed")]
            private float _shadowed;

            [LiveProperty]
            public float shadowed
            {
                get => _shadowed;
                set => _shadowed = value;
            }
        }

        [SetUp]
        public void StartClean()
        {
            LiveObjectRegistry.ClearAll();
            LiveClass.RegisterFromAttributes<Fixture>();
            FrameGate.ResetState("[test] cleared");
            FrameGate.SetClock(new FrameCounterClock(FrameRate.FPS60));
        }

        [TearDown]
        public void Finish()
        {
            LiveObjectRegistry.ClearAll();
            FrameGate.ResetState("[test] cleared");
            FrameGate.RestoreDefaultClock();
        }

        private static LivePropertyType Member(string name)
        {
            var liveClass = LiveClass.Find(typeof(Fixture));
            foreach (var member in liveClass.propertyTypes)
            {
                if (member.name == name) return member;
            }

            Assert.Fail($"'{name}' is not exposed");
            return null;
        }

        [Test]
        public void TheLaneADeclarationAsksFor_IsReadableAtRuntime()
        {
            // The generator reads the same declaration at compile time. Nothing could act on it at
            // runtime until now, which is why a state-lane write was also recorded as an input.
            Assert.AreEqual(FrameLane.State, Member("carried").lane);
            Assert.AreEqual(FrameLane.Input, Member("requested").lane);
        }

        [Test]
        public void APropertyOverAStateField_TakesTheFieldsLane()
        {
            // They are two faces of one value. A property left in the input lane over a field in the
            // state lane records that value twice -- once per face.
            Assert.AreEqual(FrameLane.State, Member("shadowed").lane);
        }

        [Test]
        public void OmittingOutsideAFrameHead_DoesNothing()
        {
            // The write path is reachable without the gate and must not have to know that.
            Assert.DoesNotThrow(() => FrameGate.OmitAppliedRecord("/live/object/a/b"));
        }

        [Test]
        public void AWriteTheStateLaneCarries_LeavesNoInputRecord()
        {
            const string target = "/live/object/fixture/carried";
            var before = FrameGate.omittedRecordCount;

            FrameGate._Enqueue(InputKind.PropertyWrite, "test", target, "{\"value\":2.5}",
                () =>
                {
                    FrameGate.OmitAppliedRecord(target);
                    return true;
                },
                verb: "PUT");

            FrameGate.Pump();

            using var frame = new InputFrame();
            Assert.AreEqual(FrameLookup.Found, FrameGate.buffer.TryReadLatest(frame));

            Assert.AreEqual(0, frame.inputCount, "the state lane already carries it");
            Assert.AreEqual(before + 1, FrameGate.omittedRecordCount,
                "counted, so 'no input for this' can be told from 'the input went missing'");
        }

        [Test]
        public void AWriteTheStateLaneDoesNotCarry_IsRecordedAsBefore()
        {
            const string target = "/live/object/fixture/requested";

            FrameGate._Enqueue(InputKind.PropertyWrite, "test", target, "{\"value\":2.5}",
                () =>
                {
                    FrameGate.StampAppliedPayload(target, typeof(float), 2.5f);
                    return true;
                },
                verb: "PUT");

            FrameGate.Pump();

            using var frame = new InputFrame();
            Assert.AreEqual(FrameLookup.Found, FrameGate.buffer.TryReadLatest(frame));

            Assert.AreEqual(1, frame.inputCount);
            Assert.AreEqual("System.Single", FrameGate.symbols.Resolve(frame[0].payloadTypeId));
        }

        [Test]
        public void OmittingOneOfAGroup_LeavesTheRest()
        {
            // A bundle can touch both kinds at once. Dropping the whole group because one member of
            // it is state would lose the writes that are only in the input lane.
            var operations = new[]
            {
                new InputDescriptor(InputKind.PropertyWrite, "PUT", "/live/a", "1"),
                new InputDescriptor(InputKind.PropertyWrite, "PUT", "/live/b", "2"),
            };

            FrameGate._Enqueue(operations, "test", () =>
            {
                FrameGate.OmitAppliedRecord("/live/a");
                return true;
            });

            FrameGate.Pump();

            using var frame = new InputFrame();
            Assert.AreEqual(FrameLookup.Found, FrameGate.buffer.TryReadLatest(frame));

            Assert.AreEqual(1, frame.inputCount);
            Assert.AreEqual("/live/b", FrameGate.symbols.Resolve(frame[0].targetId));
        }
    }
}
