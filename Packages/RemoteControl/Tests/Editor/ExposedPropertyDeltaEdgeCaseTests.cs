// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Lilium.RemoteControl;
using Lilium.RemoteControl.Scene;
using Newtonsoft.Json.Linq;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// Serializer 分割(将来の低結合シーム抽出)に着手する前の安全網。
    /// 既存テストで未カバーかつ高リスクな2経路のみを対象とする:
    ///  - A群: ExposedPropertySerializer._ForceIncludeUntrackedProperties のうち、
    ///         「ExposedClass 未登録の要素型を持つコレクション」(単一参照プロパティは既存テストでカバー済み)。
    ///  - B群: 3層以上ネストした ExposedClass グラフでの delta / round-trip / callback 伝播
    ///         (既存テストは2層止まり)。
    /// プロダクションコードは変更しない。テストが失敗した場合は実装側の潜在バグの所見であり、
    /// テスト期待値を緩めてはならない。
    /// </summary>
    [TestFixture]
    public class ExposedPropertyDeltaEdgeCaseTests
    {
        #region Test Classes

        // ExposedClass 未登録のプレーン Serializable 要素型。
        [Serializable]
        public class PlainItem
        {
            public string label;
            public int amount;

            public PlainItem() { label = ""; amount = 0; }
            public PlainItem(string label, int amount) { this.label = label; this.amount = amount; }
        }

        // 未登録要素型のコレクションを ExposedField に持つ(_ForceIncludeUntrackedProperties 経路)。
        [Serializable]
        [ExposedClass("TestUntrackedListContainer")]
        public class TestUntrackedListContainer
        {
            [ExposedField]
            public int id;

            [ExposedField]
            public List<PlainItem> items = new List<PlainItem>();
        }

        // 3層ネスト: Root3 -> Mid3 -> Leaf3 (すべて [ExposedClass])。
        [Serializable]
        [ExposedClass("TestEdgeLeaf3")]
        public class Leaf3 : IExposedDeserializeCallback
        {
            [ExposedField]
            public int leafValue;

            [ExposedField]
            public string leafName = "leaf";

            [NonSerialized]
            public int callbackCount;

            void IExposedDeserializeCallback.OnAfterExposedDeserialize()
            {
                callbackCount++;
            }
        }

        [Serializable]
        [ExposedClass("TestEdgeMid3")]
        public class Mid3
        {
            [ExposedField]
            public string midName = "mid";

            [ExposedField]
            public Leaf3 leaf = new Leaf3();
        }

        [Serializable]
        [ExposedClass("TestEdgeRoot3")]
        public class Root3
        {
            [ExposedField]
            public int rootId;

            [ExposedField]
            public Mid3 mid = new Mid3();
        }

        #endregion

        private TestExposedObjectResolver _resolver;

        [SetUp]
        public void SetUp()
        {
            ExposedClass.Clear();

            ExposedClass.RegisterFromAttributes<TestUntrackedListContainer>();
            ExposedClass.RegisterFromAttributes<Leaf3>();
            ExposedClass.RegisterFromAttributes<Mid3>();
            ExposedClass.RegisterFromAttributes<Root3>();

            var toRemove = ExposedObjectRegistry.instances.ToList();
            foreach (var obj in toRemove)
            {
                obj.Unregister();
            }

            _resolver = new TestExposedObjectResolver();
        }

        [TearDown]
        public void TearDown()
        {
            var toRemove = ExposedObjectRegistry.instances.ToList();
            foreach (var obj in toRemove)
            {
                obj.Unregister();
            }
        }

        // ---- Group A: untracked-element-type collection ----

        [Test]
        public void UntrackedElementList_Changed_IncludedInSceneDelta_AndRoundTrips()
        {
            var testObj = new TestUntrackedListContainer
            {
                id = 1,
                items = new List<PlainItem> { new PlainItem("a", 1), new PlainItem("b", 2) }
            };
            var exposedClass = ExposedClass.Find(typeof(TestUntrackedListContainer));
            var exposedObj = new ExposedObject("untracked-list-1", exposedClass, testObj);
            ExposedPropertyUtility.SetDefault(exposedObj);

            // 要素を変更(未登録要素型のため dirty 追跡外 → 強制 include 経路)
            testObj.items = new List<PlainItem> { new PlainItem("a", 1), new PlainItem("x", 9) };

            var json = ExposedSceneSerializer.SceneToJson(
                new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver, SerializeMode.Delta);

            var jRoot = JObject.Parse(json);
            var jArray = jRoot["objects"] as JArray;
            Assert.IsNotNull(jArray, "objects array should exist");
            Assert.IsTrue(jArray.Count > 0, "changed untracked collection must produce an object entry");
            Assert.IsNotNull((jArray[0] as JObject)?["items"],
                "untracked-element-type list must be force-included in delta when changed");

            // round-trip: 同一 id の新規オブジェクトに復元
            exposedObj.Unregister();
            var testObj2 = new TestUntrackedListContainer
            {
                id = 1,
                items = new List<PlainItem> { new PlainItem("a", 1), new PlainItem("b", 2) }
            };
            var exposedObj2 = new ExposedObject("untracked-list-1", exposedClass, testObj2);
            ExposedPropertyUtility.SetDefault(exposedObj2);

            ExposedSceneSerializer.SceneFromJson(json, _resolver);

            Assert.AreEqual(2, testObj2.items.Count);
            Assert.AreEqual("a", testObj2.items[0].label);
            Assert.AreEqual(1, testObj2.items[0].amount);
            Assert.AreEqual("x", testObj2.items[1].label);
            Assert.AreEqual(9, testObj2.items[1].amount);
        }

        [Test]
        public void UntrackedElementList_ForPersistenceTrueAndFalse_NoDataLoss()
        {
            // forPersistence の true/false 双方で、変更された未登録要素コレクションが
            // delta 出力に欠落なく(全要素・全フィールド)含まれること。
            // (末尾省略や強制 include の分岐がデータを落とさない回帰検出)
            foreach (var forPersistence in new[] { true, false })
            {
                var toRemove = ExposedObjectRegistry.instances.ToList();
                foreach (var o in toRemove) o.Unregister();

                var testObj = new TestUntrackedListContainer
                {
                    id = 7,
                    items = new List<PlainItem> { new PlainItem("p", 3) }
                };
                var exposedClass = ExposedClass.Find(typeof(TestUntrackedListContainer));
                var exposedObj = new ExposedObject("untracked-list-fp", exposedClass, testObj);
                ExposedPropertyUtility.SetDefault(exposedObj);

                testObj.items = new List<PlainItem> { new PlainItem("p", 3), new PlainItem("q", 8) };

                var json = ExposedPropertySerializer.ToJson(
                    exposedObj, _resolver, isDirtyOnly: true, forPersistence: forPersistence);
                var jObj = JObject.Parse(json);

                var items = jObj["items"] as JArray;
                Assert.IsNotNull(items,
                    $"changed untracked collection must be present (forPersistence={forPersistence})");
                Assert.AreEqual(2, items.Count,
                    $"all elements must be serialized without truncation (forPersistence={forPersistence})");

                // 末尾要素(変更分)が全フィールド保持されていること
                var last = items[1] as JObject;
                Assert.IsNotNull(last, $"forPersistence={forPersistence}");
                Assert.AreEqual("q", last["label"]?.Value<string>(), $"forPersistence={forPersistence}");
                Assert.AreEqual(8, last["amount"]?.Value<int>(), $"forPersistence={forPersistence}");
            }
        }

        [Test]
        public void UntrackedElementList_Unchanged_ExcludedFromDelta()
        {
            var testObj = new TestUntrackedListContainer
            {
                id = 1,
                items = new List<PlainItem> { new PlainItem("a", 1) }
            };
            var exposedClass = ExposedClass.Find(typeof(TestUntrackedListContainer));
            var exposedObj = new ExposedObject("untracked-list-unchanged", exposedClass, testObj);
            ExposedPropertyUtility.SetDefault(exposedObj);

            // 変更しない
            var json = ExposedPropertySerializer.ToJson(
                exposedObj, _resolver, isDirtyOnly: true, forPersistence: true);
            var jObj = JObject.Parse(json);

            Assert.IsNull(jObj["items"],
                "unchanged untracked collection must NOT appear in delta");
            Assert.IsFalse(ExposedPropertySerializer.HasNonMetaProperties(jObj),
                "unchanged object must reduce to metadata only (excluded from scene delta)");
        }

        // ---- Group B: 3-level nested ExposedClass graph ----

        [Test]
        public void ThreeLevelNest_LeafChange_DeltaRoundTrips_SiblingsUntouched()
        {
            var testObj = new Root3 { rootId = 1 };
            var exposedClass = ExposedClass.Find(typeof(Root3));
            var exposedObj = new ExposedObject("nest3-leaf", exposedClass, testObj);
            ExposedPropertyUtility.SetDefault(exposedObj);

            testObj.mid.leaf.leafValue = 77;

            var json = ExposedSceneSerializer.SceneToJson(
                new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver, SerializeMode.Delta);

            var jRoot = JObject.Parse(json);
            var jArray = jRoot["objects"] as JArray;
            Assert.IsNotNull(jArray, "objects array should exist");
            Assert.IsTrue(jArray.Count > 0, "level-3 leaf change must produce an object entry");

            exposedObj.Unregister();
            var testObj2 = new Root3 { rootId = 1 };
            var exposedObj2 = new ExposedObject("nest3-leaf", exposedClass, testObj2);
            ExposedPropertyUtility.SetDefault(exposedObj2);

            ExposedSceneSerializer.SceneFromJson(json, _resolver);

            Assert.AreEqual(77, testObj2.mid.leaf.leafValue, "level-3 leaf value must round-trip");
            Assert.AreEqual(1, testObj2.rootId, "untouched root sibling must remain default");
            Assert.AreEqual("mid", testObj2.mid.midName, "untouched level-2 sibling must remain default");
            Assert.AreEqual("leaf", testObj2.mid.leaf.leafName, "untouched level-3 sibling must remain default");
        }

        [Test]
        public void ThreeLevelNest_Unchanged_ExcludedFromDelta()
        {
            var testObj = new Root3 { rootId = 5 };
            var exposedClass = ExposedClass.Find(typeof(Root3));
            var exposedObj = new ExposedObject("nest3-unchanged", exposedClass, testObj);
            ExposedPropertyUtility.SetDefault(exposedObj);

            var json = ExposedPropertySerializer.ToJson(
                exposedObj, _resolver, isDirtyOnly: true, forPersistence: true);
            var jObj = JObject.Parse(json);

            Assert.IsNull(jObj["mid"], "unchanged 3-level graph must not emit nested 'mid'");
            Assert.IsFalse(ExposedPropertySerializer.HasNonMetaProperties(jObj),
                "unchanged 3-level object must reduce to metadata only");
        }

        [Test]
        public void ThreeLevelNest_Callback_FiresOnLevel3Leaf_OnSceneFromJson()
        {
            var testObj = new Root3 { rootId = 1 };
            var exposedClass = ExposedClass.Find(typeof(Root3));
            var exposedObj = new ExposedObject("nest3-cb", exposedClass, testObj);
            ExposedPropertyUtility.SetDefault(exposedObj);

            testObj.mid.leaf.leafValue = 42;

            var json = ExposedSceneSerializer.SceneToJson(
                new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver, SerializeMode.Delta);

            exposedObj.Unregister();
            var testObj2 = new Root3 { rootId = 1 };
            var exposedObj2 = new ExposedObject("nest3-cb", exposedClass, testObj2);
            ExposedPropertyUtility.SetDefault(exposedObj2);

            ExposedSceneSerializer.SceneFromJson(json, _resolver);

            Assert.AreEqual(42, testObj2.mid.leaf.leafValue, "precondition: leaf value round-trips");
            Assert.GreaterOrEqual(testObj2.mid.leaf.callbackCount, 1,
                "IExposedDeserializeCallback must propagate to a level-3 nested ExposedClass");
        }
    }
}
