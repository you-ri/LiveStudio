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

            /// <summary>
            /// Says nothing about its lane, and is carried anyway: the generator puts an undeclared
            /// field in the block, while the attribute it was declared with still reports the event
            /// lane. Stands for most of the exposed surface.
            /// </summary>
            [LiveField] public float undeclared;

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

            /// <summary>A collection of the world. Its shape is nobody else's to say.</summary>
            [LiveField] public float[] tracked = new float[0];

            /// <summary>A collection of the machine, off the live data like any other setting.</summary>
            [LiveField(lane = FrameLane.None)] public float[] settings = new float[0];

            /// <summary>
            /// Asks for the state lane and never reaches a block. The real causes are compile-time
            /// -- text with no declared width, a type that is not unmanaged, an owner that is not
            /// partial -- and all of them end the same way: the declaration says state and nothing
            /// is carrying it. Left out of the bridge below to stand for all of them.
            /// </summary>
            [LiveField(lane = FrameLane.State)] public float uncarried;
        }

        /// <summary>
        /// What a generated block for <see cref="Fixture"/> would hold.
        ///
        /// Hand-written because the generator does not run on a test fixture (it is not partial),
        /// and what is under test is what the readers do once something is carrying a member --
        /// not how the block came to exist.
        /// </summary>
        private struct FixtureBlock
        {
            public float carried;
            public float shadowed;
            public float undeclared;
        }

        private static void _CaptureFixture(Fixture source, ref FixtureBlock block)
        {
            block.carried = source.carried;
            block.shadowed = source.shadowed;
            block.undeclared = source.undeclared;
        }

        private static void _ApplyFixture(in FixtureBlock block, Fixture target)
        {
            target.carried = block.carried;
            target.shadowed = block.shadowed;
            target.undeclared = block.undeclared;
        }

        [SetUp]
        public void StartClean()
        {
            LiveObjectRegistry.ClearAll();
            LiveClass.RegisterFromAttributes<Fixture>();

            // Named as reflection spells them: the shadowed pair is carried under the field's name,
            // which is what the generated block would assign to.
            StateBridgeRegistry.Register<Fixture, FixtureBlock>(_CaptureFixture, _ApplyFixture,
                nameof(Fixture.carried), "_shadowed", nameof(Fixture.undeclared));

            FrameGate.ResetState("[test] cleared");
            FrameGate.SetClock(new FrameCounterClock(FrameRate.FPS60));
        }

        [TearDown]
        public void Finish()
        {
            LiveObjectRegistry.ClearAll();
            StateBridgeRegistry.Unregister(typeof(Fixture));
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

        /// <summary>
        /// Resetting a member to its default asks the same question a write does, and for a while
        /// only the write path was asking it: a reset of a state-lane member left an event record,
        /// so a take remembered the reset and a replay performed it again. Reached through the same
        /// routing table a request and a replay both use.
        /// </summary>
        [Test]
        public void ResettingAMemberOffTheEventLane_LeavesNoRecord()
        {
            var fixture = new Fixture { carried = 5f };
            var handle = LiveObjectRegistry.Create(typeof(Fixture), fixture, kResetFixtureId);

            // Edit-mode tests run in the editor, where a write to a member the live scene owns is
            // refused before it reaches any of this. What is under test is what the running app does.
            using var session = new LiveEditorSession.Override(editorSession: false);

            try
            {
                const string target = "/live/object/" + kResetFixtureId + "/carried/@reset";
                var before = FrameGate.omittedRecordCount;

                _EnqueueReset(target);
                FrameGate.Pump();

                using var frame = new EventFrame();
                Assert.AreEqual(FrameLookup.Found, FrameGate.buffer.TryReadLatest(frame));

                Assert.AreEqual(0, frame.eventCount, "the state lane already carries this member");
                Assert.AreEqual(before + 1, FrameGate.omittedRecordCount,
                    "counted, so 'no evt for this' can be told from 'the evt went missing'");

                // What the reset leaves the member holding is not the subject here -- it depends on
                // a default having been captured, which needs a write first. That the operation ran
                // at all is asserted inside the gate, above.
            }
            finally
            {
                handle?.Unregister();
            }
        }

        [Test]
        public void ResettingAnEventLaneMember_IsRecordedAsBefore()
        {
            var fixture = new Fixture { requested = 5f };
            var handle = LiveObjectRegistry.Create(typeof(Fixture), fixture, kResetFixtureId);

            using var session = new LiveEditorSession.Override(editorSession: false);

            try
            {
                _EnqueueReset("/live/object/" + kResetFixtureId + "/requested/@reset");
                FrameGate.Pump();

                using var frame = new EventFrame();
                Assert.AreEqual(FrameLookup.Found, FrameGate.buffer.TryReadLatest(frame));

                Assert.AreEqual(1, frame.eventCount, "a reset of an event-lane member is a change to keep");
            }
            finally
            {
                handle?.Unregister();
            }
        }

        /// <summary>
        /// Changing a collection's shape is not the same question as writing a member's value, and
        /// the two rules part company on the state lane: it carries the values of the elements that
        /// exist and says nothing about which exist, so a shape change still has to be recorded.
        /// None means what it always means.
        /// </summary>
        [Test]
        public void AddingToACollectionOffTheLiveData_LeavesNoRecord()
        {
            var fixture = new Fixture();
            var handle = LiveObjectRegistry.Create(typeof(Fixture), fixture, kResetFixtureId);

            using var session = new LiveEditorSession.Override(editorSession: false);

            try
            {
                var before = FrameGate.omittedRecordCount;

                _EnqueueAdd("/live/object/" + kResetFixtureId + "/settings");
                FrameGate.Pump();

                using var frame = new EventFrame();
                Assert.AreEqual(FrameLookup.Found, FrameGate.buffer.TryReadLatest(frame));

                Assert.AreEqual(0, frame.eventCount, "a setting's collection is not part of the take");
                Assert.AreEqual(before + 1, FrameGate.omittedRecordCount);
                Assert.AreEqual(1, fixture.settings.Length, "the element was still added");
            }
            finally
            {
                handle?.Unregister();
            }
        }

        [Test]
        public void AddingToACollectionOfTheWorld_IsRecorded()
        {
            var fixture = new Fixture();
            var handle = LiveObjectRegistry.Create(typeof(Fixture), fixture, kResetFixtureId);

            using var session = new LiveEditorSession.Override(editorSession: false);

            try
            {
                _EnqueueAdd("/live/object/" + kResetFixtureId + "/tracked");
                FrameGate.Pump();

                using var frame = new EventFrame();
                Assert.AreEqual(FrameLookup.Found, FrameGate.buffer.TryReadLatest(frame));

                Assert.AreEqual(1, frame.eventCount, "nothing else says the collection grew");
                Assert.AreEqual(1, fixture.tracked.Length);
            }
            finally
            {
                handle?.Unregister();
            }
        }

        private static void _EnqueueAdd(string target)
        {
            FrameGate._Enqueue(EventKind.PropertyWrite, "test", target, "{\"value\":0}",
                () =>
                {
                    var ok = LiveObjectHandler.ApplyRecordedOperation(
                        null, DefaultLiveObjectResolver.Instance, "POST", target, "{\"value\":0}",
                        out var status, out var error);
                    Assert.IsTrue(ok, $"the add did not run: {status} {error}");
                    return ok;
                },
                verb: "POST");
        }

        /// <summary>Runs a write the way a request and a replay both run one.</summary>
        private static void _EnqueueSet(string target, string body)
        {
            FrameGate._Enqueue(EventKind.PropertyWrite, "test", target, body,
                () =>
                {
                    var ok = LiveObjectHandler.ApplyRecordedOperation(
                        null, DefaultLiveObjectResolver.Instance, "PUT", target, body,
                        out var status, out var error);
                    Assert.IsTrue(ok, $"the write did not run: {status} {error}");
                    return ok;
                },
                verb: "PUT");
        }

        /// <summary>
        /// The failure this exists for. Declaring the state lane is a request that can be refused --
        /// text with no width, a type that is not unmanaged, an owner that is not partial -- and the
        /// write path used to drop the record on the strength of the declaration alone. The block
        /// did not hold the value and the file did not say it changed, so the member fell out of
        /// both lanes and a replay left it at whatever the machine happened to hold.
        /// </summary>
        [Test]
        public void AWriteToAStateMemberNothingCarries_IsStillRecorded()
        {
            var fixture = new Fixture();
            var handle = LiveObjectRegistry.Create(typeof(Fixture), fixture, kResetFixtureId);

            using var session = new LiveEditorSession.Override(editorSession: false);

            try
            {
                _EnqueueSet("/live/object/" + kResetFixtureId + "/uncarried", "{\"value\":2.5}");
                FrameGate.Pump();

                using var frame = new EventFrame();
                Assert.AreEqual(FrameLookup.Found, FrameGate.buffer.TryReadLatest(frame));

                Assert.AreEqual(2.5f, fixture.uncarried, 1e-5f, "the write still lands");
                Assert.AreEqual(1, frame.eventCount,
                    "nothing else in the file says this member changed");
            }
            finally
            {
                handle?.Unregister();
            }
        }

        /// <summary>
        /// A field that never asked for the state lane and is carried by it regardless.
        ///
        /// The generator puts an undeclared field in the block; the attribute it was declared with
        /// answers <see cref="FrameLane.Event"/>. Branching on the declaration therefore kept an
        /// event record for a value the block was already copying every frame, which is the same
        /// value in both lanes -- what this whole area exists to prevent, arrived at from the other
        /// direction.
        /// </summary>
        [Test]
        public void AWriteToAFieldTheBlockCarriesWithoutSayingSo_LeavesNoRecord()
        {
            var fixture = new Fixture();
            var handle = LiveObjectRegistry.Create(typeof(Fixture), fixture, kResetFixtureId);

            using var session = new LiveEditorSession.Override(editorSession: false);

            try
            {
                Assert.AreEqual(FrameLane.Event, Member("undeclared").lane,
                    "the declaration says event -- the block carrying it is what has to be asked");

                var before = FrameGate.omittedRecordCount;

                _EnqueueSet("/live/object/" + kResetFixtureId + "/undeclared", "{\"value\":2.5}");
                FrameGate.Pump();

                using var frame = new EventFrame();
                Assert.AreEqual(FrameLookup.Found, FrameGate.buffer.TryReadLatest(frame));

                Assert.AreEqual(2.5f, fixture.undeclared, 1e-5f, "the write still lands");
                Assert.AreEqual(0, frame.eventCount, "the block already carries it");
                Assert.AreEqual(before + 1, FrameGate.omittedRecordCount);
            }
            finally
            {
                handle?.Unregister();
            }
        }

        /// <summary>The same question asked by a reset rather than a write.</summary>
        [Test]
        public void ResettingAStateMemberNothingCarries_IsStillRecorded()
        {
            var fixture = new Fixture();
            var handle = LiveObjectRegistry.Create(typeof(Fixture), fixture, kResetFixtureId);

            using var session = new LiveEditorSession.Override(editorSession: false);

            try
            {
                // A default is only captured by a write, and the reset needs one to run at all.
                _EnqueueSet("/live/object/" + kResetFixtureId + "/uncarried", "{\"value\":2.5}");
                FrameGate.Pump();

                _EnqueueReset("/live/object/" + kResetFixtureId + "/uncarried/@reset");
                FrameGate.Pump();

                using var frame = new EventFrame();
                Assert.AreEqual(FrameLookup.Found, FrameGate.buffer.TryReadLatest(frame));

                Assert.AreEqual(1, frame.eventCount,
                    "a reset of a member no lane carries is a change to keep");
            }
            finally
            {
                handle?.Unregister();
            }
        }

        /// <summary>
        /// The other side of it: a member the bridge does move is still kept out of the event lane,
        /// which is the whole point of declaring the state lane in the first place.
        /// </summary>
        [Test]
        public void AWriteToAStateMemberTheBridgeCarries_LeavesNoRecord()
        {
            var fixture = new Fixture();
            var handle = LiveObjectRegistry.Create(typeof(Fixture), fixture, kResetFixtureId);

            using var session = new LiveEditorSession.Override(editorSession: false);

            try
            {
                var before = FrameGate.omittedRecordCount;

                _EnqueueSet("/live/object/" + kResetFixtureId + "/carried", "{\"value\":2.5}");
                FrameGate.Pump();

                using var frame = new EventFrame();
                Assert.AreEqual(FrameLookup.Found, FrameGate.buffer.TryReadLatest(frame));

                Assert.AreEqual(2.5f, fixture.carried, 1e-5f, "the write still lands");
                Assert.AreEqual(0, frame.eventCount, "the state lane already carries it");
                Assert.AreEqual(before + 1, FrameGate.omittedRecordCount);
            }
            finally
            {
                handle?.Unregister();
            }
        }

        private const string kResetFixtureId = "lane-reset-fixture";

        /// <summary>
        /// Runs a reset the way a request and a replay both run one: through the routing table,
        /// inside the gate, addressed by the path the record is keyed on.
        /// </summary>
        private static void _EnqueueReset(string target)
        {
            FrameGate._Enqueue(EventKind.PropertyWrite, "test", target, null,
                () =>
                {
                    var ok = LiveObjectHandler.ApplyRecordedOperation(
                        null, DefaultLiveObjectResolver.Instance, "POST", target, null,
                        out var status, out var error);
                    Assert.IsTrue(ok, $"the reset did not run: {status} {error}");
                    return ok;
                },
                verb: "POST");
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
