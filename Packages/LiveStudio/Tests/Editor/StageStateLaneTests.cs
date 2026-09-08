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
        /// Which stages are loaded rides the frame as a collection of live objects: the structure
        /// lane carries the elements being there, so a replay starting anywhere sees the whole set
        /// rather than having to walk the loads that produced it.
        ///
        /// It was a string[] while the shape was what kept it off the frame -- a value array is not
        /// unmanaged, so the block refused it (LRC002) and the member fell out of every lane without
        /// anything at runtime saying so. A collection of [LiveClass] elements is the shape the frame
        /// does carry.
        /// </summary>
        [Test]
        public void TheLoadedStages_AreARecordedCollection()
        {
            var member = _Member("loadedSets");

            Assert.AreNotEqual(FrameLane.None, member.lane, "the member is off the frame");
            Assert.IsTrue(LiveObjectWalk.HoldsLiveObjectCollection(member),
                "the loaded stages are not a collection of live objects, so the walk skips them");
        }

        /// <summary>
        /// An array, not a List, and the difference is whether anything happens on replay.
        ///
        /// The structure lane puts elements in and out through LiveProperty.Add / RemoveAt. The List
        /// branch edits the IList in place and tells the owner nothing; only the array branch builds
        /// a new array and SetValues it, which is what raises onPropertyChanged and gets the manager
        /// to actually load the set. As a List the set would come back while the stage stayed empty.
        /// </summary>
        [Test]
        public void TheLoadedStages_AreAnArraySoTheOwnerHearsAboutIt()
        {
            Assert.IsTrue(_Member("loadedSets").valueType.IsArray,
                "a List is reconciled without notifying the owner, so nothing would apply the change");
        }

        /// <summary>
        /// Elements are addressed by name. A position would land on a different set on a machine
        /// whose catalog is ordered differently -- which is every other machine.
        /// </summary>
        [Test]
        public void ALoadedStage_IsKeyedByName()
        {
            Assert.AreEqual("Dawn_Star_Sky",
                LiveObjectWalk.KeyOf(new LoadedSet { name = "Dawn_Star_Sky" }),
                "a loaded stage has no key, so replay would match it by position instead");
        }

        /// <summary>
        /// The projection the remote app renders is off the frame: it is rebuilt from the catalog,
        /// which is a setting of this machine. Writing an entry's enabled flag is still how a set is
        /// loaded -- what a take carries is <c>loadedSets</c> following along afterwards, not the
        /// write itself.
        /// </summary>
        [Test]
        public void TheSetProjectionStaysOffTheFrame()
        {
            Assert.AreEqual(FrameLane.None, _Member("sets").lane);
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
