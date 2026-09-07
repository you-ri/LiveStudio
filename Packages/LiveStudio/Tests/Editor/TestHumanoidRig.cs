// Copyright (c) You-Ri, 2026

using NUnit.Framework;
using UnityEngine;

namespace Lilium.LiveStudio.EditorTests
{
    /// <summary>
    /// A humanoid rig built from nothing, for tests that need one and should not depend on an asset.
    ///
    /// Shared rather than copied: the muscle-space tests and the wire-conversion tests both need a
    /// rig, and they need to agree on what one looks like -- a rig missing a bone silently drops that
    /// bone's muscles, which reads as a conversion bug rather than as a rig that was never complete.
    /// </summary>
    public static class TestHumanoidRig
    {
        /// <summary>
        /// Builds a complete humanoid rig -- every bone Unity has a muscle for -- and a runtime
        /// Avatar for it. <paramref name="scale"/> multiplies every bone offset, so two rigs of
        /// different size can be posed against each other.
        ///
        /// Bone rest rotations are identity, so two rigs built here differ only in their proportions.
        /// That is what makes them usable as stand-ins for "the same pose on a different model".
        /// </summary>
        public static GameObject Build(float scale, out Animator animator, out HumanDescription description)
        {
            // Kept out of the scene's saved contents: an edit-mode test that leaves the open scene
            // dirty makes the next run stop on a "Scene(s) Have Been Modified" dialog, which blocks
            // the editor entirely.
            var root = new GameObject("TestHumanoidRig") { hideFlags = HideFlags.HideAndDontSave };

            var bones = new System.Collections.Generic.List<(HumanBodyBones bone, string parent, Vector3 offset)>
            {
                (HumanBodyBones.Hips,          null,            new Vector3(0f, 1f, 0f)),
                (HumanBodyBones.Spine,         "Hips",          new Vector3(0f, 0.1f, 0f)),
                (HumanBodyBones.Chest,         "Spine",         new Vector3(0f, 0.12f, 0f)),
                (HumanBodyBones.UpperChest,    "Chest",         new Vector3(0f, 0.12f, 0f)),
                (HumanBodyBones.Neck,          "UpperChest",    new Vector3(0f, 0.15f, 0f)),
                (HumanBodyBones.Head,          "Neck",          new Vector3(0f, 0.1f, 0f)),
                (HumanBodyBones.LeftEye,       "Head",          new Vector3(0.03f, 0.08f, 0.05f)),
                (HumanBodyBones.RightEye,      "Head",          new Vector3(-0.03f, 0.08f, 0.05f)),
                (HumanBodyBones.Jaw,           "Head",          new Vector3(0f, 0.02f, 0.03f)),
                (HumanBodyBones.LeftShoulder,  "UpperChest",    new Vector3(0.05f, 0.1f, 0f)),
                (HumanBodyBones.LeftUpperArm,  "LeftShoulder",  new Vector3(0.1f, 0f, 0f)),
                (HumanBodyBones.LeftLowerArm,  "LeftUpperArm",  new Vector3(0.25f, 0f, 0f)),
                (HumanBodyBones.LeftHand,      "LeftLowerArm",  new Vector3(0.25f, 0f, 0f)),
                (HumanBodyBones.RightShoulder, "UpperChest",    new Vector3(-0.05f, 0.1f, 0f)),
                (HumanBodyBones.RightUpperArm, "RightShoulder", new Vector3(-0.1f, 0f, 0f)),
                (HumanBodyBones.RightLowerArm, "RightUpperArm", new Vector3(-0.25f, 0f, 0f)),
                (HumanBodyBones.RightHand,     "RightLowerArm", new Vector3(-0.25f, 0f, 0f)),
                (HumanBodyBones.LeftUpperLeg,  "Hips",          new Vector3(0.1f, 0f, 0f)),
                (HumanBodyBones.LeftLowerLeg,  "LeftUpperLeg",  new Vector3(0f, -0.4f, 0f)),
                (HumanBodyBones.LeftFoot,      "LeftLowerLeg",  new Vector3(0f, -0.4f, 0f)),
                (HumanBodyBones.LeftToes,      "LeftFoot",      new Vector3(0f, -0.05f, 0.1f)),
                (HumanBodyBones.RightUpperLeg, "Hips",          new Vector3(-0.1f, 0f, 0f)),
                (HumanBodyBones.RightLowerLeg, "RightUpperLeg", new Vector3(0f, -0.4f, 0f)),
                (HumanBodyBones.RightFoot,     "RightLowerLeg", new Vector3(0f, -0.4f, 0f)),
                (HumanBodyBones.RightToes,     "RightFoot",     new Vector3(0f, -0.05f, 0.1f)),
            };

            // Fingers, so the 40 finger muscles have bones to land on. Without them the round-trip
            // check would only cover the body half, which is the half that was never in doubt.
            _AddFingers(bones, isLeft: true);
            _AddFingers(bones, isLeft: false);

            var objects = new System.Collections.Generic.Dictionary<string, GameObject>();
            var skeleton = new System.Collections.Generic.List<UnityEngine.SkeletonBone>
            {
                new UnityEngine.SkeletonBone
                {
                    name = root.name,
                    position = Vector3.zero,
                    rotation = Quaternion.identity,
                    scale = Vector3.one,
                },
            };
            var human = new System.Collections.Generic.List<UnityEngine.HumanBone>();

            foreach (var (bone, parent, offset) in bones)
            {
                string name = bone.ToString();
                var go = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
                go.transform.SetParent(parent == null ? root.transform : objects[parent].transform, false);
                go.transform.localPosition = offset * scale;
                objects[name] = go;

                skeleton.Add(new UnityEngine.SkeletonBone
                {
                    name = name,
                    position = go.transform.localPosition,
                    rotation = Quaternion.identity,
                    scale = Vector3.one,
                });
                human.Add(new UnityEngine.HumanBone
                {
                    boneName = name,
                    humanName = HumanTrait.BoneName[(int)bone],
                    limit = new UnityEngine.HumanLimit { useDefaultValues = true },
                });
            }

            description = new HumanDescription
            {
                human = human.ToArray(),
                skeleton = skeleton.ToArray(),
                upperArmTwist = 0.5f,
                lowerArmTwist = 0.5f,
                upperLegTwist = 0.5f,
                lowerLegTwist = 0.5f,
                armStretch = 0.05f,
                legStretch = 0.05f,
                feetSpacing = 0f,
                hasTranslationDoF = false,
            };

            var avatar = AvatarBuilder.BuildHumanAvatar(root, description);
            Assert.IsTrue(avatar != null && avatar.isValid, "failed to build a humanoid avatar for the test rig");
            avatar.name = "TestAvatar";

            animator = root.AddComponent<Animator>();
            animator.avatar = avatar;
            Assert.IsTrue(animator.isHuman, "test rig animator is not humanoid");
            return root;
        }


