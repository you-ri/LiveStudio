// Copyright (c) You-Ri, 2026
using System.Linq;
using NUnit.Framework;
using Lilium.RemoteControl.Frames;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// Two members of the same width, which is the case a width check cannot see.
    ///
    /// ⚠ Declared beside the fixture rather than inside it. A type nested in a class the generator
    /// cannot write a second half of gets no block at all, and the symptom is not a diagnostic --
    /// it is this type quietly having no description, which is exactly what these tests would then
    /// be asserting about nothing.
    /// </summary>
    [LiveClass("StateSchemaLantern")]
    public partial class Lantern
    {
        [LiveField(lane = FrameLane.State)] public float intensity;
        [LiveField(lane = FrameLane.State)] public float range;
    }

    /// <summary>
    /// A recording says what each block holds, so a take made before a type gained or lost a member
    /// can still be played.
    ///
    /// Until this existed the recording said only how wide an element was, and a width cannot
    /// survive a member being added: one member moving cost the whole type its state, every other
    /// member included. These are the pieces that let a reader put back what both builds have.
    /// </summary>
    public class StateSchemaTests
    {
        [Test]
        public void AGeneratedTypeDescribesItsBlock()
        {
            var schema = StateSchemaRegistry.Find(typeof(Lantern).FullName);

            Assert.IsNotNull(schema, "the generator declared no description for a state-lane type");
            CollectionAssert.AreEqual(new[] { "intensity", "range" },
                schema.members.Select(m => m.name).ToArray());

            // Read off the block struct at load rather than worked out by the generator, which is
            // what keeps pointer arithmetic out of assemblies that may not allow it.
            Assert.AreEqual(typeof(float).FullName, schema.members[0].typeName);
            Assert.AreEqual(4, schema.members[0].size);
            Assert.AreEqual(0, schema.members[0].offset);
            Assert.AreEqual(4, schema.members[1].offset);

            // The metadata is three fields of a struct, not a constant sixteen -- though for a
            // block of floats it comes to sixteen.
            Assert.AreEqual(16, schema.metaSize);
            Assert.AreEqual(schema.metaSize + 8, schema.stride);
        }

        [Test]
        public void ADescriptionSurvivesTheTextItTravelsAs()
        {
            var original = new StateSchema(16, 24, new[]
            {
                new StateSchemaMember("intensity", "System.Single", 0, 4),
                new StateSchemaMember("pose", "Lilium.RemoteControl.TransformValue", 4, 40, 0xDEADBEEFUL),
            });

            var round = StateSchema.TryParse(original.ToText());

            Assert.IsNotNull(round);
            Assert.AreEqual(original.metaSize, round.metaSize);
            Assert.AreEqual(original.stride, round.stride);
            Assert.AreEqual(2, round.members.Length);
            Assert.IsTrue(round.members[0].SameValueAs(in original.members[0]));
            Assert.IsTrue(round.members[1].SameValueAs(in original.members[1]));
            Assert.AreEqual(0xDEADBEEFUL, round.members[1].layout);
        }

        [Test]
        public void SomethingThatIsNotADescription_ParsesAsNothing()
        {
            // Descriptions share the mapping table with addresses and type names, so telling them
            // apart is a question the reader asks of every string it resolves.
            Assert.IsNull(StateSchema.TryParse("/live/object/avatar/position"));
            Assert.IsNull(StateSchema.TryParse("Lilium.LiveStudio.AvatarController"));
            Assert.IsNull(StateSchema.TryParse(null));
        }

        [Test]
        public void TwoBuildsThatAgree_AreReadAsOneCopy()
        {
            var schema = new StateSchema(16, 24, new[]
            {
                new StateSchemaMember("a", "System.Single", 0, 4),
                new StateSchemaMember("b", "System.Single", 4, 4),
            });

            var plan = StateReadPlan.Build(schema, schema);

            Assert.IsNotNull(plan);
            Assert.IsTrue(plan.isIdentical,
                "the ordinary replay would pay for the general path instead of one memcpy");
        }

        [Test]
        public void AMemberThatMoved_BecomesACopyToWhereItNowLives()
        {
            var recorded = new StateSchema(16, 24, new[]
            {
                new StateSchemaMember("a", "System.Single", 0, 4),
                new StateSchemaMember("b", "System.Single", 4, 4),
            });

            var mine = new StateSchema(16, 24, new[]
            {
                new StateSchemaMember("b", "System.Single", 0, 4),
                new StateSchemaMember("a", "System.Single", 4, 4),
            });

            var plan = StateReadPlan.Build(recorded, mine);

            Assert.IsFalse(plan.isIdentical);
            Assert.AreEqual(0b11UL, plan.appliedMemberMask, "both members are here and in the take");
            CollectionAssert.IsEmpty(plan.droppedMembers);
            CollectionAssert.IsEmpty(plan.unwrittenMembers);

            // Two copies rather than one: the members are adjacent on both sides but they cross, so
            // joining them would move the pair verbatim and undo the swap.
            Assert.AreEqual(2, plan.runs.Length);
            Assert.AreEqual(4, plan.runs[0].sourceOffset);
            Assert.AreEqual(0, plan.runs[0].destinationOffset);
        }

        [Test]
        public void MembersSideBySideOnBothSides_BecomeOneCopy()
        {
            var recorded = new StateSchema(16, 32, new[]
            {
                new StateSchemaMember("gone", "System.Single", 0, 4),
                new StateSchemaMember("a", "System.Single", 4, 4),
                new StateSchemaMember("b", "System.Single", 8, 4),
            });

            var mine = new StateSchema(16, 24, new[]
            {
                new StateSchemaMember("a", "System.Single", 0, 4),
                new StateSchemaMember("b", "System.Single", 4, 4),
            });

            var plan = StateReadPlan.Build(recorded, mine);

            Assert.AreEqual(1, plan.runs.Length, "two members that stayed together cost two copies");
            Assert.AreEqual(8, plan.runs[0].length);
            CollectionAssert.AreEqual(new[] { "gone" }, plan.droppedMembers);
        }

        [Test]
        public void AMemberOnlyThisBuildHas_IsOutsideTheMask()
        {
            var recorded = new StateSchema(16, 20, new[]
            {
                new StateSchemaMember("a", "System.Single", 0, 4),
            });

            var mine = new StateSchema(16, 24, new[]
            {
                new StateSchemaMember("a", "System.Single", 0, 4),
                new StateSchemaMember("added", "System.Single", 4, 4),
            });

            var plan = StateReadPlan.Build(recorded, mine);

            Assert.AreEqual(0b01UL, plan.appliedMemberMask);
            CollectionAssert.AreEqual(new[] { "added" }, plan.unwrittenMembers);
        }

        /// <summary>
        /// The same name over a different type is not the same member.
        ///
        /// Nothing about the bytes says which one they are, so copying them because the names agree
        /// puts one type's value where another's goes -- which looks like a value rather than like
        /// an error, and is the failure worth refusing.
        /// </summary>
        [Test]
        public void TheSameNameOverADifferentType_IsNotAMatch()
        {
            var recorded = new StateSchema(16, 20, new[]
            {
                new StateSchemaMember("level", "System.Single", 0, 4),
            });

            var mine = new StateSchema(16, 20, new[]
            {
                new StateSchemaMember("level", "System.Int32", 0, 4),
            });

            var plan = StateReadPlan.Build(recorded, mine);

            Assert.AreEqual(0UL, plan.appliedMemberMask);
            CollectionAssert.AreEqual(new[] { "level" }, plan.unwrittenMembers);
            CollectionAssert.IsEmpty(plan.runs);
        }

        /// <summary>
        /// A struct member whose fields moved is not the same member either, though its name, its
        /// type name and its size all still agree. This is the one a name-and-type match cannot see
        /// on its own, and the reason a member carries the shape of its own inside.
        /// </summary>
        [Test]
        public void AStructMemberWhoseInsideMoved_IsNotAMatch()
        {
            var recorded = new StateSchema(16, 56, new[]
            {
                new StateSchemaMember("pose", "Lilium.RemoteControl.TransformValue", 0, 40, 0x1111UL),
            });

            var mine = new StateSchema(16, 56, new[]
            {
                new StateSchemaMember("pose", "Lilium.RemoteControl.TransformValue", 0, 40, 0x2222UL),
            });

            var plan = StateReadPlan.Build(recorded, mine);

            Assert.AreEqual(0UL, plan.appliedMemberMask, "a struct whose fields moved was copied anyway");
            CollectionAssert.IsEmpty(plan.runs);
        }

        /// <summary>
        /// A type too wide for a mask is refused rather than half-read.
        ///
        /// Reading it would put back the members that lined up and write zero over the ones that did
        /// not, with nothing able to say which was which -- the failure the mask exists to prevent.
        /// </summary>
        [Test]
        public void ATypeWiderThanAMask_HasNoPlan()
        {
            var wide = new StateSchemaMember[StateReadPlan.kMaxMaskedMembers + 1];
            for (int i = 0; i < wide.Length; i++)
            {
                wide[i] = new StateSchemaMember("m" + i, "System.Single", i * 4, 4);
            }

            var schema = new StateSchema(16, 16 + wide.Length * 4, wide);

            Assert.IsNull(StateReadPlan.Build(schema, schema));
        }
    }
}
