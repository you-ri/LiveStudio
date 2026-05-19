// Copyright (c) You-Ri, 2026

using System;
using System.Collections.Generic;
using System.Linq;
using Lilium.LiveStudio;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRCDriver = VRC.SDK3.Avatars.Components.VRCAvatarParameterDriver;
using VRCParameter = VRC.SDKBase.VRC_AvatarParameterDriver.Parameter;

namespace Lilium.VRChatAvatarTransfer.Editor
{
    /// <summary>
    /// Replaces every VRChat <see cref="VRCDriver"/> StateMachineBehaviour found on the
    /// states / sub-state machines of an <see cref="AnimatorController"/> with a
    /// VRCSDK-independent <see cref="AvatarParameterDriver"/> carrying the same data.
    /// The caller is responsible for passing a copy of the controller so the original
    /// VRChat asset is not modified.
    /// </summary>
    internal static class AvatarParameterDriverConverter
    {
        /// <returns>Number of VRChat drivers replaced.</returns>
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
                    $"'{controller.name}': replaced {replaced} VRCAvatarParameterDriver with AvatarParameterDriver.");
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
                if (!(b is VRCDriver vrc)) continue;

                var driver = add(typeof(AvatarParameterDriver)) as AvatarParameterDriver;
                if (driver == null)
                {
                    VRChatAvatarTransferLog.Error("Failed to add AvatarParameterDriver behaviour.");
                    continue;
                }
                _Copy(vrc, driver);
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

        private static void _Copy(VRCDriver src, AvatarParameterDriver dst)
        {
            dst.localOnly = src.localOnly;
            dst.debugString = src.debugString;
            dst.parameters = new List<AvatarParameterDriverParameter>();

            if (src.parameters == null) return;
            foreach (VRCParameter sp in src.parameters)
            {
                if (sp == null) continue;
                dst.parameters.Add(new AvatarParameterDriverParameter
                {
                    name = sp.name,
                    source = sp.source,
                    // ChangeType enum values are identical (Set/Add/Random/Copy).
                    type = (AvatarParameterChangeType)(int)sp.type,
                    value = sp.value,
                    valueMin = sp.valueMin,
                    valueMax = sp.valueMax,
                    chance = sp.chance,
                    sourceMin = sp.sourceMin,
                    sourceMax = sp.sourceMax,
                    destMin = sp.destMin,
                    destMax = sp.destMax,
                    convertRange = sp.convertRange,
                });
            }
        }
    }
}
