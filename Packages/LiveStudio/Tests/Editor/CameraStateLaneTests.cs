// Copyright (c) You-Ri, 2026
using NUnit.Framework;
using UnityEngine;
using Unity.Cinemachine;
using Lilium.RemoteControl;
using Lilium.RemoteControl.Frames;

namespace Lilium.LiveStudio.Tests
{
    /// <summary>
    /// Camera control on the state lane: the members a drag moves many times a second, carried in
    /// the frame instead of as an event record each.
    /// </summary>
    public class CameraStateLaneTests
    {
        private GameObject _go;
        private LiveCamera _camera;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("state-lane-camera", typeof(CinemachineCamera));
            _camera = new LiveCamera(_go.GetComponent<CinemachineCamera>());
        }

        [TearDown]
        public void TearDown()
        {
            _camera?.OnDisable();
            _camera = null;

            if (_go != null) Object.DestroyImmediate(_go);
            _go = null;
        }

        [Test]
        public void AFreeCameraControllersState_IsCarriedUnderTheCamerasIdAndItsMember()
        {
            // The controller is not registered anywhere -- it is a member of the camera -- so the
            // frame addresses it the way the event lane addresses a write to it.
            var controller = new FreeCameraController { yaw = 30f, pitch = -10f, position = new Vector3(1f, 2f, 3f) };
            _camera.controller = controller;

            using var state = new StateBlockSet();
            LiveStateSystem.CaptureInto(state, time: 0);

            var blocks = state.Find<FreeCameraController.LiveStateBlock>();
            Assert.IsNotNull(blocks, "the camera controller's state was not carried at all");

            var index = blocks.IndexOf(FrameGate.symbols.Intern(_camera.id + "/controller"));
            Assert.GreaterOrEqual(index, 0, "the controller was not carried under camera id + member name");

            Assert.AreEqual(30f, blocks[index].value.yaw);
            Assert.AreEqual(-10f, blocks[index].value.pitch);
            Assert.AreEqual(new Vector3(1f, 2f, 3f), blocks[index].value.position);
        }

        [Test]
        public void TheControllersState_ComesBackOnApply()
        {
            var controller = new FreeCameraController { yaw = 45f, position = new Vector3(4f, 0f, 0f) };
            _camera.controller = controller;

            using var state = new StateBlockSet();
            LiveStateSystem.CaptureInto(state, time: 0);

            controller.yaw = 0f;
            controller.position = Vector3.zero;

            LiveStateSystem.ApplyFrom(state);

            Assert.AreEqual(45f, controller.yaw);
            Assert.AreEqual(new Vector3(4f, 0f, 0f), controller.position);
        }

        [Test]
        public void SwappingTheController_KeepsTheAddressAndChangesWhatIsAtIt()
        {
            // Why the address is composed rather than an id of its own: the slot is what persists
            // through a change of controller, and a recording has to be able to say so.
            _camera.controller = new FreeCameraController { yaw = 10f };

            using var first = new StateBlockSet();
            LiveStateSystem.CaptureInto(first, time: 0);

            _camera.controller = new OrbitalFollowCameraController { distance = 2.5f };

            using var second = new StateBlockSet();
            LiveStateSystem.CaptureInto(second, time: 1);

            var address = FrameGate.symbols.Intern(_camera.id + "/controller");

            Assert.GreaterOrEqual(first.Find<FreeCameraController.LiveStateBlock>().IndexOf(address), 0);

            var orbital = second.Find<OrbitalFollowCameraController.LiveStateBlock>();
            var index = orbital.IndexOf(address);
            Assert.GreaterOrEqual(index, 0, "the new controller was not carried at the same address");
            Assert.AreEqual(2.5f, orbital[index].value.distance);
        }

        [Test]
        public void TheBlocks_CarryTheMembersADragMoves_NamedAfterTheProperties()
        {
            // Named after the property because that is what moves the value: the getter knows where
            // the value really is (the orbit, the lens) and the setter is what makes a write land.
            CollectionAssert.AreEquivalent(
                new[] { "yaw", "pitch", "position", "fov", "screenPosition" },
                System.Array.ConvertAll(
                    typeof(FreeCameraController.LiveStateBlock).GetFields(), f => f.Name));

            CollectionAssert.AreEquivalent(
                new[] { "yaw", "pitch", "distance", "fov", "screenPosition" },
                System.Array.ConvertAll(
                    typeof(OrbitalFollowCameraController.LiveStateBlock).GetFields(), f => f.Name));
        }

        [Test]
        public void ThoseMembers_AreOffTheEventLane_SoAWriteIsNotRecordedTwice()
        {
            // The state lane copies them every frame, so keeping the write as an event as well
            // would say the same thing twice -- at 548 bytes a record, sixty times a second.
            var liveClass = LiveClass.Get<OrbitalFollowCameraController>();

            foreach (var name in new[] { "yaw", "pitch", "distance", "fov", "screenPosition" })
            {
                var member = System.Array.Find(liveClass.propertyTypes, p => p.name == name);

                Assert.IsNotNull(member, $"'{name}' is not exposed any more");
                Assert.AreEqual(FrameLane.State, member.lane, $"'{name}' is not on the state lane");
            }
        }
    }
}