        /// <summary>
        /// Appends one hand's five fingers (three bones each) to the rig layout. Laid out along the
        /// arm with the fingers spread on Z, which is enough structure for Unity to resolve the
        /// finger muscles; the exact proportions do not matter to what is being measured.
        /// </summary>
        private static void _AddFingers(
            System.Collections.Generic.List<(HumanBodyBones bone, string parent, Vector3 offset)> bones, bool isLeft)
        {
            float side = isLeft ? 1f : -1f;
            string hand = isLeft ? "LeftHand" : "RightHand";
            var fingers = new[]
            {
                (name: "Thumb",  first: isLeft ? HumanBodyBones.LeftThumbProximal : HumanBodyBones.RightThumbProximal,  spread: 0.03f),
                (name: "Index",  first: isLeft ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal,  spread: 0.015f),
                (name: "Middle", first: isLeft ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal, spread: 0f),
                (name: "Ring",   first: isLeft ? HumanBodyBones.LeftRingProximal : HumanBodyBones.RightRingProximal,    spread: -0.015f),
                (name: "Little", first: isLeft ? HumanBodyBones.LeftLittleProximal : HumanBodyBones.RightLittleProximal, spread: -0.03f),
            };

            foreach (var finger in fingers)
            {
                // Proximal / Intermediate / Distal are consecutive in HumanBodyBones.
                var proximal = finger.first;
                var intermediate = (HumanBodyBones)((int)finger.first + 1);
                var distal = (HumanBodyBones)((int)finger.first + 2);

                bones.Add((proximal, hand, new Vector3(0.05f * side, 0f, finger.spread)));
                bones.Add((intermediate, proximal.ToString(), new Vector3(0.03f * side, 0f, 0f)));
                bones.Add((distal, intermediate.ToString(), new Vector3(0.02f * side, 0f, 0f)));
            }
        }
    }
}
