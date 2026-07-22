// Copyright (c) You-Ri, 2026
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Newtonsoft.Json.Linq;
using Lilium.RemoteControl;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// POST /exposed/batch (Epic Remote Control の /remote/batch 相当) の検証。
    /// <see cref="ExposedObjectHandler.ExecuteBatch"/> は HTTP 非依存コアなので、サーバーを
    /// 立てずにコンテナ + リゾルバを直接渡して駆動できる (CollectExposedObjects と同じ流儀)。
    ///
    /// 検証項目:
    /// - 複数プロパティ set が 1 リクエストで全件反映され、responses の順序・status・body が正しい。
    /// - 1 件失敗 (object/property 未解決) しても他オペは適用される (continue-on-error)。
    /// - POST /exposed/function 呼び出しが batch 経由で動作し result を返す。
    /// - method/path 欠落は per-item 400。
    /// </summary>
    [TestFixture]
    public class ExposedObjectBatchTests
    {
        const string kTypeName = "TestBatchTarget";

        [ExposedClass(kTypeName)]
        public class TestBatchComponent : MonoBehaviour
        {
            [ExposedField] public int a;
            [ExposedField] public float b;
            [ExposedField] public bool flag;

            [ExposedFunction]
            public int GetA() => a;
        }

        readonly List<GameObject> _created = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            ExposedObjectRegistry.ClearAll();
            ExposedClass.Clear();
            ExposedClass.RegisterFromAttributes<TestBatchComponent>();
        }

        [TearDown]
        public void TearDown()
        {
            ExposedObjectRegistry.ClearAll();
            foreach (var go in _created)
            {
                if (go != null) Object.DestroyImmediate(go);
            }
            _created.Clear();
        }

        // id には component の instanceID を使う。FindExposedObjectById の数値フォールバックが
        // 未登録 UnityEngine.Object を一時的な ExposedObjectHandle にラップする (本番と同じ経路)。
        TestBatchComponent CreateTarget(out string id)
        {
            var go = new GameObject("batch-target");
            _created.Add(go);
            var comp = go.AddComponent<TestBatchComponent>();
            id = ExposedObjectUtility.GetInstanceID(comp).ToString();
            return comp;
        }

        static ExposedObjectContainer EmptyContainer()
            => new ExposedObjectContainer("test", new List<IExposedObject>());

        static IExposedObjectResolver Resolver => DefaultExposedObjectResolver.Instance;

        static JArray RunBatch(JObject requests)
        {
            var json = ExposedObjectHandler.ExecuteBatch(EmptyContainer(), Resolver, requests.ToString());
            return (JArray)JObject.Parse(json)["responses"];
        }

        [Test]
        public void Batch_MultiplePropertySets_AllAppliedAndEchoed()
        {
            var comp = CreateTarget(out var id);

            var requests = new JObject
            {
                ["requests"] = new JArray
                {
                    new JObject { ["id"] = 1, ["method"] = "PUT", ["path"] = $"/exposed/object/{id}/a", ["body"] = new JObject { ["value"] = 5 } },
                    new JObject { ["id"] = 2, ["method"] = "PUT", ["path"] = $"/exposed/object/{id}/b", ["body"] = new JObject { ["value"] = 2.5f } },
                    new JObject { ["id"] = 3, ["method"] = "PUT", ["path"] = $"/exposed/object/{id}/flag", ["body"] = new JObject { ["value"] = true } },
                }
            };

            var responses = RunBatch(requests);

            Assert.AreEqual(3, responses.Count);
            Assert.IsTrue(responses.All(r => (int)r["status"] == 200), "all sets succeed");

            // 値がすべて反映される。
            Assert.AreEqual(5, comp.a);
            Assert.AreEqual(2.5f, comp.b);
            Assert.IsTrue(comp.flag);

            // id は順序どおりエコーされ、body は当該プロパティの応答 ({value,...}) を含む。
            Assert.AreEqual(1, (int)responses[0]["id"]);
            Assert.AreEqual(5, (int)responses[0]["body"]["value"]);
        }

        [Test]
        public void Batch_OneInvalidItem_OthersStillApplied()
        {
            var comp = CreateTarget(out var id);

            var requests = new JObject
            {
                ["requests"] = new JArray
                {
                    new JObject { ["id"] = "ok", ["method"] = "PUT", ["path"] = $"/exposed/object/{id}/a", ["body"] = new JObject { ["value"] = 7 } },
                    new JObject { ["id"] = "bad-object", ["method"] = "PUT", ["path"] = "/exposed/object/does-not-exist/a", ["body"] = new JObject { ["value"] = 1 } },
                    new JObject { ["id"] = "bad-path", ["method"] = "PUT", ["path"] = $"/exposed/object/{id}/nope", ["body"] = new JObject { ["value"] = 1 } },
                }
            };

            var responses = RunBatch(requests);

            Assert.AreEqual(7, comp.a, "valid set must apply despite sibling failures");

            Assert.AreEqual(200, (int)responses[0]["status"]);
            Assert.AreEqual(404, (int)responses[1]["status"]);
            Assert.AreEqual("Object not found", (string)responses[1]["body"]["error"]);
            Assert.AreEqual(404, (int)responses[2]["status"]);
            Assert.AreEqual("Property not found", (string)responses[2]["body"]["error"]);
        }

        [Test]
        public void Batch_FunctionInvoke_ReturnsResult()
        {
            var comp = CreateTarget(out var id);
            comp.a = 42;

            // A function's apiName is lower-cased (ExposedFunctionType uses ToLowerInvariant),
            // and the remote app invokes it the same way (functionName.toLowerCase()), so the
            // invoke path segment for GetA is "geta", not the PascalCase method name.
            var requests = new JObject
            {
                ["requests"] = new JArray
                {
                    new JObject { ["id"] = 1, ["method"] = "POST", ["path"] = $"/exposed/function/{id}/geta" },
                }
            };

            var responses = RunBatch(requests);

            Assert.AreEqual(200, (int)responses[0]["status"]);
            Assert.AreEqual(42, (int)responses[0]["body"]["result"]);
        }

        [Test]
        public void Batch_MissingMethod_ReportsInvalidRequestFormat()
        {
            CreateTarget(out var id);

            var requests = new JObject
            {
                ["requests"] = new JArray
                {
                    new JObject { ["id"] = 1, ["path"] = $"/exposed/object/{id}/a", ["body"] = new JObject { ["value"] = 5 } },
                }
            };

            var responses = RunBatch(requests);

            Assert.AreEqual(1, responses.Count);
            Assert.AreEqual(400, (int)responses[0]["status"]);
        }

        [Test]
        public void Batch_UnknownPath_ReportsNotFound()
        {
            CreateTarget(out _);

            var requests = new JObject
            {
                ["requests"] = new JArray
                {
                    new JObject { ["id"] = 1, ["method"] = "GET", ["path"] = "/api/status" },
                }
            };

            var responses = RunBatch(requests);

            Assert.AreEqual(404, (int)responses[0]["status"]);
            Assert.AreEqual("Not found", (string)responses[0]["body"]["error"]);
        }
    }
}
