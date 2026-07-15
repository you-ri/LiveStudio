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
    /// A foot IK goal captured from the override clip, expressed relative to the avatar root so
    /// it stays fixed while the hips rotate under a position-locked root. Converted to world space
    /// each frame with the current <see cref="RootWorldPose"/>.
    /// </summary>
    public struct FootIKGoal
    {
        public Vector3 localPosition;
        public Quaternion localRotation;
    }

    /// <summary>Avatar root world pose, refreshed each frame by the driver for foot-IK goal conversion.</summary>
    public struct RootWorldPose
    {
        public Vector3 position;
        public Quaternion rotation;
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

        // 下半身ロック時に hips を固定する root 基準オフセット (humanScale 正規化済み)。
        // humanoid の腰位置はアバターの humanScale (腰高) が乗って解決されるため、サンプリング値を
        // そのまま使うとキャラクターの身長でロック位置が上下する。正規化済みオフセットを rootWorld
        // (anchor 固定された root) で world へ変換して適用することで、リグの親子構成や身長に依らず
        // 全キャラ同じ位置に固定する。hasBasePose==1 かつ lockLowerBody==1 のとき有効。
        [ReadOnly] public NativeReference<Vector3> lockHipOffset;

        // 下半身の位置ロック (1=ON)。ON の間は hips のローカル位置を基準姿勢 (クリップ) に固定する。
        // 回転は通常どおり mocap を反映する (root の位置・回転ロックは Tick 側)。
        [ReadOnly] public NativeReference<byte> lockLowerBody;

        // 下半身ロックで root 回転を anchor に固定した際、mocap の root 回転に含まれていた腰の向き
        // (主に yaw) を hips ボーンへ畳み込んで補償する回転。inverse(anchorRot) * mocapRootRot。
        [ReadOnly] public NativeReference<Quaternion> hipsRootCompensation;

        // 足 IK (下半身ロック中に有効)。Unity 標準の humanoid IK (AnimationHumanStream.SolveIK) で
        // 両足を上書きクリップの足位置へ固定し、腰の回転 (mocap) で足が振られても接地位置を保つ。
        // footGoalValid==1 かつ lockLowerBody==1 のとき適用。ゴールは root ローカルで持ち、毎フレーム
        // rootWorld (anchor 固定された root) で world 変換する。
        [ReadOnly] public NativeReference<byte> footGoalValid;
        [ReadOnly] public NativeReference<RootWorldPose> rootWorld;
        [ReadOnly] public NativeReference<FootIKGoal> leftFootGoal;
        [ReadOnly] public NativeReference<FootIKGoal> rightFootGoal;

        // 足 IK 対象の脚ボーンの handles 内インデックス (無い場合 -1)。
        public int leftUpperLegIndex;
        public int leftLowerLegIndex;
        public int leftFootIndex;
        public int rightUpperLegIndex;
        public int rightLowerLegIndex;
        public int rightFootIndex;

        public void ProcessRootMotion(AnimationStream stream) { }

        public void ProcessAnimation(AnimationStream stream)
        {
            var data = input.Value;
            bool useBase = hasBasePose.Value == 1;
            bool lockLower = lockLowerBody.Value == 1;

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

            // 下半身ロック中は root 回転を anchor に固定しているため、mocap で root 側にあった腰の向き
            // (主に yaw) が失われる。これを hips ボーンのローカル回転へ畳み込んで補償し、腰の yaw を戻す。
            // hips は root 直下なので、ここを補正すれば配下 (spine/脚) の world 姿勢も追従する。
            if (lockLower && hipsHandleIndex >= 0)
            {
                var hipsLocal = handles[hipsHandleIndex].GetLocalRotation(stream);
                handles[hipsHandleIndex].SetLocalRotation(stream, hipsRootCompensation.Value * hipsLocal);
            }

            if (hipsHandleIndex >= 0)
            {
                if (lockLower)
                {
                    // 位置ロック中は hips を基準姿勢 (クリップ) の腰位置へ固定する (回転は上のループで
                    // mocap を反映済み)。lockHipOffset は humanScale 正規化済みの root 基準オフセットで、
                    // anchor 固定された root (rootWorld) から world へ変換して直接設定するため、
                    // キャラクターの身長やリグの親子構成に依らず腰は同じ位置に固定される。
                    if (useBase)
                    {
                        var rw = rootWorld.Value;
                        handles[hipsHandleIndex].SetPosition(stream, rw.position + rw.rotation * lockHipOffset.Value);
                    }
                }
                else
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

            // 足 IK: 下半身ロック中は脚 (upperLeg→lowerLeg→foot) の 2 ボーン IK で両足を上書きクリップの
            // 足位置へ固定する。各ボーンの回転 (mocap) で腰が回っても足の接地位置は動かない。ゴールは
            // root ローカルで持つので、anchor 固定された root (rootWorld) で world へ変換して与える。
            // Unity 標準の humanoid IK (AnimationHumanStream.SolveIK) は muscle 空間で解くため、この job の
            // transform 直書きポーズを丸ごと上書きして全身を固定してしまう。よって transform 空間で自前に解く。
            if (lockLower && footGoalValid.Value == 1)
            {
                var root = rootWorld.Value;
                _SolveFootIK(stream, leftUpperLegIndex, leftLowerLegIndex, leftFootIndex, leftFootGoal.Value, root);
                _SolveFootIK(stream, rightUpperLegIndex, rightLowerLegIndex, rightFootIndex, rightFootGoal.Value, root);
            }
        }

        // 片脚の 2 ボーン IK。ゴール (root ローカル) を world 変換し、upperLeg/lowerLeg を解いて foot を
        // 目標位置へ届かせ、foot 回転も目標へ合わせる。ハンドルが揃っていなければ何もしない。
        void _SolveFootIK(AnimationStream stream, int upperIdx, int midIdx, int tipIdx, FootIKGoal goal, RootWorldPose root)
        {
            if (upperIdx < 0 || midIdx < 0 || tipIdx < 0) return;

            Vector3 targetPosition = root.position + root.rotation * goal.localPosition;
            Quaternion targetRotation = root.rotation * goal.localRotation;
            _TwoBoneIK(stream, handles[upperIdx], handles[midIdx], handles[tipIdx], targetPosition, targetRotation);
        }

        // 解析的 2 ボーン IK (Unity Animation Rigging の SolveTwoBoneIK と同手法)。膝の曲げ方向は
        // 現在の三角形 (upper-mid-tip) 法線を使うので mocap の膝向きを保つ。muscle 空間を使わない。
        static void _TwoBoneIK(AnimationStream stream,
            TransformStreamHandle upper, TransformStreamHandle mid, TransformStreamHandle tip,
            Vector3 targetPosition, Quaternion targetRotation)
        {
            Vector3 a = upper.GetPosition(stream);
            Vector3 b = mid.GetPosition(stream);
            Vector3 c = tip.GetPosition(stream);

            Quaternion aRotation = upper.GetRotation(stream);
            Quaternion bRotation = mid.GetRotation(stream);

            Vector3 ab = b - a;
            Vector3 bc = c - b;
            Vector3 ac = c - a;
            Vector3 at = targetPosition - a;

            float abLen = ab.magnitude;
            float bcLen = bc.magnitude;
            float acLen = ac.magnitude;
            float atLen = at.magnitude;

            if (abLen < 1e-6f || bcLen < 1e-6f || atLen < 1e-6f) return;

            // 目標が脚の可動域を超えないようにクランプ (完全伸展でも解が存在するように)。
            float maxLen = abLen + bcLen - 1e-4f;
            if (atLen > maxLen) { at *= maxLen / atLen; atLen = maxLen; targetPosition = a + at; }

            float oldAngle = _TriangleAngle(acLen, abLen, bcLen);
            float newAngle = _TriangleAngle(atLen, abLen, bcLen);

            // 膝の曲げ軸: 現在の脚三角形の法線。直線に近く縮退する場合のみ代替軸。
            Vector3 axis = Vector3.Cross(ab, bc);
            if (axis.sqrMagnitude < 1e-8f)
            {
                axis = Vector3.Cross(ab, Vector3.up);
                if (axis.sqrMagnitude < 1e-8f) axis = Vector3.Cross(ab, Vector3.right);
            }
            axis = axis.normalized;

            // 膝 (mid) を新旧の内角差ぶん回す。quaternion の回転角は半角の 2 倍。
            float half = 0.5f * (oldAngle - newAngle);
            float sin = Mathf.Sin(half);
            float cos = Mathf.Cos(half);
            Quaternion bendDelta = new Quaternion(axis.x * sin, axis.y * sin, axis.z * sin, cos);
            mid.SetRotation(stream, bendDelta * bRotation);

            // 膝を曲げた後の tip 位置を読み直し、upper を回して tip を目標へ向ける。
            Vector3 c2 = tip.GetPosition(stream);
            Vector3 ac2 = c2 - a;
            if (ac2.sqrMagnitude > 1e-10f)
                upper.SetRotation(stream, Quaternion.FromToRotation(ac2, at) * aRotation);

            // foot の向きを目標へ合わせる。
            tip.SetRotation(stream, targetRotation);
        }

        // aLen に対する角 (辺 aLen1, aLen2 の間の角) を余弦定理で求める。
        static float _TriangleAngle(float aLen, float aLen1, float aLen2)
        {
            float c = Mathf.Clamp((aLen1 * aLen1 + aLen2 * aLen2 - aLen * aLen) / (2f * aLen1 * aLen2), -1f, 1f);
            return Mathf.Acos(c);
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
        // 足 IK 用の脚ボーンの _handles 内インデックス (無い場合 -1)。
        int _leftUpperLegHandleIndex = -1;
        int _leftLowerLegHandleIndex = -1;
        int _leftFootHandleIndex = -1;
        int _rightUpperLegHandleIndex = -1;
        int _rightLowerLegHandleIndex = -1;
        int _rightFootHandleIndex = -1;
        NativeReference<HumanoidPoseInput> _poseInput;

        // 上書きクリップから焼いた基準姿勢。job と共有する。
        NativeArray<Quaternion> _basePose;
        NativeReference<byte> _hasBasePose;
        NativeReference<Vector3> _baseHipPosition;
        // 下半身ロック用の root 基準 hips オフセット (humanScale 正規化済み)。_SampleBasePose で焼く。
        NativeReference<Vector3> _lockHipOffset;

        // 下半身の位置ロック (job 側: hips ローカル位置を基準姿勢へ固定)。root の位置・回転ロックは Tick 側。
        NativeReference<byte> _lockLowerBody;
        // SetLowerBodyPoseLock で保持する現在値。Initialize 前に設定されても保持し、
        // Initialize 完了時に _lockLowerBody へ反映する。Tick の root 位置ロックでも参照する。
        bool _lockLowerBodyPose;

        // 足 IK ゴール (root ローカル)。_SampleBasePose で上書きクリップの足ポーズから焼く。
        NativeReference<byte> _footGoalValid;
        NativeReference<FootIKGoal> _leftFootGoal;
        NativeReference<FootIKGoal> _rightFootGoal;
        // アバター root の world 姿勢。Tick で毎フレーム更新し、job が足 IK ゴールの world 変換に使う。
        NativeReference<RootWorldPose> _rootWorld;
        // 下半身ロックで root 回転を anchor 固定した際の hips 補償回転 (Tick で毎フレーム更新)。
        NativeReference<Quaternion> _hipsRootCompensation;

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

            // 足 IK 用の脚ボーンの handles 内インデックスを控える (無ければ -1)。
            _leftUpperLegHandleIndex = indicesList.IndexOf((int)HumanBodyBones.LeftUpperLeg);
            _leftLowerLegHandleIndex = indicesList.IndexOf((int)HumanBodyBones.LeftLowerLeg);
            _leftFootHandleIndex = indicesList.IndexOf((int)HumanBodyBones.LeftFoot);
            _rightUpperLegHandleIndex = indicesList.IndexOf((int)HumanBodyBones.RightUpperLeg);
            _rightLowerLegHandleIndex = indicesList.IndexOf((int)HumanBodyBones.RightLowerLeg);
            _rightFootHandleIndex = indicesList.IndexOf((int)HumanBodyBones.RightFoot);

            _handles = new NativeArray<TransformStreamHandle>(handlesList.ToArray(), Allocator.Persistent);
            _boneIndices = new NativeArray<int>(indicesList.ToArray(), Allocator.Persistent);
            _boneTransforms = transformsList.ToArray();
            _poseInput = new NativeReference<HumanoidPoseInput>(Allocator.Persistent);

            _basePose = new NativeArray<Quaternion>(_handles.Length, Allocator.Persistent);
            _hasBasePose = new NativeReference<byte>(Allocator.Persistent);
            _baseHipPosition = new NativeReference<Vector3>(Allocator.Persistent);
            _lockHipOffset = new NativeReference<Vector3>(Allocator.Persistent);

            // 下半身の位置ロックの共有状態。
            _lockLowerBody = new NativeReference<byte>(Allocator.Persistent);
            _lockLowerBody.Value = (byte)(_lockLowerBodyPose ? 1 : 0);

            // 足 IK 用の共有状態。ゴールは直後の _SampleBasePose で焼く。
            _footGoalValid = new NativeReference<byte>(Allocator.Persistent);
            _leftFootGoal = new NativeReference<FootIKGoal>(Allocator.Persistent);
            _rightFootGoal = new NativeReference<FootIKGoal>(Allocator.Persistent);
            _rootWorld = new NativeReference<RootWorldPose>(Allocator.Persistent);
            _hipsRootCompensation = new NativeReference<Quaternion>(Allocator.Persistent);
            _hipsRootCompensation.Value = Quaternion.identity;

            // 上書きクリップを基準姿勢へ焼く (存在すれば _hasBasePose=1)。足 IK ゴールもここで焼く。
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
                lockHipOffset = _lockHipOffset,
                lockLowerBody = _lockLowerBody,
                footGoalValid = _footGoalValid,
                rootWorld = _rootWorld,
                leftFootGoal = _leftFootGoal,
                rightFootGoal = _rightFootGoal,
                hipsRootCompensation = _hipsRootCompensation,
                leftUpperLegIndex = _leftUpperLegHandleIndex,
                leftLowerLegIndex = _leftLowerLegHandleIndex,
                leftFootIndex = _leftFootHandleIndex,
                rightUpperLegIndex = _rightUpperLegHandleIndex,
                rightLowerLegIndex = _rightLowerLegHandleIndex,
                rightFootIndex = _rightFootHandleIndex,
            };
            _posePlayable = AnimationScriptPlayable.Create(_graph, job);
            _posePlayable.SetInputCount(1);

            _BuildController(animator);

            _output = AnimationPlayableOutput.Create(_graph, "Body", animator);
            _output.SetSourcePlayable(_posePlayable);

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

            if (!_graph.IsValid()) return; // Initialize 前。次の Initialize で反映される。

            _SampleBasePose();

            // 基準姿勢ができたら job が姿勢を書けるよう graph 再生を保証する。
            if (_HasPoseSource && !_isGraphPlaying) PlayGraph();
        }

        /// <summary>
        /// 下半身の位置ロックを切り替える。ON の間: hips は override クリップの腰位置 (humanScale
        /// 正規化した root 基準オフセットを anchor 固定の root で world 変換した位置) に固定され、
        /// root (アバター全体) は位置・回転とも motionSource.anchor に固定される (<see cref="Tick"/>)。
        /// キャラクターの身長 (humanScale) に依らず腰は同じ位置に固定される。各ボーンの回転は通常
        /// どおり mocap を反映するので、アバターの移動・向きだけが固定されポーズは動く。さらに脚の
        /// 2 ボーン IK で両足を接地位置へ固定し、腰の回転で足が動かないようにする。クリップ未設定時は
        /// hips 位置は上流 (controller) のまま (足 IK も無効)。<see cref="Initialize"/> 前は値だけ保持する。
        /// </summary>
        public void SetLowerBodyPoseLock(bool locked)
        {
            _lockLowerBodyPose = locked;
            if (_lockLowerBody.IsCreated) _lockLowerBody.Value = (byte)(locked ? 1 : 0);
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
                if (_footGoalValid.IsCreated) _footGoalValid.Value = 0;
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

            // 下半身ロック用: クリップ姿勢の hips 位置を root 基準オフセットとして捕捉し、humanScale で
            // 正規化する。humanoid の腰位置 (RootT) はアバターの humanScale (腰高) が乗って解決されるため、
            // サンプリング値をそのまま固定に使うとキャラクターの身長でロック位置が上下する。
            var hipsSampled = _HipsTransform();
            if (hipsSampled != null)
            {
                float humanScale = _animator.isHuman ? _animator.humanScale : 1f;
                if (humanScale <= 0f) humanScale = 1f;
                Vector3 rootOffset = Quaternion.Inverse(rootT.rotation) * (hipsSampled.position - rootT.position);
                _lockHipOffset.Value = rootOffset / humanScale;
            }

            // 足 IK ゴールを clip の足ポーズ (適用中) から root ローカルで焼く。両足が揃うときのみ有効。
            var leftFoot = _animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            var rightFoot = _animator.GetBoneTransform(HumanBodyBones.RightFoot);
            if (_footGoalValid.IsCreated && leftFoot != null && rightFoot != null)
            {
                _leftFootGoal.Value = _CaptureFootGoal(rootT, leftFoot);
                _rightFootGoal.Value = _CaptureFootGoal(rootT, rightFoot);
                _footGoalValid.Value = 1;
            }
            else if (_footGoalValid.IsCreated)
            {
                _footGoalValid.Value = 0;
            }

            // 退避した姿勢へ復元。
            for (int i = 0; i < n; i++)
                if (_boneTransforms[i] != null) _boneTransforms[i].localRotation = savedRot[i];
            if (_HipsTransform() != null) _HipsTransform().localPosition = savedHip;
            rootT.localPosition = savedRootPos;
            rootT.localRotation = savedRootRot;

            _hasBasePose.Value = 1;
        }

        Transform _HipsTransform()
            => (_hipsHandleIndex >= 0 && _boneTransforms != null && _hipsHandleIndex < _boneTransforms.Length)
                ? _boneTransforms[_hipsHandleIndex] : null;

        // 上書きクリップ適用中の foot 世界姿勢を root ローカルへ変換して足 IK ゴールにする。
        static FootIKGoal _CaptureFootGoal(Transform root, Transform foot)
        {
            return new FootIKGoal
            {
                localPosition = root.InverseTransformPoint(foot.position),
                localRotation = Quaternion.Inverse(root.rotation) * foot.rotation,
            };
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
                // 下半身の位置ロック中は root (アバター全体) の位置・回転を anchor (AvatarController)
                // に固定する。各ボーンの回転は job が mocap を反映するので、アバターの移動・向きだけが
                // 固定され、ポーズ自体は動く。
                if (_lockLowerBodyPose)
                {
                    var anchor = motionSource.anchor;
                    Vector3 rootPos; Quaternion rootRot;
                    if (anchor != null)
                    {
                        rootPos = anchor.position;
                        rootRot = anchor.rotation;
                        _animator.transform.SetPositionAndRotation(rootPos, rootRot);
                    }
                    else
                    {
                        rootPos = _animator.transform.position;
                        rootRot = _animator.transform.rotation;
                    }
                    // 足 IK ゴールの world 変換基準として root 姿勢を job へ渡す。
                    if (_rootWorld.IsCreated)
                        _rootWorld.Value = new RootWorldPose { position = rootPos, rotation = rootRot };
                    // root 回転を anchor に固定したぶん、mocap root に含まれる腰の向き (yaw 等) を hips へ
                    // 畳み込むための補償回転を渡す。root が anchor と一致していれば identity。
                    if (_hipsRootCompensation.IsCreated)
                        _hipsRootCompensation.Value = Quaternion.Inverse(rootRot) * frameData.root.rotation;
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
            if (_handles.IsCreated) _handles.Dispose();
            if (_boneIndices.IsCreated) _boneIndices.Dispose();
            if (_poseInput.IsCreated) _poseInput.Dispose();
            if (_basePose.IsCreated) _basePose.Dispose();
            if (_hasBasePose.IsCreated) _hasBasePose.Dispose();
            if (_baseHipPosition.IsCreated) _baseHipPosition.Dispose();
            if (_lockHipOffset.IsCreated) _lockHipOffset.Dispose();
            if (_lockLowerBody.IsCreated) _lockLowerBody.Dispose();
            if (_footGoalValid.IsCreated) _footGoalValid.Dispose();
            if (_leftFootGoal.IsCreated) _leftFootGoal.Dispose();
            if (_rightFootGoal.IsCreated) _rightFootGoal.Dispose();
            if (_rootWorld.IsCreated) _rootWorld.Dispose();
            if (_hipsRootCompensation.IsCreated) _hipsRootCompensation.Dispose();
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
