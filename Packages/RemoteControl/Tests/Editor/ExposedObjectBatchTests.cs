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

        // 生の JSON 文字列を batch 本文として流す (ストリーム解析の忠実性テスト用)。
        // コメント・切断・末尾ゴミ・重複キー・コンテナ値など JObject 経由では作れない入力を渡せる。
        static JArray RunBatchRaw(ExposedObjectContainer container, string json)
        {
            var result = ExposedObjectHandler.ExecuteBatch(container, Resolver, json);
            return (JArray)JObject.Parse(result)["responses"];
        }

        static JArray RunBatchRaw(string json) => RunBatchRaw(EmptyContainer(), json);

        // ---- 変更フィード (/exposed/changes) ----
        // クライアントは毎サイクル、表示中プロパティの GET と同じバッチに変更フィードを 1 件載せる。
        // ここが 404 に落ちると値の追従だけが動き、一覧や他クライアントの変更が永久に届かなくなる
        // (症状が出るのが遅く、原因も遠いので必ずテストで押さえる)。

        [Test]
        public void Batch_Changes_WithoutSince_ReturnsRevisionAndNoIds()
        {
            ExposedChangeLog.Clear();
            ExposedChangeLog.Record("obj-1");

            var requests = new JObject
            {
                ["requests"] = new JArray
                {
                    new JObject { ["id"] = 1, ["method"] = "GET", ["path"] = "/exposed/changes" },
                }
            };

            var responses = RunBatch(requests);

            Assert.AreEqual(200, (int)responses[0]["status"]);
            Assert.AreEqual(1, (long)responses[0]["body"]["revision"]);
            Assert.IsEmpty((JArray)responses[0]["body"]["changes"], "sync-up call reports no ids");
            ExposedChangeLog.Clear();
        }

        [Test]
        public void Batch_Changes_WithSince_ReturnsIdsRecordedAfterIt()
        {
            ExposedChangeLog.Clear();
            ExposedChangeLog.Record("obj-1");
            ExposedChangeLog.Record("obj-2");

            var requests = new JObject
            {
                ["requests"] = new JArray
                {
                    new JObject { ["id"] = 1, ["method"] = "GET", ["path"] = "/exposed/changes?since=1" },
                }
            };

            var responses = RunBatch(requests);

            Assert.AreEqual(200, (int)responses[0]["status"]);
            Assert.AreEqual(2, (long)responses[0]["body"]["revision"]);
            var changes = ((JArray)responses[0]["body"]["changes"]).Select(t => (string)t).ToArray();
            CollectionAssert.AreEqual(new[] { "obj-2" }, changes);
            ExposedChangeLog.Clear();
        }

        [Test]
        public void Batch_Changes_AlongsidePropertyReads_BothSucceed()
        {
            ExposedChangeLog.Clear();
            var comp = CreateTarget(out var id);
            comp.a = 7;

            var requests = new JObject
            {
                ["requests"] = new JArray
                {
                    new JObject { ["id"] = -1, ["method"] = "GET", ["path"] = "/exposed/changes" },
                    new JObject { ["id"] = 0, ["method"] = "GET", ["path"] = $"/exposed/object/{id}/a" },
                }
            };

            var responses = RunBatch(requests);

            Assert.AreEqual(2, responses.Count);
            Assert.AreEqual(-1, (int)responses[0]["id"], "negative ids echo back unchanged");
            Assert.AreEqual(200, (int)responses[0]["status"]);
            Assert.AreEqual(200, (int)responses[1]["status"]);
            Assert.AreEqual(7, (int)responses[1]["body"]["value"]);
            ExposedChangeLog.Clear();
        }

        [Test]
        public void Batch_Changes_WithNonGetMethod_Returns405()
        {
            var requests = new JObject
            {
                ["requests"] = new JArray
                {
                    new JObject { ["id"] = 1, ["method"] = "PUT", ["path"] = "/exposed/changes" },
                }
            };

            var responses = RunBatch(requests);

            Assert.AreEqual(405, (int)responses[0]["status"]);
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

        // ---- ストリーム解析の忠実性 (JObject.Parse 経由と同じ意味論を固定する) ----
        // ExecuteBatch は本文を JObject ツリー化せずストリームで抽出するため、コメント/切断/末尾ゴミ/
        // 重複キー/コンテナ値/フィールド順などで挙動が JObject.Parse と一致することを回帰テストで固定する。

        [Test]
        public void Batch_FieldOrderIndependent_Applies()
        {
            var comp = CreateTarget(out var id);
            var json = @"{""requests"":[{""body"":{""value"":9},""path"":""/exposed/object/ID/a"",""method"":""PUT"",""id"":7}]}"
                .Replace("ID", id);

            var responses = RunBatchRaw(json);

            Assert.AreEqual(1, responses.Count);
            Assert.AreEqual(200, (int)responses[0]["status"]);
            Assert.AreEqual(7, (int)responses[0]["id"]);
            Assert.AreEqual(9, comp.a);
        }

        [Test]
        public void Batch_IdTypes_EchoedFaithfully()
        {
            var json = @"{""requests"":[
                {""id"":""str-id"",""method"":""GET"",""path"":""/api/status""},
                {""method"":""GET"",""path"":""/api/status""},
                {""id"":null,""method"":""GET"",""path"":""/api/status""}
            ]}";

            var responses = RunBatchRaw(json);

            Assert.AreEqual(3, responses.Count);
            Assert.AreEqual("str-id", (string)responses[0]["id"]);       // 文字列 id
            Assert.AreEqual(JTokenType.Null, responses[1]["id"].Type);   // 欠落 → null echo
            Assert.AreEqual(JTokenType.Null, responses[2]["id"].Type);   // JSON null → null echo
        }

        [Test]
        public void Batch_BodyNull_TreatedAsNoBody()
        {
            var comp = CreateTarget(out var id);
            comp.a = 42;
            var json = @"{""requests"":[{""id"":1,""method"":""POST"",""path"":""/exposed/function/ID/geta"",""body"":null}]}"
                .Replace("ID", id);

            var responses = RunBatchRaw(json);

            Assert.AreEqual(200, (int)responses[0]["status"]);
            Assert.AreEqual(42, (int)responses[0]["body"]["result"]);
        }

        [Test]
        public void Batch_NonObjectElements_ReportInvalidWithNullId()
        {
            var json = @"{""requests"":[5, null, {""id"":9,""method"":""GET"",""path"":""/api/status""}]}";

            var responses = RunBatchRaw(json);

            Assert.AreEqual(3, responses.Count);
            Assert.AreEqual(400, (int)responses[0]["status"]);
            Assert.AreEqual(JTokenType.Null, responses[0]["id"].Type);
            Assert.AreEqual(400, (int)responses[1]["status"]);
            Assert.AreEqual(404, (int)responses[2]["status"]);
            Assert.AreEqual(9, (int)responses[2]["id"]);
        }

        [Test]
        public void Batch_UnknownFields_Ignored()
        {
            var json = @"{""requests"":[{""id"":1,""extra"":{""nested"":[1,2,3]},""method"":""GET"",""foo"":42,""path"":""/api/status""}]}";

            var responses = RunBatchRaw(json);

            Assert.AreEqual(1, responses.Count);
            Assert.AreEqual(1, (int)responses[0]["id"]);
            Assert.AreEqual(404, (int)responses[0]["status"]);
        }

        [Test]
        public void Batch_TruncatedJson_EmptyResponsesAndNoSideEffects()
        {
            var comp = CreateTarget(out var id);
            // 末尾の root 閉じ } を欠いた切断入力。JObject.Parse なら例外 → 実行ゼロ。
            var json = @"{""requests"":[{""id"":1,""method"":""PUT"",""path"":""/exposed/object/ID/a"",""body"":{""value"":5}}]"
                .Replace("ID", id);

            var responses = RunBatchRaw(json);

            Assert.AreEqual(0, responses.Count);
            Assert.AreEqual(0, comp.a, "malformed batch must not apply any operation (atomicity)");
        }

        [Test]
        public void Batch_TruncatedMidStructure_Empty()
        {
            Assert.AreEqual(0, RunBatchRaw(@"{""requests"":[").Count);
            Assert.AreEqual(0, RunBatchRaw(@"{""requests"":[{""id"":1,""method"":""GET""").Count);
        }

        [Test]
        public void Batch_TrailingGarbage_EmptyResponsesAndNoSideEffects()
        {
            var comp = CreateTarget(out var id);
            var json = @"{""requests"":[{""id"":1,""method"":""PUT"",""path"":""/exposed/object/ID/a"",""body"":{""value"":5}}]} trailing-garbage"
                .Replace("ID", id);

            var responses = RunBatchRaw(json);

            Assert.AreEqual(0, responses.Count);
            Assert.AreEqual(0, comp.a, "trailing garbage must reject the whole batch");
        }

        [Test]
        public void Batch_Comments_SkippedLikeJObjectLoad()
        {
            var json = @"/* leading */ {""requests"":[{""id"":1, /* mid */ ""method"":""GET"",""path"":""/api/status"" /* trail */ }]}";

            var responses = RunBatchRaw(json);

            Assert.AreEqual(1, responses.Count);
            Assert.AreEqual(1, (int)responses[0]["id"]);
            Assert.AreEqual(404, (int)responses[0]["status"]);
        }

        [Test]
        public void Batch_DuplicateRequestsKey_LastWins()
        {
            var comp = CreateTarget(out var id);
            var json = (@"{""requests"":[{""id"":""A"",""method"":""PUT"",""path"":""/exposed/object/ID/a"",""body"":{""value"":1}}],"
                      + @"""requests"":[{""id"":""B"",""method"":""PUT"",""path"":""/exposed/object/ID/a"",""body"":{""value"":2}}]}")
                .Replace("ID", id);

            var responses = RunBatchRaw(json);

            Assert.AreEqual(1, responses.Count);
            Assert.AreEqual("B", (string)responses[0]["id"]);
            Assert.AreEqual(2, comp.a, "duplicate top-level requests key must be last-wins");
        }

        [Test]
        public void Batch_DuplicateFieldInRequest_LastWins()
        {
            var comp = CreateTarget(out var id);
            var json = @"{""requests"":[{""id"":1,""method"":""GET"",""method"":""PUT"",""path"":""/exposed/object/ID/a"",""body"":{""value"":3}}]}"
                .Replace("ID", id);

            var responses = RunBatchRaw(json);

            Assert.AreEqual(200, (int)responses[0]["status"]);
            Assert.AreEqual(3, comp.a, "duplicate field must be last-wins (PUT)");
        }

        [Test]
        public void Batch_ContainerValuedMethod_YieldsMethodNotAllowed()
        {
            CreateTarget(out var id);
            // method がコンテナ値: 従来 req["method"].ToString() は非空 → 405 (400 ではない)。
            var json = @"{""requests"":[{""id"":1,""method"":{""x"":1},""path"":""/exposed/object/ID/a""}]}"
                .Replace("ID", id);

            var responses = RunBatchRaw(json);

            Assert.AreEqual(1, responses.Count);
            Assert.AreEqual(405, (int)responses[0]["status"]);
        }

        [Test]
        public void Batch_EmptyArrayAndEmptyObject()
        {
            Assert.AreEqual(0, RunBatchRaw(@"{""requests"":[]}").Count);

            var responses = RunBatchRaw(@"{""requests"":[{}]}");
            Assert.AreEqual(1, responses.Count);
            Assert.AreEqual(400, (int)responses[0]["status"]);
            Assert.AreEqual(JTokenType.Null, responses[0]["id"].Type);
        }

        [Test]
        public void Batch_RequestsNotArrayOrAbsentOrNonObjectRoot_Empty()
        {
            Assert.AreEqual(0, RunBatchRaw(@"{""requests"":5}").Count);
            Assert.AreEqual(0, RunBatchRaw(@"{""other"":[1,2]}").Count);
            Assert.AreEqual(0, RunBatchRaw(@"{}").Count);
            Assert.AreEqual(0, RunBatchRaw(@"[1,2,3]").Count);
            Assert.AreEqual(0, RunBatchRaw("5").Count);
            Assert.AreEqual(0, RunBatchRaw("\"hello\"").Count);
            Assert.AreEqual(0, RunBatchRaw("").Count);
        }
    }
}
