// Copyright (c) You-Ri, 2026

using NUnit.Framework;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Lilium.LiveStudio.EditorTests
{
    /// <summary>
    /// What the humanoid muscle path actually does, measured rather than assumed.
    ///
    /// The avatar pose is moving from transform-space bone rotations to muscle space so a recording
    /// can be replayed on a different model. Every question that decision rests on is a question
    /// about behavior Unity documents thinly: whether a muscle index lines up with a
    /// <see cref="MuscleHandle"/>, whether reading a muscle inside an animation job returns what the
    /// upstream playable wrote, whether IK goals need a weight, and what space the body position is
    /// in. Getting any of them wrong lands a value on the wrong joint, silently.
    ///
    /// These run on a synthetic humanoid rig built here, so they answer for the Unity version the
    /// project is on rather than for the version the documentation was written against.
    /// </summary>
    public class HumanoidMuscleStreamTests
    {
        private GameObject _root;
        private Animator _animator;

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
            _root = null;
            _animator = null;
        }

        //----------------------------------------------------------------------
        // 1. Index spaces
        //----------------------------------------------------------------------

        /// <summary>
        /// Whether the muscle handles come back in the same order as <see cref="HumanTrait.MuscleName"/>,
        /// which is the order <see cref="HumanPose.muscles"/> uses. If they differ, every write needs a
        /// name-matched lookup table instead of a shared index.
        ///
        /// Measured: the order is the same, but the two APIs spell the finger muscles differently --
        /// a handle says "LeftHand.Thumb.1 Stretched" where the trait table says "Left Thumb 1
        /// Stretched". Comparing the strings raw reports 40 mismatches that are not mismatches, so
        /// the spelling is normalized here. The claim this test actually guards is the order;
        /// <see cref="MuscleIndex_RoundTripsThroughTheStream"/> proves it by value.
        /// </summary>
        [Test]
        public void MuscleHandleOrder_MatchesHumanTraitMuscleNames()
        {
            var handles = new MuscleHandle[MuscleHandle.muscleHandleCount];
            MuscleHandle.GetMuscleHandles(handles);

            Assert.AreEqual(HumanTrait.MuscleCount, MuscleHandle.muscleHandleCount,
                "muscle handle count differs from HumanTrait.MuscleCount");

            int mismatches = 0;
            var report = new System.Text.StringBuilder();
            for (int i = 0; i < handles.Length; i++)
            {
                string handleName = _NormalizeMuscleName(handles[i].name);
                string traitName = _NormalizeMuscleName(HumanTrait.MuscleName[i]);
                if (handleName != traitName)
                {
                    mismatches++;
                    if (mismatches <= 10) report.AppendLine($"  [{i}] handle=\"{handles[i].name}\" trait=\"{HumanTrait.MuscleName[i]}\"");
                }
            }

            Assert.AreEqual(0, mismatches,
                $"MuscleHandle order does not match HumanTrait.MuscleName ({mismatches} of {HumanTrait.MuscleCount}):\n{report}");
        }

        /// <summary>
        /// The <see cref="HumanMuscle"/> enum exists to label the recorded muscles, so its order has
        /// to be Unity's. Names are compared with the spaces and hyphens dropped, which is exactly the
        /// transformation the enum's identifiers were generated with.
        /// </summary>
        [Test]
        public void HumanMuscleEnum_MatchesUnitysMuscleTable()
        {
            Assert.AreEqual(HumanTrait.MuscleCount, HumanoidPoseData.kMuscleCount,
                "the pose's muscle count no longer matches Unity's");

            var names = System.Enum.GetNames(typeof(HumanMuscle));
            Assert.AreEqual(HumanTrait.MuscleCount, names.Length, "HumanMuscle has the wrong number of entries");

            for (int i = 0; i < names.Length; i++)
            {
                string expected = HumanTrait.MuscleName[i].Replace("-", "").Replace(" ", "");
                Assert.AreEqual(expected, names[i], $"HumanMuscle[{i}] does not name Unity's muscle {i}");
                Assert.AreEqual(i, (int)System.Enum.Parse(typeof(HumanMuscle), names[i]),
                    $"HumanMuscle.{names[i]} is not numbered {i}");
            }
        }

        /// <summary>
        /// The finger muscles are spelled "LeftHand.Thumb.1 Stretched" by <see cref="MuscleHandle"/>
        /// and "Left Thumb 1 Stretched" by <see cref="HumanTrait"/>. Same muscle, two conventions.
        /// </summary>
        private static string _NormalizeMuscleName(string name)
            => name.Replace("LeftHand.", "Left ").Replace("RightHand.", "Right ").Replace(".", " ");

        /// <summary>
        /// The load-bearing claim: muscle index i in <see cref="HumanPose.muscles"/> is the muscle
        /// that handle i drives. Proven by value rather than by name -- every muscle is written to a
        /// distinct number through the job, and the pose read back off the rig afterwards has to
        /// carry those same numbers at those same indices. A permuted handle table would land the
        /// values on other joints and this would fail.
        /// </summary>
        [Test]
        public void MuscleIndex_RoundTripsThroughTheStream()
        {
            _root = TestHumanoidRig.Build(1f, out _animator, out _);

            // Modest, distinct values: far enough apart to catch a permutation, well inside the
            // muscle range so nothing is clamped on the way through.
            var written = new float[HumanTrait.MuscleCount];
            for (int i = 0; i < written.Length; i++)
                written[i] = (i % 2 == 0 ? 1f : -1f) * (0.20f + 0.004f * i);

            using (var rig = new MuscleRig(_animator))
            {
                rig.WriteAll(written);
                rig.Evaluate();
            }

            var handler = new HumanPoseHandler(_animator.avatar, _animator.transform);
            var pose = new HumanPose();
            handler.GetHumanPose(ref pose);
            handler.Dispose();

            int mismatches = 0;
            var report = new System.Text.StringBuilder();
            for (int i = 0; i < written.Length; i++)
            {
                if (Mathf.Abs(pose.muscles[i] - written[i]) <= 0.05f) continue;
                mismatches++;
                if (mismatches <= 15)
                    report.AppendLine($"  [{i}] {HumanTrait.MuscleName[i]}: wrote {written[i]:F3}, read {pose.muscles[i]:F3}");
            }

            Assert.AreEqual(0, mismatches,
                $"{mismatches} of {HumanTrait.MuscleCount} muscles did not survive the write/read round trip:\n{report}");
        }

        //----------------------------------------------------------------------
        // 2. Writing and reading muscles inside an animation job
        //----------------------------------------------------------------------

        /// <summary>
        /// A muscle written in the job has to reach the bone transforms after the graph evaluates.
        /// This is the whole premise of driving the avatar from muscle values.
        /// </summary>
        [Test]
        public void SetMuscle_InJob_MovesTheBone()
        {
            _root = TestHumanoidRig.Build(1f, out _animator, out _);
            var lowerArm = _animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            Quaternion before = lowerArm.localRotation;

            int muscle = _MuscleIndex(HumanBodyBones.LeftLowerArm, "Stretch");
            Assert.GreaterOrEqual(muscle, 0, "left lower arm stretch muscle not found");

            using (var rig = new MuscleRig(_animator))
            {
                rig.Write(muscle, -0.8f);
                rig.Evaluate();
            }

            Assert.AreNotEqual(before, lowerArm.localRotation,
                "writing a muscle in the job did not change the bone");
        }

        /// <summary>
        /// Whether <c>GetMuscle</c> inside a job returns what an upstream playable put on the stream.
        /// The untracked-bone fallback depends on it: presence 0 means "leave the upstream pose
        /// alone", and a partial presence blends against it.
        /// </summary>
        [Test]
        public void GetMuscle_InJob_ReadsWhatUpstreamWrote()
        {
            _root = TestHumanoidRig.Build(1f, out _animator, out _);
            int muscle = _MuscleIndex(HumanBodyBones.LeftLowerArm, "Stretch");

            using (var rig = new MuscleRig(_animator, withUpstream: true))
            {
                rig.WriteUpstream(muscle, -0.6f);
                rig.ReadBack(muscle);
                rig.Evaluate();

                Assert.AreEqual(-0.6f, rig.readValue, 1e-4f,
                    "GetMuscle did not return the value the upstream playable wrote");
            }
        }

        //----------------------------------------------------------------------
        // 3. Body position / rotation
        //----------------------------------------------------------------------

        /// <summary>
        /// Whether <see cref="HumanPose.bodyPosition"/> is normalized by the avatar's scale. The
        /// recording is only model-independent if it is: two rigs of different size holding the same
        /// pose must record the same number.
        /// </summary>
        [Test]
        public void BodyPosition_IsNormalizedByHumanScale()
        {
            _root = TestHumanoidRig.Build(1f, out _animator, out _);
            var smallHandler = new HumanPoseHandler(_animator.avatar, _animator.transform);
            var smallPose = new HumanPose();
            smallHandler.GetHumanPose(ref smallPose);
            float smallScale = _animator.humanScale;
            smallHandler.Dispose();
            Object.DestroyImmediate(_root);

            _root = TestHumanoidRig.Build(2f, out _animator, out _);
            var bigHandler = new HumanPoseHandler(_animator.avatar, _animator.transform);
            var bigPose = new HumanPose();
            bigHandler.GetHumanPose(ref bigPose);
            float bigScale = _animator.humanScale;
            bigHandler.Dispose();

            // Measured: humanScale 1.069 vs 2.139 (exactly double), bodyPosition identical.
            Assert.AreEqual(smallScale * 2f, bigScale, 1e-3f, "the second rig is not twice the size of the first");

            Assert.AreEqual(smallPose.bodyPosition.y, bigPose.bodyPosition.y, 1e-3f,
                "bodyPosition.y differs between rigs of different size, so it is not humanScale-normalized");
        }

        //----------------------------------------------------------------------
        // 4. Clamping
        //----------------------------------------------------------------------

        /// <summary>
        /// Whether reading a pose out of an out-of-range bone rotation clamps the muscle value.
        ///
        /// Measured: it does not. An elbow bent far past its limit reads back as -1.125, outside the
        /// nominal -1..1 range. So capturing through muscle space does not itself throw away an
        /// extreme pose -- the range is a description of the joint, not a gate on the value. What
        /// does clamp is writing the pose back onto a rig, which is where a model with tighter
        /// limits than the one recorded on will differ.
        /// </summary>
        [Test]
        public void GetHumanPose_DoesNotClampMusclesToRange()
        {
            _root = TestHumanoidRig.Build(1f, out _animator, out _);

            // Bend the elbow far past anything the muscle range allows.
            var lowerArm = _animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            lowerArm.localRotation = Quaternion.Euler(0f, 170f, 0f);

            var handler = new HumanPoseHandler(_animator.avatar, _animator.transform);
            var pose = new HumanPose();
            handler.GetHumanPose(ref pose);
            handler.Dispose();

            int muscle = _MuscleIndex(HumanBodyBones.LeftLowerArm, "Stretch");
            float value = pose.muscles[muscle];

            Assert.IsFalse(float.IsNaN(value), "muscle value is NaN for an out-of-range bone rotation");
            Assert.Greater(Mathf.Abs(value), 1f,
                $"muscle {HumanTrait.MuscleName[muscle]} came back clamped ({value}); reading a pose is lossy after all");
        }

        //----------------------------------------------------------------------
        // 5. Foot IK
        //----------------------------------------------------------------------

        /// <summary>
        /// Whether the built-in humanoid IK moves the foot, and whether it needs a goal weight to do
        /// it. The lower-body lock pins both feet to the override clip's stance while the hips keep
        /// turning; if the goal weight defaults to zero, wiring the goals without setting it fails
        /// silently and the feet skate.
        ///
        /// Measured: with weight 1 the foot lands on the goal; with the goal set but no weight it
        /// does not move at all.
        /// </summary>
        [Test]
        public void SolveIK_MovesTheFoot_OnlyWithAGoalWeight()
        {
            _root = TestHumanoidRig.Build(1f, out _animator, out _);
            var foot = _animator.GetBoneTransform(HumanBodyBones.LeftFoot);

            // The baseline is the pose the graph produces, not the pose the rig was built in: with
            // nothing upstream the stream carries zeroed muscles, which is a different stance from
            // the rest pose the bones were authored in.
            using (var rig = new MuscleRig(_animator))
            {
                rig.Evaluate();
            }
            Vector3 baseline = foot.position;

            // A reachable spot: forward and slightly up from where the foot ends up.
            Vector3 goal = baseline + new Vector3(0f, 0.05f, 0.2f);

            using (var rig = new MuscleRig(_animator))
            {
                rig.SolveFootGoal(goal, weight: 0f);
                rig.Evaluate();
            }
            Vector3 withoutWeight = foot.position;

            using (var rig = new MuscleRig(_animator))
            {
                rig.SolveFootGoal(goal, weight: 1f);
                rig.Evaluate();
            }
            Vector3 withWeight = foot.position;

            Assert.AreEqual(0f, Vector3.Distance(baseline, withoutWeight), 1e-3f,
                "the foot moved even though the goal weight was zero");
            Assert.Less(Vector3.Distance(goal, withWeight), 0.02f,
                $"the foot did not reach the IK goal (goal {goal}, foot {withWeight})");
        }

        //----------------------------------------------------------------------
        // Helpers
        //----------------------------------------------------------------------

        /// <summary>Muscle index of a bone's dof whose name contains <paramref name="dofName"/>.</summary>
        private static int _MuscleIndex(HumanBodyBones bone, string dofName)
        {
            for (int dof = 0; dof < 3; dof++)
            {
                int muscle = HumanTrait.MuscleFromBone((int)bone, dof);
                if (muscle >= 0 && HumanTrait.MuscleName[muscle].Contains(dofName)) return muscle;
            }
            return -1;
        }

        //----------------------------------------------------------------------
        // Graph harness
        //----------------------------------------------------------------------

        /// <summary>
        /// A PlayableGraph whose output is the test rig, with one muscle-writing job and -- when
        /// asked for -- a second job upstream of it, so a read can be checked against a known write
        /// from the playable before it.
        /// </summary>
        private class MuscleRig : System.IDisposable
        {
            private PlayableGraph _graph;
            private AnimationScriptPlayable _downstream;
            private AnimationScriptPlayable _upstream;
            private NativeArray<MuscleHandle> _handles;
            private NativeArray<MuscleWrite> _downstreamWrite;
            private NativeArray<MuscleWrite> _upstreamWrite;
            private NativeArray<float> _read;
            private NativeArray<float> _allValues;
            private bool _hasUpstream;

            public float readValue => _read.IsCreated ? _read[0] : float.NaN;

            public MuscleRig(Animator animator, bool withUpstream = false)
            {
                _hasUpstream = withUpstream;

                var managed = new MuscleHandle[MuscleHandle.muscleHandleCount];
                MuscleHandle.GetMuscleHandles(managed);
                _handles = new NativeArray<MuscleHandle>(managed, Allocator.Persistent);

                _downstreamWrite = new NativeArray<MuscleWrite>(1, Allocator.Persistent);
                _downstreamWrite[0] = new MuscleWrite { index = -1, readIndex = -1 };
                _upstreamWrite = new NativeArray<MuscleWrite>(1, Allocator.Persistent);
                _upstreamWrite[0] = new MuscleWrite { index = -1, readIndex = -1 };
                _read = new NativeArray<float>(1, Allocator.Persistent);

                _graph = PlayableGraph.Create("MuscleRigTest");
                _downstream = AnimationScriptPlayable.Create(_graph,
                    new MuscleJob { handles = _handles, write = _downstreamWrite, read = _read });
                _downstream.SetInputCount(1);

                if (withUpstream)
                {
                    _upstream = AnimationScriptPlayable.Create(_graph,
                        new MuscleJob { handles = _handles, write = _upstreamWrite, read = default });
                    _graph.Connect(_upstream, 0, _downstream, 0);
                    _downstream.SetInputWeight(0, 1f);
                }

                var output = AnimationPlayableOutput.Create(_graph, "Body", animator);
                output.SetSourcePlayable(_downstream);
            }

            public void WriteAll(float[] values)
            {
                if (!_allValues.IsCreated) _allValues = new NativeArray<float>(MuscleHandle.muscleHandleCount, Allocator.Persistent);
                _allValues.CopyFrom(values);
                var job = _downstream.GetJobData<MuscleJob>();
                job.allValues = _allValues;
                _downstream.SetJobData(job);
                _downstreamWrite[0] = new MuscleWrite { index = -1, readIndex = -1, writeAll = 1 };
            }

            /// <summary>Pins the left foot to a world-space goal, at the given goal weight.</summary>
            public void SolveFootGoal(Vector3 worldGoal, float weight)
                => _downstreamWrite[0] = new MuscleWrite
                {
                    index = -1,
                    readIndex = -1,
                    goalPosition = worldGoal,
                    goalWeight = weight,
                    solveIK = 1,
                };

            public void Write(int muscle, float value)
                => _downstreamWrite[0] = new MuscleWrite { index = muscle, value = value, readIndex = _downstreamWrite[0].readIndex };

            public void WriteUpstream(int muscle, float value)
                => _upstreamWrite[0] = new MuscleWrite { index = muscle, value = value, readIndex = -1 };

            public void ReadBack(int muscle)
                => _downstreamWrite[0] = new MuscleWrite { index = -1, readIndex = muscle };

            public void Evaluate() => _graph.Evaluate(0f);

            public void Dispose()
            {
                if (_graph.IsValid()) _graph.Destroy();
                if (_handles.IsCreated) _handles.Dispose();
                if (_downstreamWrite.IsCreated) _downstreamWrite.Dispose();
                if (_upstreamWrite.IsCreated) _upstreamWrite.Dispose();
                if (_read.IsCreated) _read.Dispose();
                if (_allValues.IsCreated) _allValues.Dispose();
            }
        }

        private struct MuscleWrite
        {
            public int index;
            public float value;
            public int readIndex;
            public byte writeAll;
            public Vector3 goalPosition;
            public float goalWeight;
            public byte solveIK;
        }

        private struct MuscleJob : IAnimationJob
        {
            [ReadOnly] public NativeArray<MuscleHandle> handles;
            [ReadOnly] public NativeArray<MuscleWrite> write;
            [ReadOnly] public NativeArray<float> allValues;
            public NativeArray<float> read;

            public void ProcessRootMotion(AnimationStream stream) { }

            public void ProcessAnimation(AnimationStream stream)
            {
                if (!stream.isHumanStream) return;
                var human = stream.AsHuman();
                var request = write[0];

                if (request.readIndex >= 0 && read.IsCreated)
                    read[0] = human.GetMuscle(handles[request.readIndex]);

                if (request.writeAll == 1 && allValues.IsCreated)
                {
                    for (int i = 0; i < handles.Length; i++)
                        human.SetMuscle(handles[i], allValues[i]);
                }

                if (request.index >= 0)
                    human.SetMuscle(handles[request.index], request.value);

                if (request.solveIK == 1)
                {
                    human.SetGoalPosition(AvatarIKGoal.LeftFoot, request.goalPosition);
                    human.SetGoalWeightPosition(AvatarIKGoal.LeftFoot, request.goalWeight);
                    human.SolveIK();
                }
            }
        }
    }
}
