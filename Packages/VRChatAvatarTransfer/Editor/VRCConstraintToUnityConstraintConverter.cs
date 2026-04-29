// Copyright (c) You-Ri, 2026
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.Constraint.Components;

namespace Lilium.VRChatAvatarTransfer.Editor
{
    internal static class VRCConstraintToUnityConstraintConverter
    {
        public static int Convert(GameObject avatarRoot)
        {
            if (avatarRoot == null)
            {
                VRChatAvatarTransferLog.Error("Avatar root is null.");
                return 0;
            }

            var vrcConstraints = avatarRoot.GetComponentsInChildren<VRCConstraintBase>(true);
            if (vrcConstraints.Length == 0)
            {
                VRChatAvatarTransferLog.Info($"'{avatarRoot.name}': no VRC constraints found.");
                return 0;
            }

            Undo.RegisterFullObjectHierarchyUndo(avatarRoot, "Convert VRC Constraints");

            int converted = 0;
            foreach (var vrc in vrcConstraints)
            {
                if (vrc == null) continue;
                if (TryConvertOne(vrc)) converted++;
            }

            VRChatAvatarTransferLog.Info($"'{avatarRoot.name}': converted {converted}/{vrcConstraints.Length} VRC constraint(s).");
            return converted;
        }

        private static bool TryConvertOne(VRCConstraintBase vrc)
        {
            if (vrc.FreezeToWorld)
            {
                VRChatAvatarTransferLog.Warn(
                    $"'{vrc.gameObject.name}' {vrc.GetType().Name} uses FreezeToWorld which is unsupported. Skipped.");
                return false;
            }

            var target = vrc.TargetTransform != null ? vrc.TargetTransform : vrc.transform;
            if (target != vrc.transform)
            {
                VRChatAvatarTransferLog.Warn(
                    $"'{vrc.gameObject.name}' {vrc.GetType().Name} has a non-self TargetTransform '{target.name}'. " +
                    "Unity Constraints always target the host GameObject. Skipped.");
                return false;
            }

            var host = target.gameObject;

            switch (vrc)
            {
                case VRCParentConstraint vp:
                {
                    var u = Undo.AddComponent<ParentConstraint>(host);
                    CopyCommon(vp, u);
                    CopyParentSources(vp, u);
                    break;
                }
                case VRCPositionConstraint vp:
                {
                    var u = Undo.AddComponent<PositionConstraint>(host);
                    CopyCommon(vp, u);
                    CopySources(vp, u);
                    u.translationAtRest = vp.PositionAtRest;
                    u.translationOffset = vp.PositionOffset;
                    u.translationAxis = ToAxis(vp.AffectsPositionX, vp.AffectsPositionY, vp.AffectsPositionZ);
                    break;
                }
                case VRCRotationConstraint vr:
                {
                    var u = Undo.AddComponent<RotationConstraint>(host);
                    CopyCommon(vr, u);
                    CopySources(vr, u);
                    u.rotationAtRest = vr.RotationAtRest;
                    u.rotationOffset = vr.RotationOffset;
                    u.rotationAxis = ToAxis(vr.AffectsRotationX, vr.AffectsRotationY, vr.AffectsRotationZ);
                    break;
                }
                case VRCScaleConstraint vs:
                {
                    var u = Undo.AddComponent<ScaleConstraint>(host);
                    CopyCommon(vs, u);
                    CopySources(vs, u);
                    u.scaleAtRest = vs.ScaleAtRest;
                    u.scaleOffset = vs.ScaleOffset;
                    u.scalingAxis = ToAxis(vs.AffectsScaleX, vs.AffectsScaleY, vs.AffectsScaleZ);
                    break;
                }
                case VRCAimConstraint va:
                {
                    var u = Undo.AddComponent<AimConstraint>(host);
                    CopyCommon(va, u);
                    CopySources(va, u);
                    u.aimVector = va.AimAxis;
                    u.upVector = va.UpAxis;
                    u.worldUpVector = va.WorldUpVector;
                    u.worldUpObject = va.WorldUpTransform;
                    u.worldUpType = (AimConstraint.WorldUpType)(int)va.WorldUp;
                    break;
                }
                case VRCLookAtConstraint vl:
                {
                    var u = Undo.AddComponent<LookAtConstraint>(host);
                    CopyCommon(vl, u);
                    CopySources(vl, u);
                    u.roll = vl.Roll;
                    u.worldUpObject = vl.WorldUpTransform;
                    u.useUpObject = vl.UseUpTransform;
                    break;
                }
                default:
                    VRChatAvatarTransferLog.Warn($"Unknown VRC constraint type {vrc.GetType().Name} on '{vrc.gameObject.name}'. Skipped.");
                    return false;
            }

            Undo.DestroyObjectImmediate(vrc);
            return true;
        }

        private static void CopyCommon(VRCConstraintBase src, IConstraint dst)
        {
            dst.weight = src.GlobalWeight;
            dst.constraintActive = src.IsActive;
            dst.locked = src.Locked;
        }

        private static void CopySources(VRCConstraintBase src, IConstraint dst)
        {
            foreach (var s in src.Sources)
            {
                dst.AddSource(new ConstraintSource
                {
                    sourceTransform = s.SourceTransform,
                    weight = s.Weight,
                });
            }
        }

        private static void CopyParentSources(VRCParentConstraint src, ParentConstraint dst)
        {
            for (int i = 0; i < src.Sources.Count; i++)
            {
                var s = src.Sources[i];
                dst.AddSource(new ConstraintSource
                {
                    sourceTransform = s.SourceTransform,
                    weight = s.Weight,
                });
                dst.SetTranslationOffset(i, s.ParentPositionOffset);
                dst.SetRotationOffset(i, s.ParentRotationOffset);
            }
        }

        private static Axis ToAxis(bool x, bool y, bool z)
        {
            var axis = Axis.None;
            if (x) axis |= Axis.X;
            if (y) axis |= Axis.Y;
            if (z) axis |= Axis.Z;
            return axis;
        }

    }
}
