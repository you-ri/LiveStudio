// Copyright (c) You-Ri, 2026

using System;
using System.Collections.Generic;
using System.Linq;
using Lilium.LiveStudio;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRCTrackingControl = VRC.SDK3.Avatars.Components.VRCAnimatorTrackingControl;
using VRCTrackingType = VRC.SDKBase.VRC_AnimatorTrackingControl.TrackingType;

namespace Lilium.VRChatAvatarTransfer.Editor
{
    /// <summary>
    /// Replaces every VRChat <see cref="VRCTrackingControl"/> StateMachineBehaviour found
    /// on the states / sub-state machines of an <see cref="AnimatorController"/> with a
    /// VRCSDK-independent <see cref="AvatarAnimatorTrackingControl"/> carrying the same data.
    /// The caller is responsible for passing a copy of the controller so the original
    /// VRChat asset is not modified.
    ///
    /// TODO: _ProcessStateMachine / _ReplaceOn duplicate AvatarParameterDriverConverter.
    /// Extract a shared StateMachineBehaviour-replace helper in a later refactor
    /// (kept duplicated here to follow the existing per-converter pattern).
    /// </summary>
    internal static class AvatarAnimatorTrackingControlConverter
    {
        /// <returns>Number of VRChat tracking controls replaced.</returns>
        public static int Convert(AnimatorController controller)
        {
            if (controller == null) return 0;

            int replaced = 0;
            foreach (var layer in controller.layers)
            {
                replaced += _ProcessStateMachine(layer.stateMachine);
            }

            if (replaced > 0)
            {
                EditorUtility.SetDirty(controller);
                VRChatAvatarTransferLog.Info(
                    $"'{controller.name}': replaced {replaced} VRCAnimatorTrackingControl with AvatarAnimatorTrackingControl.");
            }
            return replaced;
        }

        private static int _ProcessStateMachine(AnimatorStateMachine sm)
        {
            if (sm == null) return 0;

            int replaced = _ReplaceOn(
                sm.behaviours,
                t => sm.AddStateMachineBehaviour(t),
                () => sm.behaviours,
                arr => sm.behaviours = arr);

            foreach (var child in sm.states)
            {
                var state = child.state;
                if (state == null) continue;
                replaced += _ReplaceOn(
                    state.behaviours,
                    t => state.AddStateMachineBehaviour(t),
                    () => state.behaviours,
                    arr => state.behaviours = arr);
            }

            foreach (var childSm in sm.stateMachines)
            {
                replaced += _ProcessStateMachine(childSm.stateMachine);
            }
            return replaced;
        }

        private static int _ReplaceOn(
            StateMachineBehaviour[] behaviours,
            Func<Type, StateMachineBehaviour> add,
            Func<StateMachineBehaviour[]> get,
            Action<StateMachineBehaviour[]> set)
        {
            if (behaviours == null || behaviours.Length == 0) return 0;

            var toRemove = new List<StateMachineBehaviour>();
            foreach (var b in behaviours)
            {
                if (!(b is VRCTrackingControl vrc)) continue;

                var dst = add(typeof(AvatarAnimatorTrackingControl)) as AvatarAnimatorTrackingControl;
                if (dst == null)
                {
                    VRChatAvatarTransferLog.Error("Failed to add AvatarAnimatorTrackingControl behaviour.");
                    continue;
                }
                _Copy(vrc, dst);
                toRemove.Add(b);
            }

            if (toRemove.Count == 0) return 0;

            var filtered = get().Where(x => !toRemove.Contains(x)).ToArray();
            set(filtered);
            foreach (var old in toRemove)
            {
                AssetDatabase.RemoveObjectFromAsset(old);
                UnityEngine.Object.DestroyImmediate(old, true);
            }
            return toRemove.Count;
        }

        private static void _Copy(VRCTrackingControl src, AvatarAnimatorTrackingControl dst)
        {
            // TrackingType enum values are identical (NoChange/Tracking/Animation).
            dst.trackingHead = (AvatarTrackingType)(int)src.trackingHead;
            dst.trackingLeftHand = (AvatarTrackingType)(int)src.trackingLeftHand;
            dst.trackingRightHand = (AvatarTrackingType)(int)src.trackingRightHand;
            dst.trackingHip = (AvatarTrackingType)(int)src.trackingHip;
            dst.trackingLeftFoot = (AvatarTrackingType)(int)src.trackingLeftFoot;
            dst.trackingRightFoot = (AvatarTrackingType)(int)src.trackingRightFoot;
            dst.trackingLeftFingers = (AvatarTrackingType)(int)src.trackingLeftFingers;
            dst.trackingRightFingers = (AvatarTrackingType)(int)src.trackingRightFingers;
            dst.trackingEyes = (AvatarTrackingType)(int)src.trackingEyes;
            dst.trackingMouth = (AvatarTrackingType)(int)src.trackingMouth;
            dst.debugString = src.debugString;
        }
    }
}
