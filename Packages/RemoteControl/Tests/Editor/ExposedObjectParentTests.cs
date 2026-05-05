// Copyright (c) You-Ri, 2026
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Newtonsoft.Json.Linq;

using GameObjectUtility = Lilium.RemoteControl.GameObjectUtility;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// ExposedObject 同士の親子関係の動作検証。
    /// - parentId は Unity hierarchy から派生する getter-only プロパティ
    /// - 親子関係の変更は ExposedObjectRegistry.SetParent 経由 (内部で GameObjectUtility.SetTransformParent)
    /// - @parent シリアライズは desired Unity hierarchy の復元ポイント
    /// </summary>
    [TestFixture]
    public class ExposedObjectParentTests
    {
        readonly List<GameObject> _createdObjects = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            ExposedObjectRegistry.ClearAll();
            ExposedClass.Clear();
            ExposedClass.RegisterFromAttributes<ExposedGameObject>();
            ExposedClass.RegisterFromAttributes<ExposedGameObjectWithTransform>();
            ExposedClass.RegisterFromAttributes<TransformRef>();
            ExposedClass.RegisterFromAttributes<ExposedObjectContainer>();
        }

        [TearDown]
        public void TearDown()
        {
            ExposedObjectRegistry.ClearAll();
            foreach (var go in _createdObjects)
            {
                if (go != null) Object.DestroyImmediate(go);
            }
            _createdObjects.Clear();
        }

        GameObject _CreateGO(string name)
        {
            var go = new GameObject(name);
            _createdObjects.Add(go);
            return go;
        }

        // ------------------------------------------------------------
        // parentId getter (Unity hierarchy からの派生)
        // ------------------------------------------------------------

        [Test]
        public void ParentId_Getter_NullWhenNoUnityParent()
        {
            var exposed = new ExposedGameObject(_CreateGO("no-parent"));
            exposed.OnEnable();

            Assert.IsNull(exposed.parentId);
        }

        [Test]
        public void ParentId_Getter_FollowsUnityTransformHierarchy()
        {
            var parentGO = _CreateGO("p");
            var childGO = _CreateGO("c");
            var parent = new ExposedGameObject(parentGO);
            var child = new ExposedGameObject(childGO);
            parent.OnEnable();
            child.OnEnable();

            childGO.transform.SetParent(parentGO.transform);

            Assert.AreEqual(parent.id, child.parentId);
        }

        [Test]
        public void ParentId_Getter_SkipsNonExposedAncestor()
        {
            var grandGO = _CreateGO("grand-exposed");
            var middleGO = _CreateGO("middle-not-exposed");
            var childGO = _CreateGO("child-exposed");
            middleGO.transform.SetParent(grandGO.transform);
            childGO.transform.SetParent(middleGO.transform);

            var grand = new ExposedGameObject(grandGO);
            var child = new ExposedGameObject(childGO);
            grand.OnEnable();
            child.OnEnable();

            // middle は ExposedObject ではないので透過して grand が親として返る
            Assert.AreEqual(grand.id, child.parentId);
        }

        // ------------------------------------------------------------
        // ExposedObjectRegistry.SetParent / GetChildren / 循環検出
        // ------------------------------------------------------------

        [Test]
        public void SetParent_ValidParentAndChild_ReparentsTransformAndUpdatesGetter()
        {
            var parentGO = _CreateGO("p");
            var childGO = _CreateGO("c");
            var parent = new ExposedGameObject(parentGO);
            var child = new ExposedGameObject(childGO);
            parent.OnEnable();
            child.OnEnable();

            var ok = ExposedObjectRegistry.SetParent(child.id, parent.id, out var err);

            Assert.IsTrue(ok, err);
            Assert.AreSame(parentGO.transform, childGO.transform.parent, "Unity hierarchy が更新される");
            Assert.AreEqual(parent.id, child.parentId, "getter が派生値を返す");
        }

        [Test]
        public void SetParent_SelfAsParent_RejectedWithError()
        {
            var a = new ExposedGameObject(_CreateGO("a"));
            a.OnEnable();

            var ok = ExposedObjectRegistry.SetParent(a.id, a.id, out var err);

            Assert.IsFalse(ok);
            Assert.IsNotNull(err);
        }

        [Test]
        public void SetParent_CyclicRelationship_Rejected()
        {
            var a = new ExposedGameObject(_CreateGO("a"));
            var b = new ExposedGameObject(_CreateGO("b"));
            a.OnEnable();
            b.OnEnable();

            // a -> b の親子関係を確立 (a が b 配下)
            Assert.IsTrue(ExposedObjectRegistry.SetParent(a.id, b.id, out _));

            // b -> a は循環になるので reject
            var ok = ExposedObjectRegistry.SetParent(b.id, a.id, out var err);

            Assert.IsFalse(ok);
            Assert.That(err, Does.Contain("Cyclic").IgnoreCase);
        }

        [Test]
        public void SetParent_ClearParent_AcceptsNull()
        {
            var parentGO = _CreateGO("p");
            var childGO = _CreateGO("c");
            var parent = new ExposedGameObject(parentGO);
            var child = new ExposedGameObject(childGO);
            parent.OnEnable();
            child.OnEnable();
            ExposedObjectRegistry.SetParent(child.id, parent.id, out _);

            var ok = ExposedObjectRegistry.SetParent(child.id, null, out var err);

            Assert.IsTrue(ok, err);
            Assert.IsNull(childGO.transform.parent, "Unity hierarchy が root に戻る");
            Assert.IsNull(child.parentId, "getter が null を返す");
        }

        [Test]
        public void SetParent_NonExistentParentId_Rejected()
        {
            var child = new ExposedGameObject(_CreateGO("c"));
            child.OnEnable();

            var ok = ExposedObjectRegistry.SetParent(child.id, "no-such-id", out var err);

            Assert.IsFalse(ok);
            Assert.IsNotNull(err);
        }

        [Test]
        public void GetChildren_ReturnsDirectChildrenOnly()
        {
            var grand = new ExposedGameObject(_CreateGO("grand"));
            var parent = new ExposedGameObject(_CreateGO("p"));
            var child1 = new ExposedGameObject(_CreateGO("c1"));
            var child2 = new ExposedGameObject(_CreateGO("c2"));
            grand.OnEnable(); parent.OnEnable(); child1.OnEnable(); child2.OnEnable();

            ExposedObjectRegistry.SetParent(parent.id, grand.id, out _);
            ExposedObjectRegistry.SetParent(child1.id, parent.id, out _);
            ExposedObjectRegistry.SetParent(child2.id, parent.id, out _);

            var children = ExposedObjectRegistry.GetChildren(parent.id).ToList();

            Assert.AreEqual(2, children.Count);
            CollectionAssert.Contains(children.Select(o => o.id).ToList(), child1.id);
            CollectionAssert.Contains(children.Select(o => o.id).ToList(), child2.id);
        }

        [Test]
        public void GetRootObjects_ExcludesObjectsWithParent()
        {
            var root = new ExposedGameObject(_CreateGO("root"));
            var child = new ExposedGameObject(_CreateGO("child"));
            root.OnEnable(); child.OnEnable();
            ExposedObjectRegistry.SetParent(child.id, root.id, out _);

            var roots = ExposedObjectRegistry.GetRootObjects().Select(o => o.id).ToList();

            CollectionAssert.Contains(roots, root.id);
            CollectionAssert.DoesNotContain(roots, child.id);
        }

        // ------------------------------------------------------------
        // @parent シリアライズ / デシリアライズ
        // ------------------------------------------------------------

        [Test]
        public void Serialize_ExposedObjectWithParent_EmitsAtParentMetadata()
        {
            var parent = new ExposedGameObject(_CreateGO("parent-s"));
            var child = new ExposedGameObject(_CreateGO("child-s"));
            parent.OnEnable(); child.OnEnable();
            ExposedObjectRegistry.SetParent(child.id, parent.id, out _);

            var json = ExposedPropertySerializer.ToJson(
                child.exposedObject, DefaultExposedObjectResolver.Instance);
            var root = JObject.Parse(json);

            Assert.AreEqual(parent.id, root["@parent"]?.Value<string>());
        }

        [Test]
        public void Serialize_ExposedObjectWithoutParent_OmitsAtParent()
        {
            var child = new ExposedGameObject(_CreateGO("child-no-parent"));
            child.OnEnable();

            var json = ExposedPropertySerializer.ToJson(
                child.exposedObject, DefaultExposedObjectResolver.Instance);
            var root = JObject.Parse(json);

            Assert.IsNull(root["@parent"]);
        }

        [Test]
        public void Deserialize_JsonWithAtParent_RestoresUnityHierarchy()
        {
            var parentGO = _CreateGO("p-d");
            var childGO = _CreateGO("c-d");
            var parent = new ExposedGameObject(parentGO);
            var child = new ExposedGameObject(childGO);
            parent.OnEnable(); child.OnEnable();

            var json = new JObject
            {
                ["@type"] = child.exposedObject.targetTypeName,
                ["@id"] = child.id,
                ["@parent"] = parent.id,
            }.ToString();

            ExposedPropertySerializer.FromJson(json, child.exposedObject);

            Assert.AreSame(parentGO.transform, childGO.transform.parent,
                "@parent デシリアライズは SetParent 経由で Unity hierarchy に反映される");
            Assert.AreEqual(parent.id, child.parentId);
        }

        // ------------------------------------------------------------
        // TransformRef
        // ------------------------------------------------------------

        [Test]
        public void TransformRef_StoresOwnOwnerName()
        {
            var path = new TransformRef();
            path.ownerName = "standalone-name";

            Assert.AreEqual("standalone-name", path.ownerName);
        }

        [Test]
        public void TransformRef_ChangingOwnerName_PreservesTransformPath()
        {
            // ownerName を切り替えても _transformPath は保持される。
            // (新しい親配下にも同じ path / name が存在するケースで利便性が高い。
            //  解決できなければ Resolve のフォールバックに委ねる。)
            var path = new TransformRef("parent-1", "some-bone");
            path.ownerName = "parent-2";

            Assert.AreEqual("parent-2", path.ownerName);
            Assert.AreEqual("some-bone", path.transformPath,
                "親切替で transformPath はクリアされず保持される");
        }

        [Test]
        public void TransformRef_ResolveOwner_FindsGameObjectByName()
        {
            var parentGO = _CreateGO("parent-root");
            var parent = new ExposedGameObject(parentGO);
            parent.OnEnable();

            var path = new TransformRef();
            path.ownerName = parentGO.name;

            var resolved = path.ResolveOwner();

            Assert.AreSame(parentGO, resolved);
        }

        [Test]
        public void TransformRef_ResolveOwner_ReturnsNullWhenNameUnregistered()
        {
            // name に対応する ExposedObject が Registry に未登録なら null。
            // 起動シーケンス中の「親がまだ登録されていない」状態をシミュレート。
            _CreateGO("Main Avatar"); // ExposedGameObject を作らない = Registry に未登録

            var path = new TransformRef("Main Avatar", "");

            Assert.IsNull(path.ResolveOwner(),
                "Registry 未登録なら ResolveOwner は null");
            Assert.IsFalse(path.isResolved,
                "未解決状態は isResolved false で表現される");
        }

        [Test]
        public void TransformRef_Resolve_EmptyTransformName_ReturnsRootTransform()
        {
            var parentGO = _CreateGO("p-resolve-empty");
            var parent = new ExposedGameObject(parentGO);
            parent.OnEnable();

            var path = new TransformRef();
            path.ownerName = parentGO.name;

            Assert.AreSame(parentGO.transform, path.Resolve());
        }

        [Test]
        public void TransformRef_Resolve_ChildTransformName_ReturnsDescendant()
        {
            var parentGO = _CreateGO("p-resolve-child");
            var childGO = _CreateGO("Head");
            childGO.transform.SetParent(parentGO.transform);

            var parent = new ExposedGameObject(parentGO);
            parent.OnEnable();

            var path = new TransformRef();
            path.ownerName = parentGO.name;
            path.transformName = "Head";

            Assert.AreSame(childGO.transform, path.Resolve());
        }

        [Test]
        public void TransformRef_Resolve_DuplicateNames_UsesExactPath()
        {
            // 同名 Transform が子孫に複数ある場合でも、相対 path 指定なら正しい方を解決する。
            var parentGO = _CreateGO("p-dup");
            var branchA = _CreateGO("BranchA");
            var branchB = _CreateGO("BranchB");
            branchA.transform.SetParent(parentGO.transform);
            branchB.transform.SetParent(parentGO.transform);

            var headInA = _CreateGO("Head");
            var headInB = _CreateGO("Head");
            headInA.transform.SetParent(branchA.transform);
            headInB.transform.SetParent(branchB.transform);

            var parent = new ExposedGameObject(parentGO);
            parent.OnEnable();

            var path = new TransformRef(parentGO.name, "BranchB/Head");
            Assert.AreSame(headInB.transform, path.Resolve(),
                "path で指定された BranchB 配下の Head が解決される");
        }

        [Test]
        public void TransformRef_SetTransformName_StoresRelativePath()
        {
            // setter は name を受け取り、root からの相対 path に正規化して格納する。
            // 表示用 getter (transformName) は path の leaf を返す。
            var parentGO = _CreateGO("p-setter-path");
            var midGO = _CreateGO("Mid");
            var leafGO = _CreateGO("Leaf");
            midGO.transform.SetParent(parentGO.transform);
            leafGO.transform.SetParent(midGO.transform);

            var parent = new ExposedGameObject(parentGO);
            parent.OnEnable();

            var path = new TransformRef();
            path.ownerName = parentGO.name;
            path.transformName = "Leaf";

            Assert.AreSame(leafGO.transform, path.Resolve(),
                "正規化された path で Resolve が leaf まで辿れる");
            Assert.AreEqual("Leaf", path.transformName,
                "表示 getter は path の leaf name を返す");
        }

        [Test]
        public void TransformRef_InitFromTransform_StoresRelativePath()
        {
            // Unity hierarchy から snapshot する際、root→target の相対 path を格納する。
            var rootGO = _CreateGO("init-root");
            var midGO = _CreateGO("Mid");
            var leafGO = _CreateGO("Leaf");
            midGO.transform.SetParent(rootGO.transform);
            leafGO.transform.SetParent(midGO.transform);

            var rootExposed = new ExposedGameObject(rootGO);
            rootExposed.OnEnable();

            var path = new TransformRef();
            path.InitFromTransform(leafGO.transform, silent: true);

            Assert.AreSame(leafGO.transform, path.Resolve(),
                "InitFromTransform で格納された path が正しく解決される");
            Assert.AreEqual("Leaf", path.transformName,
                "表示用 name は path の leaf セグメント");
        }

        // ------------------------------------------------------------
        // TransformRef.SearchType
        // ------------------------------------------------------------

        [Test]
        public void TransformRef_DefaultSearchType_IsPath()
        {
            // 既存コードとの互換確認: パラメータ無しコンストラクタも 2 引数コンストラクタも Path がデフォルト。
            var defaultRef = new TransformRef();
            Assert.AreEqual(TransformRef.SearchType.Path, defaultRef.searchType,
                "デフォルトコンストラクタは Path モード");

            var twoArgRef = new TransformRef("parent-id", "Head");
            Assert.AreEqual(TransformRef.SearchType.Path, twoArgRef.searchType,
                "2 引数コンストラクタも Path モード (旧呼び出しの互換性)");
        }

        [Test]
        public void TransformRef_NameMode_ResolvesDescendantByName()
        {
            // Name モード: 親 root 配下を再帰的に検索して同名 Transform を first-match で返す。
            var parentGO = _CreateGO("p-name-mode");
            var midGO = _CreateGO("Mid");
            var headGO = _CreateGO("Head");
            midGO.transform.SetParent(parentGO.transform);
            headGO.transform.SetParent(midGO.transform);

            var parent = new ExposedGameObject(parentGO);
            parent.OnEnable();

            var path = new TransformRef(parentGO.name, "Head", TransformRef.SearchType.Name);

            Assert.AreSame(headGO.transform, path.Resolve(),
                "Name モードでは深い階層の同名 Transform を解決できる");
        }

        [Test]
        public void TransformRef_NameMode_TransformNameSetter_StoresRawValue()
        {
            // Name モードでは _NormalizeTransformInput が path 化せず生値を保持する。
            var parentGO = _CreateGO("p-name-setter");
            var midGO = _CreateGO("Mid");
            var headGO = _CreateGO("Head");
            midGO.transform.SetParent(parentGO.transform);
            headGO.transform.SetParent(midGO.transform);

            var parent = new ExposedGameObject(parentGO);
            parent.OnEnable();

            var path = new TransformRef(parentGO.name, "", TransformRef.SearchType.Name);
            path.transformName = "Head";

            Assert.AreEqual("Head", path.transformPath,
                "Name モードの setter は生値をそのまま transformPath に格納する");
            Assert.AreEqual("Head", path.transformName,
                "transformName getter は leaf を返すので Head のまま");
            Assert.AreSame(headGO.transform, path.Resolve(),
                "格納された name で深い階層の Head が解決される");
        }

        [Test]
        public void TransformRef_NameMode_InitFromTransform_StoresLeafNameOnly()
        {
            // Name モードで InitFromTransform を呼ぶと、相対 path ではなく leaf name のみが格納される。
            var rootGO = _CreateGO("init-name-root");
            var midGO = _CreateGO("Mid");
            var leafGO = _CreateGO("Leaf");
            midGO.transform.SetParent(rootGO.transform);
            leafGO.transform.SetParent(midGO.transform);

            var rootExposed = new ExposedGameObject(rootGO);
            rootExposed.OnEnable();

            var path = new TransformRef("", "", TransformRef.SearchType.Name);
            path.InitFromTransform(leafGO.transform, silent: true);

            Assert.AreEqual("Leaf", path.transformPath,
                "Name モードでは leaf name のみが格納される (path 化されない)");
            Assert.AreSame(leafGO.transform, path.Resolve(),
                "格納された name で Leaf が解決される");
        }

        [Test]
        public void TransformRef_SearchTypeSetter_PreservesTransformPathAndFiresOnChanged()
        {
            // searchType を切り替えても _transformPath は保持される。onChanged が発火する。
            var path = new TransformRef("parent-id", "Armature/Hips/Head");
            int changedCount = 0;
            path.onChanged += () => changedCount++;

            path.searchType = TransformRef.SearchType.Name;

            Assert.AreEqual(TransformRef.SearchType.Name, path.searchType);
            Assert.AreEqual("Armature/Hips/Head", path.transformPath,
                "searchType 変更で transformPath はクリアされず保持される");
            Assert.AreEqual(1, changedCount,
                "searchType 変更時に onChanged が一度発火する");
        }

        [Test]
        public void TransformRef_SearchTypeSetter_NoOpWhenSameValue()
        {
            // 同じ値を再代入したときは onChanged を発火させない。
            var path = new TransformRef("parent-id", "Armature/Hips/Head");
            int changedCount = 0;
            path.onChanged += () => changedCount++;

            path.searchType = TransformRef.SearchType.Path;

            Assert.AreEqual("Armature/Hips/Head", path.transformPath);
            Assert.AreEqual(0, changedCount,
                "同値代入では onChanged は発火しない");
        }

        [Test]
        public void TransformRef_PathMode_SingleSegmentNameFallback_StillWorks()
        {
            // 旧データ互換: Path モードでスラッシュ無しの値が深い階層の同名 Transform を name fallback で解決する。
            // SearchType 追加後もこの挙動が壊れていないことを担保する。
            var parentGO = _CreateGO("p-path-fallback");
            var branchGO = _CreateGO("Branch");
            var headGO = _CreateGO("Head");
            branchGO.transform.SetParent(parentGO.transform);
            headGO.transform.SetParent(branchGO.transform);

            var parent = new ExposedGameObject(parentGO);
            parent.OnEnable();

            var path = new TransformRef(parentGO.name, "Head"); // Path モード (デフォルト)
            Assert.AreSame(headGO.transform, path.Resolve(),
                "Path モードでも単一セグメントは name fallback で深い階層の Head を解決できる (旧挙動互換)");
        }

        // ------------------------------------------------------------
        // GameObjectUtility.SetTransformParent / hierarchy 購読
        // ------------------------------------------------------------

        [Test]
        public void SetTransformParent_ReparentsAndMaintainsLocalTRS()
        {
            var child = _CreateGO("c-reparent");
            var parent = _CreateGO("p-reparent");
            child.transform.localPosition = new Vector3(1, 2, 3);

            GameObjectUtility.SetTransformParent(child.transform, parent.transform, "test");

            Assert.AreSame(parent.transform, child.transform.parent);
            Assert.AreEqual(new Vector3(1, 2, 3), child.transform.localPosition);
        }

        [Test]
        public void InvokeHierarchyChanged_FiresRegisteredCallback()
        {
            int callCount = 0;
            System.Action callback = () => callCount++;
            GameObjectUtility.RegisterHierarchyChanged(callback);
            try
            {
                GameObjectUtility.InvokeHierarchyChanged();
                Assert.AreEqual(1, callCount);
            }
            finally
            {
                GameObjectUtility.UnregisterHierarchyChanged(callback);
            }
        }

        // ------------------------------------------------------------
        // TransformRef.Apply 未解決 owner の扱い
        // ------------------------------------------------------------

        [Test]
        public void Attach_UnresolvedOwner_DoesNotDetach()
        {
            // Play mode 突入時、親 ExposedObject がまだ registry に未登録の状態で Attach が走るケース。
            // ownerName は指定されているが Registry の name 検索でヒットしない → 現状の Unity hierarchy を維持する。
            var actualParentGO = _CreateGO("actual-parent");
            var selfGO = _CreateGO("self");
            selfGO.transform.SetParent(actualParentGO.transform);

            var path = new TransformRef("unregistered-name", "");
            Transform attached = null;
            TransformAttachment.Attach(path, selfGO.transform, ref attached);

            Assert.AreSame(actualParentGO.transform, selfGO.transform.parent,
                "未解決状態では Unity hierarchy を維持する (detach しない)");
        }

        [Test]
        public void Attach_EmptyOwner_DetachesToRoot()
        {
            // ownerName 未指定 = 明示的な root 配置の意図なので従来通り detach する (regression 防止)
            var actualParentGO = _CreateGO("actual-parent");
            var selfGO = _CreateGO("self");
            selfGO.transform.SetParent(actualParentGO.transform);

            var path = new TransformRef();
            Transform attached = null;
            TransformAttachment.Attach(path, selfGO.transform, ref attached);

            Assert.IsNull(selfGO.transform.parent,
                "ownerName 未指定なら root に戻す");
        }

        [Test]
        public void Attach_ResolvedOwner_AttachesToParent()
        {
            // 通常ケース: ownerName が解決できる状態で Attach → 実際に親付け
            var parentGO = _CreateGO("resolved-parent");
            var parent = new ExposedGameObject(parentGO);
            parent.OnEnable();

            var selfGO = _CreateGO("self");

            var path = new TransformRef();
            path.ownerName = parentGO.name;
            Transform attached = null;
            TransformAttachment.Attach(path, selfGO.transform, ref attached);

            Assert.AreSame(parentGO.transform, selfGO.transform.parent);
            Assert.AreSame(parentGO.transform, attached,
                "attached キャッシュが解決結果と同期する");
        }

        [Test]
        public void Attach_Idempotent_WhenAlreadyAttachedToSameTarget()
        {
            // 冪等性: 同じターゲットに対して 2 回 Attach を呼んでも余分な SetParent が走らない。
            var parentGO = _CreateGO("idem-parent");
            var parent = new ExposedGameObject(parentGO);
            parent.OnEnable();

            var selfGO = _CreateGO("self");

            var path = new TransformRef();
            path.ownerName = parentGO.name;
            Transform attached = null;
            TransformAttachment.Attach(path, selfGO.transform, ref attached);

            // 2 回目: 既に attached == target なので冪等
            TransformAttachment.Attach(path, selfGO.transform, ref attached);

            Assert.AreSame(parentGO.transform, selfGO.transform.parent);
            Assert.AreSame(parentGO.transform, attached);
        }

        [Test]
        public void Detach_ClearsAttachedAndResetsParent()
        {
            // Detach は SetParent(null) を呼び attached をクリアする。
            var parentGO = _CreateGO("detach-parent");
            var parent = new ExposedGameObject(parentGO);
            parent.OnEnable();

            var selfGO = _CreateGO("self");

            var path = new TransformRef();
            path.ownerName = parentGO.name;
            Transform attached = null;
            TransformAttachment.Attach(path, selfGO.transform, ref attached);

            TransformAttachment.Detach(selfGO.transform, ref attached);

            Assert.IsNull(attached);
            Assert.IsNull(selfGO.transform.parent);
        }

        // ------------------------------------------------------------
        // ExposedGameObjectWithTransform の hierarchy 同期
        // ------------------------------------------------------------

        [Test]
        public void WithTransform_UserDrag_SyncsTransformRef()
        {
            // ExposedGameObjectWithTransform は自前で hierarchy change を購読し、
            // 実際の transform.parent を TransformRef (_ownerName) に silent で反映する。
            // これにより次の TransformAttachment.Attach で user drag を revert しない。
            var parentGO = _CreateGO("wt-parent");
            var childGO = _CreateGO("wt-child");

            var parentExposed = new ExposedGameObject(parentGO);
            var childExposed = new ExposedGameObjectWithTransform(childGO);
            parentExposed.OnEnable();
            childExposed.OnEnable();

            // ユーザーが Editor でドラッグした状況を模倣
            childGO.transform.SetParent(parentGO.transform);
            GameObjectUtility.InvokeHierarchyChanged();

            // ResolveOwner で内部状態 (_ownerName) を確認
            Assert.AreSame(parentGO, childExposed.parent.ResolveOwner(),
                "hierarchy 変更が TransformRef._ownerName に silent 同期される");
            Assert.AreSame(parentGO.transform, childGO.transform.parent,
                "その後の lifecycle 再評価でも user drag が維持される");
        }
    }
}
