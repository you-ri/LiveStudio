// Copyright (c) You-Ri, 2026

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Lilium.RemoteControl;
using Lilium.RemoteControl.LiveScene;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// Verifies the [RawJson] member behavior: a string field whose value is itself a JSON document is
    /// embedded inline (as an object/array) instead of as an escaped string, re-stringified on read, and
    /// reads back legacy files that stored it as an escaped string. Covers both serialization paths — the
    /// JToken path (ToJson/FromJson, used by REST) and the persistence/delta path (LiveScene save/restore).
    /// </summary>
    [TestFixture]
    public class RawJsonSerializationTests
    {
        [Serializable]
        [LiveClass("TestRawJsonClass")]
        public class TestRawJsonClass
        {
            // The value is itself a JSON document.
            [LiveField, RawJson]
            public string state;

            // A plain string field (control): must stay an escaped string.
            [LiveField]
            public string plain;
        }

        private const string kStateJson = "{\"version\":1,\"nested\":{\"x\":2},\"list\":[1,2,3]}";

        private TestLiveObjectResolver _resolver;

        [SetUp]
        public void SetUp()
        {
            LiveClass.Clear();
            foreach (var obj in LiveObjectRegistry.instances.ToList()) obj.Unregister();
            _resolver = new TestLiveObjectResolver();
            LiveClass.RegisterFromAttributes<TestRawJsonClass>();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in LiveObjectRegistry.instances.ToList()) obj.Unregister();
        }

        private static JToken StateOf(JObject value) => value["state"];

        // -------------------------------------------------------
        // JToken path (ToJson / FromJson) — REST scope
        // -------------------------------------------------------

        [Test]
        public void ToJson_RawJsonField_EmbeddedAsObjectNotString()
        {
            var obj = new TestRawJsonClass { state = kStateJson, plain = "{\"a\":1}" };

            var root = JObject.Parse(LivePropertySerializer.ToJson(obj));
            var value = (JObject)root["value"];

            // state is embedded as a real object (not double-encoded), with its structure preserved.
            Assert.AreEqual(JTokenType.Object, StateOf(value).Type);
            Assert.AreEqual(1, value["state"]["version"].Value<int>());
            Assert.AreEqual(2, value["state"]["nested"]["x"].Value<int>());

            // plain string is NOT affected by [RawJson]: it stays an escaped string.
            Assert.AreEqual(JTokenType.String, value["plain"].Type);
        }

        [Test]
        public void RoundTrip_RawJsonField_RestoresEquivalentJson()
        {
            var obj = new TestRawJsonClass { state = kStateJson };

            var json = LivePropertySerializer.ToJson(obj);
            var restored = LivePropertySerializer.FromJson<TestRawJsonClass>(json);

            // The restored string is semantically the same JSON document (formatting/order aside).
            Assert.IsTrue(JToken.DeepEquals(JToken.Parse(kStateJson), JToken.Parse(restored.state)));
        }

        [Test]
        public void FromJson_LegacyEscapedString_RestoresVerbatim()
        {
            // Old files stored the value as an escaped JSON string. It must still read.
            var legacy = new JObject
            {
                ["value"] = new JObject
                {
                    ["@type"] = "TestRawJsonClass",
                    ["state"] = kStateJson, // a JValue string, not an embedded object
                },
            };

            var restored = LivePropertySerializer.FromJson<TestRawJsonClass>(legacy.ToString());

            Assert.AreEqual(kStateJson, restored.state);
        }

        [Test]
        public void ToJson_NonJsonValue_FallsBackToPlainString()
        {
            // A [RawJson] value that is not parseable JSON must not throw; it falls back to a plain string.
            var obj = new TestRawJsonClass { state = "not json at all" };

            var json = LivePropertySerializer.ToJson(obj);
            var value = (JObject)JObject.Parse(json)["value"];
            Assert.AreEqual(JTokenType.String, StateOf(value).Type);

            var restored = LivePropertySerializer.FromJson<TestRawJsonClass>(json);
            Assert.AreEqual("not json at all", restored.state);
        }

        // -------------------------------------------------------
        // Persistence / delta path (LiveScene save / restore)
        // -------------------------------------------------------

        [Test]
        public void LiveSceneToJson_RawJsonField_EmbeddedAsObject()
        {
            // Snapshot mode exercises the persistence serialize path (SerializeFullToJObject); the live
            // save embeds in-use assets as @op:new array elements, whose element serialization shares the
            // same [RawJson] handling covered by the ToJson (JToken) tests above.
            var obj = new TestRawJsonClass { state = kStateJson };
            var liveClass = LiveClass.Find(typeof(TestRawJsonClass));
            new LiveObjectHandle("rawjson-1", liveClass, obj);

            var json = LiveSceneSerializer.LiveSceneToJson(
                new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver, SerializeMode.Snapshot);
            var entry = (JObject)((JArray)JObject.Parse(json)["objects"])[0];

            Assert.AreEqual(JTokenType.Object, entry["state"].Type);
            Assert.AreEqual(1, entry["state"]["version"].Value<int>());
        }

        [Test]
        public void LiveSceneFromJson_RawJsonObject_RestoresCompactString()
        {
            var obj = new TestRawJsonClass { state = null };
            var liveClass = LiveClass.Find(typeof(TestRawJsonClass));
            new LiveObjectHandle("rawjson-1", liveClass, obj);

            var json = new JObject
            {
                ["objects"] = new JArray
                {
                    new JObject
                    {
                        ["@type"] = "TestRawJsonClass",
                        ["@id"] = "rawjson-1",
                        ["state"] = JObject.Parse(kStateJson), // embedded object (new format)
                    },
                },
            }.ToString();

            LiveSceneSerializer.LiveSceneFromJson(json, _resolver);

            Assert.IsTrue(JToken.DeepEquals(JToken.Parse(kStateJson), JToken.Parse(obj.state)));
        }

        [Test]
        public void LiveSceneFromJson_LegacyEscapedString_RestoresVerbatim()
        {
            var obj = new TestRawJsonClass { state = null };
            var liveClass = LiveClass.Find(typeof(TestRawJsonClass));
            new LiveObjectHandle("rawjson-1", liveClass, obj);

            // Legacy live scene stored state as an escaped string.
            var json = new JObject
            {
                ["objects"] = new JArray
                {
                    new JObject
                    {
                        ["@type"] = "TestRawJsonClass",
                        ["@id"] = "rawjson-1",
                        ["state"] = kStateJson, // a JValue string
                    },
                },
            }.ToString();

            LiveSceneSerializer.LiveSceneFromJson(json, _resolver);

            Assert.AreEqual(kStateJson, obj.state);
        }
    }
}
