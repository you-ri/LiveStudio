// Copyright (c) You-Ri, 2026
using NUnit.Framework;
using Lilium.RemoteControl;

namespace Lilium.LiveStudio.Tests
{
    /// <summary>
    /// Which lane carries the ambient settings.
    ///
    /// They are set once and then left alone for whole takes, which reads like an argument for the
    /// event lane -- and it is the wrong one. A recording holds no snapshot of what the world looked
    /// like when it started, so a value that is only ever written before the take begins is nowhere
    /// in the file: a scrub, a replay from the top and a spare machine joining midway all show
    /// whatever ambient that machine happened to have. The state lane is what makes a frame answer
    /// for itself.
    ///
    /// The cost of being wrong here is not subtle -- it is the whole scene lit differently -- and
    /// paying 40 bytes a frame to avoid it is not a trade worth thinking about twice.
    /// </summary>
    public class EnvironmentLightLaneTests
    {
        [Test]
        public void TheAmbientSettings_AreOnTheStateLane_SoAFrameCarriesThemWhetherOrNotTheyMoved()
        {
            var liveClass = LiveClass.Get<EnvironmentLight>();

            foreach (var name in new[] { "ambientLightSource", "ambientColor", "ambientIntensity" })
            {
                var member = System.Array.Find(liveClass.propertyTypes, p => p.name == name);

                Assert.IsNotNull(member, $"'{name}' is not exposed any more");
                Assert.AreEqual(FrameLane.State, member.lane, $"'{name}' is not on the state lane");
            }
        }

        [Test]
        public void TheBlock_CarriesEveryAmbientSetting()
        {
            // Named after the properties: the shadow fields travel through them, so a value read
            // out of the block is the one the setter would have applied.
            CollectionAssert.AreEquivalent(
                new[] { "ambientLightSource", "ambientColor", "ambientIntensity" },
                System.Array.ConvertAll(
                    typeof(EnvironmentLight.LiveStateBlock).GetFields(), f => f.Name));
        }
    }
}
