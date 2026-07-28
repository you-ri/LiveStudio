// Copyright (c) You-Ri, 2026

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Newtonsoft.Json.Linq;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// AssetSelectorAttribute の動作確認テスト。
    /// - 値のシリアライズ: AssetRegistry 登録済みアセットは {"@type","@guid"} で出力される
    /// - 値のデシリアライズ: @guid (または素の guid 文字列) から AssetRegistry 経由で解決する
    /// - 未登録アセット / 未知の guid / null の取り扱い
    /// </summary>
    [TestFixture]
    public class AssetSelectorTests
    {
        #region Test Classes

        [LiveClass("TestClipHolder")]
        public class TestClipHolder : MonoBehaviour
        {
            [LiveProperty, Hide]
            public string[] clipGuids => new[] { string.Empty, kClipGuid };

            [SerializeField, LiveField, AssetSelector(nameof(clipGuids))]
            public AnimationClip clip;
        }

        // A nested (inline-expanded) exposed object that carries the AssetSelector field. Serializing
        // the outer holder expands this via SerializeLiveObject — the nested path where the selector
        // dispatch was previously missing, so an [AssetSelector] AnimationClip fell through to the
        // generic UnityEngine.Object serializer and threw. See the "Nested serialization" region.
        [LiveClass("InnerClipData")]
        public class InnerClipData
        {
            [LiveProperty, Hide]
            public string[] clipGuids => new[] { string.Empty, kClipGuid };

            [SerializeField, LiveField, AssetSelector(nameof(clipGuids))]
            public AnimationClip clip;
        }

        [LiveClass("TestNestedClipHolder")]
        public class TestNestedClipHolder : MonoBehaviour
        {
            [LiveField]
            public InnerClipData inner = new InnerClipData();
        }

        #endregion

        const string kClipGuid = "0123456789abcdef0123456789abcdef";

        private TestLiveObjectResolver _resolver;
        private readonly List<Object> _createdObjects = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            LiveClass.Clear();
            AssetRegistry.Clear();

            var toRemove = LiveObjectRegistry.instances.ToList();
            foreach (var obj in toRemove) obj.Unregister();

            LiveClass.RegisterFromAttributes<TestClipHolder>();
            LiveClass.RegisterFromAttributes<InnerClipData>();
            LiveClass.RegisterFromAttributes<TestNestedClipHolder>();

            _resolver = new TestLiveObjectResolver();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in _createdObjects)
            {
                if (obj != null) Object.DestroyImmediate(obj);
            }
            _createdObjects.Clear();
            AssetRegistry.Clear();

            var toRemove = LiveObjectRegistry.instances.ToList();
            foreach (var obj in toRemove) obj.Unregister();
        }

        private AnimationClip _CreateClip(string name)
        {
            var clip = new AnimationClip { name = name };
            _createdObjects.Add(clip);
            return clip;
        }

        private TestClipHolder _CreateHolder(out LiveObjectHandle exposed)
        {
            var go = new GameObject("HolderGO");
            _createdObjects.Add(go);
            var holder = go.AddComponent<TestClipHolder>();
            exposed = new LiveObjectHandle("holder-id", LiveClass.Find(typeof(TestClipHolder)), holder);
            return holder;
        }

        private TestNestedClipHolder _CreateNestedHolder(out LiveObjectHandle exposed)
        {
            var go = new GameObject("NestedHolderGO");
            _createdObjects.Add(go);
            var holder = go.AddComponent<TestNestedClipHolder>();
            exposed = new LiveObjectHandle("nested-holder-id", LiveClass.Find(typeof(TestNestedClipHolder)), holder);
            return holder;
        }

        #region Serialization

        [Test]
        public void Serialize_RegisteredClip_EmitsGuid()
        {
            var clip = _CreateClip("SitClip");
            AssetRegistry.Register(kClipGuid, clip);

            var holder = _CreateHolder(out var exposed);
            holder.clip = clip;

            var root = JObject.Parse(LivePropertySerializer.ToJson(exposed, _resolver));

            var token = root["clip"] as JObject;
            Assert.IsNotNull(token, "clip token should be JObject");
            Assert.AreEqual(kClipGuid, token["@guid"]?.Value<string>());
            Assert.AreEqual("AnimationClip", token["@type"]?.Value<string>());
        }

        [Test]
        public void Serialize_NullClip_EmitsNull()
        {
            var holder = _CreateHolder(out var exposed);
            holder.clip = null;

            var root = JObject.Parse(LivePropertySerializer.ToJson(exposed, _resolver));

            Assert.AreEqual(JTokenType.Null, root["clip"].Type);
        }

        [Test]
        public void Serialize_UnregisteredClip_EmitsNull()
        {
            var holder = _CreateHolder(out var exposed);
            holder.clip = _CreateClip("Unregistered");

            var root = JObject.Parse(LivePropertySerializer.ToJson(exposed, _resolver));

            Assert.AreEqual(JTokenType.Null, root["clip"].Type, "unregistered asset cannot round-trip; serialize as null");
        }

        #endregion

        #region Nested serialization (regression: SerializeLiveObject selector dispatch)

        // These guard the divergence fix: an [AssetSelector] field on a NESTED exposed object (one
        // expanded inline via SerializeLiveObject, e.g. a component captured through a GameObject
        // wrapper's CaptureDefaults) must serialize as {@type,@guid} exactly like the top-level path.
        // Before the fix it fell through to the generic UnityEngine.Object serializer and logged
        // "LiveClass not found for type AnimationClip / JsonUtility does not support engine types".

        [Test]
        public void SerializeNested_RegisteredClip_EmitsGuid()
        {
            var clip = _CreateClip("SitClip");
            AssetRegistry.Register(kClipGuid, clip);

            var holder = _CreateNestedHolder(out var exposed);
            holder.inner.clip = clip;

            var root = JObject.Parse(LivePropertySerializer.ToJson(exposed, _resolver));

            var innerToken = root["inner"] as JObject;
            Assert.IsNotNull(innerToken, "nested inner object should expand inline as a JObject");

            var clipToken = innerToken["clip"] as JObject;
            Assert.IsNotNull(clipToken, "nested AssetSelector clip must serialize as {@type,@guid}, not fall through to the generic path");
            Assert.AreEqual(kClipGuid, clipToken["@guid"]?.Value<string>());
            Assert.AreEqual("AnimationClip", clipToken["@type"]?.Value<string>());
        }

        [Test]
        public void SerializeNested_Persistence_EmitsGuidWithoutName()
        {
            // Faithful to the production failure: CaptureDefaults serializes with forPersistence: true.
            var clip = _CreateClip("SitClip");
            AssetRegistry.Register(kClipGuid, clip);

            var holder = _CreateNestedHolder(out var exposed);
            holder.inner.clip = clip;

            var root = LivePropertySerializer.SerializeFullToJObject(exposed, _resolver, forPersistence: true);

            var clipToken = root["inner"]?["clip"] as JObject;
            Assert.IsNotNull(clipToken, "nested AssetSelector clip must serialize under persistence (the CaptureDefaults path)");
            Assert.AreEqual(kClipGuid, clipToken["@guid"]?.Value<string>());
            Assert.IsNull(clipToken["@name"], "persistence form omits @name");
        }

        [Test]
        public void SerializeNested_NullClip_EmitsNull()
        {
            var holder = _CreateNestedHolder(out var exposed);
            holder.inner.clip = null;

            var root = JObject.Parse(LivePropertySerializer.ToJson(exposed, _resolver));

            Assert.AreEqual(JTokenType.Null, root["inner"]?["clip"]?.Type,
                "null nested asset serializes as null without error");
        }

        #endregion

        #region Deserialization

        [Test]
        public void Deserialize_GuidObject_ResolvesClip()
        {
            var clip = _CreateClip("SitClip");
            AssetRegistry.Register(kClipGuid, clip);

            var holder = _CreateHolder(out var exposed);
            var property = exposed.FindProperty("clip");
            Assert.IsTrue(property.HasValue);

            var payload = "{\"value\":{\"@type\":\"AnimationClip\",\"@guid\":\"" + kClipGuid + "\"}}";
            var ok = LivePropertySerializer.FromJson(payload, property.Value, _resolver);

            Assert.IsTrue(ok);
            Assert.AreEqual(clip, holder.clip);
        }

        [Test]
        public void Deserialize_PlainGuidString_ResolvesClip()
        {
            var clip = _CreateClip("SitClip");
            AssetRegistry.Register(kClipGuid, clip);

            var holder = _CreateHolder(out var exposed);
            var property = exposed.FindProperty("clip");

            var ok = LivePropertySerializer.FromJson("{\"value\":\"" + kClipGuid + "\"}", property.Value, _resolver);

            Assert.IsTrue(ok);
            Assert.AreEqual(clip, holder.clip);
        }

        [Test]
        public void Deserialize_Null_SetsFieldToNull()
        {
            var clip = _CreateClip("SitClip");
            AssetRegistry.Register(kClipGuid, clip);

            var holder = _CreateHolder(out var exposed);
            holder.clip = clip;
            var property = exposed.FindProperty("clip");

            var ok = LivePropertySerializer.FromJson("{\"value\":null}", property.Value, _resolver);

            Assert.IsTrue(ok);
            Assert.IsNull(holder.clip);
        }

        [Test]
        public void Deserialize_UnknownGuid_SetsFieldToNull()
        {
            var clip = _CreateClip("SitClip");
            AssetRegistry.Register(kClipGuid, clip);

            var holder = _CreateHolder(out var exposed);
            holder.clip = clip;
            var property = exposed.FindProperty("clip");

            var payload = "{\"value\":{\"@guid\":\"ffffffffffffffffffffffffffffffff\"}}";
            LivePropertySerializer.FromJson(payload, property.Value, _resolver);

            Assert.IsNull(holder.clip, "unknown guid should null out the field (same as ObjectSelector v1 behavior)");
        }

        #endregion
    }
}
