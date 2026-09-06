// Copyright (c) You-Ri, 2026
using System.Collections.Generic;
using NUnit.Framework;
using Lilium.RemoteControl;
using Lilium.RemoteControl.Frames;

namespace Lilium.LiveStudio.Tests
{
    /// <summary>
    /// The objects that are the operator's own desk rather than the world being recorded.
    ///
    /// Both of these are settings all the way through -- what the operator wired their controls to,
    /// and the recorder's own controls -- so nothing on either belongs in a take. A recording that
    /// held them would, on replay, rebuild the operator's desk and start recordings on the machine
    /// playing it back.
    ///
    /// Written as a sweep over the whole type rather than as a test per member, because the failure
    /// this guards against is a member added later: the declaration is per member, an undeclared one
    /// is on a recording lane, and nothing about adding a function to a settings object suggests
    /// there is a lane to think about. This is the thing that says so.
    ///
    /// Read-only members are exempt. A lane is meaningless on them -- a value the application
    /// produced is a result, never carried as an input -- and requiring the declaration there would
    /// ask for noise on every readout.
    /// </summary>
    public class SettingsObjectLaneTests
    {
        private static IEnumerable<string> _RecordableMembers(LiveClass liveClass)
        {
            foreach (var member in liveClass.propertyTypes)
            {
                if (member == null || member.isReadOnly) continue;
                if (member.lane == FrameLane.None) continue;

                yield return $"{liveClass.typeName}.{member.name} ({member.lane})";
            }

            foreach (var function in liveClass.functionTypes)
            {
                if (function == null || function.lane == FrameLane.None) continue;

                yield return $"{liveClass.typeName}.{function.name}() ({function.lane})";
            }
        }

        private static void _AssertNothingIsRecorded(LiveClass liveClass)
        {
            var recorded = new List<string>(_RecordableMembers(liveClass));

            Assert.IsEmpty(recorded,
                "this object is a setting of the operator's machine, so nothing on it belongs in a "
                + "take. Declare lane = FrameLane.None on: " + string.Join(", ", recorded));
        }

        /// <summary>
        /// The operation sets and decks are authored in the deck files and belong to the machine
        /// that has those files. What a deck key *does* is still recorded -- against whatever it
        /// wrote to, which is where that change belongs.
        /// </summary>
        [Test]
        public void NothingOnTheOperationDesk_IsRecorded()
        {
            _AssertNothingIsRecorded(LiveClass.Get<OperationManager>());
        }

        /// <summary>
        /// The sets themselves, reached through the desk's collection. Declared off the frame in
        /// their own right as well, because this type asks for the state lane on the slider value
        /// and the hold -- a declaration nothing is carrying is the shape of this that goes wrong
        /// quietly.
        /// </summary>
        [Test]
        public void NothingOnAnOperationSet_IsRecorded()
        {
            _AssertNothingIsRecorded(LiveClass.Get<OperationSet>());
        }

        /// <summary>
        /// The generator's half of the same declaration. `lane = None` on the type has to stop the
        /// state block being emitted as well, or the runtime would be refusing to record a member
        /// that a block is copying every frame -- the two halves disagreeing is the failure this
        /// area keeps having, and the one thing a declaration test cannot see on its own.
        /// </summary>
        [Test]
        public void TheDeskHasNoStateBlock()
        {
            Assert.IsNull(StateBridgeRegistry.Find(typeof(OperationSet)),
                "the generator still emits a block for a type declared off the frame");
            Assert.IsNull(StateBridgeRegistry.Find(typeof(OperationManager)));
        }

        /// <summary>
        /// The recordings manager: the transport an operator drives, and the listing behind it.
        /// Everything on it starts, stops or picks a take, so a recorded call would be pressed again
        /// by the replay running it.
        /// </summary>
        [Test]
        public void NothingOnTheRecordingManager_IsRecorded()
        {
            _AssertNothingIsRecorded(LiveClass.Get(typeof(RecordingManager)));
        }

