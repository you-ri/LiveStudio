// Copyright (c) You-Ri, 2026

using NUnit.Framework;
using UnityEngine;

using Lilium.LiveStudio;
using Lilium.RemoteControl;
using Lilium.RemoteControl.Frames;

namespace Lilium.LiveStudio.Virgo.EditorTests
{
    /// <summary>
    /// How a recorded pose gets back onto the avatar, pinned down before the code that does it moves.
    ///
    /// The replay side of the motion source has three properties that are easy to break and quiet
    /// when broken: the row is found through the table the frame carries (not this run's), the
    /// reference point is applied to a replayed frame the same as to a live one, and a frame with no
    /// row for this source leaves the avatar alone rather than snapping it to nothing. None of them
    /// had a test; a missing row in particular produces no log at all, so a change that moved the
    /// address this source publishes under would look like nothing happening.
    /// </summary>
    public class MotionSourceStateLaneTests
    {
        private const string kOwnerAddress = "motion-source-under-test";

        private bool _retainedStateSystem;
        private GameObject _go;
        private VirgoMotionSource _source;
        private LiveGameObject _wrapper;

        [SetUp]
        public void SetUp()
        {
            FrameGate.source = null;
            // A counted clock, so one Pump is one frame: the wall clock would skip the second pump
            // in a test that runs faster than a frame interval.
            FrameGate.SetClock(new FrameCounterClock(FrameRate.FPS60));

            // Placed away from the origin so the reference point it applies is visible as a
            // translation. With no anchor the source is its own placement origin, so the matrix is
            // exactly this transform.
            _go = new GameObject("Virgo Motion Source") { hideFlags = HideFlags.HideAndDontSave };
            _go.transform.position = new Vector3(2f, 3f, 4f);

            // The pose is filed under the id of this source's GameObject, so the registry has to
            // hold a proxy for it -- without one the source has no address and publishes nowhere.
            _wrapper = new LiveGameObject(_go);
            _wrapper.ReplaceId(kOwnerAddress);
            _wrapper.OnEnable();

            _source = _go.AddComponent<VirgoMotionSource>();

            // Unity does not run OnEnable outside play mode, and that is where this component
            // subscribes to the frame head. Calling it here is what makes the thing under test
            // actually take part in the frame; without it every assertion below passes or fails on
            // an object that never ran.
            _Lifecycle("OnEnable");
        }

