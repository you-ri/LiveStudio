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

        [ExposedClass("TestClipHolder")]
        public class TestClipHolder : MonoBehaviour
        {
            [ExposedProperty, Hide]
            public string[] clipGuids => new[] { string.Empty, kClipGuid };

            [SerializeField, ExposedField, AssetSelector(nameof(clipGuids))]
            public AnimationClip clip;
        }

        #endregion

        const string kClipGuid = "0123456789abcdef0123456789abcdef";

        private TestExposedObjectResolver _resolver;
        private readonly List<Object> _createdObjects = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            ExposedClass.Clear();
            AssetRegistry.Clear();

            var toRemove = ExposedObjectRegistry.instances.ToList();
            foreach (var obj in toRemove) obj.Unregister();

            ExposedClass.RegisterFromAttributes<TestClipHolder>();

            _resolver = new TestExposedObjectResolver();
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

            var toRemove = ExposedObjectRegistry.instances.ToList();
            foreach (var obj in toRemove) obj.Unregister();
        }

        private AnimationClip _CreateClip(string name)
        {
            var clip = new AnimationClip { name = name };
            _createdObjects.Add(clip);
            return clip;
        }

        private TestClipHolder _CreateHolder(out ExposedObjectHandle exposed)
        {
            var go = new GameObject("HolderGO");
            _createdObjects.Add(go);
            var holder = go.AddComponent<TestClipHolder>();
            exposed = new ExposedObjectHandle("holder-id", ExposedClass.Find(typeof(TestClipHolder)), holder);
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

            var root = JObject.Parse(ExposedPropertySerializer.ToJson(exposed, _resolver));

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

            var root = JObject.Parse(ExposedPropertySerializer.ToJson(exposed, _resolver));

            Assert.AreEqual(JTokenType.Null, root["clip"].Type);
        }

        [Test]
        public void Serialize_UnregisteredClip_EmitsNull()
        {
            var holder = _CreateHolder(out var exposed);
            holder.clip = _CreateClip("Unregistered");

            var root = JObject.Parse(ExposedPropertySerializer.ToJson(exposed, _resolver));

            Assert.AreEqual(JTokenType.Null, root["clip"].Type, "unregistered asset cannot round-trip; serialize as null");
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
            var ok = ExposedPropertySerializer.FromJson(payload, property.Value, _resolver);

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

            var ok = ExposedPropertySerializer.FromJson("{\"value\":\"" + kClipGuid + "\"}", property.Value, _resolver);

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

            var ok = ExposedPropertySerializer.FromJson("{\"value\":null}", property.Value, _resolver);

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
            ExposedPropertySerializer.FromJson(payload, property.Value, _resolver);

            Assert.IsNull(holder.clip, "unknown guid should null out the field (same as ObjectSelector v1 behavior)");
        }

        #endregion
    }
}
