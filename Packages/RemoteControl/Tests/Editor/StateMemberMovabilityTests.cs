// Copyright (c) You-Ri, 2026
using NUnit.Framework;

using Lilium.RemoteControl.Frames;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// A type that asks for more of the state lane than the movers can give it.
    ///
    /// Top level rather than nested, because the block is emitted inside the owner and a nested
    /// owner is refused (LRC003). That this file compiles at all is half of what is under test:
    /// before the generator asked, each of the three members below turned into a compile error
    /// inside generated code -- CS0200, CS0191, CS0176 -- naming a line nobody wrote.
    /// </summary>
    [LiveClass("UnmovableStateProbe")]
    public partial class UnmovableStateProbe
    {
        /// <summary>The control: reads out and writes back like any state member.</summary>
        [LiveField(lane = FrameLane.State)] public float movable;

        /// <summary>
        /// Computed from something else and never assigned. Refused by the design as well as by the
        /// compiler: a value the application works out is a result, and replaying an application's
        /// own result then comparing against it agrees with itself.
        /// </summary>
        [LiveProperty(lane = FrameLane.State)] public float derived => movable * 2f;

        /// <summary>Fixed at construction, so a replay has nowhere to put the recorded value.</summary>
        [LiveField(lane = FrameLane.State)] public readonly float fixedAtBirth = 3f;

        /// <summary>
        /// One value for every instance, where the block holds an element per object. Carrying it
        /// would copy the same value into every element and let any one of them write it back.
        /// </summary>
        [LiveField(lane = FrameLane.State)] public static float shared;
    }

    /// <summary>
    /// What the generator does with a state-lane member it cannot move both ways.
    ///
    /// The lane is a round trip -- capture reads the member out every frame, apply writes it back on
    /// replay -- and the generated movers assign in both directions with no ceremony. A member that
    /// can only do one half used to reach them anyway, so the refusal arrived as a compile error in
    /// code the author never wrote. It is now a diagnostic (LRC008) and the member stays where it
    /// was, which is the same shape every other refusal takes (LRC002, LRC005, LRC006).
    /// </summary>
    [TestFixture]
    public class StateMemberMovabilityTests
    {
        private static StateBridge Bridge()
        {
            var bridge = StateBridgeRegistry.Find(typeof(UnmovableStateProbe));
            Assert.IsNotNull(bridge,
                "a type keeps its block for the members that do move -- one refusal is not the type's refusal");
            return bridge;
        }

        [Test]
        public void AMemberThatMovesBothWays_IsStillCarried()
        {
            Assert.IsTrue(Bridge().Carries(nameof(UnmovableStateProbe.movable)));
        }

        [Test]
        public void AMemberWithNoSetter_IsLeftOutOfTheBlock()
        {
            Assert.IsFalse(Bridge().Carries(nameof(UnmovableStateProbe.derived)),
                "a replay has no way to write it back, so the lane cannot carry it");
        }

        [Test]
        public void AReadonlyField_IsLeftOutOfTheBlock()
        {
            Assert.IsFalse(Bridge().Carries(nameof(UnmovableStateProbe.fixedAtBirth)));
        }

        [Test]
        public void AStaticMember_IsLeftOutOfTheBlock()
        {
            Assert.IsFalse(Bridge().Carries(nameof(UnmovableStateProbe.shared)),
                "the block holds an element per object; a static has one value for all of them");
        }

        /// <summary>
        /// The two halves meeting. A member the generator refused is not carried, and the write path
        /// asks the bridge rather than the declaration -- so the value still reaches the recording
        /// through the event lane instead of falling out of both.
        /// </summary>
        [Test]
        public void ARefusedMember_IsNotReportedAsCarried()
        {
            var liveClass = LiveClass.Find(typeof(UnmovableStateProbe));
            Assert.IsNotNull(liveClass);

            foreach (var member in liveClass.propertyTypes)
            {
                if (member == null || member.name != nameof(UnmovableStateProbe.derived)) continue;

                Assert.AreEqual(FrameLane.State, member.lane, "the declaration still says state");
                Assert.IsFalse(LiveStateCarriage.IsCarriedByState(member, Bridge()),
                    "and the lane still says it is not carrying it");
                return;
            }

            Assert.Fail($"'{nameof(UnmovableStateProbe.derived)}' is not exposed");
        }
    }
}
