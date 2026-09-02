// Copyright (c) You-Ri, 2026
using System.Collections.Generic;
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

            // Two of these are 128 bytes: more than the whole shared buffer the declared path used
            // to have, which is what makes them useful here.
            public Matrix4x4 wide;
            public Matrix4x4 wider;

            /// <summary>Counts what the apply side actually wrote, not what it was handed.</summary>
            public int drivenWrites;

            private float _driven;

            public float driven
            {
                get => _driven;
                set
                {
                    _driven = value;
                    drivenWrites++;
                }
            }
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

        /// <summary>Declares members on an asset the way the inspector does.</summary>
        private static LiveClassAsset.TypeDefinition DefineAsset(params LiveClassAssetMember[] members)
        {
            var definition = new LiveClassAsset.TypeDefinition
            {
                typeName = typeof(Fixture).AssemblyQualifiedName,
            };
            definition.members.AddRange(members);
            return definition;
        }

        [Test]
        public void AValueTheLaneCannotMove_IsCarriedAsEvents()
        {
            // A string field: the default puts it on the state lane and the lane cannot move it.
            // What matters is that the answer is the lane something actually carries it on -- the
            // write path omits the event record whenever the registration says State, so a member
            // registered State that the block leaves out is a member nothing carries at all.
            var member = new LiveClassAssetMember { path = "label" };
            var definition = DefineAsset(member);

            Assert.AreEqual(FrameLane.State, member.ResolveLane(typeof(Fixture)));

            var lane = definition.EffectiveLaneOf(member, typeof(Fixture), out var refusal);

            Assert.AreEqual(FrameLane.Event, lane);
            Assert.AreEqual(LiveClassAsset.TypeDefinition.LaneRefusal.UnsupportedType, refusal);
        }

        [Test]
        public void AValueTheLaneCanMove_IsCarriedByTheLaneItAsksFor()
        {
            var member = new LiveClassAssetMember { path = "driven", lane = LiveClassAssetLane.State };
            var definition = DefineAsset(member);

            Assert.AreEqual(FrameLane.State, definition.EffectiveLaneOf(member, typeof(Fixture)));
            Assert.AreEqual(typeof(float), member.ResolveValueType(typeof(Fixture)));
        }

        [Test]
        public void ADeclarationLargerThanOneSharedBuffer_IsCarriedWhole()
        {
            // The declared path used to pack every type into one 112-byte buffer, so a declaration
            // this size lost its tail. The block is built to the declaration now, so there is no
            // size at which values start disappearing.
            var liveClass = Declare(
                Member("wide", FrameLane.State),
                Member("wider", FrameLane.State),
                Member("intensity", FrameLane.State));

            var bridge = DeclaredStateBridge.Build(liveClass);

            Assert.AreEqual(3, bridge.slotCount, "a member fell off the end of the block");
            Assert.AreEqual(DeclaredStateBridge.kLayoutSize + 64 + 64 + 4, bridge.payloadSize);

            var subject = new Fixture
            {
                wide = Matrix4x4.identity,
                wider = Matrix4x4.Scale(new Vector3(2f, 3f, 4f)),
                intensity = 9f,
            };
            var handle = LiveObjectRegistry.Create(subject, "wide-fixture");
            Assert.IsNotNull(handle);

            bridge.Capture(subject, 1, _state, default, 0);

            subject.wide = default;
            subject.wider = default;
            subject.intensity = 0f;

            Assert.IsTrue(bridge.Apply(subject, 1, _state));

            Assert.AreEqual(Matrix4x4.identity, subject.wide);
            Assert.AreEqual(Matrix4x4.Scale(new Vector3(2f, 3f, 4f)), subject.wider);
            Assert.AreEqual(9f, subject.intensity, 1e-5f);
        }

        [Test]
        public void Apply_DoesNotWriteAValueTheTargetAlreadyHolds()
        {
            // The state lane restates every member on every frame, and this path goes in through
            // the accessor a REST write uses -- the old value read back, the changing and changed
            // notifications, the editor dirty mark. Without asking first, a replay pays all of that
            // sixty times a second for values that did not move.
            var bridge = DeclaredStateBridge.Build(Declare(Member("driven", FrameLane.State)));
            var subject = new Fixture { driven = 3f };
            var handle = LiveObjectRegistry.Create(subject, "guarded-fixture");
            Assert.IsNotNull(handle);

            bridge.Capture(subject, 1, _state, default, 0);

            subject.drivenWrites = 0;
            Assert.IsTrue(bridge.Apply(subject, 1, _state));
            Assert.IsTrue(bridge.Apply(subject, 1, _state));

            Assert.AreEqual(0, subject.drivenWrites, "an unchanged value was written back anyway");
            Assert.AreEqual(3f, subject.driven);
        }

        [Test]
        public void Apply_StillWritesAValueThatMoved()
        {
            var bridge = DeclaredStateBridge.Build(Declare(Member("driven", FrameLane.State)));
            var subject = new Fixture { driven = 3f };
            var handle = LiveObjectRegistry.Create(subject, "guarded-fixture");
            Assert.IsNotNull(handle);

            bridge.Capture(subject, 1, _state, default, 0);

            subject.driven = 8f;
            subject.drivenWrites = 0;

            Assert.IsTrue(bridge.Apply(subject, 1, _state));
            Assert.IsTrue(bridge.Apply(subject, 1, _state));

            // Once for the value that had moved, and nothing for the second pass: applying the same
            // frame twice has to land in the same place, which is what a scrub does all day.
            Assert.AreEqual(1, subject.drivenWrites);
            Assert.AreEqual(3f, subject.driven);
        }

        [Test]
        public void ATypeOnlyPaysForWhatItDeclared()
        {
            // The point of sizing the block from the declaration: a type carrying three small
            // values does not pay for the largest declaration anyone might write.
            var liveClass = Declare(
                Member("intensity", FrameLane.State),
                Member("on", FrameLane.State),
                Member("offset", FrameLane.State));

            var bridge = DeclaredStateBridge.Build(liveClass);
            var block = (DeclaredStateBlock)bridge.EnsureBlock(_state);

            Assert.AreEqual(DeclaredStateBridge.kLayoutSize + 4 + 4 + 12, bridge.payloadSize);
            Assert.AreEqual(DeclaredStateBlock.StrideFor(bridge.payloadSize), block.elementSize);
            Assert.AreEqual(48, block.elementSize, "16 meta + 8 layout + 20 values, padded to 8");
        }

        [Test]
        public void TwoDeclaredTypes_DoNotShareABlock()
        {
            // They used to, which is why a recording needed a layout hash to tell them apart. Now
            // each one is its own block, named after the type it belongs to.
            var bridge = DeclaredStateBridge.Build(Declare(Member("intensity", FrameLane.State)));
            var block = bridge.EnsureBlock(_state);

            Assert.AreEqual(typeof(Fixture), block.elementType);
            Assert.AreEqual(typeof(Fixture).FullName, block.elementType.FullName,
                "the block has to be named after the type so a recording can find it again");
        }

        [Test]
        public void AnEnumOnTheLane_IsMeasuredByWhatItIsUnderneath()
        {
            // Marshal.SizeOf refuses an enum type outright on this runtime, so asking it directly
            // would throw at registration -- for a declaration naming an enum, which is ordinary.
            Assert.AreEqual(4, DeclaredStateBridge.SizeOf(typeof(System.DayOfWeek)));
            Assert.IsTrue(DeclaredStateBridge.CanCarry(typeof(System.DayOfWeek)));
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
