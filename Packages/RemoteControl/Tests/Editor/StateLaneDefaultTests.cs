// Copyright (c) You-Ri, 2026
using NUnit.Framework;

using Lilium.RemoteControl.Frames;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// What a member that says nothing about its lane ends up on, asked of the generator.
    ///
    /// The rule is the persistence (<see cref="FrameLaneRules"/>): what the live scene saves, the
    /// frame carries. The generator re-derives it from the attribute arguments alone, and the two
    /// halves have to reach the same answer -- when they do not, the member is off the frame for
    /// every write path (its event records are dropped) while a block copies it into every frame
    /// anyway, and nothing says so.
    ///
    /// The member below that made this a real defect is <see cref="bare"/>: a property saves
    /// nothing on its own -- only a shadow field or an [InlineReference] persists one -- so the
    /// runtime puts it off the frame, while its silence used to read here as "saved to the scene".
    /// RenderQuality.quality was recorded that way, and a replay wrote a per-machine setting onto
    /// the machine playing it back.
    /// </summary>
    [LiveClass("StateLaneDefaultProbe")]
    public partial class StateLaneDefaultProbe
    {
        /// <summary>Saved to the scene and silent: the common case, and the state lane's default.</summary>
        [LiveField] public float plain;

        /// <summary>The convention: the field is what is saved, and it is what declares.</summary>
        [LiveField, Hide]
        [FormerlyNamedAs("shadowed")]
        private float _shadowed;

        [LiveProperty]
        public float shadowed { get => _shadowed; set => _shadowed = value; }

        /// <summary>
        /// A property standing on its own. Nothing persists it, so nothing carries it either --
        /// neither lane, which is what being off the frame means.
        /// </summary>
        [LiveProperty] public float bare { get; set; }

        /// <summary>
        /// The exception, said out loud: carried although nothing saves it. This is how a property
        /// the application drives from the inside reaches a take.
        /// </summary>
        [LiveProperty(lane = FrameLane.State)] public float asked { get; set; }

        /// <summary>A setting of this machine. Saved, but not to the scene, so off the frame.</summary>
        [LiveField(persistScope = PersistScope.Project), Hide]
        [FormerlyNamedAs("setting")]
        private float _setting;

        [LiveProperty]
        public float setting { get => _setting; set => _setting = value; }
    }

    public class StateLaneDefaultTests
    {
        private static StateBridge _Bridge()
        {
            var bridge = StateBridgeRegistry.Find(typeof(StateLaneDefaultProbe));

            Assert.IsNotNull(bridge, "the probe has no state block at all");
            return bridge;
        }

        private static LivePropertyType _Member(string name)
        {
            var member = System.Array.Find(
                LiveClass.Get<StateLaneDefaultProbe>().propertyTypes, p => p.name == name);

            Assert.IsNotNull(member, $"'{name}' is not exposed any more");
            return member;
        }

        [Test]
        public void ASavedFieldThatSaysNothing_IsCarried()
        {
            Assert.IsTrue(LiveStateCarriage.IsCarriedByState(_Member("plain"), _Bridge()));
        }

        /// <summary>The pair travels through the property, under the field's declaration.</summary>
        [Test]
        public void ASavedShadowPairThatSaysNothing_IsCarried()
        {
            Assert.IsTrue(LiveStateCarriage.IsCarriedByState(_Member("shadowed"), _Bridge()));
        }

        /// <summary>
        /// The defect this file exists for. Both halves have to agree, so the check is put to the
        /// block as well as to the resolved lane: agreeing on "off the frame" is what keeps a
        /// per-machine setting out of a take.
        /// </summary>
        [Test]
        public void APropertyThatSavesNothing_IsOffTheFrame_AndNoBlockCarriesIt()
        {
            Assert.AreEqual(FrameLane.None, _Member("bare").lane);
            Assert.IsFalse(LiveStateCarriage.IsCarriedByState(_Member("bare"), _Bridge()),
                "a property nothing saves is copied into every frame while every write path treats "
                + "it as off the frame");
        }

        /// <summary>The other direction: asking out loud still works on a property.</summary>
        [Test]
        public void APropertyThatAsksForTheLane_IsCarried()
        {
            Assert.AreEqual(FrameLane.State, _Member("asked").lane);
            Assert.IsTrue(LiveStateCarriage.IsCarriedByState(_Member("asked"), _Bridge()));
        }

        [Test]
        public void AProjectSetting_IsOffTheFrame_AndNoBlockCarriesIt()
        {
            Assert.AreEqual(FrameLane.None, _Member("setting").lane);
            Assert.IsFalse(LiveStateCarriage.IsCarriedByState(_Member("setting"), _Bridge()));
        }
    }
}
