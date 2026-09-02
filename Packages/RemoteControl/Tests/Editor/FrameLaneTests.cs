// Copyright (c) You-Ri, 2026
using NUnit.Framework;
using Lilium.RemoteControl.Frames;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// A value belongs to one lane.
    ///
    /// The two lanes are of equal standing, so carrying the same value in both is not redundancy
    /// that costs nothing: the state lane copies it every frame regardless, and the event record
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

            /// <summary>A setting of the machine: the resolution it renders at, the language it
            /// reads in. Not part of the world, so no lane carries it.</summary>
            [LiveField(lane = FrameLane.None)] public float setting;

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

            [LiveField(lane = FrameLane.None), Hide]
            [FormerlyNamedAs("shadowedSetting")]
            private float _shadowedSetting;

            [LiveProperty]
            public float shadowedSetting
            {
                get => _shadowedSetting;
                set => _shadowedSetting = value;
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
            // runtime until now, which is why a state-lane write was also recorded as an event.
            Assert.AreEqual(FrameLane.State, Member("carried").lane);
            Assert.AreEqual(FrameLane.Event, Member("requested").lane);
            Assert.AreEqual(FrameLane.None, Member("setting").lane);
        }

        /// <summary>
        /// What the write path branches on. It leaves a record for the event lane and for nothing
        /// else: the state lane because that value is copied every frame anyway, and None because
        /// the value is not part of the take at all.
        /// </summary>
        [Test]
        public void OnlyTheEventLane_WantsARecord()
        {
            Assert.AreEqual(FrameLane.Event, Member("requested").lane);
            Assert.AreNotEqual(FrameLane.Event, Member("carried").lane);
            Assert.AreNotEqual(FrameLane.Event, Member("setting").lane);
        }

        [Test]
        public void APropertyOverAFieldOffTheLane_IsAlsoOffIt()
        {
            // Same reasoning as the state pair below, and it matters more here: a setting exposed
            // through a property with side effects is the usual shape -- the field holds the value
            // and the property pushes it at Screen or QualitySettings. Missing the property would
            // record the half a client actually writes to.
            Assert.AreEqual(FrameLane.None, Member("shadowedSetting").lane);
        }

        [Test]
        public void APropertyOverAStateField_TakesTheFieldsLane()
        {
            // They are two faces of one value. A property left in the event lane over a field in the
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

            FrameGate._Enqueue(EventKind.PropertyWrite, "test", target, "{\"value\":2.5}",
                () =>
                {
                    FrameGate.OmitAppliedRecord(target);
                    return true;
                },
                verb: "PUT");

            FrameGate.Pump();

            using var frame = new EventFrame();
            Assert.AreEqual(FrameLookup.Found, FrameGate.buffer.TryReadLatest(frame));

            Assert.AreEqual(0, frame.eventCount, "the state lane already carries it");
            Assert.AreEqual(before + 1, FrameGate.omittedRecordCount,
                "counted, so 'no evt for this' can be told from 'the evt went missing'");
        }

        [Test]
        public void AWriteTheStateLaneDoesNotCarry_IsRecordedAsBefore()
        {
            const string target = "/live/object/fixture/requested";

            FrameGate._Enqueue(EventKind.PropertyWrite, "test", target, "{\"value\":2.5}",
                () =>
                {
                    FrameGate.StampAppliedPayload(target, typeof(float), 2.5f);
                    return true;
                },
                verb: "PUT");

            FrameGate.Pump();

            using var frame = new EventFrame();
            Assert.AreEqual(FrameLookup.Found, FrameGate.buffer.TryReadLatest(frame));

            Assert.AreEqual(1, frame.eventCount);
            Assert.AreEqual("System.Single", FrameGate.symbols.Resolve(frame[0].payloadTypeId));
        }

        /// <summary>
        /// A setting written mid-take leaves the recording alone. Before this, the declaration only
        /// kept it off the state lane and out of the keyframe restatement -- a write during the take
        /// was recorded like any other, and replaying it changed the operator's own language,
        /// resolution or quality level.
        /// </summary>
        [Test]
        public void AWriteToAMemberOffTheLane_LeavesNoRecord()
        {
            const string target = "/live/object/fixture/setting";
            var before = FrameGate.omittedRecordCount;

            FrameGate._Enqueue(EventKind.PropertyWrite, "test", target, "{\"value\":2.5}",
                () =>
                {
                    FrameGate.OmitAppliedRecord(target);
                    return true;
                },
                verb: "PUT");

            FrameGate.Pump();

            using var frame = new EventFrame();
            Assert.AreEqual(FrameLookup.Found, FrameGate.buffer.TryReadLatest(frame));

            Assert.AreEqual(0, frame.eventCount, "a setting is not part of the take");
            Assert.AreEqual(before + 1, FrameGate.omittedRecordCount);
        }

        [Test]
        public void OmittingOneOfAGroup_LeavesTheRest()
        {
            // A bundle can touch both kinds at once. Dropping the whole group because one member of
            // it is state would lose the writes that are only in the event lane.
            var operations = new[]
            {
                new EventDescriptor(EventKind.PropertyWrite, "PUT", "/live/a", "1"),
                new EventDescriptor(EventKind.PropertyWrite, "PUT", "/live/b", "2"),
            };

            FrameGate._Enqueue(operations, "test", () =>
            {
                FrameGate.OmitAppliedRecord("/live/a");
                return true;
            });

            FrameGate.Pump();

            using var frame = new EventFrame();
            Assert.AreEqual(FrameLookup.Found, FrameGate.buffer.TryReadLatest(frame));

            Assert.AreEqual(1, frame.eventCount);
            Assert.AreEqual("/live/b", FrameGate.symbols.Resolve(frame[0].targetId));
        }
    }
}
