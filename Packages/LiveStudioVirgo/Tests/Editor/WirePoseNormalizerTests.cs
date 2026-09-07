// Copyright (c) You-Ri, 2026

using NUnit.Framework;
using UnityEngine;

using Lilium.LiveStudio;
using Lilium.LiveStudio.EditorTests;

namespace Lilium.LiveStudio.Virgo.EditorTests
{
    /// <summary>
    /// The claim the muscle-space pose was introduced for: a pose captured on one model plays back on
    /// a different one.
    ///
    /// The received frame carries bone rotations, which only mean anything against the rig they were
    /// measured on -- that is why the pose is normalized on the way in. These tests take a pose off one
    /// rig, put it on a rig of a different size, and check the body is doing the same thing there.
    /// </summary>
    public class WirePoseNormalizerTests
    {
        private GameObject _capturedOn;
        private GameObject _replayedOn;
        private WirePoseNormalizer _normalizer;
        private HumanPoseHandler _replayHandler;
        private HumanPoseHandler _captureHandler;

        [TearDown]
        public void TearDown()
        {
            _replayHandler?.Dispose();
            _replayHandler = null;
            _captureHandler?.Dispose();
            _captureHandler = null;
            _normalizer?.Dispose();
            _normalizer = null;
            if (_capturedOn != null) Object.DestroyImmediate(_capturedOn);
            if (_replayedOn != null) Object.DestroyImmediate(_replayedOn);
        }

        /// <summary>
        /// A pose captured on one rig, replayed on a rig twice the size, puts the joints in the same
        /// place.
        ///
        /// Both rigs are compared after the same pose has been applied to each, rather than against
        /// the rotations the capture rig was posed with. Muscle space cannot hold everything a bone
        /// rotation can -- an arm's roll is split between upper arm and forearm on the way in, so a
        /// round trip does not land exactly where it started even on the rig it was captured from
        /// (measured below). That loss is common to both rigs; what this test is about is whether
        /// anything is left over that depends on the model, and the answer has to be nothing.
        /// </summary>
        [Test]
        public void APoseCapturedOnOneRig_ReplaysOnADifferentlySizedOne()
        {
            _capturedOn = TestHumanoidRig.Build(1f, out var captureAnimator, out var captureDescription);
            _replayedOn = TestHumanoidRig.Build(2f, out var replayAnimator, out _);

            // Something asymmetric, so a left/right or index mix-up cannot pass.
            var posed = new (HumanBodyBones bone, Quaternion rotation)[]
            {
                (HumanBodyBones.LeftUpperArm, Quaternion.Euler(0f, 0f, -35f)),
                (HumanBodyBones.LeftLowerArm, Quaternion.Euler(0f, -50f, 0f)),
                (HumanBodyBones.RightUpperArm, Quaternion.Euler(12f, 0f, 20f)),
                (HumanBodyBones.Head, Quaternion.Euler(15f, 25f, 0f)),
                (HumanBodyBones.LeftUpperLeg, Quaternion.Euler(-20f, 0f, 0f)),
                (HumanBodyBones.RightLowerLeg, Quaternion.Euler(30f, 0f, 0f)),
                (HumanBodyBones.LeftIndexProximal, Quaternion.Euler(0f, 0f, -25f)),
            };
            foreach (var (bone, rotation) in posed)
                captureAnimator.GetBoneTransform(bone).localRotation = rotation;

            var wire = _MakeWireFrame(captureAnimator);

            _normalizer = new WirePoseNormalizer();
            _normalizer.Rebuild(AvatarBuildSystem.CreateAvatarBuildData(_capturedOn.transform, captureDescription));
            Assert.IsTrue(_normalizer.isReady, "the normalizer did not build a skeleton from the capture rig");

            Assert.IsTrue(_normalizer.TryNormalize(in wire, out var pose), "the received frame did not convert");

            // Put the recorded pose back on both bodies.
            _captureHandler = new HumanPoseHandler(captureAnimator.avatar, captureAnimator.transform);
            AvatarAnimationSystem.UpdatePose(_captureHandler, in pose);
            var onCaptureRig = new Quaternion[posed.Length];
            for (int i = 0; i < posed.Length; i++)
                onCaptureRig[i] = captureAnimator.GetBoneTransform(posed[i].bone).localRotation;

            _replayHandler = new HumanPoseHandler(replayAnimator.avatar, replayAnimator.transform);
            AvatarAnimationSystem.UpdatePose(_replayHandler, in pose);

            for (int i = 0; i < posed.Length; i++)
            {
                Quaternion replayed = replayAnimator.GetBoneTransform(posed[i].bone).localRotation;

                Assert.Less(Quaternion.Angle(onCaptureRig[i], replayed), 0.5f,
                    $"{posed[i].bone} landed {Quaternion.Angle(onCaptureRig[i], replayed):F2}° apart on a rig of a different size");
            }
        }

