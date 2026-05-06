// Copyright (c) You-Ri, 2026
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UniGLTF.SpringBoneJobs;
using UniVRM10;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace Lilium.VRChatAvatarTransfer.Editor
{
    internal static class PhysBoneToSpringBoneConverter
    {
        public readonly struct Result
        {
            public readonly int PhysBoneCount;
            public readonly int ColliderCount;
            public readonly int SpringCount;
            public readonly int JointCount;

            public Result(int pb, int col, int spring, int joints)
            {
                PhysBoneCount = pb;
                ColliderCount = col;
                SpringCount = spring;
                JointCount = joints;
            }
        }

        public static bool TryConvert(GameObject avatarRoot, out Result result)
        {
            result = default;
            if (avatarRoot == null)
            {
                VRChatAvatarTransferLog.Error("Avatar root is null.");
                return false;
            }

            var physBones = avatarRoot.GetComponentsInChildren<VRCPhysBone>(true);
            var physColliders = avatarRoot.GetComponentsInChildren<VRCPhysBoneCollider>(true);
            if (physBones.Length == 0 && physColliders.Length == 0)
            {
                VRChatAvatarTransferLog.Info($"'{avatarRoot.name}': no PhysBone components found.");
                result = new Result(0, 0, 0, 0);
                return true;
            }

            Undo.RegisterFullObjectHierarchyUndo(avatarRoot, "Convert PhysBone to SpringBone");

            var vrm10 = EnsureVrm10Instance(avatarRoot);

            var colliderMap = new Dictionary<VRCPhysBoneColliderBase, VRM10SpringBoneCollider>();
            foreach (var pbc in physColliders)
            {
                var converted = ConvertCollider(pbc);
                if (converted != null)
                {
                    colliderMap[pbc] = converted;
                }
            }

            int springCount = 0;
            int jointCount = 0;
            var usedTransforms = new HashSet<Transform>();
            try
            {
                foreach (var pb in physBones)
                {
                    var built = TryBuildSprings(pb, colliderMap, vrm10, usedTransforms);
                    springCount += built.springs;
                    jointCount += built.joints;
                }
            }
            catch (System.Exception ex)
            {
                VRChatAvatarTransferLog.Error($"Spring build failed: {ex}");
            }

            foreach (var pbc in physColliders)
            {
                if (pbc != null) Undo.DestroyObjectImmediate(pbc);
            }
            foreach (var pb in physBones)
            {
                if (pb != null) Undo.DestroyObjectImmediate(pb);
            }

            EditorUtility.SetDirty(vrm10);
            result = new Result(physBones.Length, physColliders.Length, springCount, jointCount);
            VRChatAvatarTransferLog.Info(
                $"'{avatarRoot.name}': converted {result.PhysBoneCount} PhysBone(s) -> {result.SpringCount} Spring(s) " +
                $"({result.JointCount} joint(s)), {result.ColliderCount} collider(s).");
            return true;
        }

        private static Vrm10Instance EnsureVrm10Instance(GameObject avatarRoot)
        {
            var existing = avatarRoot.GetComponent<Vrm10Instance>();
            if (existing == null)
            {
                existing = Undo.AddComponent<Vrm10Instance>(avatarRoot);
                VRChatAvatarTransferLog.Info(
                    $"'{avatarRoot.name}': added Vrm10Instance automatically as a SpringBone container.");
            }

            if (existing.Vrm == null)
            {
                existing.Vrm = Vrm10ObjectBuilder.BuildOrLoad(avatarRoot);
                EditorUtility.SetDirty(existing);
            }
            return existing;
        }

        private static VRM10SpringBoneCollider ConvertCollider(VRCPhysBoneCollider src)
        {
            if (src == null) return null;

            if (src.shapeType == VRCPhysBoneColliderBase.ShapeType.Plane && src.insideBounds)
            {
                VRChatAvatarTransferLog.Warn(
                    $"PhysBoneCollider on '{src.gameObject.name}' is a Plane with insideBounds=true. " +
                    "VRM10 SpringBone has no Plane.Inside variant; skipped.");
                return null;
            }

            // VRC PhysBoneCollider:
            //   position/rotation は rootTransform (未指定なら自身) のローカル空間
            //   Capsule では position が中心、軸は rotation*Y、height は全長 (両端の半球を含む)
            // VRM10SpringBoneCollider.Offset/Tail は collider 自身の Transform のローカル位置で、
            //   Capsule の場合は両端の半球中心を表す。
            // 解釈差を吸収するため、rootTransform 直下に子 GameObject を作り、その子の
            // localPosition/localRotation を VRC の position/rotation に合わせ、Offset=(0,0,0)
            // にした上で Tail だけ算出する (rotation は Transform に任せ、Offset/Tail で軸を作らない)。
            var parent = src.rootTransform != null ? src.rootTransform : src.transform;
            var child = new GameObject($"{src.name}_VRMCollider");
            Undo.RegisterCreatedObjectUndo(child, "Create VRM SpringBone Collider");
            Undo.SetTransformParent(child.transform, parent, "Reparent VRM Collider");
            child.transform.localPosition = src.position;
            child.transform.localRotation = src.rotation;
            child.transform.localScale = Vector3.one;

            var dst = Undo.AddComponent<VRM10SpringBoneCollider>(child);
            dst.ColliderType = MapColliderShape(src.shapeType, src.insideBounds);
            dst.Radius = Mathf.Max(0f, src.radius);

            switch (dst.ColliderType)
            {
                case VRM10SpringBoneColliderTypes.Sphere:
                case VRM10SpringBoneColliderTypes.SphereInside:
                    dst.Offset = Vector3.zero;
                    break;
                case VRM10SpringBoneColliderTypes.Capsule:
                case VRM10SpringBoneColliderTypes.CapsuleInside:
                {
                    // 子 GameObject の +Y 方向が VRC capsule 軸になる (localRotation = src.rotation)。
                    // 中心対称に Offset/Tail を両端の半球中心へ配置する。
                    float half = Mathf.Max(0f, src.height * 0.5f - dst.Radius);
                    dst.Offset = new Vector3(0f, -half, 0f);
                    dst.Tail = new Vector3(0f, half, 0f);
                    break;
                }
                case VRM10SpringBoneColliderTypes.Plane:
                    dst.Offset = Vector3.zero;
                    dst.Normal = Vector3.up;
                    break;
            }

            return dst;
        }

        private static VRM10SpringBoneColliderTypes MapColliderShape(VRCPhysBoneColliderBase.ShapeType shape, bool insideBounds)
        {
            switch (shape)
            {
                case VRCPhysBoneColliderBase.ShapeType.Sphere:
                    return insideBounds ? VRM10SpringBoneColliderTypes.SphereInside : VRM10SpringBoneColliderTypes.Sphere;
                case VRCPhysBoneColliderBase.ShapeType.Capsule:
                    return insideBounds ? VRM10SpringBoneColliderTypes.CapsuleInside : VRM10SpringBoneColliderTypes.Capsule;
                case VRCPhysBoneColliderBase.ShapeType.Plane:
                    return VRM10SpringBoneColliderTypes.Plane;
                default:
                    return VRM10SpringBoneColliderTypes.Sphere;
            }
        }

        private static (int springs, int joints) TryBuildSprings(
            VRCPhysBone pb,
            Dictionary<VRCPhysBoneColliderBase, VRM10SpringBoneCollider> colliderMap,
            Vrm10Instance vrm10,
            HashSet<Transform> usedTransforms)
        {
            var root = pb.GetRootTransform();
            if (root == null)
            {
                VRChatAvatarTransferLog.Warn($"PhysBone on '{pb.gameObject.name}' has no root transform. Skipped.");
                return (0, 0);
            }

            var ignored = new HashSet<Transform>();
            if (pb.ignoreTransforms != null)
            {
                foreach (var t in pb.ignoreTransforms)
                {
                    if (t != null) ignored.Add(t);
                }
            }

            // 分岐対応: ルートからリーフまでのチェーンを枝ごとに収集する。
            // 分岐点は親チェーンの末端ジョイントとしてのみ含まれ、子チェーンは
            // 分岐点の子から始まる (FastSpringBone の head 重複エラー回避)。
            var chains = new List<List<Transform>>();
            CollectBranchingChains(root, ignored, chains, new List<Transform>());

            // 他の PhysBone と重複する transform はスキップ
            for (int ci = 0; ci < chains.Count; ci++)
            {
                int removed = chains[ci].RemoveAll(t => !usedTransforms.Add(t));
                if (removed > 0)
                {
                    VRChatAvatarTransferLog.Warn(
                        $"PhysBone on '{pb.gameObject.name}' chain[{ci}]: {removed} transform(s) already used by another spring. Skipped those joints.");
                }
            }
            chains.RemoveAll(c => c.Count < 2);

            if (chains.Count == 0)
            {
                VRChatAvatarTransferLog.Warn($"PhysBone on '{pb.gameObject.name}' produced no usable chains. Skipped.");
                return (0, 0);
            }

            // ColliderGroup は PhysBone あたり 1 つ作って全チェーンで共有。
            // ルート GO に Component が大量に積まれるのを避けるため、専用の子 GO "ColliderGroup" にまとめる
            // (UniVRM Importer が "secondary" 子 GO にまとめるのと同じパターン)。
            VRM10SpringBoneColliderGroup sharedGroup = null;
            if (pb.colliders != null)
            {
                foreach (var pbc in pb.colliders)
                {
                    if (pbc == null || !colliderMap.TryGetValue(pbc, out var vrmCol)) continue;
                    if (sharedGroup == null)
                    {
                        var container = EnsureColliderGroupContainer(vrm10);
                        sharedGroup = Undo.AddComponent<VRM10SpringBoneColliderGroup>(container);
                        sharedGroup.Name = BuildSpringName(pb);
                        vrm10.SpringBone.ColliderGroups.Add(sharedGroup);
                    }
                    sharedGroup.Colliders.Add(vrmCol);
                }
            }

            int springCount = 0;
            int jointCount = 0;
            for (int ci = 0; ci < chains.Count; ci++)
            {
                var chain = chains[ci];
                var name = chains.Count == 1 ? BuildSpringName(pb) : $"{BuildSpringName(pb)}_{ci}";
                var spring = new Vrm10InstanceSpringBone.Spring(name);
                int added = PopulateSpringJoints(pb, chain, spring);
                if (added == 0) continue;

                if (sharedGroup != null) spring.ColliderGroups.Add(sharedGroup);
                vrm10.SpringBone.Springs.Add(spring);
                springCount++;
                jointCount += added;
            }

            if (Mathf.Abs(pb.immobile) > Mathf.Epsilon)
            {
                VRChatAvatarTransferLog.Warn($"PhysBone on '{pb.gameObject.name}' uses 'immobile' which has no VRM SpringBone equivalent. Ignored.");
            }

            return (springCount, jointCount);
        }

        private static int PopulateSpringJoints(VRCPhysBone pb, List<Transform> chain, Vrm10InstanceSpringBone.Spring spring)
        {
            // ファイル先頭の校正マップを式にフィットさせた近似:
            //   stiffnessForce = pull * (1 - spring) * 4
            //     (pull=1, spring=0) → 4 (固定相当の最大復元、VRM の LimitBreakSlider 上限)
            //     (pull=1, spring=1) → 0 (自由振動: pull が spring で打ち消される)
            //     (pull=0, *)        → 0 (ひも風: 復元なし)
            //   dragForce = 1 - pull * spring
            //     (pull=1, spring=1) → 0 (自由振動)
            //     その他              → 1 寄り (ひも風 / 固定はどちらも完全減衰)
            //   gravityPower = abs(gravity) * 20  (esperecyan に倣う)
            //
            // VRC stiffness (Advanced のみ) は本マッピングには含まれていないため使用しない。
            // pull / spring は対応する curve でジョイント位置 t ごとに評価する。
            const float StiffnessForceScale = 4.0f;
            const float GravityPowerScale = 20.0f;
            float gravity = Mathf.Abs(pb.gravity);
            Vector3 gravityDir = pb.gravity >= 0f ? new Vector3(0f, -1f, 0f) : new Vector3(0f, 1f, 0f);
            float radius = Mathf.Max(0f, pb.radius);
            var anglelimitType = MapLimitType(pb.limitType);

            int jointCount = 0;
            for (int i = 0; i < chain.Count; i++)
            {
                var tr = chain[i];
                var joint = tr.gameObject.GetComponent<VRM10SpringBoneJoint>();
                if (joint == null)
                {
                    joint = Undo.AddComponent<VRM10SpringBoneJoint>(tr.gameObject);
                }
                if (joint == null)
                {
                    VRChatAvatarTransferLog.Warn($"Failed to add VRM10SpringBoneJoint on '{tr.name}'. Skipped.");
                    continue;
                }

                float t = chain.Count <= 1 ? 0f : (float)i / (chain.Count - 1);
                var rawLimitEuler = new Vector3(
                    pb.limitRotation.x * EvaluateCurveOrOne(pb.limitRotationXCurve, t),
                    pb.limitRotation.y * EvaluateCurveOrOne(pb.limitRotationYCurve, t),
                    pb.limitRotation.z * EvaluateCurveOrOne(pb.limitRotationZCurve, t));

                float pullAmt   = Mathf.Max(0f,   pb.pull   * EvaluateCurveOrOne(pb.pullCurve,   t));
                float springAmt = Mathf.Clamp01(pb.spring * EvaluateCurveOrOne(pb.springCurve, t));

                joint.m_stiffnessForce = pullAmt * (1f - springAmt) * StiffnessForceScale;
                joint.m_dragForce = Mathf.Clamp01(1f - pullAmt * springAmt);
                joint.m_gravityPower = gravity * GravityPowerScale;
                joint.m_gravityDir = gravityDir;
                joint.m_jointRadius = radius * EvaluateCurveOrOne(pb.radiusCurve, t);
                joint.m_anglelimitType = anglelimitType;
                joint.m_limitSpaceOffset = Quaternion.Euler(rawLimitEuler);

                // VRC maxAngleX (X軸まわり) = Z 方向への振り角限界 = VRM phi (m_pitch)
                // VRC maxAngleZ (Z軸まわり) = X 方向への振り角限界 = VRM theta (m_yaw)
                float angleX = pb.maxAngleX * EvaluateCurveOrOne(pb.maxAngleXCurve, t) * Mathf.Deg2Rad;
                float angleZ = pb.maxAngleZ * EvaluateCurveOrOne(pb.maxAngleZCurve, t) * Mathf.Deg2Rad;
                joint.m_pitch = Mathf.Clamp(angleX, 0f, Mathf.PI);
                joint.m_yaw = anglelimitType == AnglelimitTypes.Spherical
                    ? Mathf.Clamp(angleZ, 0f, Mathf.PI * 0.5f)
                    : 0f;
                spring.Joints.Add(joint);
                jointCount++;
            }

            // VRM10 SpringBone の FastSpringBoneBuffer は spring.joints.Length - 1 までしかループしないため、
            // 末端 Joint 自体はシミュレーションされない。VRC PhysBone と挙動を揃えるため、
            // 末端 Joint の子に "_end" Transform を生成し、これをチェーンの最後の Joint (= tail 専用) として登録する。
            if (chain.Count >= 2)
            {
                var leaf = chain[chain.Count - 1];
                var parentOfLeaf = chain[chain.Count - 2];
                var endTr = EnsureLeafEndTransform(leaf, parentOfLeaf);
                if (endTr != null)
                {
                    var endJoint = endTr.gameObject.GetComponent<VRM10SpringBoneJoint>();
                    if (endJoint == null)
                    {
                        endJoint = Undo.AddComponent<VRM10SpringBoneJoint>(endTr.gameObject);
                    }
                    if (endJoint != null)
                    {
                        // _end Joint のパラメータは FastSpringBoneBuffer のループ範囲外で参照されないため、
                        // デフォルト値のままで問題ない (tail position の参照点としてのみ使われる)。
                        spring.Joints.Add(endJoint);
                        jointCount++;
                    }
                }
            }

            return jointCount;
        }

        private static GameObject EnsureColliderGroupContainer(Vrm10Instance vrm10)
        {
            const string ContainerName = "ColliderGroup";
            var existing = vrm10.transform.Find(ContainerName);
            if (existing != null) return existing.gameObject;

            var go = new GameObject(ContainerName);
            Undo.RegisterCreatedObjectUndo(go, "Create VRM SpringBone ColliderGroup Container");
            Undo.SetTransformParent(go.transform, vrm10.transform, "Reparent ColliderGroup Container");
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            return go;
        }

        private static Transform EnsureLeafEndTransform(Transform leaf, Transform parent)
        {
            var endName = $"{leaf.name}_end";

            // 既存の同名子があれば再利用 (再変換時の重複生成回避)
            var existing = leaf.Find(endName);
            if (existing != null) return existing;

            var delta = leaf.position - parent.position;
            if (delta.sqrMagnitude < 1e-10f)
            {
                // 親と末端が同位置: 方向を決めようがないので安全側にフォールバック
                VRChatAvatarTransferLog.Warn(
                    $"Leaf '{leaf.name}' coincides with its parent '{parent.name}'. " +
                    "Falling back to a small +Y offset for the SpringBone tail.");
                delta = Vector3.up * 0.01f;
            }

            var go = new GameObject(endName);
            Undo.RegisterCreatedObjectUndo(go, "Create VRM SpringBone Tail");
            Undo.SetTransformParent(go.transform, leaf, "Reparent VRM SpringBone Tail");
            go.transform.position = leaf.position + delta;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            return go.transform;
        }

        private static string BuildSpringName(VRCPhysBone pb)
        {
            var root = pb.GetRootTransform();
            return root != null ? root.name : pb.gameObject.name;
        }

        private static AnglelimitTypes MapLimitType(VRCPhysBoneBase.LimitType limitType)
        {
            switch (limitType)
            {
                case VRCPhysBoneBase.LimitType.Angle: return AnglelimitTypes.Cone;
                case VRCPhysBoneBase.LimitType.Hinge: return AnglelimitTypes.Hinge;
                case VRCPhysBoneBase.LimitType.Polar: return AnglelimitTypes.Spherical;
                default: return AnglelimitTypes.None;
            }
        }

        private static float EvaluateCurveOrOne(AnimationCurve curve, float t)
        {
            if (curve == null || curve.length == 0) return 1f;
            return curve.Evaluate(t);
        }

        private static void CollectFirstChildChain(Transform t, HashSet<Transform> ignored, List<Transform> chain)
        {
            if (t == null || ignored.Contains(t)) return;
            chain.Add(t);
            if (t.childCount == 0) return;
            CollectFirstChildChain(t.GetChild(0), ignored, chain);
        }

        // 分岐対応のチェーン収集。分岐点 (子が 2 個以上) は親チェーンの末端ジョイントとして含み、
        // 各子は新しいチェーンの先頭から始める。これにより同じ Transform が複数 Spring の
        // head として登録されることを避ける (FastSpringBone の重複エラー回避)。
        private static void CollectBranchingChains(
            Transform t, HashSet<Transform> ignored,
            List<List<Transform>> chains, List<Transform> current)
        {
            if (t == null || ignored.Contains(t))
            {
                if (current.Count > 0) chains.Add(new List<Transform>(current));
                return;
            }

            current.Add(t);

            int liveChildCount = 0;
            Transform onlyLiveChild = null;
            var liveChildren = new List<Transform>(t.childCount);
            for (int i = 0; i < t.childCount; i++)
            {
                var c = t.GetChild(i);
                if (ignored.Contains(c)) continue;
                liveChildren.Add(c);
                onlyLiveChild = c;
                liveChildCount++;
            }

            if (liveChildCount == 0)
            {
                chains.Add(new List<Transform>(current));
            }
            else if (liveChildCount == 1)
            {
                CollectBranchingChains(onlyLiveChild, ignored, chains, current);
            }
            else
            {
                // 分岐: 現在のチェーンをここで確定し、各子から新規チェーンを始める
                chains.Add(new List<Transform>(current));
                foreach (var c in liveChildren)
                {
                    CollectBranchingChains(c, ignored, chains, new List<Transform>());
                }
            }
            current.RemoveAt(current.Count - 1);
        }
    }
}