        /// <summary>
        /// The machinery behind the page, by the same argument: what it holds is the state of the
        /// take being made, not of the world being taken.
        ///
        /// The controller does exclude its own object from the recording, but that exclusion is
        /// applied to events only -- the state walk never consults it. So a member of this type
        /// reaching the state lane leaves the exclusion behind without saying anything, which is
        /// what happened to <c>replayPaused</c> when the lane's default stopped depending on
        /// whether a member was spelled as a field.
        /// </summary>
        [Test]
        public void NothingOnTheRecorder_IsRecorded()
        {
            _AssertNothingIsRecorded(LiveClass.Get(typeof(FrameRecorderController)));
        }

        [Test]
        public void TheRecorderHasNoStateBlock()
        {
            Assert.IsNull(StateBridgeRegistry.Find(typeof(FrameRecorderController)),
                "a replayed pause would pause the replay that is playing it back");
        }

        /// <summary>
        /// The desk is not reached by the walk either, so the state lane does not copy what a write
        /// is no longer allowed to record. Asked of the walk rather than of the frame, because the
        /// answer has to hold whether or not anything is recording.
        /// </summary>
        [Test]
        public void TheWalkDoesNotGoIntoTheDesk()
        {
            var liveClass = LiveClass.Get<OperationManager>();
            var sets = System.Array.Find(liveClass.propertyTypes, p => p.name == "operationSets");

            Assert.IsNotNull(sets, "the operation sets are not exposed any more");
            Assert.IsFalse(LiveObjectWalk.HoldsLiveObjectCollection(sets),
                "the desk's sets would still be copied into every frame");
        }

        /// <summary>
        /// The project's assets are what this machine has on disk, not what the show did. The calls
        /// are housekeeping (a replayed DeleteAssetFile would delete on the machine playing it back)
        /// and the list goes with them.
        ///
        /// ⚠ The asset list being off the frame is a decision with a cost, taken deliberately:
        /// avatar selection survives through the view below, but a take no longer says which props
        /// or which stage were up -- the writes into assets[i]/enabled were the only thing that did.
        /// </summary>
        [Test]
        public void NothingOnTheAssetManager_IsRecorded()
        {
            _AssertNothingIsRecorded(LiveClass.Get<ExternalAssetManager>());
        }

        /// <summary>
        /// No asset type carries a state block.
        ///
        /// The catalog is off the frame, so a block for one of these could never be fed -- the walk
        /// stops at the collection holding them. Before each type said so for itself, the generator
        /// emitted one anyway: dead weight in every build, and a standing invitation to conclude
        /// from its existence that asset state is carried.
        ///
        /// Swept over the assembly rather than listed, because the thing that goes wrong is an asset
        /// type added later without the declaration, and a list would not know about it.
        /// </summary>
        [Test]
        public void NoAssetTypeHasAStateBlock()
        {
            var offenders = new List<string>();

            foreach (var type in typeof(AssetBase).Assembly.GetTypes())
            {
                if (type.IsAbstract || !typeof(AssetBase).IsAssignableFrom(type)) continue;
                if (StateBridgeRegistry.Find(type) != null) offenders.Add(type.Name);
            }

            Assert.IsEmpty(offenders,
                "the catalog is off the frame, so nothing will ever feed these blocks. Declare "
                + "[LiveClass(lane = FrameLane.None)] on: " + string.Join(", ", offenders));
        }

        /// <summary>
        /// The one asset decision that is not affected, and the reason the one above is affordable:
        /// the avatar is carried as a view of its own, on the state lane, whoever writes it.
        /// </summary>
        [Test]
        public void TheAvatarSelectionIsStillCarried_ByItsOwnView()
        {
            var liveClass = LiveClass.Get<ExternalAvatarSource>();
            var selected = System.Array.Find(liveClass.propertyTypes, p => p.name == "selectedAvatar");

            Assert.IsNotNull(selected, "the avatar selection view is not exposed any more");
            Assert.AreEqual(FrameLane.State, selected.lane);
        }
    }
}