        /// <summary>
        /// What muscle space costs, stated rather than assumed: the same rig, posed and then given its
        /// own recorded pose back, does not land exactly where it started.
        ///
        /// Measured: within 6° on every joint, and the joint that moves is the upper arm -- its roll
        /// is redistributed with the forearm's twist, which muscle space has no separate room for. It
        /// is worth knowing this is the size of the loss: it is a property of humanoid retargeting,
        /// not of this conversion, and the same loss was already being taken every time a humanoid
        /// clip or Fusion's own pose normalization ran.
        /// </summary>
        [Test]
        public void MuscleSpace_LosesSomeOfTheOriginalRotation()
        {
            _capturedOn = TestHumanoidRig.Build(1f, out var animator, out var description);

            var posed = new (HumanBodyBones bone, Quaternion rotation)[]
            {
                (HumanBodyBones.LeftUpperArm, Quaternion.Euler(0f, 0f, -35f)),
                (HumanBodyBones.LeftLowerArm, Quaternion.Euler(0f, -50f, 0f)),
                (HumanBodyBones.Head, Quaternion.Euler(15f, 25f, 0f)),
                (HumanBodyBones.RightLowerLeg, Quaternion.Euler(30f, 0f, 0f)),
            };
            foreach (var (bone, rotation) in posed)
                animator.GetBoneTransform(bone).localRotation = rotation;

            var wire = _MakeWireFrame(animator);

            _normalizer = new WirePoseNormalizer();
            _normalizer.Rebuild(AvatarBuildSystem.CreateAvatarBuildData(_capturedOn.transform, description));
            Assert.IsTrue(_normalizer.TryNormalize(in wire, out var pose));

            _captureHandler = new HumanPoseHandler(animator.avatar, animator.transform);
            AvatarAnimationSystem.UpdatePose(_captureHandler, in pose);

            foreach (var (bone, authored) in posed)
            {
                float drift = Quaternion.Angle(authored, animator.GetBoneTransform(bone).localRotation);
                Assert.Less(drift, 6f, $"{bone} drifted {drift:F1}° through muscle space, more than measured before");
            }
        }

        /// <summary>
        /// Presence arrives per bone and has to reach every muscle that bone drives, or a partly
        /// tracked body would be applied as if it were fully tracked (and the avatar's own animation
        /// would stop showing through where tracking was lost).
        /// </summary>
        [Test]
        public void BonePresence_ReachesEveryMuscleOfThatBone()
        {
            _capturedOn = TestHumanoidRig.Build(1f, out var captureAnimator, out var captureDescription);

            var wire = _MakeWireFrame(captureAnimator);
            wire.AsPresence((int)HumanBodyBones.LeftLowerArm) = 0.25f;
            wire.AsPresence((int)HumanBodyBones.Hips) = 0.5f;

            _normalizer = new WirePoseNormalizer();
            _normalizer.Rebuild(AvatarBuildSystem.CreateAvatarBuildData(_capturedOn.transform, captureDescription));
            Assert.IsTrue(_normalizer.TryNormalize(in wire, out var pose));

            Assert.AreEqual(0.5f, pose.bodyPresence, 1e-4f, "the hips' presence should carry the body pose's");

            for (int dof = 0; dof < 3; dof++)
            {
                int muscle = HumanTrait.MuscleFromBone((int)HumanBodyBones.LeftLowerArm, dof);
                if (muscle < 0) continue;

                Assert.AreEqual(0.25f, pose.AsMusclePresence(muscle), 1e-4f,
                    $"{HumanTrait.MuscleName[muscle]} did not take the lower arm's presence");
            }

            int elsewhere = HumanTrait.MuscleFromBone((int)HumanBodyBones.RightLowerArm, 0);
            Assert.AreEqual(1f, pose.AsMusclePresence(elsewhere), 1e-4f,
                "a bone's presence leaked onto another bone's muscles");
        }

        /// <summary>
        /// Before an avatar is loaded there is no rig to read a pose off. That is a normal state at
        /// startup, not an error, and it has to come back as "no pose this frame" rather than as a
        /// zeroed one -- a zeroed pose would snap the avatar to a stance nobody performed.
        /// </summary>
        [Test]
        public void WithoutASkeleton_NoPoseIsProduced()
        {
            _normalizer = new WirePoseNormalizer();
            Assert.IsFalse(_normalizer.isReady);

            var wire = default(AnimationFrameData);
            Assert.IsFalse(_normalizer.TryNormalize(in wire, out _));
        }

        /// <summary>Builds the frame Fusion would send for the rig's current pose.</summary>
        private static AnimationFrameData _MakeWireFrame(Animator animator)
        {
            var wire = default(AnimationFrameData);
            wire.valid = (byte)(AnimationFrameData.kBodyValidFlag | AnimationFrameData.kFaceValidFlag);
            wire.scale = Vector3.one;
            wire.rotation = Quaternion.identity;

            for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
            {
                var bone = animator.GetBoneTransform((HumanBodyBones)i);
                if (bone == null) continue;
                wire.AsRotation(i) = bone.localRotation;
                wire.AsPresence(i) = 1f;
            }

            wire.hipPosition = animator.GetBoneTransform(HumanBodyBones.Hips).localPosition;
            return wire;
        }
    }
}
