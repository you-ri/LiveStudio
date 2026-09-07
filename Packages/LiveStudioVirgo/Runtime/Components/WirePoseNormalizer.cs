// Copyright (c) You-Ri, 2026

using System;
using UnityEngine;

using Lilium.LiveStudio;

namespace Lilium.LiveStudio.Virgo
{
    /// <summary>
    /// Turns the wire pose (per-bone local rotations) into the muscle-space pose the rest of Studio
    /// works in.
    ///
    /// ⚠ Transitional. The right place for this is Fusion, which already holds a
    /// <see cref="HumanPoseHandler"/> and reads a pose out of it every frame before throwing the
    /// muscles away to send bone rotations instead. Doing it there would make the wire smaller and
    /// drop the requirement that both ends stand up the same rig. That change touches the wire format
    /// (two hand-synced copies of <see cref="AnimationFrameData"/>, Fusion, the Warudo plugin and the
    /// UE receiver), so it is a separate step; until then the conversion happens here and this whole
    /// file goes away when the wire carries muscles.
    ///
    /// Converting here is exact rather than approximate: Studio sends Fusion the avatar's own
    /// <see cref="AvatarBuildData"/> and Fusion rebuilds the skeleton from it, so the rotations coming
    /// back are local rotations of this very rig. The same build data is used here to stand up a
    /// hidden skeleton to read the pose off, which keeps the conversion away from the visible avatar
    /// -- the live one is being driven by the animation graph at the same time, and writing bones on
    /// it to read them back would fight that.
    /// </summary>
    public sealed class WirePoseNormalizer : IDisposable
    {
        // The bone each muscle belongs to, so per-bone presence from the wire can be spread onto the
        // muscles that bone drives. Derived from Unity's own table rather than written out by hand.
        //
        // ⚠ Built on first use, not in a static initializer. HumanTrait is one of the APIs Unity
        // refuses to serve from a MonoBehaviour constructor or field initializer, and this class is
        // constructed in one (VirgoMotionSource's). A static initializer would throw there, and the
        // throw would come out as a type initializer failure far from its cause.
        private static int[] _boneOfMuscle;

        private GameObject _root;
        private Animator _animator;
        private HumanPoseHandler _handler;
        private HumanPose _pose;

        // Bone transforms by HumanBodyBones index (null where the rig has no such bone), so applying
        // a frame does not go through Animator.GetBoneTransform 55 times.
        private Transform[] _bones;

        /// <summary>True once a skeleton has been built and poses can be converted.</summary>
        public bool isReady => _handler != null;

        /// <summary>
        /// (Re)builds the hidden skeleton from the avatar's build data. Called whenever the avatar is
        /// built, which is the same event that sends the skeleton to Fusion -- so the rig converting
        /// the pose and the rig producing it come from one description.
        /// </summary>
        public void Rebuild(in AvatarBuildData data)
        {
            Dispose();
            _EnsureBoneOfMuscle();

            if (data.skeletonBones == null || data.skeletonBones.Length == 0) return;

            _root = new GameObject("VirgoWirePoseNormalizer")
            {
                // Not part of the scene: it must not be saved, must not show up in the hierarchy, and
                // must not be walked by anything that scans the scene for live objects.
                hideFlags = HideFlags.HideAndDontSave,
            };

            if (AvatarBuildSystem.BuildSkeleton(in data, _root) == null)
            {
                Dispose();
                return;
            }

            var avatar = AvatarBuildSystem.BuildHumanAvatar(_root, in data, "VirgoWirePoseNormalizerAvatar");
            if (avatar == null || !avatar.isValid)
            {
                Debug.LogWarning("[Studio] Could not build a skeleton to normalize the received pose with; poses will not be recorded until the next avatar build.");
                Dispose();
                return;
            }

            _animator = _root.GetComponent<Animator>();
            _animator.avatar = avatar;

            _bones = new Transform[(int)HumanBodyBones.LastBone];
            for (int i = 0; i < _bones.Length; i++)
            {
                _bones[i] = _animator.GetBoneTransform((HumanBodyBones)i);
            }

            _handler = new HumanPoseHandler(avatar, _root.transform);
            _pose.muscles = new float[HumanoidPoseData.kMuscleCount];
        }

        /// <summary>
        /// Converts one received frame's pose. False while no skeleton has been built yet (Studio
        /// starts before an avatar is loaded), in which case there is nothing to convert and the
        /// caller has no pose for this frame.
        /// </summary>
        public bool TryNormalize(in AnimationFrameData src, out HumanoidPoseData dst)
        {
            dst = default;
            if (_handler == null) return false;

            _ApplyWirePose(in src);
            _handler.GetHumanPose(ref _pose);

            dst.bodyPosition = _pose.bodyPosition;
            dst.bodyRotation = _pose.bodyRotation;
            dst.bodyPresence = _Presence(in src, (int)HumanBodyBones.Hips);

            for (int m = 0; m < HumanoidPoseData.kMuscleCount; m++)
            {
                dst.AsMuscle(m) = _pose.muscles[m];

                int bone = _boneOfMuscle[m];
                dst.AsMusclePresence(m) = bone >= 0 ? _Presence(in src, bone) : 1f;
            }

            return true;
        }

        private void _ApplyWirePose(in AnimationFrameData src)
        {
            // AsRotation is not a readonly member, so reading it off the `in` parameter would copy the
            // whole frame per bone. One copy up front instead.
            var frame = src;

            for (int i = 0; i < _bones.Length; i++)
            {
                var bone = _bones[i];
                if (bone == null) continue;
                bone.localRotation = frame.AsRotation(i);
            }

            var hips = _bones[(int)HumanBodyBones.Hips];
            if (hips != null) hips.localPosition = frame.hipPosition;
        }

        private static float _Presence(in AnimationFrameData src, int bone)
        {
            var frame = src;
            return frame.AsPresence(bone);
        }

        private static void _EnsureBoneOfMuscle()
        {
            if (_boneOfMuscle != null) return;

            var table = new int[HumanoidPoseData.kMuscleCount];
            for (int m = 0; m < table.Length; m++) table[m] = -1;

            for (int bone = 0; bone < (int)HumanBodyBones.LastBone; bone++)
            {
                for (int dof = 0; dof < 3; dof++)
                {
                    int muscle = HumanTrait.MuscleFromBone(bone, dof);
                    if (muscle >= 0 && muscle < table.Length) table[muscle] = bone;
                }
            }

            _boneOfMuscle = table;
        }

        public void Dispose()
        {
            _handler?.Dispose();
            _handler = null;
            _bones = null;
            _animator = null;

            if (_root != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(_root);
                else UnityEngine.Object.DestroyImmediate(_root);
                _root = null;
            }
        }
    }
}
