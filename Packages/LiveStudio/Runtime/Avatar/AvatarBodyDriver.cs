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
    /// PlayableGraph that pipes the avatar's <see cref="AnimatorControllerPlayable"/>
    /// into a <see cref="HumanoidPoseJob"/>. The mocap pose overwrites the controller
    /// pose while tracking; on tracking loss the controller animation plays through.
    /// The root transform is written directly (not through the stream) via
    /// <see cref="AvatarAnimationSystem.UpdateRoot"/>.
    ///
    /// Animator parameters must be read/written through this driver's accessors
    /// (<see cref="SetFloat"/> / <see cref="GetFloat"/> etc.); Animator.SetFloat/GetFloat
    /// do not reach a controller wrapped inside a PlayableGraph. The read accessors also
    /// let an external object bridge mirror the avatar's parameter values onto its own Animator.
    /// </summary>
    public sealed class AvatarBodyDriver : IDisposable
    {
        Animator _animator;
        Renderer[] _renderers;

        PlayableGraph _graph;
        AnimationScriptPlayable _posePlayable;
        AnimationPlayableOutput _output;

        // ラップした AnimatorController と、それが宣言するパラメータ nameHash 集合。
        // 単一コントローラだが、リスト構造はパラメータアクセサ実装をそのまま使えるよう残す。
        readonly List<AnimatorControllerPlayable> _controllerPlayables = new List<AnimatorControllerPlayable>();
        readonly List<HashSet<int>> _controllerParamHashes = new List<HashSet<int>>();

        NativeArray<TransformStreamHandle> _handles;
        NativeArray<int> _boneIndices;
        NativeReference<HumanoidPoseInput> _poseInput;

        bool _isGraphPlaying;
        bool _isTracking;

        // Previous face-tracking state for the show rising-edge detection in Tick().
        bool _prevFaceValid;

        /// <summary>Source of the per-frame avatar pose. Set by the owning component.</summary>
        public MotionSourceBase motionSource { get; set; }

        /// <summary>True while the motion source is providing valid frames.</summary>
        public bool isTracking => _isTracking;

        /// <summary>True when at least one runtime controller was wrapped into the graph.</summary>
        public bool hasControllerPlayable => _controllerPlayables.Count > 0;

        /// <summary>
        /// The wrapped controller playable. Valid only when <see cref="hasControllerPlayable"/>.
        /// Prefer the parameter accessors (<see cref="SetFloat"/> / <see cref="GetFloat"/> etc.).
        /// </summary>
        public AnimatorControllerPlayable controllerPlayable
            => _controllerPlayables.Count > 0 ? _controllerPlayables[0] : default;

        /// <summary>
        /// Builds the PlayableGraph and binds the humanoid bones. Call from Start
        /// (for VRM avatars, after Vrm10Instance.Runtime has reconstructed transforms).
        /// </summary>
        public void Initialize(Animator animator)
        {
            _animator = animator;
            _renderers = animator.GetComponentsInChildren<Renderer>(true);

            // controller は未トラッキング部位のアニメ流し込み専用。root はあくまで mocap が
            // 権威 (UpdateRoot で毎フレーム書く) なので、graph 内で再生される controller の
            // root motion がアバター全体を動かして沈めないよう無効化する。
            animator.applyRootMotion = false;

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

            _BuildController(animator);

            _output = AnimationPlayableOutput.Create(_graph, "Body", animator);
            _output.SetSourcePlayable(_posePlayable);

            // With a controller, keep the graph playing so its animation runs even
            // before/after tracking. Without one, stay stopped until the first valid
            // frame so the avatar keeps its imported pose instead of snapping to T-pose.
            if (hasControllerPlayable)
            {
                PlayGraph();
            }
        }

        /// <summary>
        /// Wraps the avatar's runtime controller in an <see cref="AnimatorControllerPlayable"/>,
        /// records its declared parameter hashes, and connects it into the pose job.
        /// </summary>
        void _BuildController(Animator animator)
        {
            if (animator.runtimeAnimatorController == null) return; // コントローラ無し（凍結経路へ）
            _AddControllerPlayable(animator.runtimeAnimatorController);
            _posePlayable.AddInput(_controllerPlayables[0], 0, 1f);
        }

        void _AddControllerPlayable(RuntimeAnimatorController controller)
        {
            var playable = AnimatorControllerPlayable.Create(_graph, controller);

            var hashes = new HashSet<int>();
            int paramCount = playable.GetParameterCount();
            for (int i = 0; i < paramCount; i++)
            {
                hashes.Add(playable.GetParameter(i).nameHash);
            }

            _controllerPlayables.Add(playable);
            _controllerParamHashes.Add(hashes);
        }

        /// <summary>
        /// Per-frame body update. Handles tracking transitions and mesh visibility,
        /// writes the root transform directly, and hands the pose to the job.
        /// Returns true while visible (the owning component should then apply its
        /// own facial/look-at processing).
        /// Visibility conditions (matching <see cref="AvatarVisibilityGate"/>): hide the
        /// moment BOTH body (MediaPipe) and face (ARKit) tracking are lost, show on the
        /// rising edge of face tracking. The body signal flickers frame-to-frame (its
        /// validity beats against the receive phase) so a per-frame isValid toggle would
        /// blink; the face signal is stable and anchors both transitions.
        /// </summary>
        public bool Tick()
        {
            bool bodyValid = motionSource != null && motionSource.frameData.bodyTracked;
            bool faceValid = motionSource != null && motionSource.frameData.faceTracked;

            if (_isTracking)
            {
                // Hide the moment both body (MediaPipe) and face (ARKit) tracking are lost.
                if (!bodyValid && !faceValid)
                {
                    SetShowMeshes(false);
                    SetPoseEnabled(false);
                    // No controller to fall back to: freeze the last pose by stopping
                    // the graph (an empty stream would otherwise reset to T-pose).
                    if (!hasControllerPlayable) StopGraph();
                    _isTracking = false;
                }
            }
            else
            {
                // Show the moment face (ARKit) tracking becomes valid.
                if (!_prevFaceValid && faceValid)
                {
                    SetShowMeshes(true);
                    if (!_isGraphPlaying) PlayGraph();
                    _isTracking = true;
                }
            }
            _prevFaceValid = faceValid;

            if (!_isTracking) return false;

            // While visible, only consume the frame when it is valid; on a momentary
            // invalid (body-blip) frame keep the last pose instead of writing stale data.
            if (motionSource.frameData.isValid)
            {
                ref AvatarAnimationData frameData = ref motionSource.frameData;
                AvatarAnimationSystem.UpdateRoot(_animator.transform, in frameData.root);
                _poseInput.Value = new HumanoidPoseInput { enabled = 1, pose = frameData.pose };
            }
            return true;
        }

        public void Dispose()
        {
            if (_graph.IsValid()) _graph.Destroy();
            if (_handles.IsCreated) _handles.Dispose();
            if (_boneIndices.IsCreated) _boneIndices.Dispose();
            if (_poseInput.IsCreated) _poseInput.Dispose();
            _controllerPlayables.Clear();
            _controllerParamHashes.Clear();
        }

        //----------------------------------------------------------------------
        // Animator パラメータのブロードキャスト
        // 同名パラメータを宣言する全コントローラへ書き込み、読み取りは最初の宣言から行う。
        // 未宣言のコントローラへ書くと Unity が警告を出すため Contains で事前判定する。
        //----------------------------------------------------------------------

        /// <summary>いずれかのコントローラが指定 nameHash のパラメータを宣言していれば true。</summary>
        public bool HasParameter(int nameHash)
        {
            for (int i = 0; i < _controllerParamHashes.Count; i++)
            {
                if (_controllerParamHashes[i].Contains(nameHash)) return true;
            }
            return false;
        }

        /// <summary>名前で最初に一致したパラメータ定義を返す。</summary>
        public bool TryGetParameter(string name, out AnimatorControllerParameter result)
        {
            for (int i = 0; i < _controllerPlayables.Count; i++)
            {
                var ctrl = _controllerPlayables[i];
                int count = ctrl.GetParameterCount();
                for (int j = 0; j < count; j++)
                {
                    var p = ctrl.GetParameter(j);
                    if (p.name == name)
                    {
                        result = p;
                        return true;
                    }
                }
            }
            result = default;
            return false;
        }

        public void SetFloat(int nameHash, float value)
        {
            for (int i = 0; i < _controllerPlayables.Count; i++)
            {
                if (_controllerParamHashes[i].Contains(nameHash))
                    _controllerPlayables[i].SetFloat(nameHash, value);
            }
        }

        public void SetInteger(int nameHash, int value)
        {
            for (int i = 0; i < _controllerPlayables.Count; i++)
            {
                if (_controllerParamHashes[i].Contains(nameHash))
                    _controllerPlayables[i].SetInteger(nameHash, value);
            }
        }

        public void SetBool(int nameHash, bool value)
        {
            for (int i = 0; i < _controllerPlayables.Count; i++)
            {
                if (_controllerParamHashes[i].Contains(nameHash))
                    _controllerPlayables[i].SetBool(nameHash, value);
            }
        }

        public float GetFloat(int nameHash)
        {
            for (int i = 0; i < _controllerPlayables.Count; i++)
            {
                if (_controllerParamHashes[i].Contains(nameHash))
                    return _controllerPlayables[i].GetFloat(nameHash);
            }
            return 0f;
        }

        public int GetInteger(int nameHash)
        {
            for (int i = 0; i < _controllerPlayables.Count; i++)
            {
                if (_controllerParamHashes[i].Contains(nameHash))
                    return _controllerPlayables[i].GetInteger(nameHash);
            }
            return 0;
        }

        public bool GetBool(int nameHash)
        {
            for (int i = 0; i < _controllerPlayables.Count; i++)
            {
                if (_controllerParamHashes[i].Contains(nameHash))
                    return _controllerPlayables[i].GetBool(nameHash);
            }
            return false;
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
