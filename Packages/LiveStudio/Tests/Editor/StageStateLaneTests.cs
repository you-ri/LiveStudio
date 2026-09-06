// Copyright (c) You-Ri, 2026
using NUnit.Framework;
using Lilium.RemoteControl;
using Lilium.RemoteControl.Frames;

namespace Lilium.LiveStudio.Tests
{
    /// <summary>
    /// Which stage is up reaches a recording from the scene, not from the asset catalog.
    ///
    /// The sets live in <see cref="ExternalAssetManager"/>, which is off the frame: which bundles this
    /// machine has on disk and has loaded is a setting of the machine. Which one is *up* is the show,
    /// so it is carried here, as an intent the apply side can produce the effect from.
    /// </summary>
    public class StageStateLaneTests
    {
        private static LivePropertyType _Member(string name)
        {
            var liveClass = LiveClass.Get<StageManager>();
            var member = System.Array.Find(liveClass.propertyTypes, p => p.name == name);

            Assert.IsNotNull(member, $"'{name}' is not exposed any more");
            return member;
        }

        [Test]
        public void TheActiveStage_AsksForTheStateLane()
        {
            Assert.AreEqual(FrameLane.State, _Member("activeSet").lane);
        }

        /// <summary>
        /// The half a declaration cannot promise. Asking for the state lane is a request that can be
        /// refused -- an owner the movers cannot reach, a type that is not unmanaged -- and a member
        /// that asks and is not carried falls out of both lanes: the block does not hold it and the
        /// file does not say it changed. This is what says the request was granted.
        /// </summary>
        [Test]
        public void TheActiveStage_IsActuallyCarried()
        {
            var bridge = StateBridgeRegistry.Find(typeof(StageManager));

            Assert.IsNotNull(bridge, "nothing carries the stage manager's state");
            Assert.IsTrue(LiveStateCarriage.IsCarriedByState(_Member("activeSet"), bridge),
                "the active stage asks for the state lane and no block moves it");
        }

        /// <summary>
        /// The asset side stays off the frame, which is the decision this member exists to make
        /// survivable. If this ever flips back, the same value is in two lanes.
        /// </summary>
        [Test]
        public void TheAssetCatalogStaysOffTheFrame()
        {
            var assets = System.Array.Find(
                LiveClass.Get<ExternalAssetManager>().propertyTypes, p => p.name == "assets");

            Assert.IsNotNull(assets, "the asset list is not exposed any more");
            Assert.AreEqual(FrameLane.None, assets.lane);
        }
    }
}
