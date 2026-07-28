// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Lilium.RemoteControl;
using Lilium.RemoteControl.LiveScene;
using Newtonsoft.Json.Linq;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// Serializer 分割(将来の低結合シーム抽出)に着手する前の安全網。
    /// 既存テストで未カバーかつ高リスクな2経路のみを対象とする:
    ///  - A群: LivePropertySerializer._ForceIncludeUntrackedProperties のうち、
    ///         「LiveClass 未登録の要素型を持つコレクション」(単一参照プロパティは既存テストでカバー済み)。
    ///  - B群: 3層以上ネストした LiveClass グラフでの delta / round-trip / callback 伝播
    ///         (既存テストは2層止まり)。
    /// プロダクションコードは変更しない。テストが失敗した場合は実装側の潜在バグの所見であり、
    /// テスト期待値を緩めてはならない。
    /// </summary>
    [TestFixture]
    public class LivePropertyDeltaEdgeCaseTests
    {
        #region Test Classes

        // LiveClass 未登録のプレーン Serializable 要素型。
        [Serializable]
        public class PlainItem
        {
            public string label;
            public int amount;

            public PlainItem() { label = ""; amount = 0; }
            public PlainItem(string label, int amount) { this.label = label; this.amount = amount; }
        }

        // 未登録要素型のコレクションを LiveField に持つ(_ForceIncludeUntrackedProperties 経路)。
        [Serializable]
        [LiveClass("TestUntrackedListContainer")]
        public class TestUntrackedListContainer
        {
            [LiveField]
            public int id;

            [LiveField]
            public List<PlainItem> items = new List<PlainItem>();
        }

        // 3層ネスト: Root3 -> Mid3 -> Leaf3 (すべて [LiveClass])。
        [Serializable]
        [LiveClass("TestEdgeLeaf3")]
        public class Leaf3 : ILiveDeserializeCallback
        {
            [LiveField]
            public int leafValue;

            [LiveField]
            public string leafName = "leaf";

            [NonSerialized]
            public int callbackCount;

            void ILiveDeserializeCallback.OnAfterLiveDeserialize()
            {
                callbackCount++;
            }
        }

        [Serializable]
        [LiveClass("TestEdgeMid3")]
        public class Mid3
        {
            [LiveField]
            public string midName = "mid";

            [LiveField]
            public Leaf3 leaf = new Leaf3();
        }

        [Serializable]
        [LiveClass("TestEdgeRoot3")]
        public class Root3
        {
            [LiveField]
            public int rootId;

            [LiveField]
            public Mid3 mid = new Mid3();
        }

        // 追跡型(LiveClass)のネスト要素を持つリスト。
        // LiveSceneFromJson(captureDefaults=false) で新規追加された要素の primitive が
        // 段⑤(object,false)→段⑥(primitive) を通る経路の検証用。
        [Serializable]
        [LiveClass("TestEdgeTrackedItem")]
        public class TrackedItem
        {
            [LiveField]
            public int value;
        }

        [Serializable]
        [LiveClass("TestEdgeTrackedListHolder")]
        public class TrackedListHolder
        {
            [LiveField]
            public List<TrackedItem> items = new List<TrackedItem>();
        }

        #endregion

        private TestLiveObjectResolver _resolver;

        [SetUp]
        public void SetUp()
        {
            LiveClass.Clear();

            LiveClass.RegisterFromAttributes<TestUntrackedListContainer>();
            LiveClass.RegisterFromAttributes<Leaf3>();
            LiveClass.RegisterFromAttributes<Mid3>();
            LiveClass.RegisterFromAttributes<Root3>();
            LiveClass.RegisterFromAttributes<TrackedItem>();
            LiveClass.RegisterFromAttributes<TrackedListHolder>();

            var toRemove = LiveObjectRegistry.instances.ToList();
            foreach (var obj in toRemove)
            {
                obj.Unregister();
            }

            _resolver = new TestLiveObjectResolver();
        }

        [TearDown]
        public void TearDown()
        {
            var toRemove = LiveObjectRegistry.instances.ToList();
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
            var liveClass = LiveClass.Find(typeof(TestUntrackedListContainer));
            var liveObj = new LiveObjectHandle("untracked-list-1", liveClass, testObj);
            LivePropertyUtility.SetDefault(liveObj);

            // 要素を変更(未登録要素型のため dirty 追跡外 → 強制 include 経路)
            testObj.items = new List<PlainItem> { new PlainItem("a", 1), new PlainItem("x", 9) };

            var json = LiveSceneSerializer.LiveSceneToJson(
                new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver, SerializeMode.Delta);

            var jRoot = JObject.Parse(json);
            var jArray = jRoot["objects"] as JArray;
            Assert.IsNotNull(jArray, "objects array should exist");
            Assert.IsTrue(jArray.Count > 0, "changed untracked collection must produce an object entry");
            Assert.IsNotNull((jArray[0] as JObject)?["items"],
                "untracked-element-type list must be force-included in delta when changed");

            // round-trip: 同一 id の新規オブジェクトに復元
            liveObj.Unregister();
            var testObj2 = new TestUntrackedListContainer
            {
                id = 1,
                items = new List<PlainItem> { new PlainItem("a", 1), new PlainItem("b", 2) }
            };
            var liveObj2 = new LiveObjectHandle("untracked-list-1", liveClass, testObj2);
            LivePropertyUtility.SetDefault(liveObj2);

            LiveSceneSerializer.LiveSceneFromJson(json, _resolver);

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
                var toRemove = LiveObjectRegistry.instances.ToList();
                foreach (var o in toRemove) o.Unregister();

                var testObj = new TestUntrackedListContainer
                {
                    id = 7,
                    items = new List<PlainItem> { new PlainItem("p", 3) }
                };
                var liveClass = LiveClass.Find(typeof(TestUntrackedListContainer));
                var liveObj = new LiveObjectHandle("untracked-list-fp", liveClass, testObj);
                LivePropertyUtility.SetDefault(liveObj);

                testObj.items = new List<PlainItem> { new PlainItem("p", 3), new PlainItem("q", 8) };

                var json = LivePropertySerializer.ToJson(
                    liveObj, _resolver, isDirtyOnly: true, forPersistence: forPersistence);
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
            var liveClass = LiveClass.Find(typeof(TestUntrackedListContainer));
            var liveObj = new LiveObjectHandle("untracked-list-unchanged", liveClass, testObj);
            LivePropertyUtility.SetDefault(liveObj);

            // 変更しない
            var json = LivePropertySerializer.ToJson(
                liveObj, _resolver, isDirtyOnly: true, forPersistence: true);
            var jObj = JObject.Parse(json);

            Assert.IsNull(jObj["items"],
                "unchanged untracked collection must NOT appear in delta");
            Assert.IsFalse(LivePropertySerializer.HasNonMetaProperties(jObj),
                "unchanged object must reduce to metadata only (excluded from scene delta)");
        }

        // ---- Group B: 3-level nested LiveClass graph ----

        [Test]
        public void ThreeLevelNest_LeafChange_DeltaRoundTrips_SiblingsUntouched()
        {
            var testObj = new Root3 { rootId = 1 };
            var liveClass = LiveClass.Find(typeof(Root3));
            var liveObj = new LiveObjectHandle("nest3-leaf", liveClass, testObj);
            LivePropertyUtility.SetDefault(liveObj);

            testObj.mid.leaf.leafValue = 77;

            var json = LiveSceneSerializer.LiveSceneToJson(
                new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver, SerializeMode.Delta);

            var jRoot = JObject.Parse(json);
            var jArray = jRoot["objects"] as JArray;
            Assert.IsNotNull(jArray, "objects array should exist");
            Assert.IsTrue(jArray.Count > 0, "level-3 leaf change must produce an object entry");

            liveObj.Unregister();
            var testObj2 = new Root3 { rootId = 1 };
            var liveObj2 = new LiveObjectHandle("nest3-leaf", liveClass, testObj2);
            LivePropertyUtility.SetDefault(liveObj2);

            LiveSceneSerializer.LiveSceneFromJson(json, _resolver);

            Assert.AreEqual(77, testObj2.mid.leaf.leafValue, "level-3 leaf value must round-trip");
            Assert.AreEqual(1, testObj2.rootId, "untouched root sibling must remain default");
            Assert.AreEqual("mid", testObj2.mid.midName, "untouched level-2 sibling must remain default");
            Assert.AreEqual("leaf", testObj2.mid.leaf.leafName, "untouched level-3 sibling must remain default");
        }

        [Test]
        public void ThreeLevelNest_Unchanged_ExcludedFromDelta()
        {
            var testObj = new Root3 { rootId = 5 };
            var liveClass = LiveClass.Find(typeof(Root3));
            var liveObj = new LiveObjectHandle("nest3-unchanged", liveClass, testObj);
            LivePropertyUtility.SetDefault(liveObj);

            var json = LivePropertySerializer.ToJson(
                liveObj, _resolver, isDirtyOnly: true, forPersistence: true);
            var jObj = JObject.Parse(json);

            Assert.IsNull(jObj["mid"], "unchanged 3-level graph must not emit nested 'mid'");
            Assert.IsFalse(LivePropertySerializer.HasNonMetaProperties(jObj),
                "unchanged 3-level object must reduce to metadata only");
        }

        [Test]
        public void ThreeLevelNest_Callback_FiresOnLevel3Leaf_OnLiveSceneFromJson()
        {
            var testObj = new Root3 { rootId = 1 };
            var liveClass = LiveClass.Find(typeof(Root3));
            var liveObj = new LiveObjectHandle("nest3-cb", liveClass, testObj);
            LivePropertyUtility.SetDefault(liveObj);

            testObj.mid.leaf.leafValue = 42;

            var json = LiveSceneSerializer.LiveSceneToJson(
                new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver, SerializeMode.Delta);

            liveObj.Unregister();
            var testObj2 = new Root3 { rootId = 1 };
            var liveObj2 = new LiveObjectHandle("nest3-cb", liveClass, testObj2);
            LivePropertyUtility.SetDefault(liveObj2);

            LiveSceneSerializer.LiveSceneFromJson(json, _resolver);

            Assert.AreEqual(42, testObj2.mid.leaf.leafValue, "precondition: leaf value round-trips");
            Assert.GreaterOrEqual(testObj2.mid.leaf.callbackCount, 1,
                "ILiveDeserializeCallback must propagate to a level-3 nested LiveClass");
        }

        // ---- Group C: captureDefaults propagation for new array element primitive ----

        [Test]
        public void NewArrayElementPrimitive_LiveSceneFromJsonThenReDelta_DoesNotLoseValue()
        {
            // 段⑥ primitive の captureDefault 不整合検証。
            // LiveSceneFromJson(captureDefaults=false) で新規追加された配列要素の primitive が
            // 段⑤(object, captureDefault:false)→段⑥(primitive) を通る。段⑥が captureDefaults を
            // 無視して default を誤キャプチャすると、続く Delta 保存で当該値が脱落し、
            // 2 段目 round-trip でデータが失われる(保存時データ消失)。
            var ec = LiveClass.Find(typeof(TrackedListHolder));

            // hop1: src は要素1個で SetDefault → 要素追加(value=99) → Delta 保存
            var src = new TrackedListHolder { items = new List<TrackedItem> { new TrackedItem { value = 1 } } };
            var srcLive = new LiveObjectHandle("tracked-h", ec, src);
            LivePropertyUtility.SetDefault(srcLive);
            src.items.Add(new TrackedItem { value = 99 });
            var s1 = LiveSceneSerializer.LiveSceneToJson(
                new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver, SerializeMode.Delta);
            srcLive.Unregister();

            // dst: 要素1個で SetDefault → LiveSceneFromJson(s1) で2要素へ(captureDefaults=false 経路) → 再 Delta
            var dst = new TrackedListHolder { items = new List<TrackedItem> { new TrackedItem { value = 1 } } };
            var dstLive = new LiveObjectHandle("tracked-h", ec, dst);
            LivePropertyUtility.SetDefault(dstLive);
            LiveSceneSerializer.LiveSceneFromJson(s1, _resolver);

            Assert.AreEqual(2, dst.items.Count, "precondition: new element loaded");
            Assert.AreEqual(99, dst.items[1].value, "precondition: loaded primitive value");

            var s2 = LiveSceneSerializer.LiveSceneToJson(
                new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver, SerializeMode.Delta);
            dstLive.Unregister();

            // hop2: s2 を別オブジェクトへロード。段⑥誤キャプチャがあると items[1].value が
            // s2 から脱落しており、ここで 99 が復元されない(データ消失)。
            var third = new TrackedListHolder { items = new List<TrackedItem> { new TrackedItem { value = 1 } } };
            var thirdLive = new LiveObjectHandle("tracked-h", ec, third);
            LivePropertyUtility.SetDefault(thirdLive);
            LiveSceneSerializer.LiveSceneFromJson(s2, _resolver);

            Assert.AreEqual(2, third.items.Count,
                "new element must survive a second delta round-trip");
            Assert.AreEqual(99, third.items[1].value,
                "new array element primitive must not be mis-captured as default (captureDefaults must propagate to segment ⑥)");
        }
    }
}
