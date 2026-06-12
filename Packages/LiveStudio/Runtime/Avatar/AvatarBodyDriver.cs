// Copyright (c) You-Ri, 2026

using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Job input passed to <see cref="HumanoidPoseJob"/> through a single
    /// <see cref="NativeReference{T}"/>. Bundles the enable flag and the full
    /// humanoid pose so the worker thread reads one blittable value.
    /// </summary>
    public struct HumanoidPoseInput
    {
        /// <summary>1 = overwrite the controller pose with <see cref="pose"/>; 0 = pass through.</summary>
        public byte enabled;

        public HumanoidPoseData pose;
    }

    /// <summary>
    /// Animation job that blends the mocap pose over the upstream (AnimatorController)
    /// pose per bone, weighted by each bone's tracking presence. Runs downstream of the
    /// controller in the PlayableGraph, so what it writes wins over the controller's
    /// output. A bone with presence 1 takes the mocap rotation fully; presence 0 leaves
    /// the controller pose untouched (its animation flows through); values in between
    /// slerp from the controller pose toward the mocap pose. When the whole frame is
    /// disabled the controller pose (e.g. an idle/ROM clip) passes through entirely.
    /// </summary>
    public struct HumanoidPoseJob : IAnimationJob
    {
        public NativeArray<TransformStreamHandle> handles;
        public NativeArray<int> boneIndices;
        public int hipsHandleIndex;

        [ReadOnly] public NativeReference<HumanoidPoseInput> input;

        public void ProcessRootMotion(AnimationStream stream) { }

        public void ProcessAnimation(AnimationStream stream)
        {
            var data = input.Value;
            if (data.enabled == 0) return;

            for (int i = 0; i < handles.Length; i++)
            {
                int boneIndex = boneIndices[i];
                float presence = data.pose.AsPresence(boneIndex);
                if (presence <= 0f) continue; // untracked bone: keep the controller pose

                var mocapRotation = data.pose.AsRotation(boneIndex);
                if (presence >= 1f)
                {
                    handles[i].SetLocalRotation(stream, mocapRotation);
                }
                else
                {
                    var controllerRotation = handles[i].GetLocalRotation(stream);
                    handles[i].SetLocalRotation(stream, Quaternion.Slerp(controllerRotation, mocapRotation, presence));
                }
            }

            if (hipsHandleIndex >= 0)
            {
                float hipsPresence = data.pose.AsPresence((int)HumanBodyBones.Hips);
                if (hipsPresence >= 1f)
                {
                    handles[hipsHandleIndex].SetLocalPosition(stream, data.pose.hipPosition);
                }
                else if (hipsPresence > 0f)
                {
                    var controllerPosition = handles[hipsHandleIndex].GetLocalPosition(stream);
                    handles[hipsHandleIndex].SetLocalPosition(stream, Vector3.Lerp(controllerPosition, data.pose.hipPosition, hipsPresence));
                }
            }
        }
    }

    /// <summary>
    /// Shared body-animation driver for avatar components (VRM1Avatar / VRCFTAvatar).
    /// Owns the motion source reference, tracking state with mesh visibility, and a
    /// PlayableGraph that pipes an optional <see cref="AnimatorControllerPlayable"/>
    /// into a <see cref="HumanoidPoseJob"/>. The mocap pose overwrites the controller
    /// pose while tracking; on tracking loss the controller animation plays through.
    /// The root transform is written directly (not through the stream) via
    /// <see cref="AvatarAnimationSystem.UpdateRoot"/>.
    /// </summary>
    public sealed class AvatarBodyDriver : IDisposable
    {
        Animator _animator;
        Renderer[] _renderers;

        PlayableGraph _graph;
        AnimationScriptPlayable _posePlayable;
        AnimatorControllerPlayable _controllerPlayable;
        AnimationPlayableOutput _output;

        NativeArray<TransformStreamHandle> _handles;
        NativeArray<int> _boneIndices;
        NativeReference<HumanoidPoseInput> _poseInput;

        bool _hasControllerPlayable;
        bool _isGraphPlaying;
        bool _isTracking;

        /// <summary>Source of the per-frame avatar pose. Set by the owning component.</summary>
        public MotionSourceBase motionSource { get; set; }

        /// <summary>True while the motion source is providing valid frames.</summary>
        public bool isTracking => _isTracking;

        /// <summary>True when the animator had a runtime controller wrapped into the graph.</summary>
        public bool hasControllerPlayable => _hasControllerPlayable;

        /// <summary>
        /// The wrapped controller playable. Valid only when <see cref="hasControllerPlayable"/>.
        /// Owning components must write animator parameters here (Animator.SetFloat does
        /// not reach a controller wrapped inside a PlayableGraph).
        /// </summary>
        public AnimatorControllerPlayable controllerPlayable => _controllerPlayable;

        /// <summary>
        /// Builds the PlayableGraph and binds the humanoid bones. Call from Start
        /// (for VRM avatars, after Vrm10Instance.Runtime has reconstructed transforms).
        /// </summary>
        public void Initialize(Animator animator)
        {
            _animator = animator;
            _renderers = animator.GetComponentsInChildren<Renderer>(true);

            _graph = PlayableGraph.Create($"{animator.name}.AvatarBody");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);

            // Bind only the humanoid bones that exist, keeping the arrays compact.
            var handlesList = new List<TransformStreamHandle>((int)HumanBodyBones.LastBone);
            var indicesList = new List<int>((int)HumanBodyBones.LastBone);
            int hipsHandleIndex = -1;
            for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
            {
                var bone = animator.GetBoneTransform((HumanBodyBones)i);
                if (bone == null) continue;
                if (i == (int)HumanBodyBones.Hips) hipsHandleIndex = handlesList.Count;
                handlesList.Add(animator.BindStreamTransform(bone));
                indicesList.Add(i);
            }

            _handles = new NativeArray<TransformStreamHandle>(handlesList.ToArray(), Allocator.Persistent);
            _boneIndices = new NativeArray<int>(indicesList.ToArray(), Allocator.Persistent);
            _poseInput = new NativeReference<HumanoidPoseInput>(Allocator.Persistent);

            var job = new HumanoidPoseJob
            {
                handles = _handles,
                boneIndices = _boneIndices,
                hipsHandleIndex = hipsHandleIndex,
                input = _poseInput,
            };
            _posePlayable = AnimationScriptPlayable.Create(_graph, job);

            if (animator.runtimeAnimatorController != null)
            {
                _controllerPlayable = AnimatorControllerPlayable.Create(_graph, animator.runtimeAnimatorController);
                _posePlayable.AddInput(_controllerPlayable, 0, 1f);
                _hasControllerPlayable = true;
            }

            _output = AnimationPlayableOutput.Create(_graph, "Body", animator);
            _output.SetSourcePlayable(_posePlayable);

            // With a controller, keep the graph playing so its animation runs even
            // before/after tracking. Without one, stay stopped until the first valid
            // frame so the avatar keeps its imported pose instead of snapping to T-pose.
            if (_hasControllerPlayable)
            {
                PlayGraph();
            }
        }

        /// <summary>
        /// Per-frame body update. Handles tracking transitions and mesh visibility,
        /// writes the root transform directly, and hands the pose to the job.
        /// Returns true while tracking (the owning component should then apply its
        /// own facial/look-at processing).
        /// </summary>
        public bool Tick()
        {
            if (motionSource == null || !motionSource.frameData.isValid)
            {
                if (_isTracking)
                {
                    SetShowMeshes(false);
                    SetPoseEnabled(false);
                    // No controller to fall back to: freeze the last pose by stopping
                    // the graph (an empty stream would otherwise reset to T-pose).
                    if (!_hasControllerPlayable) StopGraph();
                }
                _isTracking = false;
                return false;
            }

            if (!_isTracking)
            {
                SetShowMeshes(true);
                if (!_isGraphPlaying) PlayGraph();
            }
            _isTracking = true;

            ref AvatarAnimationData frameData = ref motionSource.frameData;
            AvatarAnimationSystem.UpdateRoot(_animator.transform, in frameData.root);
            _poseInput.Value = new HumanoidPoseInput { enabled = 1, pose = frameData.pose };
            return true;
        }

        public void Dispose()
        {
            if (_graph.IsValid()) _graph.Destroy();
            if (_handles.IsCreated) _handles.Dispose();
            if (_boneIndices.IsCreated) _boneIndices.Dispose();
            if (_poseInput.IsCreated) _poseInput.Dispose();
        }

        void SetPoseEnabled(bool enabled)
        {
            if (!_poseInput.IsCreated) return;
            var value = _poseInput.Value;
            value.enabled = (byte)(enabled ? 1 : 0);
            _poseInput.Value = value;
        }

        void PlayGraph()
        {
            if (!_graph.IsValid()) return;
            _graph.Play();
            _isGraphPlaying = true;
        }

        void StopGraph()
        {
            if (!_graph.IsValid()) return;
            _graph.Stop();
            _isGraphPlaying = false;
        }

        void SetShowMeshes(bool visible)
        {
            if (_renderers == null) return;
            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] != null) _renderers[i].enabled = visible;
            }
        }
    }
}
