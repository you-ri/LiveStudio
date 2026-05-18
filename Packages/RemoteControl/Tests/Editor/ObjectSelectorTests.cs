// Copyright (c) You-Ri, 2026

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Newtonsoft.Json.Linq;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// ObjectSelectorAttribute の動作確認テスト。
    /// - 値のシリアライズ: Component は "rootId.components[N]" 形式の @ref で出力される
    /// - 値のデシリアライズ: path 付き @ref から Component を解決する
    /// - controller.options: フィールド型に代入可能なコンポーネントが候補として並ぶ
    /// - None (null) 選択で値を null にできる
    /// </summary>
    [TestFixture]
    public class ObjectSelectorTests
    {
        #region Test Classes

        public abstract class TestReceiverBase : MonoBehaviour
        {
            public int port;
        }

        [ExposedClass("TestReceiver")]
        public class TestReceiver : TestReceiverBase
        {
            [ExposedField]
            public int value;
        }

        [ExposedClass("TestOtherComponent")]
        public class TestOtherComponent : MonoBehaviour
        {
            [ExposedField]
            public string label;
        }

        [ExposedClass("TestHolder")]
        public class TestHolder : MonoBehaviour
        {
            [SerializeField, ExposedField, ObjectSelector]
            public TestReceiverBase receiver;
        }

        #endregion

        private TestExposedObjectResolver _resolver;
        private readonly List<GameObject> _createdGameObjects = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            ExposedClass.Clear();

            var toRemove = ExposedObjectRegistry.instances.ToList();
            foreach (var obj in toRemove) obj.Unregister();

            ExposedClass.RegisterFromAttributes<ExposedGameObject>();
            ExposedClass.RegisterFromAttributes<TestReceiver>();
            ExposedClass.RegisterFromAttributes<TestOtherComponent>();
            ExposedClass.RegisterFromAttributes<TestHolder>();

            _resolver = new TestExposedObjectResolver();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _createdGameObjects)
            {
                if (go != null) GameObject.DestroyImmediate(go);
            }
            _createdGameObjects.Clear();

            var toRemove = ExposedObjectRegistry.instances.ToList();
            foreach (var obj in toRemove) obj.Unregister();
        }

        private GameObject _CreateGameObject(string name)
        {
            var go = new GameObject(name);
            _createdGameObjects.Add(go);
            return go;
        }

        private ExposedGameObject _WrapGameObject(GameObject go)
        {
            var wrapper = new ExposedGameObject(go);
            wrapper.OnEnable();
            return wrapper;
        }

        #region Serialization

        [Test]
        public void Serialize_ComponentValue_EmitsComponentsPathRef()
        {
            // Arrange: receiver GameObject (ExposedGameObject wrapper) with TestReceiver component
            var receiverGo = _CreateGameObject("ReceiverGO");
            var receiverWrapper = _WrapGameObject(receiverGo);
            var receiver = receiverGo.AddComponent<TestReceiver>();

            // holder GameObject with TestHolder.receiver = receiver
            var holderGo = _CreateGameObject("HolderGO");
            var holder = holderGo.AddComponent<TestHolder>();
            holder.receiver = receiver;
            var holderExposed = new ExposedObject("holder-id", ExposedClass.Find(typeof(TestHolder)), holder);

            // Act
            var fullJson = ExposedPropertySerializer.ToJson(holderExposed, _resolver);
            var root = JObject.Parse(fullJson);

            // Assert
            var receiverToken = root["receiver"] as JObject;
            Assert.IsNotNull(receiverToken, "receiver token should be JObject");

            var refKey = receiverToken["@ref"]?.Value<string>();
            Assert.IsNotNull(refKey);
            StringAssert.StartsWith(receiverWrapper.id, refKey, "@ref should start with GameObject wrapper id");
            StringAssert.Contains(".components[", refKey, "@ref should contain components[N] path");
            Assert.AreEqual("TestReceiver", receiverToken["@type"]?.Value<string>());
        }

        [Test]
        public void Serialize_NullValue_EmitsNull()
        {
            var holderGo = _CreateGameObject("HolderGO");
            var holder = holderGo.AddComponent<TestHolder>();
            holder.receiver = null;
            var holderExposed = new ExposedObject("holder-id", ExposedClass.Find(typeof(TestHolder)), holder);

            var fullJson = ExposedPropertySerializer.ToJson(holderExposed, _resolver);
            var root = JObject.Parse(fullJson);

            Assert.AreEqual(JTokenType.Null, root["receiver"].Type);
        }

        [Test]
        public void Serialize_ComponentWithMultipleComponents_UsesFilteredIndex()
        {
            // Arrange: GameObject with TestOtherComponent (index 0) and TestReceiver (index 1) in filtered list
            var targetGo = _CreateGameObject("MultiCompGO");
            var wrapper = _WrapGameObject(targetGo);
            targetGo.AddComponent<TestOtherComponent>();
            var receiver = targetGo.AddComponent<TestReceiver>();

            var holderGo = _CreateGameObject("HolderGO");
            var holder = holderGo.AddComponent<TestHolder>();
            holder.receiver = receiver;
            var holderExposed = new ExposedObject("holder-id", ExposedClass.Find(typeof(TestHolder)), holder);

            // Act
            var fullJson = ExposedPropertySerializer.ToJson(holderExposed, _resolver);
            var root = JObject.Parse(fullJson);

            // Assert: path の index は ExposedGameObject._components のフィルタ済み配列に揃う
            var refKey = root["receiver"]?["@ref"]?.Value<string>();
            Assert.IsNotNull(refKey);
            Assert.AreEqual($"{wrapper.id}.components[1]", refKey);
        }

        #endregion

        #region Deserialization

        [Test]
        public void Deserialize_ComponentsPathRef_ResolvesToComponent()
        {
            // Arrange
            var receiverGo = _CreateGameObject("ReceiverGO");
            var receiverWrapper = _WrapGameObject(receiverGo);
            var receiver = receiverGo.AddComponent<TestReceiver>();

            var holderGo = _CreateGameObject("HolderGO");
            var holder = holderGo.AddComponent<TestHolder>();
            holder.receiver = null;
            var holderExposed = new ExposedObject("holder-id", ExposedClass.Find(typeof(TestHolder)), holder);

            var payload = "{\"value\":{\"@type\":\"TestReceiver\",\"@ref\":\"" + receiverWrapper.id + ".components[0]\"}}";
            var property = holderExposed.FindProperty("receiver");
            Assert.IsTrue(property.HasValue);

            // Act
            var ok = ExposedPropertySerializer.FromJson(payload, property.Value, _resolver);

            // Assert
            Assert.IsTrue(ok);
            Assert.AreEqual(receiver, holder.receiver);
        }

        [Test]
        public void Deserialize_NullValue_SetsFieldToNull()
        {
            var receiverGo = _CreateGameObject("ReceiverGO");
            _WrapGameObject(receiverGo);
            var receiver = receiverGo.AddComponent<TestReceiver>();

            var holderGo = _CreateGameObject("HolderGO");
            var holder = holderGo.AddComponent<TestHolder>();
            holder.receiver = receiver;
            var holderExposed = new ExposedObject("holder-id", ExposedClass.Find(typeof(TestHolder)), holder);

            var property = holderExposed.FindProperty("receiver");
            Assert.IsTrue(property.HasValue);

            var ok = ExposedPropertySerializer.FromJson("{\"value\":null}", property.Value, _resolver);

            Assert.IsTrue(ok);
            Assert.IsNull(holder.receiver);
        }

        [Test]
        public void Deserialize_UnknownRef_LeavesValueUnchangedOrNull()
        {
            // path で解決できなければ null 代入 (v1 仕様)
            var receiverGo = _CreateGameObject("ReceiverGO");
            _WrapGameObject(receiverGo);
            var receiver = receiverGo.AddComponent<TestReceiver>();

            var holderGo = _CreateGameObject("HolderGO");
            var holder = holderGo.AddComponent<TestHolder>();
            holder.receiver = receiver;
            var holderExposed = new ExposedObject("holder-id", ExposedClass.Find(typeof(TestHolder)), holder);

            var property = holderExposed.FindProperty("receiver");

            var payload = "{\"value\":{\"@type\":\"TestReceiver\",\"@ref\":\"non-existent-id.components[0]\"}}";
            ExposedPropertySerializer.FromJson(payload, property.Value, _resolver);

            Assert.IsNull(holder.receiver, "unresolved @ref should null out the field (v1 behavior)");
        }

        #endregion

        #region Controller Options

        [Test]
        public void Options_IncludesGameObjectComponentCandidates()
        {
            // Arrange: 2 つの GameObject に TestReceiver
            var goA = _CreateGameObject("RecvA");
            var wrapperA = _WrapGameObject(goA);
            goA.AddComponent<TestReceiver>();

            var goB = _CreateGameObject("RecvB");
            var wrapperB = _WrapGameObject(goB);
            goB.AddComponent<TestReceiver>();

            // Act: types JSON を取得
            var typeJson = ExposedTypeInfoSerializer.ToJson(ExposedClass.Find(typeof(TestHolder)));
            var parsed = JObject.Parse(typeJson);
            var properties = parsed["properties"] as JArray;
            var receiverProp = properties.First(p => p["name"]?.Value<string>() == "receiver") as JObject;
            var options = receiverProp?["controller"]?["options"] as JArray;

            // Assert
            Assert.IsNotNull(options, "options array must exist");
            Assert.AreEqual(2, options.Count);
            var ids = options.Select(o => o["id"].Value<string>()).ToList();
            CollectionAssert.Contains(ids, $"{wrapperA.id}.components[0]");
            CollectionAssert.Contains(ids, $"{wrapperB.id}.components[0]");
            foreach (var opt in options)
            {
                Assert.AreEqual("TestReceiver", opt["type"]?.Value<string>());
            }
        }

        [Test]
        public void Options_ExcludesGameObjectsWithoutMatchingComponent()
        {
            // 一方だけ TestReceiver を持つ
            var goA = _CreateGameObject("RecvA");
            _WrapGameObject(goA);
            goA.AddComponent<TestReceiver>();

            var goB = _CreateGameObject("NoMatchGO");
            _WrapGameObject(goB);
            goB.AddComponent<TestOtherComponent>(); // 型ミスマッチ

            var typeJson = ExposedTypeInfoSerializer.ToJson(ExposedClass.Find(typeof(TestHolder)));
            var parsed = JObject.Parse(typeJson);
            var receiverProp = (parsed["properties"] as JArray)
                .First(p => p["name"]?.Value<string>() == "receiver") as JObject;
            var options = receiverProp["controller"]["options"] as JArray;

            Assert.AreEqual(1, options.Count, "only the GO with matching component should be listed");
        }

        #endregion

        #region Roundtrip

        [Test]
        public void Roundtrip_SerializeThenDeserialize_RestoresSameReference()
        {
            var receiverGo = _CreateGameObject("ReceiverGO");
            _WrapGameObject(receiverGo);
            var receiver = receiverGo.AddComponent<TestReceiver>();

            var holderGo = _CreateGameObject("HolderGO");
            var holder = holderGo.AddComponent<TestHolder>();
            holder.receiver = receiver;
            var holderExposed = new ExposedObject("holder-id", ExposedClass.Find(typeof(TestHolder)), holder);

            // Serialize full object, extract receiver token, wrap in {value:...}, and apply back after clearing
            var fullJson = ExposedPropertySerializer.ToJson(holderExposed, _resolver);
            var fullRoot = JObject.Parse(fullJson);
            var receiverToken = fullRoot["receiver"].DeepClone();

            holder.receiver = null;
            var property = holderExposed.FindProperty("receiver");
            var payload = new JObject { ["value"] = receiverToken };

            var ok = ExposedPropertySerializer.FromJson(payload.ToString(), property.Value, _resolver);
            Assert.IsTrue(ok);
            Assert.AreEqual(receiver, holder.receiver);
        }

        #endregion

        #region Dirty tracking

        [Test]
        public void IsPropertyDirty_InitialStateAfterCapture_ReturnsFalse()
        {
            // Arrange: holder.receiver に初期値をセットしてデフォルトをキャプチャ
            var receiverGo = _CreateGameObject("ReceiverGO");
            _WrapGameObject(receiverGo);
            var receiver = receiverGo.AddComponent<TestReceiver>();

            var holderGo = _CreateGameObject("HolderGO");
            var holder = holderGo.AddComponent<TestHolder>();
            holder.receiver = receiver;
            var holderExposed = new ExposedObject("holder-id", ExposedClass.Find(typeof(TestHolder)), holder);

            // Act: デフォルト値をキャプチャ（Play開始相当）
            ExposedPropertyUtility.SetDefault(holderExposed);

            // Assert: 値を変更していないのでdirtyではない
            Assert.IsFalse(holderExposed.IsPropertyDirty("receiver"),
                "ObjectSelector field should not be dirty immediately after capture");
            Assert.IsFalse(ExposedObjectDefaultRegistry.HasDirtyChildProperty(
                holderExposed, "receiver", _resolver),
                "HasDirtyChildProperty should return false immediately after capture");
        }

        [Test]
        public void IsPropertyDirty_AfterChangeToOther_ReturnsTrue()
        {
            // Arrange: 初期値 receiverA、途中で receiverB に変更
            var goA = _CreateGameObject("ReceiverA");
            _WrapGameObject(goA);
            var receiverA = goA.AddComponent<TestReceiver>();

            var goB = _CreateGameObject("ReceiverB");
            _WrapGameObject(goB);
            var receiverB = goB.AddComponent<TestReceiver>();

            var holderGo = _CreateGameObject("HolderGO");
            var holder = holderGo.AddComponent<TestHolder>();
            holder.receiver = receiverA;
            var holderExposed = new ExposedObject("holder-id", ExposedClass.Find(typeof(TestHolder)), holder);

            ExposedPropertyUtility.SetDefault(holderExposed);

            // Act: receiverB に変更
            holder.receiver = receiverB;

            // Assert: dirty になるべき
            Assert.IsTrue(holderExposed.IsPropertyDirty("receiver"),
                "receiver should be dirty after changing to a different component");
        }

        #endregion

        #region Revert

        [Test]
        public void Revert_AfterChangeToNull_RestoresInitialComponent()
        {
            // Arrange: holder.receiver に初期値をセットしてデフォルトをキャプチャ
            var receiverGo = _CreateGameObject("ReceiverGO");
            _WrapGameObject(receiverGo);
            var receiver = receiverGo.AddComponent<TestReceiver>();

            var holderGo = _CreateGameObject("HolderGO");
            var holder = holderGo.AddComponent<TestHolder>();
            holder.receiver = receiver;
            var holderExposed = new ExposedObject("holder-id", ExposedClass.Find(typeof(TestHolder)), holder);

            ExposedPropertyUtility.SetDefault(holderExposed);

            // Act: 値を変更 (None = null) してから Revert
            holder.receiver = null;
            var reverted = holderExposed.Revert("receiver");

            // Assert: 初期値 (receiver) に戻ること
            Assert.IsTrue(reverted, "Revert should return true");
            Assert.AreEqual(receiver, holder.receiver, "receiver should be reverted to initial component");
        }

        [Test]
        public void Revert_AfterChangeToOtherComponent_RestoresInitialComponent()
        {
            // Arrange: holder.receiver に初期値 receiverA をセット
            var goA = _CreateGameObject("ReceiverA");
            _WrapGameObject(goA);
            var receiverA = goA.AddComponent<TestReceiver>();

            var goB = _CreateGameObject("ReceiverB");
            _WrapGameObject(goB);
            var receiverB = goB.AddComponent<TestReceiver>();

            var holderGo = _CreateGameObject("HolderGO");
            var holder = holderGo.AddComponent<TestHolder>();
            holder.receiver = receiverA;
            var holderExposed = new ExposedObject("holder-id", ExposedClass.Find(typeof(TestHolder)), holder);

            ExposedPropertyUtility.SetDefault(holderExposed);

            // Act: receiverB に変更してから Revert
            holder.receiver = receiverB;
            var reverted = holderExposed.Revert("receiver");

            // Assert: receiverA に戻ること
            Assert.IsTrue(reverted);
            Assert.AreEqual(receiverA, holder.receiver);
        }

        [Test]
        public void Revert_InitialNull_RestoresNull()
        {
            // Arrange: 初期値 null でデフォルトをキャプチャ
            var receiverGo = _CreateGameObject("ReceiverGO");
            _WrapGameObject(receiverGo);
            var receiver = receiverGo.AddComponent<TestReceiver>();

            var holderGo = _CreateGameObject("HolderGO");
            var holder = holderGo.AddComponent<TestHolder>();
            holder.receiver = null;
            var holderExposed = new ExposedObject("holder-id", ExposedClass.Find(typeof(TestHolder)), holder);

            ExposedPropertyUtility.SetDefault(holderExposed);

            // Act: receiver をセットしてから Revert
            holder.receiver = receiver;
            var reverted = holderExposed.Revert("receiver");

            // Assert: null に戻ること
            Assert.IsTrue(reverted);
            Assert.IsNull(holder.receiver);
        }

        #endregion
    }
}
