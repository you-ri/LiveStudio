// Copyright (c) You-Ri, 2026

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Lilium.VRChatAvatarTransfer.Editor
{
    /// <summary>
    /// Removes any remaining VRChat StateMachineBehaviour (e.g. VRCAnimatorLayerControl,
    /// VRCPlayableLayerControl) from an <see cref="AnimatorController"/> copy. Unlike
    /// VRCAvatarParameterDriver / VRCAnimatorTrackingControl, these have no VRCSDK-independent
    /// equivalent and no function outside VRChat, so they are simply dropped. The Studio runtime has
    /// no VRChat SDK, so leaving them in the controller assigned to the avatar's Animator makes them
    /// load as "missing script" once the avatar bundle is loaded. They are identified by their VRChat
    /// namespace / assembly. The caller must pass a copy so the original asset is not modified.
    /// </summary>
    internal static class VrcStateMachineBehaviourStripper
    {
        /// <returns>Number of VRChat StateMachineBehaviours removed.</returns>
        public static int Strip(AnimatorController controller)
        {
            if (controller == null) return 0;

            int removed = 0;
            foreach (var layer in controller.layers)
            {
                removed += _Process(layer.stateMachine);
            }

            if (removed > 0)
            {
                EditorUtility.SetDirty(controller);
                VRChatAvatarTransferLog.Info(
                    $"'{controller.name}': stripped {removed} VRChat StateMachineBehaviour(s) (layer/playable control, etc.).");
            }
            return removed;
        }

        private static int _Process(AnimatorStateMachine sm)
        {
            if (sm == null) return 0;

            int removed = _RemoveOn(() => sm.behaviours, arr => sm.behaviours = arr);

            foreach (var child in sm.states)
            {
                var state = child.state;
                if (state == null) continue;
                removed += _RemoveOn(() => state.behaviours, arr => state.behaviours = arr);
            }

            foreach (var childSm in sm.stateMachines)
            {
                removed += _Process(childSm.stateMachine);
            }
            return removed;
        }

        private static int _RemoveOn(Func<StateMachineBehaviour[]> get, Action<StateMachineBehaviour[]> set)
        {
            var behaviours = get();
            if (behaviours == null || behaviours.Length == 0) return 0;

            var toRemove = new List<StateMachineBehaviour>();
            foreach (var b in behaviours)
            {
                if (b == null) continue;
                if (_IsVrc(b.GetType())) toRemove.Add(b);
            }

            if (toRemove.Count == 0) return 0;

            set(behaviours.Where(x => !toRemove.Contains(x)).ToArray());
            foreach (var old in toRemove)
            {
                AssetDatabase.RemoveObjectFromAsset(old);
                UnityEngine.Object.DestroyImmediate(old, true);
            }
            return toRemove.Count;
        }

        private static bool _IsVrc(Type type)
        {
            var ns = type.Namespace ?? string.Empty;
            var asm = type.Assembly.GetName().Name ?? string.Empty;
            return ns == "VRC" || ns.StartsWith("VRC.") || asm.StartsWith("VRC");
        }
    }
}
