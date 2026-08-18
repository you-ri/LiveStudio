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
    /// POST /live/batch の検証。
    /// <see cref="LiveObjectHandler.ExecuteBatch"/> は HTTP 非依存コアなので、サーバーを
    /// 立てずにコンテナ + リゾルバを直接渡して駆動できる (CollectLiveObjects と同じ流儀)。
    ///
    /// 検証項目:
    /// - 複数プロパティ set が 1 リクエストで全件反映され、responses の順序・status・body が正しい。
    /// - 1 件失敗 (object/property 未解決) しても他オペは適用される (continue-on-error)。
    /// - POST /live/function 呼び出しが batch 経由で動作し result を返す。
    /// - method/path 欠落は per-item 400。
    /// </summary>
    [TestFixture]
    public class LiveObjectBatchTests
    {
        const string kTypeName = "TestBatchTarget";

        [LiveClass(kTypeName)]
        public class TestBatchComponent : MonoBehaviour
        {
            [LiveField] public int a;
            [LiveField] public float b;
            [LiveField] public bool flag;

            [LiveFunction]
            public int GetA() => a;
        }

        readonly List<GameObject> _created = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            LiveObjectRegistry.ClearAll();
            LiveClass.Clear();
            LiveClass.RegisterFromAttributes<TestBatchComponent>();
        }

        [TearDown]
        public void TearDown()
        {
            LiveObjectRegistry.ClearAll();
            foreach (var go in _created)
            {
                if (go != null) Object.DestroyImmediate(go);
            }
            _created.Clear();
        }

        // id には component の instanceID を使う。FindLiveObjectById の数値フォールバックが
        // 未登録 UnityEngine.Object を一時的な LiveObjectHandle にラップする (本番と同じ経路)。
        TestBatchComponent CreateTarget(out string id)
        {
            var go = new GameObject("batch-target");
            _created.Add(go);
            var comp = go.AddComponent<TestBatchComponent>();
            id = LiveObjectUtility.GetInstanceID(comp).ToString();
            return comp;
        }

        static LiveObjectContainer EmptyContainer()
            => new LiveObjectContainer("test", new List<ILiveObject>());

        static ILiveObjectResolver Resolver => DefaultLiveObjectResolver.Instance;

        static JArray RunBatch(JObject requests)
        {
            var json = LiveObjectHandler.ExecuteBatch(EmptyContainer(), Resolver, requests.ToString());
            return (JArray)JObject.Parse(json)["responses"];
        }

        // 生の JSON 文字列を batch 本文として流す (ストリーム解析の忠実性テスト用)。
        // コメント・切断・末尾ゴミ・重複キー・コンテナ値など JObject 経由では作れない入力を渡せる。
        static JArray RunBatchRaw(LiveObjectContainer container, string json)
        {
            var result = LiveObjectHandler.ExecuteBatch(container, Resolver, json);
            return (JArray)JObject.Parse(result)["responses"];
        }

        static JArray RunBatchRaw(string json) => RunBatchRaw(EmptyContainer(), json);

        // ---- 変更フィード (/live/changes) ----
        // クライアントは毎サイクル、表示中プロパティの GET と同じバッチに変更フィードを 1 件載せる。
        // ここが 404 に落ちると値の追従だけが動き、一覧や他クライアントの変更が永久に届かなくなる
        // (症状が出るのが遅く、原因も遠いので必ずテストで押さえる)。

        [Test]
        public void Batch_Changes_WithoutSince_ReturnsRevisionAndNoIds()
        {
            LiveChangeLog.Clear();
            LiveChangeLog.Record("obj-1");

            var requests = new JObject
            {
                ["requests"] = new JArray
                {
                    new JObject { ["id"] = 1, ["method"] = "GET", ["path"] = "/live/changes" },
                }
            };

            var responses = RunBatch(requests);

            Assert.AreEqual(200, (int)responses[0]["status"]);
            Assert.AreEqual(1, (long)responses[0]["body"]["revision"]);
            Assert.IsEmpty((JArray)responses[0]["body"]["changes"], "sync-up call reports no ids");
            LiveChangeLog.Clear();
        }

        [Test]
        public void Batch_Changes_WithSince_ReturnsIdsRecordedAfterIt()
        {
            LiveChangeLog.Clear();
            LiveChangeLog.Record("obj-1");
            LiveChangeLog.Record("obj-2");

            var requests = new JObject
            {
                ["requests"] = new JArray
                {
                    new JObject { ["id"] = 1, ["method"] = "GET", ["path"] = "/live/changes?since=1" },
                }
            };

            var responses = RunBatch(requests);

            Assert.AreEqual(200, (int)responses[0]["status"]);
            Assert.AreEqual(2, (long)responses[0]["body"]["revision"]);
            var changes = ((JArray)responses[0]["body"]["changes"]).Select(t => (string)t).ToArray();
            CollectionAssert.AreEqual(new[] { "obj-2" }, changes);
            LiveChangeLog.Clear();
        }

        [Test]
        public void Batch_Changes_AlongsidePropertyReads_BothSucceed()
        {
            LiveChangeLog.Clear();
            var comp = CreateTarget(out var id);
            comp.a = 7;

            var requests = new JObject
            {
                ["requests"] = new JArray
                {
                    new JObject { ["id"] = -1, ["method"] = "GET", ["path"] = "/live/changes" },
                    new JObject { ["id"] = 0, ["method"] = "GET", ["path"] = $"/live/object/{id}/a" },
                }
            };

            var responses = RunBatch(requests);

            Assert.AreEqual(2, responses.Count);
            Assert.AreEqual(-1, (int)responses[0]["id"], "negative ids echo back unchanged");
            Assert.AreEqual(200, (int)responses[0]["status"]);
            Assert.AreEqual(200, (int)responses[1]["status"]);
            Assert.AreEqual(7, (int)responses[1]["body"]["value"]);
            LiveChangeLog.Clear();
        }

        // --- 受け口の宣言 (LiveRouteScope) が守られていること ---

        [Test]
        public void Batch_CannotNestAnotherBatch()
        {
            // まとめ送りの中からまとめ送りは呼べない (再帰でメインスレッドを占有できてしまう)。
            // /live/batch は単発専用として宣言されているので、まとめ送りからは «無い» パス。
            var requests = new JObject
            {
                ["requests"] = new JArray
                {
                    new JObject
                    {
                        ["id"] = 1,
                        ["method"] = "POST",
                        ["path"] = "/live/batch",
                        ["body"] = new JObject { ["requests"] = new JArray() },
                    },
                }
            };

            var responses = RunBatch(requests);

            Assert.AreEqual(404, (int)responses[0]["status"]);
        }

        [Test]
        public void Batch_Inbox_NeedsAClientAndRejectsOtherMethods()
        {
            // 受信箱はまとめ送り専用の受け口。誰宛かはコンテナからは解けないので、サーバーを
            // 介さない直接実行ではここまで来て 400 になる (本番は HandleBatch が先に解決する)。
            var requests = new JObject
            {
                ["requests"] = new JArray
                {
                    new JObject { ["id"] = 1, ["method"] = "GET", ["path"] = "/live/events?since=3" },
                    new JObject { ["id"] = 2, ["method"] = "POST", ["path"] = "/live/events" },
                }
            };

            var responses = RunBatch(requests);

            Assert.AreEqual(400, (int)responses[0]["status"]);
            Assert.AreEqual(405, (int)responses[1]["status"], "宣言が GET のみなので他の動詞は 405");
        }

        [Test]
        public void Batch_UnknownPath_Returns404()
        {
            var requests = new JObject
            {
                ["requests"] = new JArray
                {
                    new JObject { ["id"] = 1, ["method"] = "GET", ["path"] = "/live/nope" },
                }
            };

            Assert.AreEqual(404, (int)RunBatch(requests)[0]["status"]);
        }

        [Test]
        public void Batch_Changes_WithNonGetMethod_Returns405()
        {
            var requests = new JObject
            {
                ["requests"] = new JArray
                {
                    new JObject { ["id"] = 1, ["method"] = "PUT", ["path"] = "/live/changes" },
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
                    new JObject { ["id"] = 1, ["method"] = "PUT", ["path"] = $"/live/object/{id}/a", ["body"] = new JObject { ["value"] = 5 } },
                    new JObject { ["id"] = 2, ["method"] = "PUT", ["path"] = $"/live/object/{id}/b", ["body"] = new JObject { ["value"] = 2.5f } },
                    new JObject { ["id"] = 3, ["method"] = "PUT", ["path"] = $"/live/object/{id}/flag", ["body"] = new JObject { ["value"] = true } },
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
                    new JObject { ["id"] = "ok", ["method"] = "PUT", ["path"] = $"/live/object/{id}/a", ["body"] = new JObject { ["value"] = 7 } },
                    new JObject { ["id"] = "bad-object", ["method"] = "PUT", ["path"] = "/live/object/does-not-exist/a", ["body"] = new JObject { ["value"] = 1 } },
                    new JObject { ["id"] = "bad-path", ["method"] = "PUT", ["path"] = $"/live/object/{id}/nope", ["body"] = new JObject { ["value"] = 1 } },
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

            // A function's apiName is lower-cased (LiveFunctionType uses ToLowerInvariant),
            // and the remote app invokes it the same way (functionName.toLowerCase()), so the
            // invoke path segment for GetA is "geta", not the PascalCase method name.
            var requests = new JObject
            {
                ["requests"] = new JArray
                {
                    new JObject { ["id"] = 1, ["method"] = "POST", ["path"] = $"/live/function/{id}/geta" },
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
                    new JObject { ["id"] = 1, ["path"] = $"/live/object/{id}/a", ["body"] = new JObject { ["value"] = 5 } },
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
                    new JObject { ["id"] = 1, ["method"] = "GET", ["path"] = "/live/status" },
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
            var json = @"{""requests"":[{""body"":{""value"":9},""path"":""/live/object/ID/a"",""method"":""PUT"",""id"":7}]}"
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
                {""id"":""str-id"",""method"":""GET"",""path"":""/live/status""},
                {""method"":""GET"",""path"":""/live/status""},
                {""id"":null,""method"":""GET"",""path"":""/live/status""}
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
            var json = @"{""requests"":[{""id"":1,""method"":""POST"",""path"":""/live/function/ID/geta"",""body"":null}]}"
                .Replace("ID", id);

            var responses = RunBatchRaw(json);

            Assert.AreEqual(200, (int)responses[0]["status"]);
            Assert.AreEqual(42, (int)responses[0]["body"]["result"]);
        }

        [Test]
        public void Batch_NonObjectElements_ReportInvalidWithNullId()
        {
            var json = @"{""requests"":[5, null, {""id"":9,""method"":""GET"",""path"":""/live/status""}]}";

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
            var json = @"{""requests"":[{""id"":1,""extra"":{""nested"":[1,2,3]},""method"":""GET"",""foo"":42,""path"":""/live/status""}]}";

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
            var json = @"{""requests"":[{""id"":1,""method"":""PUT"",""path"":""/live/object/ID/a"",""body"":{""value"":5}}]"
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
            var json = @"{""requests"":[{""id"":1,""method"":""PUT"",""path"":""/live/object/ID/a"",""body"":{""value"":5}}]} trailing-garbage"
                .Replace("ID", id);

            var responses = RunBatchRaw(json);

            Assert.AreEqual(0, responses.Count);
            Assert.AreEqual(0, comp.a, "trailing garbage must reject the whole batch");
        }

        [Test]
        public void Batch_Comments_SkippedLikeJObjectLoad()
        {
            var json = @"/* leading */ {""requests"":[{""id"":1, /* mid */ ""method"":""GET"",""path"":""/live/status"" /* trail */ }]}";

            var responses = RunBatchRaw(json);

            Assert.AreEqual(1, responses.Count);
            Assert.AreEqual(1, (int)responses[0]["id"]);
            Assert.AreEqual(404, (int)responses[0]["status"]);
        }

        [Test]
        public void Batch_DuplicateRequestsKey_LastWins()
        {
            var comp = CreateTarget(out var id);
            var json = (@"{""requests"":[{""id"":""A"",""method"":""PUT"",""path"":""/live/object/ID/a"",""body"":{""value"":1}}],"
                      + @"""requests"":[{""id"":""B"",""method"":""PUT"",""path"":""/live/object/ID/a"",""body"":{""value"":2}}]}")
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
            var json = @"{""requests"":[{""id"":1,""method"":""GET"",""method"":""PUT"",""path"":""/live/object/ID/a"",""body"":{""value"":3}}]}"
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
            var json = @"{""requests"":[{""id"":1,""method"":{""x"":1},""path"":""/live/object/ID/a""}]}"
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

        // ---- プロパティ reset (/@reset) ----
        // 素の "reset" だと、"reset" という名前のメンバーへの配列 append と同じ URL になり、
        // ルート表の並び順だけが決め手になっていた。@ は実在のメンバー名の先頭に来られないので、
        // この綴りで初めて「実在のメンバーか、メンバーに対する操作か」が分かれる。

        [Test]
        public void Batch_ResetPseudoMember_ReachesTheResetOperation()
        {
            // 値がどこへ戻るか (既定値の採取) はハンドル層の責務で、
            // LiveObjectDirtyTrackingTests が押さえている。ここで固定するのは経路
            // — @reset が末尾から剥がされ、対象メンバー "a" の reset として解かれること。
            var target = CreateTarget(out var id);
            target.a = 7;

            var requests = new JObject
            {
                ["requests"] = new JArray
                {
                    new JObject { ["id"] = 1, ["method"] = "POST", ["path"] = $"/live/object/{id}/a/@reset" },
                }
            };

            var responses = RunBatch(requests);

            Assert.AreEqual(1, responses.Count);
            // 対象はシーンに直接置いたオブジェクトなので、エディタには戻す先が無い (LiveEditorProperty)。
            // 経路が解けていることは reset 固有の文面で分かる — "a/@reset" のまま配列 append として
            // 読まれていれば "Failed to add array element" になる。
            Assert.AreEqual(400, (int)responses[0]["status"]);
            Assert.AreEqual("Property cannot be reverted", (string)responses[0]["body"]["error"]);
            Assert.AreEqual(7, target.a, "戻せないときは値を触らない");
        }

        [Test]
        public void Batch_BareReset_IsNotThePseudoMember()
        {
            // 実メンバー "a/reset" への append として読まれ、そんなメンバーは無いので失敗する。
            // ここが 200 に戻ったら、"reset" という名前のメンバーが二度と編集できなくなる合図。
            var target = CreateTarget(out var id);
            target.a = 7;

            var requests = new JObject
            {
                ["requests"] = new JArray
                {
                    new JObject { ["id"] = 1, ["method"] = "POST", ["path"] = $"/live/object/{id}/a/reset" },
                }
            };

            var responses = RunBatch(requests);

            Assert.AreEqual(1, responses.Count);
            Assert.AreNotEqual(200, (int)responses[0]["status"], "bare reset must not reach the reset operation");
            Assert.AreEqual(7, target.a, "bare reset leaves the value alone");
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
