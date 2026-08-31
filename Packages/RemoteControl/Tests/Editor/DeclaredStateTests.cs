// Copyright (c) You-Ri, 2026
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Lilium.RemoteControl.Frames;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// Putting a type declared by an asset onto the state lane.
    ///
    /// The generated path gets a struct shaped like the members it carries. A declaration read at
    /// load has nothing to generate from, so the values are packed into a fixed buffer and the
    /// declaration says where each one sits. What that costs is the check the generated path gets
    /// for free -- an element's size no longer says what is inside it -- which is why the layout is
    /// hashed and carried with the values.
    /// </summary>
    [TestFixture]
    public class DeclaredStateTests
    {
        /// <summary>A plain type with no attributes on it, as an asset-declared type would be.</summary>
        public class Fixture
        {
            public float intensity;
            public bool on;
            public Vector3 offset;

            public string label = string.Empty;

            public float driven { get; set; }
        }

        private StateBlockSet _state;

        [SetUp]
        public void StartClean()
        {
            LiveObjectRegistry.ClearAll();
            _state = new StateBlockSet();
        }

        [TearDown]
        public void Finish()
        {
            LiveObjectRegistry.ClearAll();
            StateBridgeRegistry.Unregister(typeof(Fixture));
            _state.Dispose();
        }

        /// <summary>Declares members the way an asset does, without any attributes involved.</summary>
        private static LiveClass Declare(params LivePropertyDefine[] members)
        {
            return LiveClass.Register(typeof(Fixture), nameof(Fixture), members);
        }

        private static LivePropertyDefine Member(string name, FrameLane lane)
        {
            return new LivePropertyDefine { name = name, path = name, lane = lane };
        }

        [Test]
        public void AFieldWithNoLaneSaid_GoesOnTheStateLane()
        {
            // A field usually holds a value something else drives every frame, which is what the
            // state lane is for. A property is usually written from outside, which is not.
            var member = new LiveClassAssetMember { path = "intensity" };
            var property = new LiveClassAssetMember { path = "driven" };

            Assert.AreEqual(FrameLane.State, member.ResolveLane(typeof(Fixture)));
            Assert.AreEqual(FrameLane.Event, property.ResolveLane(typeof(Fixture)));
        }

        [Test]
        public void ALaneSaidOutright_WinsOverTheDefault()
        {
            var member = new LiveClassAssetMember
            {
                path = "intensity",
                lane = LiveClassAssetLane.Event,
            };

            Assert.AreEqual(FrameLane.Event, member.ResolveLane(typeof(Fixture)));
        }

        [Test]
        public void ATypeThatDeclaresNoState_GetsNoBridge()
        {
            // Making a block is how a type announces it belongs on the lane at all, so a type with
            // nothing on it must not make one.
            var liveClass = Declare(Member("intensity", FrameLane.Event));

            Assert.IsNull(DeclaredStateBridge.Build(liveClass));
        }

        [Test]
        public void DeclaredValues_MakeTheRoundTrip()
        {
            var liveClass = Declare(
                Member("intensity", FrameLane.State),
                Member("on", FrameLane.State),
                Member("offset", FrameLane.State));

            var bridge = DeclaredStateBridge.Build(liveClass);
            Assert.IsNotNull(bridge);
            Assert.AreEqual(3, bridge.slotCount);

            var subject = new Fixture { intensity = 2.5f, on = true, offset = new Vector3(1, 2, 3) };
            var handle = LiveObjectRegistry.Create(subject, "fixture");
            Assert.IsNotNull(handle);

            var ownerId = 7;
            bridge.Capture(subject, ownerId, _state, default, 0);

            // Changed underneath, then written back from the frame: this is what a replay does.
            subject.intensity = 0f;
            subject.on = false;
            subject.offset = Vector3.zero;

            Assert.IsTrue(bridge.Apply(subject, ownerId, _state));

            Assert.AreEqual(2.5f, subject.intensity, 1e-5f);
            Assert.IsTrue(subject.on, "bool cannot be pinned, so it goes through the marshaller");
            Assert.AreEqual(new Vector3(1, 2, 3), subject.offset);
        }

        [Test]
        public void AValueTheFrameCannotCarry_IsLeftOffRatherThanPacked()
        {
            // A reference has no fixed width to reserve. Refused at registration, where it can be
            // said once, rather than dropped every frame.
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("label"));

            var liveClass = Declare(
                Member("label", FrameLane.State),
                Member("intensity", FrameLane.State));

            var bridge = DeclaredStateBridge.Build(liveClass);

            Assert.AreEqual(1, bridge.slotCount);
        }

        [Test]
        public void ARecordingWrittenUnderADifferentDeclaration_IsRefused()
        {
            // Every declared type shares one struct, so its width says nothing about what is inside
            // it. Two builds can disagree completely about the layout and still agree on the size,
            // and reading one under the other lands each value in the wrong member.
            var before = DeclaredStateBridge.Build(Declare(
                Member("intensity", FrameLane.State),
                Member("on", FrameLane.State)));

            var subject = new Fixture { intensity = 2.5f, on = true };
            LiveObjectRegistry.Create(subject, "fixture");
            before.Capture(subject, 7, _state, default, 0);

            // The declaration is edited: a member is dropped, so everything after it moves.
            LiveObjectRegistry.ClearAll();
            var after = DeclaredStateBridge.Build(Declare(Member("on", FrameLane.State)));
            Assert.AreNotEqual(before.layout, after.layout, "the hash has to notice the move");

            var target = new Fixture();
            LiveObjectRegistry.Create(target, "fixture");

            Assert.IsFalse(after.Apply(target, 7, _state),
                "refused rather than read as whatever the bytes happen to say now");
            Assert.IsFalse(target.on, "and nothing was written");
        }

        [Test]
        public void MovingAMember_ChangesTheLayout()
        {
            // Moving one is as much a change as adding one: the bytes after it all shift.
            var a = DeclaredStateBridge.Build(Declare(
                Member("intensity", FrameLane.State),
                Member("offset", FrameLane.State)));

            LiveObjectRegistry.ClearAll();

            var b = DeclaredStateBridge.Build(Declare(
                Member("offset", FrameLane.State),
                Member("intensity", FrameLane.State)));

            Assert.AreNotEqual(a.layout, b.layout);
        }
    }
}
