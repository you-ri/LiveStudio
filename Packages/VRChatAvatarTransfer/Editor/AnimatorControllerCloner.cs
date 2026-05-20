// Copyright (c) You-Ri, 2026

using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Lilium.VRChatAvatarTransfer.Editor
{
    /// <summary>
    /// Deep-clones an <see cref="AnimatorController"/> into a fresh standalone .controller asset
    /// at <c>dstPath</c>, recreating parameters, layers, state machines, states, transitions,
    /// state-machine behaviours, and BlendTree motions in the destination asset.
    ///
    /// Why: when the source controller is a sub-asset of a multi-controller container
    /// (custom asset whose main object is not the AnimatorController and which holds several
    /// AnimatorControllers as sub-assets), <see cref="AssetDatabase.CopyAsset"/> on the container
    /// has been observed to lose transitions on the copied controllers. Building the destination
    /// controller from scratch via Unity's AnimatorController API avoids that and yields a clean
    /// standalone .controller that can be mutated freely (e.g. by behaviour converters) without
    /// touching the source.
    /// </summary>
    internal static class AnimatorControllerCloner
    {
        public static AnimatorController CloneToPath(AnimatorController source, string dstPath)
        {
            if (AssetDatabase.LoadMainAssetAtPath(dstPath) != null)
            {
                AssetDatabase.DeleteAsset(dstPath);
            }
            var dst = AnimatorController.CreateAnimatorControllerAtPath(dstPath);
            // CreateAnimatorControllerAtPath adds a default empty "Base Layer". Clear it and rebuild.
            dst.layers = new AnimatorControllerLayer[0];

            // Parameters (struct-like value class — direct field copy).
            var srcParams = source.parameters;
            var dstParams = new AnimatorControllerParameter[srcParams.Length];
            for (int i = 0; i < srcParams.Length; i++)
            {
                var p = srcParams[i];
                dstParams[i] = new AnimatorControllerParameter
                {
                    name = p.name,
                    type = p.type,
                    defaultBool = p.defaultBool,
                    defaultFloat = p.defaultFloat,
                    defaultInt = p.defaultInt,
                };
            }
            dst.parameters = dstParams;

            // Layers: clone each layer's state-machine tree, then wire transitions (2-phase to allow
            // forward / cross-state references to be remapped via the src -> dst maps).
            var srcLayers = source.layers;
            var dstLayers = new AnimatorControllerLayer[srcLayers.Length];
            for (int i = 0; i < srcLayers.Length; i++)
            {
                var sl = srcLayers[i];
                var stateMap = new Dictionary<AnimatorState, AnimatorState>();
                var smMap = new Dictionary<AnimatorStateMachine, AnimatorStateMachine>();
                var dstSm = _CloneStateMachineStructure(sl.stateMachine, dst, stateMap, smMap);
                _BuildTransitions(sl.stateMachine, dst, stateMap, smMap);

                dstLayers[i] = new AnimatorControllerLayer
                {
                    name = sl.name,
                    defaultWeight = sl.defaultWeight,
                    blendingMode = sl.blendingMode,
                    iKPass = sl.iKPass,
                    syncedLayerIndex = sl.syncedLayerIndex,
                    syncedLayerAffectsTiming = sl.syncedLayerAffectsTiming,
                    avatarMask = sl.avatarMask,
                    stateMachine = dstSm,
                };
            }
            dst.layers = dstLayers;

            EditorUtility.SetDirty(dst);
            AssetDatabase.SaveAssets();
            return dst;
        }

        // --- Phase 1: clone state machines, states, behaviours, motions. No transitions yet. ---

        private static AnimatorStateMachine _CloneStateMachineStructure(
            AnimatorStateMachine src,
            AnimatorController owner,
            Dictionary<AnimatorState, AnimatorState> stateMap,
            Dictionary<AnimatorStateMachine, AnimatorStateMachine> smMap)
        {
            var dst = new AnimatorStateMachine
            {
                name = src.name,
                hideFlags = src.hideFlags,
                anyStatePosition = src.anyStatePosition,
                entryPosition = src.entryPosition,
                exitPosition = src.exitPosition,
                parentStateMachinePosition = src.parentStateMachinePosition,
            };
            AssetDatabase.AddObjectToAsset(dst, owner);
            smMap[src] = dst;

            // States
            var srcStates = src.states;
            var dstChildStates = new ChildAnimatorState[srcStates.Length];
            for (int i = 0; i < srcStates.Length; i++)
            {
                var cs = srcStates[i];
                var ss = cs.state;
                AnimatorState ds = null;
                if (ss != null)
                {
                    ds = _CloneState(ss, owner);
                    stateMap[ss] = ds;
                }
                dstChildStates[i] = new ChildAnimatorState { state = ds, position = cs.position };
            }
            dst.states = dstChildStates;

            // Child state machines (recurse)
            var srcChildSms = src.stateMachines;
            var dstChildSms = new ChildAnimatorStateMachine[srcChildSms.Length];
            for (int i = 0; i < srcChildSms.Length; i++)
            {
                var c = srcChildSms[i];
                AnimatorStateMachine dc = null;
                if (c.stateMachine != null)
                {
                    dc = _CloneStateMachineStructure(c.stateMachine, owner, stateMap, smMap);
                }
                dstChildSms[i] = new ChildAnimatorStateMachine { stateMachine = dc, position = c.position };
            }
            dst.stateMachines = dstChildSms;

            // Behaviours on the state machine itself
            _AddClonedBehaviours(src.behaviours, dst);

            // defaultState is one of this SM's own states; stateMap has it at this point.
            if (src.defaultState != null && stateMap.TryGetValue(src.defaultState, out var ds2))
            {
                dst.defaultState = ds2;
            }
            return dst;
        }

        private static AnimatorState _CloneState(AnimatorState src, AnimatorController owner)
        {
            var dst = new AnimatorState
            {
                name = src.name,
                hideFlags = src.hideFlags,
                speed = src.speed,
                cycleOffset = src.cycleOffset,
                iKOnFeet = src.iKOnFeet,
                writeDefaultValues = src.writeDefaultValues,
                mirror = src.mirror,
                speedParameterActive = src.speedParameterActive,
                mirrorParameterActive = src.mirrorParameterActive,
                cycleOffsetParameterActive = src.cycleOffsetParameterActive,
                timeParameterActive = src.timeParameterActive,
                speedParameter = src.speedParameter,
                mirrorParameter = src.mirrorParameter,
                cycleOffsetParameter = src.cycleOffsetParameter,
                timeParameter = src.timeParameter,
                tag = src.tag,
                motion = _CloneMotion(src.motion, owner),
            };
            AssetDatabase.AddObjectToAsset(dst, owner);
            _AddClonedBehaviours(src.behaviours, dst);
            return dst;
        }

        // BlendTrees are sub-assets owned by the controller and must be deep-cloned.
        // AnimationClips and AvatarMasks are external assets — share reference.
        private static Motion _CloneMotion(Motion src, AnimatorController owner)
        {
            if (src == null) return null;
            if (!(src is BlendTree srcBt)) return src; // AnimationClip etc. -> shared ref

            var dstBt = new BlendTree
            {
                name = srcBt.name,
                hideFlags = srcBt.hideFlags,
                blendType = srcBt.blendType,
                blendParameter = srcBt.blendParameter,
                blendParameterY = srcBt.blendParameterY,
                minThreshold = srcBt.minThreshold,
                maxThreshold = srcBt.maxThreshold,
                useAutomaticThresholds = srcBt.useAutomaticThresholds,
            };
            AssetDatabase.AddObjectToAsset(dstBt, owner);

            var srcChildren = srcBt.children;
            var dstChildren = new ChildMotion[srcChildren.Length];
            for (int i = 0; i < srcChildren.Length; i++)
            {
                var c = srcChildren[i];
                dstChildren[i] = new ChildMotion
                {
                    motion = _CloneMotion(c.motion, owner),
                    threshold = c.threshold,
                    position = c.position,
                    timeScale = c.timeScale,
                    cycleOffset = c.cycleOffset,
                    directBlendParameter = c.directBlendParameter,
                    mirror = c.mirror,
                };
            }
            dstBt.children = dstChildren;
            return dstBt;
        }

        // StateMachineBehaviour は AnimatorState.behaviours / AnimatorStateMachine.behaviours の
        // 配列直代入では controller のサブアセットとして正しく登録されず、AvatarParameterDriverConverter
        // 等の後段が見つけられない。Add*StateMachineBehaviour(type) API で 1 つずつ追加して
        // EditorUtility.CopySerialized でフィールドを移植する。
        private static void _AddClonedBehaviours(StateMachineBehaviour[] src, AnimatorState dstState)
        {
            if (src == null) return;
            foreach (var s in src)
            {
                if (s == null) continue;
                var added = dstState.AddStateMachineBehaviour(s.GetType());
                if (added == null) continue;
                EditorUtility.CopySerialized(s, added);
                added.name = s.name;
                added.hideFlags = s.hideFlags;
            }
        }

        private static void _AddClonedBehaviours(StateMachineBehaviour[] src, AnimatorStateMachine dstSm)
        {
            if (src == null) return;
            foreach (var s in src)
            {
                if (s == null) continue;
                var added = dstSm.AddStateMachineBehaviour(s.GetType());
                if (added == null) continue;
                EditorUtility.CopySerialized(s, added);
                added.name = s.name;
                added.hideFlags = s.hideFlags;
            }
        }

        // --- Phase 2: rebuild transitions on the dst tree, remapping refs via the maps. ---

        private static void _BuildTransitions(
            AnimatorStateMachine src,
            AnimatorController owner,
            Dictionary<AnimatorState, AnimatorState> stateMap,
            Dictionary<AnimatorStateMachine, AnimatorStateMachine> smMap)
        {
            var dstSm = smMap[src];

            // State-level transitions
            foreach (var cs in src.states)
            {
                var ss = cs.state;
                if (ss == null) continue;
                var ds = stateMap[ss];
                foreach (var t in ss.transitions)
                {
                    _AddStateTransition(ds, t, stateMap, smMap);
                }
            }

            // Any-state transitions
            foreach (var t in src.anyStateTransitions)
            {
                _AddAnyStateTransition(dstSm, t, stateMap, smMap);
            }

            // Entry transitions
            foreach (var t in src.entryTransitions)
            {
                _AddEntryTransition(dstSm, t, stateMap, smMap);
            }

            // Child-state-machine transitions (stored on the parent, keyed by child SM)
            foreach (var c in src.stateMachines)
            {
                var childSrc = c.stateMachine;
                if (childSrc == null) continue;
                var childDst = smMap[childSrc];
                var stmTransitions = src.GetStateMachineTransitions(childSrc);
                if (stmTransitions != null)
                {
                    foreach (var t in stmTransitions)
                    {
                        _AddStateMachineTransition(dstSm, childDst, t, stateMap, smMap);
                    }
                }
                // Recurse into the child to wire its own transitions.
                _BuildTransitions(childSrc, owner, stateMap, smMap);
            }
        }

        private static void _AddStateTransition(
            AnimatorState dstState,
            AnimatorStateTransition srcT,
            Dictionary<AnimatorState, AnimatorState> stateMap,
            Dictionary<AnimatorStateMachine, AnimatorStateMachine> smMap)
        {
            AnimatorStateTransition newT;
            if (srcT.isExit)
            {
                newT = dstState.AddExitTransition();
            }
            else if (srcT.destinationStateMachine != null && smMap.TryGetValue(srcT.destinationStateMachine, out var dsm))
            {
                newT = dstState.AddTransition(dsm);
            }
            else if (srcT.destinationState != null && stateMap.TryGetValue(srcT.destinationState, out var ds))
            {
                newT = dstState.AddTransition(ds);
            }
            else
            {
                // Floating transition (rare). Create as exit-stub then clear isExit below.
                newT = dstState.AddExitTransition();
                newT.isExit = false;
            }
            _CopyStateTransitionFields(srcT, newT, stateMap, smMap);
        }

        private static void _AddAnyStateTransition(
            AnimatorStateMachine dstSm,
            AnimatorStateTransition srcT,
            Dictionary<AnimatorState, AnimatorState> stateMap,
            Dictionary<AnimatorStateMachine, AnimatorStateMachine> smMap)
        {
            AnimatorStateTransition newT;
            if (srcT.destinationStateMachine != null && smMap.TryGetValue(srcT.destinationStateMachine, out var dsm))
            {
                newT = dstSm.AddAnyStateTransition(dsm);
            }
            else if (srcT.destinationState != null && stateMap.TryGetValue(srcT.destinationState, out var ds))
            {
                newT = dstSm.AddAnyStateTransition(ds);
            }
            else
            {
                newT = dstSm.AddAnyStateTransition((AnimatorState)null);
            }
            _CopyStateTransitionFields(srcT, newT, stateMap, smMap);
        }

        private static void _AddEntryTransition(
            AnimatorStateMachine dstSm,
            AnimatorTransition srcT,
            Dictionary<AnimatorState, AnimatorState> stateMap,
            Dictionary<AnimatorStateMachine, AnimatorStateMachine> smMap)
        {
            AnimatorTransition newT;
            if (srcT.destinationStateMachine != null && smMap.TryGetValue(srcT.destinationStateMachine, out var dsm))
            {
                newT = dstSm.AddEntryTransition(dsm);
            }
            else if (srcT.destinationState != null && stateMap.TryGetValue(srcT.destinationState, out var ds))
            {
                newT = dstSm.AddEntryTransition(ds);
            }
            else
            {
                newT = dstSm.AddEntryTransition((AnimatorState)null);
            }
            _CopyBaseTransitionFields(srcT, newT);
        }

        private static void _AddStateMachineTransition(
            AnimatorStateMachine parentDst,
            AnimatorStateMachine childDst,
            AnimatorTransition srcT,
            Dictionary<AnimatorState, AnimatorState> stateMap,
            Dictionary<AnimatorStateMachine, AnimatorStateMachine> smMap)
        {
            AnimatorTransition newT;
            if (srcT.destinationStateMachine != null && smMap.TryGetValue(srcT.destinationStateMachine, out var dsm))
            {
                newT = parentDst.AddStateMachineTransition(childDst, dsm);
            }
            else if (srcT.destinationState != null && stateMap.TryGetValue(srcT.destinationState, out var ds))
            {
                newT = parentDst.AddStateMachineTransition(childDst);
                newT.destinationState = ds;
            }
            else
            {
                newT = parentDst.AddStateMachineTransition(childDst);
            }
            _CopyBaseTransitionFields(srcT, newT);
        }

        private static void _CopyStateTransitionFields(
            AnimatorStateTransition srcT,
            AnimatorStateTransition dstT,
            Dictionary<AnimatorState, AnimatorState> stateMap,
            Dictionary<AnimatorStateMachine, AnimatorStateMachine> smMap)
        {
            // Refs were set by the Add* call but re-affirm in case the chosen overload differed.
            dstT.destinationState = (srcT.destinationState != null && stateMap.TryGetValue(srcT.destinationState, out var ds)) ? ds : null;
            dstT.destinationStateMachine = (srcT.destinationStateMachine != null && smMap.TryGetValue(srcT.destinationStateMachine, out var dsm)) ? dsm : null;

            dstT.duration = srcT.duration;
            dstT.exitTime = srcT.exitTime;
            dstT.hasExitTime = srcT.hasExitTime;
            dstT.hasFixedDuration = srcT.hasFixedDuration;
            dstT.interruptionSource = srcT.interruptionSource;
            dstT.offset = srcT.offset;
            dstT.orderedInterruption = srcT.orderedInterruption;
            dstT.canTransitionToSelf = srcT.canTransitionToSelf;
            dstT.isExit = srcT.isExit;
            _CopyBaseTransitionFields(srcT, dstT);
        }

        private static void _CopyBaseTransitionFields(AnimatorTransitionBase srcT, AnimatorTransitionBase dstT)
        {
            dstT.solo = srcT.solo;
            dstT.mute = srcT.mute;
            dstT.name = srcT.name;
            var srcConds = srcT.conditions;
            var dstConds = new AnimatorCondition[srcConds.Length];
            for (int i = 0; i < srcConds.Length; i++)
            {
                dstConds[i] = new AnimatorCondition
                {
                    mode = srcConds[i].mode,
                    parameter = srcConds[i].parameter,
                    threshold = srcConds[i].threshold,
                };
            }
            dstT.conditions = dstConds;
        }
    }
}
