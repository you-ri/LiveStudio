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
        /// <summary>1 = overwrite the pose with <see cref="pose"/>; 0 = pass through (base pose only).</summary>
        public byte enabled;

        public HumanoidPoseData pose;
    }

    /// <summary>
    /// Animation job that blends the mocap pose over a per-bone base pose, weighted by each
    /// bone's tracking presence. Presence 1 takes the mocap rotation fully; presence 0 takes
    /// the base pose; values in between slerp between them.
    ///
    /// The base pose comes from one of two sources per bone:
    /// - an override clip pre-sampled into transform space (<see cref="basePose"/>, when
    ///   <see cref="hasBasePose"/> is 1). Humanoid clips animate in muscle space, which does
    ///   NOT flow into the transform stream that this job reads, so the clip is sampled to
    ///   local bone rotations up front and written explicitly here — the same reliable path
    ///   as the mocap write, so untracked bones actually show the clip pose.
    /// - otherwise the upstream stream (e.g. an AnimatorController's pose), read via
    ///   GetLocalRotation, matching the original controller-passthrough behavior.
    /// </summary>
    public struct HumanoidPoseJob : IAnimationJob
    {
        public NativeArray<TransformStreamHandle> handles;
        public NativeArray<int> boneIndices;
        public int hipsHandleIndex;

        [ReadOnly] public NativeReference<HumanoidPoseInput> input;

        // 上書きクリップをサンプリングした基準姿勢 (handles と同順・同長)。hasBasePose==1 のとき有効。
        [ReadOnly] public NativeArray<Quaternion> basePose;
        [ReadOnly] public NativeReference<byte> hasBasePose;
        [ReadOnly] public NativeReference<Vector3> baseHipPosition;

        public void ProcessRootMotion(AnimationStream stream) { }

        public void ProcessAnimation(AnimationStream stream)
        {
            var data = input.Value;
            bool useBase = hasBasePose.Value == 1;

            // トラッキング無効フレーム: 基準姿勢があれば全身をそれにする。無ければ上流をパススルー。
            if (data.enabled == 0)
            {
                if (useBase)
                {
                    for (int i = 0; i < handles.Length; i++)
                        handles[i].SetLocalRotation(stream, basePose[i]);
                    if (hipsHandleIndex >= 0)
                        handles[hipsHandleIndex].SetLocalPosition(stream, baseHipPosition.Value);
                }
                return;
            }

            for (int i = 0; i < handles.Length; i++)
            {
                int boneIndex = boneIndices[i];
                float presence = data.pose.AsPresence(boneIndex);

                if (presence <= 0f)
                {
                    // 未トラッキング: 基準姿勢で明示的に上書き (基準姿勢が無ければ上流のまま)。
                    if (useBase) handles[i].SetLocalRotation(stream, basePose[i]);
                    continue;
                }

                var mocapRotation = data.pose.AsRotation(boneIndex);
                if (presence >= 1f)
                {
                    handles[i].SetLocalRotation(stream, mocapRotation);
                }
                else
                {
                    var baseRotation = useBase ? basePose[i] : handles[i].GetLocalRotation(stream);
                    handles[i].SetLocalRotation(stream, Quaternion.Slerp(baseRotation, mocapRotation, presence));
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
                    var basePos = useBase ? baseHipPosition.Value : handles[hipsHandleIndex].GetLocalPosition(stream);
                    handles[hipsHandleIndex].SetLocalPosition(stream, Vector3.Lerp(basePos, data.pose.hipPosition, hipsPresence));
                }
                else if (useBase)
                {
                    handles[hipsHandleIndex].SetLocalPosition(stream, baseHipPosition.Value);
                }
            }
        }
    }

    /// <summary>
    /// Shared body-animation driver for avatar components (VRM1Avatar / VRCFTAvatar).
    /// Owns the motion source reference, tracking state with mesh visibility, and a
    /// PlayableGraph that pipes the avatar's <see cref="AnimatorControllerPlayable"/>
    /// (if any) into a <see cref="HumanoidPoseJob"/>. The mocap pose overwrites the
    /// upstream pose while tracking; untracked bones fall back to the override clip
    /// (pre-sampled to a base pose) or the controller pose. The root transform is written
    /// directly (not through the stream) via <see cref="AvatarAnimationSystem.UpdateRoot"/>.
    ///
    /// Untracked-part override: a humanoid <see cref="AnimationClip"/> animates in muscle
    /// space, which is NOT resolved into the transform stream the job reads. So the clip is
    /// sampled once (<see cref="_SampleBasePose"/>) into per-bone local rotations and the job
    /// writes those explicitly for untracked bones — reusing the exact mechanism that already
    /// drives tracked bones. The clip is therefore treated as a static pose (sampled at t=0).
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

        // 未トラッキング部位の姿勢を上書きするアニメ (待機/基本ポーズ等)。ヒューマノイドクリップは
        // muscle 空間で動きストリームの transform ハンドルには乗らないため、bind 済みボーンへ一度
        // サンプリング (_SampleBasePose) して basePose に焼き、job が明示的に書き込む。
        AnimationClip _overrideClip;

        NativeArray<TransformStreamHandle> _handles;
        NativeArray<int> _boneIndices;
        Transform[] _boneTransforms; // _handles と同順。基準姿勢サンプリングでボーンを読むために保持。
        int _hipsHandleIndex = -1;
        NativeReference<HumanoidPoseInput> _poseInput;

        // 上書きクリップから焼いた基準姿勢。job と共有する。
        NativeArray<Quaternion> _basePose;
        NativeReference<byte> _hasBasePose;
        NativeReference<Vector3> _baseHipPosition;

        bool _isGraphPlaying;
        bool _isTracking;

        // Previous face-tracking state for the show rising-edge detection in Tick().
        bool _prevFaceValid;

        // 調査用: presence ログの間引きカウンタ。
        int _tickLogCountdown;

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

        /// <summary>controller か override clip の基準姿勢のいずれかが姿勢を供給するか。</summary>
        bool _HasPoseSource => _controllerPlayables.Count > 0 || (_hasBasePose.IsCreated && _hasBasePose.Value == 1);

        /// <summary>
        /// 未トラッキング部位を上書きするアニメクリップ。<see cref="Initialize"/> に渡すか
        /// <see cref="SetOverrideClip"/> で実行時に差し替える。null で controller / 凍結経路へ戻る。
        /// </summary>
        public AnimationClip overrideClip => _overrideClip;

        /// <summary>
        /// Builds the PlayableGraph and binds the humanoid bones. Call from Start
        /// (for VRM avatars, after Vrm10Instance.Runtime has reconstructed transforms).
        /// </summary>
        public void Initialize(Animator animator, AnimationClip overrideClip = null)
        {
            _animator = animator;
            _renderers = animator.GetComponentsInChildren<Renderer>(true);
            _overrideClip = overrideClip;

            // controller は未トラッキング部位のアニメ流し込み専用。root はあくまで mocap が
            // 権威 (UpdateRoot で毎フレーム書く) なので、graph 内で再生される controller の
            // root motion がアバター全体を動かして沈めないよう無効化する。
            animator.applyRootMotion = false;

            _graph = PlayableGraph.Create($"{animator.name}.AvatarBody");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);

            // Bind only the humanoid bones that exist, keeping the arrays compact.
            var handlesList = new List<TransformStreamHandle>((int)HumanBodyBones.LastBone);
            var indicesList = new List<int>((int)HumanBodyBones.LastBone);
            var transformsList = new List<Transform>((int)HumanBodyBones.LastBone);
            _hipsHandleIndex = -1;
            for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
            {
                var bone = animator.GetBoneTransform((HumanBodyBones)i);
                if (bone == null) continue;
                if (i == (int)HumanBodyBones.Hips) _hipsHandleIndex = handlesList.Count;
                handlesList.Add(animator.BindStreamTransform(bone));
                indicesList.Add(i);
                transformsList.Add(bone);
            }

            _handles = new NativeArray<TransformStreamHandle>(handlesList.ToArray(), Allocator.Persistent);
            _boneIndices = new NativeArray<int>(indicesList.ToArray(), Allocator.Persistent);
            _boneTransforms = transformsList.ToArray();
            _poseInput = new NativeReference<HumanoidPoseInput>(Allocator.Persistent);

            _basePose = new NativeArray<Quaternion>(_handles.Length, Allocator.Persistent);
            _hasBasePose = new NativeReference<byte>(Allocator.Persistent);
            _baseHipPosition = new NativeReference<Vector3>(Allocator.Persistent);

            // 上書きクリップを基準姿勢へ焼く (存在すれば _hasBasePose=1)。
            _SampleBasePose();

            var job = new HumanoidPoseJob
            {
                handles = _handles,
                boneIndices = _boneIndices,
                hipsHandleIndex = _hipsHandleIndex,
                input = _poseInput,
                basePose = _basePose,
                hasBasePose = _hasBasePose,
                baseHipPosition = _baseHipPosition,
            };
            _posePlayable = AnimationScriptPlayable.Create(_graph, job);
            _posePlayable.SetInputCount(1);

            _BuildController(animator);

            _output = AnimationPlayableOutput.Create(_graph, "Body", animator);
            _output.SetSourcePlayable(_posePlayable);

            // 調査用ログ: リグ構成と基準姿勢の焼き込み結果を確認する。
            var hipsBone = _HipsTransform();
            Debug.Log($"[AvatarBodyDriver] Initialize: animator={animator.name}, avatar={(animator.avatar != null ? animator.avatar.name : "null")}, isHuman={animator.isHuman}, bones={_handles.Length}, hipsBone={(hipsBone != null ? hipsBone.name : "null")}, controller={hasControllerPlayable}, overrideClip={(_overrideClip != null ? _overrideClip.name : "null")}, hasBasePose={_hasBasePose.Value}");

            // With a pose source (controller or a sampled override pose), keep the graph playing
            // so the job runs even before/after tracking. Without one, stay stopped until the
            // first valid frame so the avatar keeps its imported pose instead of snapping to T-pose.
            if (_HasPoseSource)
            {
                PlayGraph();
            }
        }

        /// <summary>
        /// Wraps the avatar's runtime controller (if any) into an
        /// <see cref="AnimatorControllerPlayable"/> and connects it into the pose job.
        /// </summary>
        void _BuildController(Animator animator)
        {
            if (animator.runtimeAnimatorController == null) return; // コントローラ無し（凍結/基準姿勢経路）
            _AddControllerPlayable(animator.runtimeAnimatorController);
            _graph.Connect(_controllerPlayables[0], 0, _posePlayable, 0);
            _posePlayable.SetInputWeight(0, 1f);
        }

        /// <summary>
        /// 未トラッキング部位を上書きするクリップを実行時に差し替える。クリップを基準姿勢へ
        /// 焼き直すだけで graph 構造は変えない。<see cref="Initialize"/> 前は値だけ保持する。
        /// </summary>
        public void SetOverrideClip(AnimationClip clip)
        {
            if (_overrideClip == clip) return;
            _overrideClip = clip;

            // 調査用ログ
            Debug.Log($"[AvatarBodyDriver] SetOverrideClip: {(clip != null ? clip.name : "null")}, graphValid={_graph.IsValid()}");

            if (!_graph.IsValid()) return; // Initialize 前。次の Initialize で反映される。

            _SampleBasePose();

            // 基準姿勢ができたら job が姿勢を書けるよう graph 再生を保証する。
            if (_HasPoseSource && !_isGraphPlaying) PlayGraph();
        }

        /// <summary>
        /// 上書きクリップの t=0 の姿勢を bind 済みボーンのローカル回転へサンプリングして
        /// <see cref="_basePose"/> に焼く。ヒューマノイド muscle→transform 解決を SampleAnimation で
        /// 行い、その後に元の姿勢へ復元する (一時的なポーズを残さない)。クリップが無ければ
        /// <see cref="_hasBasePose"/> を 0 にする。
        /// </summary>
        void _SampleBasePose()
        {
            if (!_hasBasePose.IsCreated) return;

            if (_overrideClip == null || _boneTransforms == null || !_basePose.IsCreated)
            {
                _hasBasePose.Value = 0;
                return;
            }

            int n = _boneTransforms.Length;

            // 現在のローカル姿勢を退避 (サンプリングでボーンが上書きされるため)。
            var savedRot = new Quaternion[n];
            for (int i = 0; i < n; i++)
                savedRot[i] = _boneTransforms[i] != null ? _boneTransforms[i].localRotation : Quaternion.identity;
            Vector3 savedHip = _HipsTransform() != null ? _HipsTransform().localPosition : Vector3.zero;
            var rootT = _animator.transform;
            var savedRootPos = rootT.localPosition;
            var savedRootRot = rootT.localRotation;

            // ヒューマノイドクリップ → ボーン transform へ解決させる。
            _overrideClip.SampleAnimation(_animator.gameObject, 0f);

            for (int i = 0; i < n; i++)
                _basePose[i] = _boneTransforms[i] != null ? _boneTransforms[i].localRotation : Quaternion.identity;
            if (_HipsTransform() != null)
                _baseHipPosition.Value = _HipsTransform().localPosition;

            // 退避した姿勢へ復元。
            for (int i = 0; i < n; i++)
                if (_boneTransforms[i] != null) _boneTransforms[i].localRotation = savedRot[i];
            if (_HipsTransform() != null) _HipsTransform().localPosition = savedHip;
            rootT.localPosition = savedRootPos;
            rootT.localRotation = savedRootRot;

            _hasBasePose.Value = 1;

            // 調査用ログ: サンプリングで実際にボーンが動いたか。変化 0 なら SampleAnimation が
            // このリグ (ControlRig 等) に効いていない = 基準姿勢が bind のまま、が確定する。
            int changed = 0;
            for (int i = 0; i < n; i++)
                if (_boneTransforms[i] != null && Quaternion.Angle(savedRot[i], _basePose[i]) > 0.5f) changed++;
            Debug.Log($"[AvatarBodyDriver] 基準姿勢サンプリング: clip={_overrideClip.name}, 変化ボーン数={changed}/{n}, baseHip={_baseHipPosition.Value:F3}");
        }

        Transform _HipsTransform()
            => (_hipsHandleIndex >= 0 && _boneTransforms != null && _hipsHandleIndex < _boneTransforms.Length)
                ? _boneTransforms[_hipsHandleIndex] : null;

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
                    // No pose source to fall back to: freeze the last pose by stopping
                    // the graph (an empty stream would otherwise reset to T-pose). With a
                    // controller or override base pose, keep playing so it drives the whole body.
                    if (!_HasPoseSource) StopGraph();
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

                // 調査用ログ: 約5秒ごとに presence と基準姿勢の状態を出す。
                if (_tickLogCountdown-- <= 0)
                {
                    _tickLogCountdown = 300;
                    Debug.Log($"[AvatarBodyDriver] tick: hasBasePose={_hasBasePose.Value}, graphPlaying={_isGraphPlaying}, presence Hips={frameData.pose.AsPresence((int)HumanBodyBones.Hips):F2} LUpLeg={frameData.pose.AsPresence((int)HumanBodyBones.LeftUpperLeg):F2} LLoLeg={frameData.pose.AsPresence((int)HumanBodyBones.LeftLowerLeg):F2} Head={frameData.pose.AsPresence((int)HumanBodyBones.Head):F2}");
                }
            }
            return true;
        }

        public void Dispose()
        {
            if (_graph.IsValid()) _graph.Destroy();
            if (_handles.IsCreated) _handles.Dispose();
            if (_boneIndices.IsCreated) _boneIndices.Dispose();
            if (_poseInput.IsCreated) _poseInput.Dispose();
            if (_basePose.IsCreated) _basePose.Dispose();
            if (_hasBasePose.IsCreated) _hasBasePose.Dispose();
            if (_baseHipPosition.IsCreated) _baseHipPosition.Dispose();
            _controllerPlayables.Clear();
            _controllerParamHashes.Clear();
            _boneTransforms = null;
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
