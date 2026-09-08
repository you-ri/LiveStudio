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
            /// reads in. Saved to the project settings rather than the scene, and off the frame for
            /// that reason alone (FrameLaneRules) -- nothing here says "None".</summary>
            [LiveField(persistScope = PersistScope.Project)] public float setting;

            /// <summary>
            /// Saved nowhere and saying nothing: a readout, a runtime status. Not in the file, so not
            /// in the take either.
            /// </summary>
            [LiveField(persistable = false)] public float transient;

            /// <summary>
            /// The exception, said out loud: saved nowhere -- a pose is not something a scene file
            /// holds -- but carried by the take because it asked to be.
            /// </summary>
            [LiveField(persistable = false, lane = FrameLane.State)] public float pose;

            /// <summary>The same exception on the sparse lane.</summary>
            [LiveField(persistable = false, lane = FrameLane.Event)] public float cue;

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

            [LiveField(persistScope = PersistScope.Project), Hide]
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
            [LiveField(persistScope = PersistScope.Project)] public float[] settings = new float[0];

            /// <summary>
            /// A setting of the machine held as an object rather than as a scalar. What it holds is
            /// a setting field by field, so nothing under it belongs in the take either.
            /// </summary>
            [LiveField(persistScope = PersistScope.Project)] public MachineSettings machine = new MachineSettings();

            /// <summary>The same shape, on the live data. The control for the member above.</summary>
            [LiveField] public WorldPart part = new WorldPart();

            /// <summary>
            /// Asks for the state lane and never reaches a block. The real causes are compile-time
            /// -- text with no declared width, a type that is not unmanaged, an owner that is not
            /// partial -- and all of them end the same way: the declaration says state and nothing
            /// is carrying it. Left out of the bridge below to stand for all of them.
            /// </summary>
            [LiveField(lane = FrameLane.State)] public float uncarried;
        }

        /// <summary>
        /// What a machine setting looks like once it is more than one number: the port it listens
        /// on, the intervals it polls at. Nothing here is of the world being recorded.
        /// </summary>
        [LiveClass]
        public class MachineSettings
        {
            /// <summary>
            /// On the event lane by its own declaration, so nothing but the member holding it can
            /// take it out of the take. That is what makes the test below say something: an
            /// undeclared field would be carried by a block (the generator reaches these fixtures
            /// too, and a field it says nothing about goes on the state lane), and the record would
            /// be omitted for that reason instead.
            /// </summary>
            [LiveField(lane = FrameLane.Event)] public float rate;

            [LiveField(lane = FrameLane.Event)] public float[] ports = new float[0];
        }

        /// <summary>Something of the world, held the same way and declared the same way.</summary>
        [LiveClass]
        public class WorldPart
        {
            [LiveField(lane = FrameLane.Event)] public float value;
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

        private static void _CaptureFixture(Fixture source, ref FixtureBlock block, FrameSymbolTable symbols)
        {
            block.carried = source.carried;
            block.shadowed = source.shadowed;
            block.undeclared = source.undeclared;
        }

        private static void _ApplyFixture(in FixtureBlock block, Fixture target, FrameSymbolTable symbols,
            ulong mask)
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
            LiveClass.RegisterFromAttributes<MachineSettings>();
            LiveClass.RegisterFromAttributes<WorldPart>();

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

        /// <summary>
        /// The lane follows from the persistence. A member nothing saves is off the frame without
        /// saying so; one that asks for a lane anyway gets it, whatever its persistence -- that is the
        /// one exception, and it is always written out loud.
        /// </summary>
        [Test]
        public void TheLaneFollowsThePersistence_UnlessTheMemberSaysOtherwise()
        {
            Assert.AreEqual(FrameLane.None, Member("transient").lane);
            Assert.AreEqual(FrameLane.State, Member("pose").lane);
            Assert.AreEqual(FrameLane.Event, Member("cue").lane);
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

            FrameGate._Enqueue(EventKind.Set, "test", target, "{\"value\":2.5}",
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
            FrameGate._Enqueue(EventKind.Set, "test", target, "{\"value\":0}",
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
            FrameGate._Enqueue(EventKind.Set, "test", target, body,
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
            FrameGate._Enqueue(EventKind.Set, "test", target, null,
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

            FrameGate._Enqueue(EventKind.Set, "test", target, "{\"value\":2.5}",
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

            FrameGate._Enqueue(EventKind.Set, "test", target, "{\"value\":2.5}",
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

        /// <summary>
        /// A write inside a member that is off the live data is off it too.
        ///
        /// The member being written says nothing about its lane, so on its own account it would be
        /// recorded; what takes it out of the take is what holds it. The declaration cannot say
        /// this, because a lane is declared on a type's member and the same type is reached through
        /// many owners -- only the path knows which owner this one came through.
        ///
        /// Before this, declaring a container off the frame kept the container out of the take and
        /// recorded everything inside it, which is the opposite of what the declaration asked for.
        /// </summary>
        [Test]
        public void AWriteInsideAMemberOffTheLane_LeavesNoRecord()
        {
            var fixture = new Fixture();
            var handle = LiveObjectRegistry.Create(typeof(Fixture), fixture, kResetFixtureId);

            using var session = new LiveEditorSession.Override(editorSession: false);

            try
            {
                var before = FrameGate.omittedRecordCount;

                _EnqueueSet("/live/object/" + kResetFixtureId + "/machine/rate", "{\"value\":2.5}");
                FrameGate.Pump();

                using var frame = new EventFrame();
                Assert.AreEqual(FrameLookup.Found, FrameGate.buffer.TryReadLatest(frame));

                Assert.AreEqual(2.5f, fixture.machine.rate, 1e-5f, "the write still lands");
                Assert.AreEqual(0, frame.eventCount, "a setting is a setting field by field");
                Assert.AreEqual(before + 1, FrameGate.omittedRecordCount);
            }
            finally
            {
                handle?.Unregister();
            }
        }

        /// <summary>
        /// The same write one member over, where nothing above it is off the frame. The control:
        /// without it the test above passes for a nested write that was never recorded at all.
        /// </summary>
        [Test]
        public void AWriteInsideAMemberOnTheLiveData_IsRecorded()
        {
            var fixture = new Fixture();
            var handle = LiveObjectRegistry.Create(typeof(Fixture), fixture, kResetFixtureId);

            using var session = new LiveEditorSession.Override(editorSession: false);

            try
            {
                var before = FrameGate.omittedRecordCount;

                _EnqueueSet("/live/object/" + kResetFixtureId + "/part/value", "{\"value\":2.5}");
                FrameGate.Pump();

                using var frame = new EventFrame();
                Assert.AreEqual(FrameLookup.Found, FrameGate.buffer.TryReadLatest(frame));

                Assert.AreEqual(2.5f, fixture.part.value, 1e-5f);
                Assert.AreEqual(before, FrameGate.omittedRecordCount,
                    "nothing above it asked for the omit");
                Assert.AreEqual(1, frame.eventCount,
                    "nothing else in the file says this member changed");
            }
            finally
            {
                handle?.Unregister();
            }
        }

        /// <summary>
        /// Shape follows the same rule as value. A collection held inside a setting is part of that
        /// setting, so growing it is not something the take remembers.
        /// </summary>
        [Test]
        public void AddingToACollectionInsideAMemberOffTheLane_LeavesNoRecord()
        {
            var fixture = new Fixture();
            var handle = LiveObjectRegistry.Create(typeof(Fixture), fixture, kResetFixtureId);

            using var session = new LiveEditorSession.Override(editorSession: false);

            try
            {
                var before = FrameGate.omittedRecordCount;

                _EnqueueAdd("/live/object/" + kResetFixtureId + "/machine/ports");
                FrameGate.Pump();

                using var frame = new EventFrame();
                Assert.AreEqual(FrameLookup.Found, FrameGate.buffer.TryReadLatest(frame));

                Assert.AreEqual(0, frame.eventCount, "a setting's collection is not part of the take");
                Assert.AreEqual(before + 1, FrameGate.omittedRecordCount);
                Assert.AreEqual(1, fixture.machine.ports.Length, "the element was still added");
            }
            finally
            {
                handle?.Unregister();
            }
        }

        /// <summary>
        /// The other half of the same rule: the walk does not go inside a member that is off the
        /// frame, so the state lane does not copy what a write is no longer allowed to record.
        ///
        /// Said for a single nested object as well as for a collection. The collection half has held
        /// since the inventory was built; the single-object half is the one that was missing, and
        /// without it a machine setting held as an object was copied into every frame.
        /// </summary>
        [Test]
        public void TheWalkDoesNotGoInsideAMemberOffTheLane()
        {
            Assert.IsFalse(LiveObjectWalk.HoldsNestedLiveObject(Member(nameof(Fixture.machine))),
                "a setting held as an object is a setting all the way down");
            Assert.IsTrue(LiveObjectWalk.HoldsNestedLiveObject(Member(nameof(Fixture.part))),
                "the control: an object of the world is still followed");
        }

        [Test]
        public void OmittingOneOfAGroup_LeavesTheRest()
        {
            // A bundle can touch both kinds at once. Dropping the whole group because one member of
            // it is state would lose the writes that are only in the event lane.
            var operations = new[]
            {
                new EventDescriptor(EventKind.Set, "PUT", "/live/a", "1"),
                new EventDescriptor(EventKind.Set, "PUT", "/live/b", "2"),
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
