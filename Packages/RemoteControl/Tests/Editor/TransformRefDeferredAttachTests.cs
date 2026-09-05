// Copyright (c) You-Ri, 2026
using NUnit.Framework;
using UnityEngine;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// A reference is resolved once, after every part of it has been written.
    ///
    /// The state lane writes a <see cref="TransformRef"/> one member at a time -- owner, then path,
    /// then search type -- and each write used to re-attach on the spot. The first of the three runs
    /// with the new owner and the *old* path, which cannot resolve and falls back to the root: a
    /// replayed frame parented the object to the root and only then to the bone it recorded. Taking
    /// the first call and dropping the rest would keep exactly the wrong one, so the flush belongs at
    /// the end of the frame rather than at the start.
    /// </summary>
    [TestFixture]
    public class TransformRefDeferredAttachTests
    {
        private GameObject _rig;
        private GameObject _otherRig;
        private GameObject _child;

        private LiveGameObject _rigProxy;
        private LiveGameObject _otherRigProxy;
        private LiveGameObjectWithTransform _childProxy;

        [SetUp]
        public void StartClean()
        {
            LiveObjectRegistry.ClearAll();

            _rig = new GameObject("Rig");
            var hips = new GameObject("Hips");
            hips.transform.SetParent(_rig.transform);
            var head = new GameObject("Head");
            head.transform.SetParent(hips.transform);

            _otherRig = new GameObject("OtherRig");
            var otherHips = new GameObject("Hips");
            otherHips.transform.SetParent(_otherRig.transform);
            var otherHead = new GameObject("Head");
            otherHead.transform.SetParent(otherHips.transform);

            _child = new GameObject("Prop");

            _rigProxy = new LiveGameObject(_rig);
            _rigProxy.OnEnable();

            _otherRigProxy = new LiveGameObject(_otherRig);
            _otherRigProxy.OnEnable();

            _childProxy = new LiveGameObjectWithTransform(_child);
            _childProxy.OnEnable();
        }

        [TearDown]
        public void Finish()
        {
            _childProxy?.OnDisable();
            _otherRigProxy?.OnDisable();
            _rigProxy?.OnDisable();

            if (_child != null) Object.DestroyImmediate(_child);
            if (_otherRig != null) Object.DestroyImmediate(_otherRig);
            if (_rig != null) Object.DestroyImmediate(_rig);

            LiveObjectRegistry.ClearAll();
        }

        private Transform _Bone(GameObject rig) => rig.transform.Find("Hips/Head");

        [Test]
        public void WritingEveryMemberOfAReference_AttachesOnce_AtTheEndOfTheFrame()
        {
            _childProxy.parent.ownerName = "Rig";
            _childProxy.parent.transformName = "Hips/Head";
            _childProxy.Update();

            Assert.AreSame(_Bone(_rig), _child.transform.parent, "the fixture never attached at all");

            // Both halves change, the way a seek writes them. Between the two writes the reference
            // says "OtherRig" + the old rig's path, which resolves to nothing under the new owner.
            _childProxy.parent.ownerName = "OtherRig";

            Assert.AreSame(_Bone(_rig), _child.transform.parent,
                "the object moved before the rest of the reference had been written");

            _childProxy.parent.transformName = "Hips/Head";
            _childProxy.Update();

            Assert.AreSame(_Bone(_otherRig), _child.transform.parent,
                "the reference did not land on the bone it names");
        }

        [Test]
        public void AFlushWithNothingPending_DoesNothing()
        {
            _childProxy.parent.ownerName = "Rig";
            _childProxy.parent.transformName = "Hips/Head";
            _childProxy.Update();

            var attached = _child.transform.parent;

            // Reparented by hand, the way a person dragging in the hierarchy does. A flush with no
            // change behind it must not undo that -- the sync runs the other way (see
            // _OnHierarchyChanged), and re-attaching here would fight the person doing it.
            _child.transform.SetParent(_otherRig.transform, worldPositionStays: false);
            _childProxy.Update();

            Assert.AreSame(_otherRig.transform, _child.transform.parent,
                "a flush with nothing pending re-attached anyway");
            Assert.IsNotNull(attached);
        }
    }
}
