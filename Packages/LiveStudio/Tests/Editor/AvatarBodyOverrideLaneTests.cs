// Copyright (c) You-Ri, 2026
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Lilium.RemoteControl;
using Lilium.RemoteControl.Frames;

namespace Lilium.LiveStudio.Tests
{
    /// <summary>
    /// The clip that drives the untracked parts of the body reaches a recording.
    ///
    /// It could not before: the choice lives in a baked <see cref="AnimationClip"/> reference and a
    /// key into an external pack, and a block can hold neither -- one is a reference, and both are
    /// private on a type that is not partial. So a take said nothing about the pose the avatar falls
    /// back to for everything the capture does not track.
    /// </summary>
    public class AvatarBodyOverrideLaneTests
    {
        private GameObject _go;
        private AvatarController _controller;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("body-override-lane", typeof(AvatarController));
            _controller = _go.GetComponent<AvatarController>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
            _go = null;
            _controller = null;
        }

        private static LivePropertyType _Member(string name)
        {
            var member = System.Array.Find(
                LiveClass.Get<AvatarController>().propertyTypes, p => p.name == name);

            Assert.IsNotNull(member, $"'{name}' is not exposed any more");
            return member;
        }

        [Test]
        public void TheOverrideClipKey_AsksForTheStateLane()
        {
            Assert.AreEqual(FrameLane.State, _Member("bodyOverrideClipKey").lane);
        }

        /// <summary>
        /// The half a declaration cannot promise. Asking for the state lane is a request that can be
        /// refused -- and this member exists precisely because the two it stands for were refused,
        /// quietly, for being private on a type whose block is generated outside it.
        /// </summary>
        [Test]
        public void TheOverrideClipKey_IsActuallyCarried()
        {
            var bridge = StateBridgeRegistry.Find(typeof(AvatarController));

            Assert.IsNotNull(bridge, "nothing carries the avatar controller's state");
            Assert.IsTrue(LiveStateCarriage.IsCarriedByState(_Member("bodyOverrideClipKey"), bridge),
                "the override clip key asks for the state lane and no block moves it");
        }

        /// <summary>
        /// The two faces of the choice say the key carries them, so the same choice does not end up
        /// in both lanes -- the state lane copying it every frame while an event record repeats it.
        /// It is also what the remote app reads to say whether a take remembers them.
        /// </summary>
        [Test]
        public void TheTwoFacesOfTheChoice_AreCarriedByTheKey()
        {
            var bridge = StateBridgeRegistry.Find(typeof(AvatarController));

            Assert.IsTrue(LiveStateCarriage.IsCarriedByState(_Member("_bodyOverrideClip"), bridge),
                "the baked clip reads as not recorded, which is the opposite of the truth");
            Assert.IsTrue(LiveStateCarriage.IsCarriedByState(_Member("_bodyOverrideClipRef"), bridge),
                "the pack key reads as not recorded, which is the opposite of the truth");
        }

        /// <summary>
        /// A pack key round-trips: what the frame reads back is what a replay would write in.
        /// Without this the member could be carried and still say nothing useful.
        /// </summary>
        [Test]
        public void APackKey_ComesBackOutOfTheKey()
        {
            const string key = "file:Assets/Packs/Motion.pack.lsb#Idle";

            // Writing the key starts a resolve, and there is no such pack here, so the loader logs
            // that it could not find one. Ignored rather than expected: the resolve is asynchronous,
            // so whether the message lands inside this test is a matter of timing, and what is under
            // test is the key going in and coming back out.
            LogAssert.ignoreFailingMessages = true;
            try
            {
                _controller.bodyOverrideClipKey = key;

                Assert.AreEqual(key, _controller.bodyOverrideClipKey);
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
            }
        }

        /// <summary>Nothing chosen reads as nothing, rather than as a stale key.</summary>
        [Test]
        public void NoChoice_ReadsAsEmpty()
        {
            Assert.AreEqual(string.Empty, _controller.bodyOverrideClipKey);
        }
    }
}
