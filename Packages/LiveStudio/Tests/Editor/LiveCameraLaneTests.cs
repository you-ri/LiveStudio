// Copyright (c) You-Ri, 2026
using NUnit.Framework;
using Lilium.RemoteControl;

namespace Lilium.LiveStudio.Tests
{
    /// <summary>
    /// Which lane carries the camera cut, and which value stands for it.
    ///
    /// A cut is rare -- a handful of them in a whole take -- which reads like an argument for the
    /// event lane, and it is the same wrong one the ambient settings make (see
    /// <see cref="EnvironmentLightLaneTests"/>). A recording holds no snapshot of the state a take
    /// started in, so the priorities set before recording began are nowhere in the file, and
    /// scrubbing into the middle of a take found no cut to restore: every camera looked untouched.
    ///
    /// The value is priority rather than a derived "which one is live" flag. The Brain picks the
    /// live camera by comparing priorities, so the flag is the folded result of all of them and
    /// cannot be folded back -- restoring "not live" says nothing about what to write, and the
    /// ordering the next live switch depends on would be lost.
    /// </summary>
    public class LiveCameraLaneTests
    {
        [Test]
        public void ThePriority_IsOnTheStateLane_SoEveryFrameSaysWhichCameraIsLive()
        {
            var liveClass = LiveClass.Get<LiveCamera>();
            var member = System.Array.Find(liveClass.propertyTypes, p => p.name == "priority");

            Assert.IsNotNull(member, "'priority' is not exposed any more");
            Assert.AreEqual(FrameLane.State, member.lane, "'priority' is not on the state lane");
        }

        [Test]
        public void TheBlock_CarriesThePriority_NamedAfterTheProperty()
        {
            // The property, not the shadow field: its getter reads the CinemachineCamera, so the
            // block records the priority the Brain is actually acting on rather than whatever was
            // last written to the field.
            //
            // 'name' rides along, as it does on every other proxy (LiveGameObject, LiveAsset,
            // LiveComponent). It did not here until 2026-09-06, and the reason is worth keeping:
            // the base stores it in a private '_name' field, and this type is generated against
            // that base as *metadata* -- another assembly -- where a private member is not among
            // the ones the generator can see. The pair is therefore invisible from here and the
            // public property is the only face a block of this type can carry, which is why the
            // base says the lane on the property as well as on the field.
            //
            // ⚠ Leaning on the lane's default instead is what put a property nothing saves on the
            // state lane, so this cannot go back to being implicit (see StateLaneDefaultTests).
            CollectionAssert.AreEquivalent(
                new[] { "name", "priority" },
                System.Array.ConvertAll(
                    typeof(LiveCamera.LiveStateBlock).GetFields(), f => f.Name));
        }

        [Test]
        public void TheLiveFlag_StaysOffTheLane_BecauseItIsDerivedFromThePriority()
        {
            var liveClass = LiveClass.Get<LiveCamera>();
            var member = System.Array.Find(liveClass.propertyTypes, p => p.name == "isLive");

            Assert.IsNotNull(member, "'isLive' is not exposed any more");
            Assert.AreNotEqual(FrameLane.State, member.lane,
                "'isLive' is derived from the priorities of every camera; carrying it would record "
                + "the same cut twice and in a form that cannot be applied back");
        }
    }
}
