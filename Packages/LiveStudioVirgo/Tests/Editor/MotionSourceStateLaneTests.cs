// Copyright (c) You-Ri, 2026

using System.Collections.Generic;
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
        /// A replay on a machine that has never received anything -- no Fusion running -- still
        /// places the take where it was shot. The camera fit is not carried by the take; it is taken
        /// again from the capture camera the take does carry, the way a live run takes one when
        /// motion starts arriving.
        ///
        /// The source under test has never received a frame, so its own fit is zero: a replay placed
        /// by it would leave the capture camera one metre off the reference point.
        /// </summary>
        [Test]
        public void OnAMachineThatNeverReceived_TheReplayFitsTheCameraFromTheTake()
        {
            var cameraPosition = new Vector3(1f, 0f, 0f);
            var recorded = _PoseWithCamera(cameraPosition, yaw: 90f);
            // Standing where the camera is, so a fitted placement lands it on the reference point.
            recorded.root.position = cameraPosition;

            FrameGate.source = new RecordedFrameSource { ownerAddress = kOwnerAddress, frame = recorded };
            FrameGate.Pump();

            // No anchor, so the reference point is this source's own transform.
            _AssertNear(new Vector3(2f, 3f, 4f), _source.frameData.root.position,
                "the capture camera was not fitted to the reference point on replay");
        }

        /// <summary>
        /// The fit is taken once, when the replay starts, not every frame. A capture camera that moves
        /// during the take moves the avatar with it, as it did live -- refitting every frame would pin
        /// the camera and cancel that motion.
        /// </summary>
        [Test]
        public void TheFit_IsTakenOnceAtTheStartOfAReplay()
        {
            var cameraPosition = new Vector3(1f, 0f, 0f);
            var recorded = _PoseWithCamera(cameraPosition, yaw: 90f);
            recorded.root.position = cameraPosition;

            var supply = new RecordedFrameSource { ownerAddress = kOwnerAddress, frame = recorded };
            FrameGate.source = supply;
            FrameGate.Pump();

            // The camera moves; the performer does not.
            supply.frame = _PoseWithCamera(new Vector3(1f, 0f, 1f), yaw: 90f);
            supply.frame.root.position = cameraPosition;
            FrameGate.Pump();

            _AssertNear(new Vector3(2f, 3f, 4f), _source.frameData.root.position,
                "the fit was taken again mid-replay, cancelling the camera's own motion");
        }

        /// <summary>
        /// A replay started straight after another takes its own fit. No live frame comes between the
        /// two -- the recorder stops one and attaches the next in the same call -- so the fit has to
        /// be tied to the replay it was taken for rather than dropped on going live.
        /// </summary>
        [Test]
        public void ANewReplay_TakesItsOwnFit()
        {
            var first = _PoseWithCamera(new Vector3(1f, 0f, 0f), yaw: 90f);
            FrameGate.source = new RecordedFrameSource { ownerAddress = kOwnerAddress, frame = first };
            FrameGate.Pump();

            var cameraPosition = new Vector3(1f, 0f, 1f);
            var second = _PoseWithCamera(cameraPosition, yaw: 90f);
            second.root.position = cameraPosition;
            FrameGate.source = new RecordedFrameSource { ownerAddress = kOwnerAddress, frame = second };
            FrameGate.Pump();

            _AssertNear(new Vector3(2f, 3f, 4f), _source.frameData.root.position,
                "the second replay was placed by the fit taken for the first");
        }

        /// <summary>
        /// Nothing on the motion source is recorded. Its members are how this machine is wired and
        /// where its rig stands, and the one value derived from the capture -- the camera fit -- is
        /// taken again on replay from what the take does carry. A member that reached a recording
        /// lane would be pressed back over the replaying machine's own.
        ///
        /// A sweep rather than a list, because what goes wrong is a member added later without the
        /// declaration. Read-only members are exempt: a lane means nothing on a value the application
        /// produced.
        /// </summary>
        [Test]
        public void NothingOnTheMotionSource_IsRecorded()
        {
            var liveClass = LiveClass.Get<VirgoMotionSource>();
            var recorded = new List<string>();

            foreach (var member in liveClass.propertyTypes)
            {
                if (member == null || member.isReadOnly || member.lane == FrameLane.None) continue;
                recorded.Add($"{member.name} ({member.lane})");
            }

            foreach (var function in liveClass.functionTypes)
            {
                if (function == null || function.lane == FrameLane.None) continue;
                recorded.Add($"{function.name}() ({function.lane})");
            }

            Assert.IsEmpty(recorded,
                "nothing on VirgoMotionSource belongs in a take. Recorded: " + string.Join(", ", recorded));
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

        /// <summary>A valid pose whose capture camera stands at <paramref name="position"/>, turned by <paramref name="yaw"/>.</summary>
        private static AvatarAnimationData _PoseWithCamera(Vector3 position, float yaw)
        {
            var frame = _PoseWith(muscle: 0, value: 0.1f);

            ref var camera = ref frame.AsCamera(0);
            camera.position = position;
            camera.rotation = Quaternion.Euler(0f, yaw, 0f);
            // A zero field of view reads as no camera, and a frame without one is not fitted.
            camera.fieldOfView = 60f;

            return frame;
        }

        private static void _AssertNear(Vector3 expected, Vector3 actual, string message)
        {
            Assert.That(Vector3.Distance(expected, actual), Is.LessThan(1e-4f),
                $"{message}: expected {expected}, got {actual}");
        }

        /// <summary>Supplies one frame carrying a pose row, the way a recording does.</summary>
        private sealed class RecordedFrameSource : IFrameSource
        {
            public readonly FrameSymbolTable symbols = new FrameSymbolTable();
            public string ownerAddress;
            public AvatarAnimationData frame;

            /// <summary>A row under a different owner number, to catch a read through the wrong table.</summary>
            public int decoyOwner = FrameSymbolTable.kNone;
            public AvatarAnimationData decoyFrame;

            public bool FillFrame(ref Frame target)
            {
                // The recording's table replaces this run's, ids and all.
                target.symbols = symbols;

                var block = target.state.GetOrCreate<AvatarAnimationData>();

                if (decoyOwner != FrameSymbolTable.kNone)
                {
                    ref var decoy = ref block.GetOrCreate(decoyOwner);
                    decoy.time = target.frameNumber;
                    decoy.value = decoyFrame;
                }

                ref var element = ref block.GetOrCreate(symbols.Intern(ownerAddress));
                element.time = target.frameNumber;
                element.value = frame;
                return true;
            }
        }
    }
}