        /// <summary>Invokes one of the component's Unity messages, which edit mode does not send.</summary>
        private void _Lifecycle(string message)
        {
            var method = typeof(VirgoMotionSource).GetMethod(message,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(method, $"VirgoMotionSource has no {message}");
            method.Invoke(_source, null);
        }

        [TearDown]
        public void TearDown()
        {
            FrameGate.source = null;

            if (_retainedStateSystem)
            {
                LiveStateSystem.Release();
                _retainedStateSystem = false;
            }

            // Matches the OnEnable in SetUp: unsubscribes the frame head and closes the socket.
            if (_source != null) _Lifecycle("OnDisable");

            if (_go != null) Object.DestroyImmediate(_go);
            _go = null;
            _source = null;

            _wrapper?.OnDisable();
            _wrapper = null;

            LiveObjectRegistry.ClearAll();
            FrameGate.RestoreDefaultClock();
        }

        /// <summary>
        /// The row is looked up through the recording's symbol table. The same address is a different
        /// number in each table, so a reader that used this run's numbering would find the wrong row
        /// or no row -- which is why the frame carries the table it was filled from.
        /// </summary>
        [Test]
        public void OnASuppliedFrame_ThePoseIsFoundThroughTheFramesOwnTable()
        {
            var recorded = _PoseWith(muscle: 12, value: 0.42f);

            var supply = new RecordedFrameSource { ownerAddress = kOwnerAddress, frame = recorded };
            // Decoys first, so the address does not intern to the same number in both tables.
            supply.symbols.Intern("something-else");
            supply.symbols.Intern("and-another");

            // A different pose filed under the number this run would use for the same address. A
            // reader that resolved through the run's table instead of the frame's would pick this up.
            supply.decoyOwner = FrameGate.symbols.Intern(kOwnerAddress);
            supply.decoyFrame = _PoseWith(muscle: 12, value: -0.99f);

            Assume.That(supply.symbols.Intern(kOwnerAddress), Is.Not.EqualTo(supply.decoyOwner),
                "the two tables agree on this address, so the decoy and the real row are the same row");

            FrameGate.source = supply;
            FrameGate.Pump();

            Assert.AreEqual(0.42f, _source.frameData.pose.AsMuscle(12), 1e-5f,
                "the recorded pose did not reach the avatar through the frame's own table");
        }

        /// <summary>
        /// The reference point is applied to a replayed frame too. The pose on the lane is the one
        /// before placement -- that is what lets the camera be moved and the take drawn again -- so
        /// a replay that skipped this step would put the avatar at the capture origin.
        /// </summary>
        [Test]
        public void OnASuppliedFrame_TheReferencePointIsStillApplied()
        {
            var recorded = _PoseWith(muscle: 0, value: 0.1f);
            recorded.root.position = Vector3.zero;

            FrameGate.source = new RecordedFrameSource { ownerAddress = kOwnerAddress, frame = recorded };
            FrameGate.Pump();

            // No anchor, so the placement origin is this source's own transform.
            Assert.AreEqual(new Vector3(2f, 3f, 4f), _source.frameData.root.position,
                "the reference point was not applied to the replayed frame");
        }

        /// <summary>
        /// A recording with nothing for this source leaves the avatar as it was. It is a normal case
        /// -- the take simply had no pose at that point -- and it is silent, which is exactly why it
        /// is worth a test: moving the address this source publishes under turns every frame into
        /// this case, with no error anywhere to say so.
        /// </summary>
        [Test]
        public void WhenTheRecordingHasNoRowForThisSource_ThePoseIsLeftAlone()
        {
            var recorded = _PoseWith(muscle: 5, value: 0.9f);

            FrameGate.source = new RecordedFrameSource { ownerAddress = "some-other-source", frame = recorded };
            FrameGate.Pump();

            Assert.AreEqual(0f, _source.frameData.pose.AsMuscle(5), 1e-5f,
                "a row belonging to another source was applied to this one");
            Assert.AreEqual(Vector3.zero, _source.frameData.root.position,
                "the frame was placed even though there was no pose to place");
        }

        /// <summary>
        /// The camera-fit offset is part of the reference point, and the reference point is what the
        /// recorded pose is placed by. Left off the lane, a take replays placed by whatever the
        /// replaying machine's offset happens to be -- which is not where it was shot.
        /// </summary>
        [Test]
        public void TheReferencePointOffset_IsCarriedOnTheStateLane()
        {
            _source._offsetPosition = new Vector3(0.5f, 0f, -1.25f);
            _source._offsetRotation = new Vector3(0f, 170f, 0f);

            using var state = new StateBlockSet();
            LiveStateSystem.CaptureInto(state, time: 0);

            // Whatever the machine happens to hold now, standing in for a different rig on replay.
            _source._offsetPosition = Vector3.zero;
            _source._offsetRotation = Vector3.zero;

            LiveStateSystem.ApplyFrom(state);

            Assert.AreEqual(new Vector3(0.5f, 0f, -1.25f), _source._offsetPosition,
                "the camera-fit offset was not carried on the state lane");
            Assert.AreEqual(new Vector3(0f, 170f, 0f), _source._offsetRotation,
                "the camera-fit rotation offset was not carried on the state lane");
        }

        /// <summary>
        /// And it reaches the placement: a restored offset moves where the pose lands, which is the
        /// whole point of recording it.
        /// </summary>
        [Test]
        public void ARestoredOffset_MovesWhereThePoseLands()
        {
            var recorded = _PoseWith(muscle: 0, value: 0.1f);
            recorded.root.position = Vector3.zero;

            _source._offsetPosition = new Vector3(1f, 0f, 0f);

            FrameGate.source = new RecordedFrameSource { ownerAddress = kOwnerAddress, frame = recorded };
            FrameGate.Pump();

            // The source sits at (2,3,4) and is its own placement origin, so the offset rides on top.
            Assert.AreEqual(new Vector3(3f, 3f, 4f), _source.frameData.root.position,
                "the offset did not reach the placement");
        }

        /// <summary>
        /// The recorded offset reaches the replay, and the replay is placed by it.
        ///
        /// ⚠ Which frame it starts on is not fixed. The exposed state is applied by
        /// <see cref="LiveStateSystem"/>'s own frame-head handler, and frame-head handlers run in
        /// registration order with no way to ask for a position -- the state system is registered by
        /// whoever retains it first (a recorder, an open viewer), which may be before or after a
        /// scene component enabled. So the recorded offset lands either on the first replayed frame
        /// or the second, and at most one frame at the start of playback is placed by the replaying
        /// machine's own offset instead. This asserts what holds either way rather than pinning an
        /// order that the next scene arrangement would change.
        /// </summary>
        [Test]
        public void OnReplay_ThePlacementFollowsTheRecordedOffset()
        {
            var recordedOffset = new Vector3(1f, 0f, 0f);

            _source._offsetPosition = recordedOffset;
            var recordedState = new StateBlockSet();
            LiveStateSystem.CaptureInto(recordedState, time: 0);

            // What this machine happens to hold: a different rig, or simply a run that never reset.
            _source._offsetPosition = new Vector3(-4f, 0f, 0f);

            LiveStateSystem.Retain();
            _retainedStateSystem = true;

            var recorded = _PoseWith(muscle: 0, value: 0.1f);
            recorded.root.position = Vector3.zero;
            var supply = new RecordedFrameSource
            {
                ownerAddress = kOwnerAddress,
                frame = recorded,
                stateOverride = recordedState,
            };

            FrameGate.source = supply;
            FrameGate.Pump();

            Assert.AreEqual(recordedOffset, _source._offsetPosition,
                "the recorded offset never reached the machine replaying the take");

            FrameGate.Pump();

            // The source sits at (2,3,4) and is its own placement origin, so the offset rides on top.
            Assert.AreEqual(new Vector3(2f, 3f, 4f) + recordedOffset, _source.frameData.root.position,
                "the replay is placed by this machine's offset rather than the recorded one");
        }

        private static AvatarAnimationData _PoseWith(int muscle, float value)
        {
            var frame = new AvatarAnimationData();
            frame.root.valid = AvatarRootData.kBodyValidFlag | AvatarRootData.kFaceValidFlag;
            frame.root.scale = Vector3.one;
            frame.root.rotation = Quaternion.identity;
            frame.pose.AsMuscle(muscle) = value;
            frame.pose.AsMusclePresence(muscle) = 1f;
            return frame;
        }

        /// <summary>Supplies one frame carrying a pose row, the way a recording does.</summary>
        private sealed class RecordedFrameSource : IFrameSource
        {
            public readonly FrameSymbolTable symbols = new FrameSymbolTable();
            public string ownerAddress;
            public AvatarAnimationData frame;

            /// <summary>The recording's own state, replacing this run's lanes the way a take does.</summary>
            public StateBlockSet stateOverride;

            /// <summary>A row under a different owner number, to catch a read through the wrong table.</summary>
            public int decoyOwner = FrameSymbolTable.kNone;
            public AvatarAnimationData decoyFrame;

            public bool FillFrame(ref Frame target)
            {
                // The recording's table replaces this run's, ids and all -- except when the caller
                // supplied a block set captured here, whose ids belong to this run's table. Mixing
                // the two would resolve every address against the wrong numbering, which is the
                // failure OnASuppliedFrame_ThePoseIsFoundThroughTheFramesOwnTable is about.
                var table = symbols;
                if (stateOverride != null)
                {
                    target.state = stateOverride;
                    table = target.symbols;
                }
                else
                {
                    target.symbols = symbols;
                }

                var block = target.state.GetOrCreate<AvatarAnimationData>();

                if (decoyOwner != FrameSymbolTable.kNone)
                {
                    ref var decoy = ref block.GetOrCreate(decoyOwner);
                    decoy.time = target.frameNumber;
                    decoy.value = decoyFrame;
                }

                ref var element = ref block.GetOrCreate(table.Intern(ownerAddress));
                element.time = target.frameNumber;
                element.value = frame;
                return true;
            }
        }
    }
}
