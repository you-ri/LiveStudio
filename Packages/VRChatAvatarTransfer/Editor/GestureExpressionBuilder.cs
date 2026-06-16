// Copyright (c) You-Ri, 2026

using System.Collections.Generic;
using Lilium.LiveStudio;
using UnityEditor.Animations;
using UnityEngine;

namespace Lilium.VRChatAvatarTransfer.Editor
{
    /// <summary>
    /// Builds the fixed set of VRChat hand-gesture <see cref="VRCExpression"/> entries (GestureLeft /
    /// GestureRight 1..7) so the converted avatar can switch hand shapes from face-tracking-independent
    /// triggers (e.g. RemoteApp "Expression.GestureLeftFist"). Each entry writes the gesture Int plus its
    /// *Weight Float = 1 onto the runtime Animator while active; <see cref="VRCExpressionDriver"/> restores
    /// the defaults on switch.
    ///
    /// A gesture value is only emitted when the applied FX AnimatorController actually wires it, i.e. some
    /// transition condition references GestureLeft/GestureRight with "Equals N". Unwired values are skipped.
    /// </summary>
    internal static class GestureExpressionBuilder
    {
        // Gesture value (1..7) -> short name. Index 0 == value 1. Value 0 (Neutral) is not an expression.
        private static readonly string[] s_gestureNames =
        {
            "Fist",        // 1
            "HandOpen",    // 2
            "FingerPoint", // 3
            "Victory",     // 4
            "RockNRoll",   // 5
            "HandGun",     // 6
            "ThumbsUp",    // 7
        };

        // Animator parameter names (VRChat standard) — written by the override.
        private const string kGestureLeft = "GestureLeft";
        private const string kGestureRight = "GestureRight";
        private const string kGestureLeftWeight = "GestureLeftWeight";
        private const string kGestureRightWeight = "GestureRightWeight";

        // Expression display-name prefixes (VRCExpression.name), e.g. "LeftFist" / "RightThumbsUp".
        private const string kLeftPrefix = "Left";
        private const string kRightPrefix = "Right";

        /// <summary>
        /// Builds gesture expressions for the gesture values that <paramref name="fxController"/> actually
        /// wires (via GestureLeft/GestureRight "Equals N" transition conditions). Returns an empty array
        /// when the controller is null or wires no gestures.
        /// </summary>
        public static VRCExpression[] Build(AnimatorController fxController)
        {
            var left = new HashSet<int>();
            var right = new HashSet<int>();
            _CollectConfiguredGestures(fxController, left, right);

            // 左を全件→右を全件の順に並べる (LeftFist..LeftThumbsUp, RightFist..RightThumbsUp)。
            var result = new List<VRCExpression>(left.Count + right.Count);
            for (int v = 1; v <= s_gestureNames.Length; v++)
            {
                if (left.Contains(v))
                {
                    result.Add(_BuildExpression(kLeftPrefix + s_gestureNames[v - 1], kGestureLeft, kGestureLeftWeight, v));
                }
            }
            for (int v = 1; v <= s_gestureNames.Length; v++)
            {
                if (right.Contains(v))
                {
                    result.Add(_BuildExpression(kRightPrefix + s_gestureNames[v - 1], kGestureRight, kGestureRightWeight, v));
                }
            }
            return result.ToArray();
        }

        private static VRCExpression _BuildExpression(string name, string gestureParam, string weightParam, int value)
        {
            return new VRCExpression
            {
                name = name,
                parameters = new[]
                {
                    new AnimationParameterOverride
                    {
                        name = gestureParam,
                        type = AnimatorControllerParameterType.Int,
                        intValue = value,
                    },
                    new AnimationParameterOverride
                    {
                        name = weightParam,
                        type = AnimatorControllerParameterType.Float,
                        floatValue = 1f,
                    },
                },
            };
        }

        /// <summary>
        /// Walks every layer's state machine (any-state / entry / per-state transitions, recursing into
        /// sub-state-machines) and records the gesture values (1..7) that appear as
        /// GestureLeft/GestureRight "Equals N" conditions.
        /// </summary>
        private static void _CollectConfiguredGestures(AnimatorController fxController, HashSet<int> left, HashSet<int> right)
        {
            if (fxController == null) return;

            foreach (var layer in fxController.layers)
            {
                _ScanStateMachine(layer.stateMachine, left, right);
            }
        }

        private static void _ScanStateMachine(AnimatorStateMachine sm, HashSet<int> left, HashSet<int> right)
        {
            if (sm == null) return;

            foreach (var t in sm.anyStateTransitions)
            {
                _ScanConditions(t, left, right);
            }
            foreach (var t in sm.entryTransitions)
            {
                _ScanConditions(t, left, right);
            }
            foreach (var child in sm.states)
            {
                var state = child.state;
                if (state == null) continue;
                foreach (var t in state.transitions)
                {
                    _ScanConditions(t, left, right);
                }
            }
            foreach (var childSm in sm.stateMachines)
            {
                _ScanStateMachine(childSm.stateMachine, left, right);
            }
        }

        private static void _ScanConditions(AnimatorTransitionBase transition, HashSet<int> left, HashSet<int> right)
        {
            if (transition == null || transition.conditions == null) return;

            foreach (var c in transition.conditions)
            {
                if (c.mode != AnimatorConditionMode.Equals) continue;

                int value = Mathf.RoundToInt(c.threshold);
                if (value < 1 || value > s_gestureNames.Length) continue;

                if (c.parameter == kGestureLeft) left.Add(value);
                else if (c.parameter == kGestureRight) right.Add(value);
            }
        }
    }
}
