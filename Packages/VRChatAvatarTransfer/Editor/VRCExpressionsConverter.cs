// Copyright (c) You-Ri, 2026

using System.Collections.Generic;
using Lilium.LiveStudio;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace Lilium.VRChatAvatarTransfer.Editor
{
    /// <summary>
    /// Ports a VRChat avatar's <see cref="VRCAvatarDescriptor.expressionsMenu"/> /
    /// <see cref="VRCAvatarDescriptor.expressionParameters"/> onto the VRCSDK-independent
    /// <see cref="VRCAvatar"/> component. Every Toggle / Button leaf Control becomes a single
    /// <see cref="VRCExpression"/> entry; SubMenu Controls are descended; Puppet Controls are
    /// skipped because they carry continuous multi-axis values that don't map cleanly to a
    /// single <c>AnimationParameterOverride</c>.
    ///
    /// Note: <c>VRCExpression.name</c> is read at runtime as a key into
    /// <c>expressionResolver.smoothedOutputs</c> (expected to match a FacialKey name). The
    /// menu Control names usually don't match FacialKey names, so the ported entries serve as
    /// a starter template — users are expected to rename them to facial keys to actually drive
    /// the avatar from face tracking. Must be called before the VRCAvatarDescriptor is stripped.
    /// </summary>
    internal static class VRCExpressionsConverter
    {
        // Mirrors the depth cap in VRCExpressionsMenuEditor.IsSubmenuRecursive to guard
        // against cycles and pathological submenu nesting.
        private const int kMaxDepth = 16;

        /// <returns>true when at least one VRCExpression was produced.</returns>
        public static bool Convert(GameObject avatarRoot, VRCAvatar target)
        {
            if (target == null) return false;

            var collected = Collect(avatarRoot);
            target.ConfigureExpressions(collected.ToArray());
            EditorUtility.SetDirty(target);
            return collected.Count > 0;
        }

        /// <summary>
        /// Walks <paramref name="avatarRoot"/>'s VRCExpressionsMenu / ExpressionParameters and
        /// returns the produced <see cref="VRCExpression"/> list (empty when unavailable). Shared
        /// by <see cref="Convert"/> (avatar) and the prop builder, which filters the result to the
        /// expressions whose parameters its own controller declares. Must be called before the
        /// VRCAvatarDescriptor is stripped.
        /// </summary>
        public static List<VRCExpression> Collect(GameObject avatarRoot)
        {
            var collected = new List<VRCExpression>();
            if (avatarRoot == null) return collected;

            var desc = avatarRoot.GetComponent<VRCAvatarDescriptor>();
            if (desc == null) return collected;

            var menu = desc.expressionsMenu;
            if (menu == null)
            {
                VRChatAvatarTransferLog.Info(
                    $"'{avatarRoot.name}': no ExpressionsMenu on VRCAvatarDescriptor; expressions not ported.");
                return collected;
            }

            var parameters = desc.expressionParameters;
            if (parameters == null || parameters.parameters == null)
            {
                VRChatAvatarTransferLog.Warn(
                    $"'{avatarRoot.name}': ExpressionsMenu exists but ExpressionParameters is unset; expressions not ported (parameter types unknown).");
                return collected;
            }

            // Parameter name -> value type (Int / Float / Bool). Used to decide which field
            // of AnimationParameterOverride receives Control.value.
            var paramTypes = new Dictionary<string, VRCExpressionParameters.ValueType>(parameters.parameters.Length);
            foreach (var p in parameters.parameters)
            {
                if (p == null || string.IsNullOrEmpty(p.name)) continue;
                paramTypes[p.name] = p.valueType;
            }

            var visited = new HashSet<VRCExpressionsMenu>();
            var stats = new ConvertStats();

            _Walk(menu, paramTypes, collected, visited, depth: 0, avatarRoot.name, stats);

            VRChatAvatarTransferLog.Info(
                $"'{avatarRoot.name}': collected {collected.Count} expression(s) from VRCExpressionsMenu "
                + $"(toggle={stats.toggle}, button={stats.button}, submenu={stats.submenu}, "
                + $"puppetSkipped={stats.puppetSkipped}, missingParam={stats.missingParam}).");
            return collected;
        }

        // Pass-by-reference counter so nested _Walk recursions accumulate into the same totals.
        private sealed class ConvertStats
        {
            public int toggle;
            public int button;
            public int submenu;
            public int puppetSkipped;
            public int missingParam;
        }

        private static void _Walk(
            VRCExpressionsMenu menu,
            Dictionary<string, VRCExpressionParameters.ValueType> paramTypes,
            List<VRCExpression> outList,
            HashSet<VRCExpressionsMenu> visited,
            int depth,
            string avatarName,
            ConvertStats stats)
        {
            if (menu == null) return;
            if (depth > kMaxDepth)
            {
                VRChatAvatarTransferLog.Warn(
                    $"'{avatarName}': ExpressionsMenu depth exceeded {kMaxDepth} at '{menu.name}'; descent stopped.");
                return;
            }
            if (!visited.Add(menu))
            {
                // Already expanded via another reference path; skipping avoids cycles and
                // also stops the same submenu from being flattened twice.
                return;
            }
            if (menu.controls == null) return;

            foreach (var control in menu.controls)
            {
                if (control == null) continue;

                switch (control.type)
                {
                    case VRCExpressionsMenu.Control.ControlType.Toggle:
                        if (_TryBuildExpression(control, paramTypes, avatarName, out var toggleExp))
                        {
                            outList.Add(toggleExp);
                            stats.toggle++;
                        }
                        else
                        {
                            stats.missingParam++;
                        }
                        break;

                    case VRCExpressionsMenu.Control.ControlType.Button:
                        // Treat Button identically to Toggle: write the target value while the
                        // expression is "active". The press-vs-hold distinction is not modeled
                        // by VRCAvatar.expressions, so this is the closest available mapping.
                        if (_TryBuildExpression(control, paramTypes, avatarName, out var buttonExp))
                        {
                            outList.Add(buttonExp);
                            stats.button++;
                        }
                        else
                        {
                            stats.missingParam++;
                        }
                        break;

                    case VRCExpressionsMenu.Control.ControlType.SubMenu:
                        stats.submenu++;
                        _Walk(control.subMenu, paramTypes, outList, visited, depth + 1, avatarName, stats);
                        break;

                    case VRCExpressionsMenu.Control.ControlType.RadialPuppet:
                    case VRCExpressionsMenu.Control.ControlType.TwoAxisPuppet:
                    case VRCExpressionsMenu.Control.ControlType.FourAxisPuppet:
                        VRChatAvatarTransferLog.Warn(
                            $"'{avatarName}': skipping Puppet control '{control.name}' ({control.type}); "
                            + "continuous multi-axis values cannot be expressed as a single AnimationParameterOverride.");
                        stats.puppetSkipped++;
                        break;
                }
            }
        }

        private static bool _TryBuildExpression(
            VRCExpressionsMenu.Control control,
            Dictionary<string, VRCExpressionParameters.ValueType> paramTypes,
            string avatarName,
            out VRCExpression result)
        {
            result = null;

            var paramName = control.parameter != null ? control.parameter.name : null;
            if (string.IsNullOrEmpty(paramName))
            {
                VRChatAvatarTransferLog.Warn(
                    $"'{avatarName}': control '{control.name}' has no parameter bound; skipped.");
                return false;
            }

            if (!paramTypes.TryGetValue(paramName, out var valueType))
            {
                VRChatAvatarTransferLog.Warn(
                    $"'{avatarName}': control '{control.name}' references parameter '{paramName}' "
                    + "that is not declared in ExpressionParameters; skipped.");
                return false;
            }

            var ov = new AnimationParameterOverride { name = paramName };
            switch (valueType)
            {
                case VRCExpressionParameters.ValueType.Bool:
                    ov.type = AnimatorControllerParameterType.Bool;
                    ov.boolValue = control.value != 0f;
                    break;
                case VRCExpressionParameters.ValueType.Int:
                    ov.type = AnimatorControllerParameterType.Int;
                    ov.intValue = Mathf.RoundToInt(control.value);
                    break;
                case VRCExpressionParameters.ValueType.Float:
                    ov.type = AnimatorControllerParameterType.Float;
                    ov.floatValue = control.value;
                    break;
                default:
                    VRChatAvatarTransferLog.Warn(
                        $"'{avatarName}': control '{control.name}' parameter '{paramName}' has unknown "
                        + $"ValueType '{valueType}'; skipped.");
                    return false;
            }

            result = new VRCExpression
            {
                name = control.name,
                parameters = new[] { ov },
            };
            return true;
        }
    }
}
