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
        /// <summary>1 = overwrite the pose with <see cref="pose"/>; 0 = pass through (upstream only).</summary>
        public byte enabled;

        public HumanoidPoseData pose;
    }

    /// <summary>
    /// Animation job that lays the mocap pose over whatever the upstream playable produced, muscle by
    /// muscle, weighted by each muscle's tracking presence. Presence 1 takes the mocap value whole;
    /// presence 0 leaves the upstream value alone; in between blends the two.
    ///
    /// It works in muscle space (<see cref="AnimationStream.AsHuman"/>) rather than writing bone
    /// transforms, which is what lets "leave the upstream value alone" be the whole implementation of
    /// the untracked case. Humanoid clips and controllers animate muscles; a job that wrote transforms
    /// could not see them, and the driver had to sample the override clip into bone rotations up front
    /// and write those back explicitly. Reading the muscle the upstream playable wrote is the same
    /// thing, done by the same mechanism as the mocap write, without the bake.
    /// </summary>
    public struct HumanoidPoseJob : IAnimationJob
    {
        /// <summary>
        /// The 95 muscles, in <see cref="HumanPose.muscles"/> order. Measured: the handles come back
        /// in exactly that order, so a muscle index addresses both.
        /// </summary>
        [ReadOnly] public NativeArray<MuscleHandle> muscleHandles;

        [ReadOnly] public NativeReference<HumanoidPoseInput> input;

        // 下半身の位置ロック (1=ON)。ON の間は body (腰) のローカル位置を上流 (上書きクリップ) の
        // 値に固定し、両足を上流の立ち位置へ IK で留める。回転は通常どおり mocap を反映する
        // (root の位置・回転ロックは Tick 側)。
        [ReadOnly] public NativeReference<byte> lockLowerBody;

        // 上流が姿勢を供給しているか (上書きクリップあり)。ロックが固定する「元の立ち位置」は
        // 上流の姿勢そのものなので、上流が無いときはロックしても固定する相手が居ない。
        [ReadOnly] public NativeReference<byte> hasUpstreamPose;

        // 下半身ロックで root 回転を anchor に固定した際、mocap の root 回転に含まれていた腰の向き
        // (主に yaw) を body へ畳み込んで補償する回転。inverse(anchorRot) * mocapRootRot。
        // bodyLocalRotation は root 基準なので、この差分をそのまま前置すればよい。ボーンのローカル
        // 回転に前置していた頃に必要だった world 空間での補正 (親の回転で軸が化ける対策) は要らない。
        [ReadOnly] public NativeReference<Quaternion> bodyRootCompensation;

        public void ProcessRootMotion(AnimationStream stream) { }

        public void ProcessAnimation(AnimationStream stream)
        {
            // 非ヒューマノイドのアバターには muscle が無い。姿勢データ自体が humanoid 前提なので、
            // 何もせず上流を素通しする。
            if (!stream.isHumanStream) return;

            var human = stream.AsHuman();
            var data = input.Value;

            // トラッキング無効フレーム: 上流 (コントローラ / 上書きクリップ) をそのまま通す。
            if (data.enabled == 0) return;

            bool lockLower = lockLowerBody.Value == 1 && hasUpstreamPose.Value == 1;

            // ロックが固定する相手は「mocap を載せる前の上流の姿勢」なので、書き込みの前に読む。
            // ゴールは world 座標で読んで world 座標で戻す。root はこの job の間動かないので、
            // 同一フレーム内では world/local のどちらで持っても同じ姿勢を指す。
            Vector3 baseBodyPosition = default;
            Vector3 leftFootPosition = default, rightFootPosition = default;
            Quaternion leftFootRotation = default, rightFootRotation = default;
            if (lockLower)
            {
                baseBodyPosition = human.bodyLocalPosition;
                leftFootPosition = human.GetGoalPosition(AvatarIKGoal.LeftFoot);
                leftFootRotation = human.GetGoalRotation(AvatarIKGoal.LeftFoot);
                rightFootPosition = human.GetGoalPosition(AvatarIKGoal.RightFoot);
                rightFootRotation = human.GetGoalRotation(AvatarIKGoal.RightFoot);
            }

            for (int i = 0; i < muscleHandles.Length; i++)
            {
                float presence = data.pose.AsMusclePresence(i);

                // 未トラッキング: 上流の値をそのまま残す。
                if (presence <= 0f) continue;

                float mocap = data.pose.AsMuscle(i);
                var handle = muscleHandles[i];
                human.SetMuscle(handle, presence >= 1f
                    ? mocap
                    : Mathf.Lerp(human.GetMuscle(handle), mocap, presence));
            }

            float bodyPresence = data.pose.bodyPresence;
            if (bodyPresence > 0f)
            {
                // 下半身ロック中は root 回転を anchor に固定しているため、mocap で root 側にあった腰の
                // 向き (主に yaw) が失われる。これを body の回転へ前置して補償する。配下 (spine/脚) も追従する。
                Quaternion bodyRotation = lockLower
                    ? bodyRootCompensation.Value * data.pose.bodyRotation
                    : data.pose.bodyRotation;

                human.bodyLocalRotation = bodyPresence >= 1f
                    ? bodyRotation
                    : Quaternion.Slerp(human.bodyLocalRotation, bodyRotation, bodyPresence);
            }

            if (lockLower)
            {
                // 腰の位置は上流 (クリップ) のまま。bodyLocalPosition は humanScale 正規化された値なので、
                // キャラクターの身長やリグの親子構成に依らず同じ立ち位置に固定される (ボーン直書きの頃に
                // 必要だった humanScale の手当ては要らない)。
                human.bodyLocalPosition = baseBodyPosition;
            }
            else if (bodyPresence > 0f)
            {
                human.bodyLocalPosition = bodyPresence >= 1f
                    ? data.pose.bodyPosition
                    : Vector3.Lerp(human.bodyLocalPosition, data.pose.bodyPosition, bodyPresence);
            }

            // 足 IK: ロック中は両足を上流の立ち位置へ戻す。腰が mocap で回っても接地位置は動かない。
            // ⚠ weight を立てないと SolveIK は何もしない (既定 0)。実測済み。
            if (lockLower)
            {
                _PinFoot(ref human, AvatarIKGoal.LeftFoot, leftFootPosition, leftFootRotation);
                _PinFoot(ref human, AvatarIKGoal.RightFoot, rightFootPosition, rightFootRotation);
                human.SolveIK();
            }
        }

        private static void _PinFoot(ref AnimationHumanStream human, AvatarIKGoal goal, Vector3 position, Quaternion rotation)
        {
            human.SetGoalPosition(goal, position);
            human.SetGoalRotation(goal, rotation);
            human.SetGoalWeightPosition(goal, 1f);
            human.SetGoalWeightRotation(goal, 1f);
        }
    }

    /// <summary>
    /// Shared body-animation driver for avatar components (VRM1Avatar / VRCFTAvatar).
    /// Owns the motion source reference, tracking state with mesh visibility, and a
    /// PlayableGraph that mixes the avatar's <see cref="AnimatorControllerPlayable"/> (if any) with
    /// the untracked-part override clip, and feeds the result to a <see cref="HumanoidPoseJob"/>.
    /// The mocap pose overwrites the upstream pose while tracking; untracked muscles keep whatever
    /// the mix produced. The root transform is written directly (not through the stream) via
    /// <see cref="AvatarAnimationSystem.UpdateRoot"/>.
    ///
    /// Untracked-part override: the clip is an <see cref="AnimationClipPlayable"/> in the graph, on
    /// an override layer above the controller. Humanoid clips animate muscles and the job reads
    /// muscles, so the clip's pose simply is the upstream pose -- no baking, and nothing to re-bake
    /// when the clip changes. It is held at t=0 (speed 0), matching the static-pose behavior the
    /// baked version had; letting it play is a one-line change if that is ever wanted.
    ///
    /// Animator parameters must be read/written through this driver's accessors
    /// (<see cref="SetFloat"/> / <see cref="GetFloat"/> etc.); Animator.SetFloat/GetFloat
    /// do not reach a controller wrapped inside a PlayableGraph. The read accessors also
    /// let an external object bridge mirror the avatar's parameter values onto its own Animator.
    /// </summary>
    public sealed class AvatarBodyDriver : IDisposable
    {
        // Graph layers: the controller underneath, the override clip on top of it.
        const int kControllerLayer = 0;
        const int kOverrideClipLayer = 1;

        Animator _animator;
        Renderer[] _renderers;

        PlayableGraph _graph;
        AnimationLayerMixerPlayable _mixer;
        AnimationScriptPlayable _posePlayable;
        AnimationPlayableOutput _output;

        // ラップした AnimatorController と、それが宣言するパラメータ nameHash 集合。
        // 単一コントローラだが、リスト構造はパラメータアクセサ実装をそのまま使えるよう残す。
        readonly List<AnimatorControllerPlayable> _controllerPlayables = new List<AnimatorControllerPlayable>();
        readonly List<HashSet<int>> _controllerParamHashes = new List<HashSet<int>>();

        // 未トラッキング部位の姿勢を上書きするアニメ (待機/基本ポーズ等)。
        AnimationClip _overrideClip;
        AnimationClipPlayable _overrideClipPlayable;

        NativeArray<MuscleHandle> _muscleHandles;
        NativeReference<HumanoidPoseInput> _poseInput;

        // 下半身の位置ロック (job 側: 腰の位置と両足を上流の姿勢に固定)。root の位置・回転ロックは Tick 側。
        NativeReference<byte> _lockLowerBody;
        NativeReference<byte> _hasUpstreamPose;
        // 下半身ロックで root 回転を anchor 固定した際の body 補償回転 (Tick で毎フレーム更新)。
        NativeReference<Quaternion> _bodyRootCompensation;

        // SetLowerBodyPoseLock で保持する現在値。Initialize 前に設定されても保持し、
        // Initialize 完了時に _lockLowerBody へ反映する。Tick の root 位置ロックでも参照する。
        bool _lockLowerBodyPose;

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
        /// Prefer the parameter accessors (<see cref="SetFloat"/> / <see cref="GetFloat"/>).
        /// </summary>
        public AnimatorControllerPlayable controllerPlayable
            => _controllerPlayables.Count > 0 ? _controllerPlayables[0] : default;

        /// <summary>controller か override clip のいずれかが姿勢を供給するか。</summary>
        bool _HasPoseSource => _controllerPlayables.Count > 0 || _overrideClip != null;

        /// <summary>
        /// 未トラッキング部位を上書きするアニメクリップ。<see cref="Initialize"/> に渡すか
        /// <see cref="SetOverrideClip"/> で実行時に差し替える。null で controller / 凍結経路へ戻る。
        /// </summary>
        public AnimationClip overrideClip => _overrideClip;

        /// <summary>
        /// Builds the PlayableGraph and the muscle handle table. Call from Start
        /// (for VRM avatars, after Vrm10Instance.Runtime has reconstructed transforms).
        /// </summary>
        public void Initialize(Animator animator, AnimationClip overrideClip = null)
        {
            _animator = animator;
            _renderers = animator.GetComponentsInChildren<Renderer>(true);
            _overrideClip = overrideClip;

            if (!animator.isHuman)
            {
                Debug.LogWarning($"[Core] {animator.name} is not a humanoid avatar; the mocap pose cannot be applied to it.");
            }

            // controller は未トラッキング部位のアニメ流し込み専用。root はあくまで mocap が
            // 権威 (UpdateRoot で毎フレーム書く) なので、graph 内で再生される controller の
            // root motion がアバター全体を動かして沈めないよう無効化する。
            animator.applyRootMotion = false;

            _graph = PlayableGraph.Create($"{animator.name}.AvatarBody");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);

            var managedHandles = new MuscleHandle[MuscleHandle.muscleHandleCount];
            MuscleHandle.GetMuscleHandles(managedHandles);
            _muscleHandles = new NativeArray<MuscleHandle>(managedHandles, Allocator.Persistent);

            _poseInput = new NativeReference<HumanoidPoseInput>(Allocator.Persistent);

            _lockLowerBody = new NativeReference<byte>(Allocator.Persistent);
            _lockLowerBody.Value = (byte)(_lockLowerBodyPose ? 1 : 0);
            _hasUpstreamPose = new NativeReference<byte>(Allocator.Persistent);
            _bodyRootCompensation = new NativeReference<Quaternion>(Allocator.Persistent);
            _bodyRootCompensation.Value = Quaternion.identity;

            var job = new HumanoidPoseJob
            {
                muscleHandles = _muscleHandles,
                input = _poseInput,
                lockLowerBody = _lockLowerBody,
                hasUpstreamPose = _hasUpstreamPose,
                bodyRootCompensation = _bodyRootCompensation,
            };
            _posePlayable = AnimationScriptPlayable.Create(_graph, job);
            _posePlayable.SetInputCount(1);

            _mixer = AnimationLayerMixerPlayable.Create(_graph, 2);
            _graph.Connect(_mixer, 0, _posePlayable, 0);
            _posePlayable.SetInputWeight(0, 1f);

            _BuildController(animator);
            _RebuildOverrideClipPlayable();

            _output = AnimationPlayableOutput.Create(_graph, "Body", animator);
            _output.SetSourcePlayable(_posePlayable);

            // With a pose source (controller or override clip), keep the graph playing so the job
            // runs even before/after tracking. Without one, stay stopped until the first valid frame
            // so the avatar keeps its imported pose instead of snapping to the zero-muscle stance.
            if (_HasPoseSource)
            {
                PlayGraph();
            }
        }

        /// <summary>
        /// Wraps the avatar's runtime controller (if any) into an
        /// <see cref="AnimatorControllerPlayable"/> on the graph's lower layer.
        /// </summary>
        void _BuildController(Animator animator)
        {
            if (animator.runtimeAnimatorController == null) return; // コントローラ無し（凍結/上書きクリップ経路）
            _AddControllerPlayable(animator.runtimeAnimatorController);
            _graph.Connect(_controllerPlayables[0], 0, _mixer, kControllerLayer);
            _mixer.SetInputWeight(kControllerLayer, 1f);
        }

        /// <summary>
        /// (Re)builds the override clip's playable on the layer above the controller. The clip is
        /// held at t=0 with speed 0, so it acts as a static pose.
        /// </summary>
        void _RebuildOverrideClipPlayable()
        {
            if (!_graph.IsValid()) return;

            if (_overrideClipPlayable.IsValid())
            {
                _graph.Disconnect(_mixer, kOverrideClipLayer);
                _overrideClipPlayable.Destroy();
                _overrideClipPlayable = default;
            }

            if (_overrideClip == null)
            {
                _mixer.SetInputWeight(kOverrideClipLayer, 0f);
                if (_hasUpstreamPose.IsCreated) _hasUpstreamPose.Value = 0;
                return;
            }

            _overrideClipPlayable = AnimationClipPlayable.Create(_graph, _overrideClip);
            _overrideClipPlayable.SetTime(0.0);
            _overrideClipPlayable.SetSpeed(0.0);
            // 足 IK はこの driver が下半身ロックで解く。クリップ自身の foot IK を重ねない。
            _overrideClipPlayable.SetApplyFootIK(false);

            _graph.Connect(_overrideClipPlayable, 0, _mixer, kOverrideClipLayer);
            _mixer.SetInputWeight(kOverrideClipLayer, 1f);
            if (_hasUpstreamPose.IsCreated) _hasUpstreamPose.Value = 1;
        }

        /// <summary>
        /// 未トラッキング部位を上書きするクリップを実行時に差し替える。graph 上のクリップ playable を
        /// 差し替えるだけ。<see cref="Initialize"/> 前は値だけ保持する。
        /// </summary>
        public void SetOverrideClip(AnimationClip clip)
        {
            if (_overrideClip == clip) return;
            _overrideClip = clip;

            if (!_graph.IsValid()) return; // Initialize 前。次の Initialize で反映される。

            _RebuildOverrideClipPlayable();

            // 姿勢の供給元ができたら job が姿勢を書けるよう graph 再生を保証する。
            if (_HasPoseSource && !_isGraphPlaying) PlayGraph();
        }

        /// <summary>
        /// 下半身の位置ロックを切り替える。ON の間: 腰は override クリップの位置 (humanScale 正規化済みの
        /// bodyLocalPosition) に固定され、root (アバター全体) は位置・回転とも motionSource.anchor に
        /// 固定される (<see cref="Tick"/>)。キャラクターの身長に依らず腰は同じ位置に固定される。各部位の
        /// 回転は通常どおり mocap を反映するので、アバターの移動・向きだけが固定されポーズは動く。さらに
        /// 両足を humanoid IK でクリップの接地位置へ固定し、腰の回転で足が振られないようにする。
        /// クリップ未設定時は固定する相手が無いので何もしない。<see cref="Initialize"/> 前は値だけ保持する。
        /// </summary>
        public void SetLowerBodyPoseLock(bool locked)
        {
            _lockLowerBodyPose = locked;
            if (_lockLowerBody.IsCreated) _lockLowerBody.Value = (byte)(locked ? 1 : 0);
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
                    // No pose source to fall back to: freeze the last pose by stopping
                    // the graph (an empty stream would otherwise reset to the zero-muscle stance).
                    // With a controller or override clip, keep playing so it drives the whole body.
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
                // 下半身の位置ロック中は root (アバター全体) の位置・回転を anchor (AvatarController)
                // に固定する。各部位の回転は job が mocap を反映するので、アバターの移動・向きだけが
                // 固定され、ポーズ自体は動く。
                if (_lockLowerBodyPose)
                {
                    var anchor = motionSource.anchor;
                    Quaternion rootRotation;
                    if (anchor != null)
                    {
                        _animator.transform.SetPositionAndRotation(anchor.position, anchor.rotation);
                        rootRotation = anchor.rotation;
                    }
                    else
                    {
                        rootRotation = _animator.transform.rotation;
                    }

                    // root 回転を anchor に固定したぶん、mocap root に含まれる腰の向き (yaw 等) を body へ
                    // 畳み込むための補償回転を渡す。bodyLocalRotation は root 基準なので、root 基準での
                    // 差分 (inverse(anchor) * mocap) をそのまま前置すればよい。両者が一致すれば identity。
                    if (_bodyRootCompensation.IsCreated)
                        _bodyRootCompensation.Value = Quaternion.Inverse(rootRotation) * frameData.root.rotation;
                }
                else
                {
                    AvatarAnimationSystem.UpdateRoot(_animator.transform, in frameData.root);
                }
                _poseInput.Value = new HumanoidPoseInput { enabled = 1, pose = frameData.pose };
            }
            return true;
        }

        public void Dispose()
        {
            if (_graph.IsValid()) _graph.Destroy();
            if (_muscleHandles.IsCreated) _muscleHandles.Dispose();
            if (_poseInput.IsCreated) _poseInput.Dispose();
            if (_lockLowerBody.IsCreated) _lockLowerBody.Dispose();
            if (_hasUpstreamPose.IsCreated) _hasUpstreamPose.Dispose();
            if (_bodyRootCompensation.IsCreated) _bodyRootCompensation.Dispose();
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
