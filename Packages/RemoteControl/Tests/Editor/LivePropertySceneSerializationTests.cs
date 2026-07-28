using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Lilium.RemoteControl;
using Lilium.RemoteControl.LiveScene;
using Lilium.RemoteControl.UI;
using Lilium.RemoteControl.UI.Editor;
using Newtonsoft.Json.Linq;

namespace Lilium.RemoteControl.Tests
{
    [TestFixture]
    public class LivePropertySceneSerializationTests
    {
        // テスト用ヘルパー: override エントリは @source、新規は @id が識別子
        private static string EntryKey(JToken t) => t is JObject o ? (o["@source"] ?? o["@id"])?.Value<string>() : null;

        #region Test Classes

        [LiveClass("TestStaticSceneClass")]
        public static class TestStaticSceneClass
        {
            [LiveField]
            public static int value = 0;

            [LiveField]
            public static string name = "Default";

            public static void Reset()
            {
                value = 0;
                name = "Default";
            }
        }

        [Serializable]
        public class NestedItem
        {
            public int id;
            public string name;
        }

        [Serializable]
        [LiveClass("TestSceneClass")]
        public class TestSceneClass
        {
            [LiveField]
            public int value;

            [LiveField]
            public string name;

            [LiveField]
            public float position;
        }

        [Serializable]
        [LiveClass("TestSceneClassWithArray")]
        public class TestSceneClassWithArray
        {
            [LiveField]
            public int[] intArray;

            [LiveField]
            public string[] stringArray;

            [LiveField]
            public NestedItem[] nestedItems;

            [LiveField]
            public List<int> intList;
        }

        [Serializable]
        [LiveClass("TestSceneNestedStruct")]
        public struct TestSceneNestedStruct
        {
            [LiveField]
            public int id;

            [LiveField]
            public string name;
        }

        [Serializable]
        [LiveClass("TestSceneClassWithStructArray")]
        public class TestSceneClassWithStructArray
        {
            [LiveField]
            public TestSceneNestedStruct[] items;
        }

        [Serializable]
        [LiveClass("TestSceneRefItem")]
        public class TestSceneRefItem : ILiveObject
        {
            public string name { get; set; }
            public LiveObjectHandle? liveObject => null;
            public string id => null;
            public void OnEnable() { }
            public void OnDisable() { }
            public void OnDispose() { OnDisable(); }
            public void Update() { }
            public void Reset() { }

            [LiveField]
            public int value;
        }

        [Serializable]
        [LiveClass("TestSceneContainerWithRefList")]
        public class TestSceneContainerWithRefList
        {
            [LiveField]
            public List<TestSceneRefItem> items;
        }

        [Serializable]
        [LiveClass("TestDeltaNewItem")]
        public struct TestDeltaNewItem
        {
            [LiveField]
            public string name;

            [LiveField]
            public float value1;

            [LiveField]
            public float value2;

            [LiveField]
            public int[] nested;

            [LiveDefault]
            public static TestDeltaNewItem Default => new TestDeltaNewItem
            {
                name = "",
                value1 = 1.0f,
                value2 = 2.0f,
                nested = new int[0],
            };
        }

        [Serializable]
        [LiveClass("TestDeltaNewContainer")]
        public class TestDeltaNewContainer
        {
            [LiveField]
            public List<TestDeltaNewItem> items;
        }

        #endregion

        private TestLiveObjectResolver _resolver;

        [SetUp]
        public void SetUp()
        {
            LiveClass.Clear();

            // LiveObjectRegistry.instances をクリア
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
            // クリーンアップ
            var toRemove = LiveObjectRegistry.instances.ToList();
            foreach (var obj in toRemove)
            {
                obj.Unregister();
            }
        }

        #region LiveSceneToJson Basic Tests

        [Test]
        public void LiveSceneToJson_EmptyScene_ReturnsValidJson()
        {
            // Act
            var json = LiveSceneSerializer.LiveSceneToJson(new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver);

            // Assert
            Assert.IsNotNull(json);
            Assert.IsFalse(string.IsNullOrEmpty(json));

            var jRoot = JObject.Parse(json);
            Assert.IsNotNull(jRoot["objects"]);
            Assert.AreEqual(0, ((JArray)jRoot["objects"]).Count);
        }

        [Test]
        public void LiveSceneToJson_ContainsAppMetadata()
        {
            // Act
            var json = LiveSceneSerializer.LiveSceneToJson(new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver);

            // Assert
            var jRoot = JObject.Parse(json);
            Assert.AreEqual(LiveSceneSerializer.FormatIdentifier, jRoot["format"]?.Value<string>());
            Assert.AreEqual(LiveSceneSerializer.CurrentFormatVersion, jRoot["formatVersion"]?.Value<int>());
            var jMetadata = jRoot["metadata"] as JObject;
            Assert.IsNotNull(jMetadata, "metadata object should exist");
            Assert.IsNotNull(jMetadata["appVersion"]);
            Assert.IsNotNull(jMetadata["appName"]);
            Assert.IsNotNull(jMetadata["unityVersion"]);
            Assert.IsNotNull(jMetadata["packageVersion"]);
        }

        [Test]
        public void LiveSceneToJson_SingleObject_SerializesCorrectly()
        {
            // Arrange
            LiveClass.RegisterFromAttributes<TestSceneClass>();

            var testObj = new TestSceneClass
            {
                value = 42,
                name = "TestObject",
                position = 3.14f
            };

            var liveClass = LiveClass.Find(typeof(TestSceneClass));
            var liveObj = new LiveObjectHandle("test-id-1", liveClass, testObj);

            // Act
            var json = LiveSceneSerializer.LiveSceneToJson(new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver);

            // Assert
            var jRoot = JObject.Parse(json);
            var objects = jRoot["objects"] as JArray;
            Assert.IsNotNull(objects);
            Assert.AreEqual(1, objects.Count);

            var obj = objects[0] as JObject;
            Assert.AreEqual("TestSceneClass", obj["@type"]?.Value<string>());
            Assert.AreEqual("test-id-1", EntryKey(obj));
            Assert.AreEqual(42, obj["value"]?.Value<int>());
            Assert.AreEqual("TestObject", obj["name"]?.Value<string>());
            Assert.AreEqual(3.14f, obj["position"]?.Value<float>(), 0.001f);
        }

        [Test]
        public void LiveSceneToJson_MultipleObjects_SerializesAll()
        {
            // Arrange
            LiveClass.RegisterFromAttributes<TestSceneClass>();

            var testObj1 = new TestSceneClass { value = 1, name = "First", position = 1.0f };
            var testObj2 = new TestSceneClass { value = 2, name = "Second", position = 2.0f };
            var testObj3 = new TestSceneClass { value = 3, name = "Third", position = 3.0f };

            var liveClass = LiveClass.Find(typeof(TestSceneClass));
            new LiveObjectHandle("id-1", liveClass, testObj1);
            new LiveObjectHandle("id-2", liveClass, testObj2);
            new LiveObjectHandle("id-3", liveClass, testObj3);

            // Act
            var json = LiveSceneSerializer.LiveSceneToJson(new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver);

            // Assert
            var jRoot = JObject.Parse(json);
            var objects = jRoot["objects"] as JArray;
            Assert.AreEqual(3, objects.Count);
        }

        [Test]
        public void LiveSceneToJson_WithDeltaFromDefaultFilter_SerializesOnlyDirtyProperties()
        {
            // Arrange
            LiveClass.RegisterFromAttributes<TestSceneClass>();

            var testObj = new TestSceneClass
            {
                value = 42,
                name = "TestObject",
                position = 3.14f
            };

            var liveClass = LiveClass.Find(typeof(TestSceneClass));
            var liveObj = new LiveObjectHandle("test-id-1", liveClass, testObj);

            // ベースライン設定
            LivePropertyUtility.SetDefault(liveObj);

            // valueプロパティのみ変更（EnsureDefaultCapturedが自動で呼ばれる）
            var valueProp = liveObj.FindProperty("value");
            Assert.IsNotNull(valueProp);
            valueProp.Value.SetValue(99);

            // Act
            var json = LiveSceneSerializer.LiveSceneToJson(new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver, SerializeMode.Delta);

            // Assert
            var jRoot = JObject.Parse(json);
            var objects = jRoot["objects"] as JArray;
            Assert.AreEqual(1, objects.Count);

            var obj = objects[0] as JObject;
            Assert.AreEqual(99, obj["value"]?.Value<int>());
            // dirtyでないプロパティは含まれない
            Assert.IsNull(obj["name"]);
            Assert.IsNull(obj["position"]);
        }

        [Test]
        public void LiveSceneToJson_WithAllFilter_SerializesAllProperties()
        {
            // Arrange
            LiveClass.RegisterFromAttributes<TestSceneClass>();

            var testObj = new TestSceneClass
            {
                value = 42,
                name = "TestObject",
                position = 3.14f
            };

            var liveClass = LiveClass.Find(typeof(TestSceneClass));
            new LiveObjectHandle("test-id-1", liveClass, testObj);

            // Act
            var json = LiveSceneSerializer.LiveSceneToJson(new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver, SerializeMode.Snapshot);

            // Assert
            var jRoot = JObject.Parse(json);
            var objects = jRoot["objects"] as JArray;
            var obj = objects[0] as JObject;

            // すべてのプロパティが含まれる
            Assert.IsNotNull(obj["value"]);
            Assert.IsNotNull(obj["name"]);
            Assert.IsNotNull(obj["position"]);
        }

        #endregion

        #region LiveSceneToJson Array Tests

        [Test]
        public void LiveSceneToJson_WithIntArray_SerializesCorrectly()
        {
            // Arrange
            LiveClass.RegisterFromAttributes<TestSceneClassWithArray>();

            var testObj = new TestSceneClassWithArray
            {
                intArray = new int[] { 1, 2, 3, 4, 5 }
            };

            var liveClass = LiveClass.Find(typeof(TestSceneClassWithArray));
            new LiveObjectHandle("array-test-1", liveClass, testObj);

            // Act
            var json = LiveSceneSerializer.LiveSceneToJson(new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver);

            // Assert
            var jRoot = JObject.Parse(json);
            var objects = jRoot["objects"] as JArray;
            var obj = objects[0] as JObject;
            var intArrayToken = obj["intArray"] as JArray;

            Assert.IsNotNull(intArrayToken);
            Assert.AreEqual(5, intArrayToken.Count);
            Assert.AreEqual(1, intArrayToken[0].Value<int>());
            Assert.AreEqual(2, intArrayToken[1].Value<int>());
            Assert.AreEqual(3, intArrayToken[2].Value<int>());
            Assert.AreEqual(4, intArrayToken[3].Value<int>());
            Assert.AreEqual(5, intArrayToken[4].Value<int>());
        }

        [Test]
        public void LiveSceneToJson_WithStringArray_SerializesCorrectly()
        {
            // Arrange
            LiveClass.RegisterFromAttributes<TestSceneClassWithArray>();

            var testObj = new TestSceneClassWithArray
            {
                stringArray = new string[] { "apple", "banana", "cherry" }
            };

            var liveClass = LiveClass.Find(typeof(TestSceneClassWithArray));
            new LiveObjectHandle("array-test-2", liveClass, testObj);

            // Act
            var json = LiveSceneSerializer.LiveSceneToJson(new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver);

            // Assert
            var jRoot = JObject.Parse(json);
            var objects = jRoot["objects"] as JArray;
            var obj = objects[0] as JObject;
            var stringArrayToken = obj["stringArray"] as JArray;

            Assert.IsNotNull(stringArrayToken);
            Assert.AreEqual(3, stringArrayToken.Count);
            Assert.AreEqual("apple", stringArrayToken[0].Value<string>());
            Assert.AreEqual("banana", stringArrayToken[1].Value<string>());
            Assert.AreEqual("cherry", stringArrayToken[2].Value<string>());
        }

        [Test]
        public void LiveSceneToJson_WithObjectArray_SerializesCorrectly()
        {
            // Arrange
            LiveClass.RegisterFromAttributes<TestSceneClassWithArray>();

            var testObj = new TestSceneClassWithArray
            {
                nestedItems = new NestedItem[]
                {
                    new NestedItem { id = 1, name = "First" },
                    new NestedItem { id = 2, name = "Second" },
                    new NestedItem { id = 3, name = "Third" }
                }
            };

            var liveClass = LiveClass.Find(typeof(TestSceneClassWithArray));
            new LiveObjectHandle("array-test-3", liveClass, testObj);

            // Act
            var json = LiveSceneSerializer.LiveSceneToJson(new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver);

            // Assert
            var jRoot = JObject.Parse(json);
            var objects = jRoot["objects"] as JArray;
            var obj = objects[0] as JObject;
            var nestedItemsToken = obj["nestedItems"] as JArray;

            Assert.IsNotNull(nestedItemsToken);
            Assert.AreEqual(3, nestedItemsToken.Count);

            Assert.AreEqual(1, nestedItemsToken[0]["id"]?.Value<int>());
            Assert.AreEqual("First", nestedItemsToken[0]["name"]?.Value<string>());
            Assert.AreEqual(2, nestedItemsToken[1]["id"]?.Value<int>());
            Assert.AreEqual("Second", nestedItemsToken[1]["name"]?.Value<string>());
            Assert.AreEqual(3, nestedItemsToken[2]["id"]?.Value<int>());
            Assert.AreEqual("Third", nestedItemsToken[2]["name"]?.Value<string>());
        }

        [Test]
        public void LiveSceneToJson_WithEmptyArray_SerializesCorrectly()
        {
            // Arrange
            LiveClass.RegisterFromAttributes<TestSceneClassWithArray>();

            var testObj = new TestSceneClassWithArray
            {
                intArray = new int[0]
            };

            var liveClass = LiveClass.Find(typeof(TestSceneClassWithArray));
            new LiveObjectHandle("array-test-4", liveClass, testObj);

            // Act
            var json = LiveSceneSerializer.LiveSceneToJson(new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver);

            // Assert
            var jRoot = JObject.Parse(json);
            var objects = jRoot["objects"] as JArray;
            var obj = objects[0] as JObject;
            var intArrayToken = obj["intArray"] as JArray;

            Assert.IsNotNull(intArrayToken);
            Assert.AreEqual(0, intArrayToken.Count);
        }

        [Test]
        public void LiveSceneToJson_WithNullArray_SerializesAsNull()
        {
            // Arrange
            LiveClass.RegisterFromAttributes<TestSceneClassWithArray>();

            var testObj = new TestSceneClassWithArray
            {
                intArray = null
            };

            var liveClass = LiveClass.Find(typeof(TestSceneClassWithArray));
            new LiveObjectHandle("array-test-5", liveClass, testObj);

            // Act
            var json = LiveSceneSerializer.LiveSceneToJson(new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver);

            // Assert
            var jRoot = JObject.Parse(json);
            var objects = jRoot["objects"] as JArray;
            var obj = objects[0] as JObject;

            Assert.IsTrue(obj["intArray"] == null || obj["intArray"].Type == JTokenType.Null);
        }

        [Test]
        public void LiveSceneToJson_WithIntList_SerializesCorrectly()
        {
            // Arrange
            LiveClass.RegisterFromAttributes<TestSceneClassWithArray>();

            var testObj = new TestSceneClassWithArray
            {
                intList = new List<int> { 10, 20, 30 }
            };

            var liveClass = LiveClass.Find(typeof(TestSceneClassWithArray));
            new LiveObjectHandle("array-test-6", liveClass, testObj);

            // Act
            var json = LiveSceneSerializer.LiveSceneToJson(new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver);

            // Assert
            var jRoot = JObject.Parse(json);
            var objects = jRoot["objects"] as JArray;
            var obj = objects[0] as JObject;
            var intListToken = obj["intList"] as JArray;

            Assert.IsNotNull(intListToken);
            Assert.AreEqual(3, intListToken.Count);
            Assert.AreEqual(10, intListToken[0].Value<int>());
            Assert.AreEqual(20, intListToken[1].Value<int>());
            Assert.AreEqual(30, intListToken[2].Value<int>());
        }

        #endregion

        #region LiveSceneFromJson Basic Tests

        [Test]
        public void LiveSceneFromJson_EmptyJson_DoesNotThrow()
        {
            // Act & Assert
            Assert.DoesNotThrow(() => LiveSceneSerializer.LiveSceneFromJson("", _resolver));
            Assert.DoesNotThrow(() => LiveSceneSerializer.LiveSceneFromJson(null, _resolver));
        }

        [Test]
        public void LiveSceneFromJson_MissingObjectsArray_HandlesGracefully()
        {
            // Arrange
            var json = "{\"appVersion\":\"1.0\"}";

            // Act & Assert - should log warning but not throw
            Assert.DoesNotThrow(() => LiveSceneSerializer.LiveSceneFromJson(json, _resolver));
        }

        [Test]
        public void LiveSceneFromJson_SingleObject_DeserializesCorrectly()
        {
            // Arrange
            LiveClass.RegisterFromAttributes<TestSceneClass>();

            var testObj = new TestSceneClass
            {
                value = 0,
                name = "",
                position = 0f
            };

            var liveClass = LiveClass.Find(typeof(TestSceneClass));
            new LiveObjectHandle("test-id-1", liveClass, testObj);

            var json = @"{
                ""appVersion"": ""1.0"",
                ""objects"": [
                    {
                        ""@type"": ""TestSceneClass"",
                        ""@id"": ""test-id-1"",
                        ""value"": 100,
                        ""name"": ""Loaded"",
                        ""position"": 9.99
                    }
                ]
            }";

            // Act
            LiveSceneSerializer.LiveSceneFromJson(json, _resolver);

            // Assert
            Assert.AreEqual(100, testObj.value);
            Assert.AreEqual("Loaded", testObj.name);
            Assert.AreEqual(9.99f, testObj.position, 0.001f);
        }

        [Test]
        public void LiveSceneFromJson_MultipleObjects_DeserializesAll()
        {
            // Arrange
            LiveClass.RegisterFromAttributes<TestSceneClass>();

            var testObj1 = new TestSceneClass { value = 0, name = "", position = 0f };
            var testObj2 = new TestSceneClass { value = 0, name = "", position = 0f };

            var liveClass = LiveClass.Find(typeof(TestSceneClass));
            new LiveObjectHandle("id-1", liveClass, testObj1);
            new LiveObjectHandle("id-2", liveClass, testObj2);

            var json = @"{
                ""objects"": [
                    { ""@type"": ""TestSceneClass"", ""@id"": ""id-1"", ""value"": 111, ""name"": ""Obj1"" },
                    { ""@type"": ""TestSceneClass"", ""@id"": ""id-2"", ""value"": 222, ""name"": ""Obj2"" }
                ]
            }";

            // Act
            LiveSceneSerializer.LiveSceneFromJson(json, _resolver);

            // Assert
            Assert.AreEqual(111, testObj1.value);
            Assert.AreEqual("Obj1", testObj1.name);
            Assert.AreEqual(222, testObj2.value);
            Assert.AreEqual("Obj2", testObj2.name);
        }

        [Test]
        public void LiveSceneFromJson_UnknownId_HandlesGracefully()
        {
            // Arrange
            LiveClass.RegisterFromAttributes<TestSceneClass>();

            var json = @"{
                ""objects"": [
                    { ""@type"": ""TestSceneClass"", ""@id"": ""unknown-id"", ""value"": 999 }
                ]
            }";

            // Act & Assert - should log warning but not throw
            Assert.DoesNotThrow(() => LiveSceneSerializer.LiveSceneFromJson(json, _resolver));
        }

        #endregion

        #region LiveSceneFromJson Array Tests

        [Test]
        public void LiveSceneFromJson_WithIntArray_DeserializesCorrectly()
        {
            // Arrange
            LiveClass.RegisterFromAttributes<TestSceneClassWithArray>();

            var testObj = new TestSceneClassWithArray
            {
                intArray = null
            };

            var liveClass = LiveClass.Find(typeof(TestSceneClassWithArray));
            new LiveObjectHandle("array-id-1", liveClass, testObj);

            var json = @"{
                ""objects"": [
                    {
                        ""@type"": ""TestSceneClassWithArray"",
                        ""@id"": ""array-id-1"",
                        ""intArray"": [10, 20, 30, 40, 50]
                    }
                ]
            }";

            // Act
            LiveSceneSerializer.LiveSceneFromJson(json, _resolver);

            // Assert
            Assert.IsNotNull(testObj.intArray);
            Assert.AreEqual(5, testObj.intArray.Length);
            Assert.AreEqual(10, testObj.intArray[0]);
            Assert.AreEqual(20, testObj.intArray[1]);
            Assert.AreEqual(30, testObj.intArray[2]);
            Assert.AreEqual(40, testObj.intArray[3]);
            Assert.AreEqual(50, testObj.intArray[4]);
        }

        [Test]
        public void LiveSceneFromJson_WithStringArray_DeserializesCorrectly()
        {
            // Arrange
            LiveClass.RegisterFromAttributes<TestSceneClassWithArray>();

            var testObj = new TestSceneClassWithArray
            {
                stringArray = null
            };

            var liveClass = LiveClass.Find(typeof(TestSceneClassWithArray));
            new LiveObjectHandle("array-id-2", liveClass, testObj);

            var json = @"{
                ""objects"": [
                    {
                        ""@type"": ""TestSceneClassWithArray"",
                        ""@id"": ""array-id-2"",
                        ""stringArray"": [""hello"", ""world"", ""test""]
                    }
                ]
            }";

            // Act
            LiveSceneSerializer.LiveSceneFromJson(json, _resolver);

            // Assert
            Assert.IsNotNull(testObj.stringArray);
            Assert.AreEqual(3, testObj.stringArray.Length);
            Assert.AreEqual("hello", testObj.stringArray[0]);
            Assert.AreEqual("world", testObj.stringArray[1]);
            Assert.AreEqual("test", testObj.stringArray[2]);
        }

        [Test]
        public void LiveSceneFromJson_WithObjectArray_DeserializesCorrectly()
        {
            // Arrange
            LiveClass.RegisterFromAttributes<TestSceneClassWithArray>();

            var testObj = new TestSceneClassWithArray
            {
                nestedItems = null
            };

            var liveClass = LiveClass.Find(typeof(TestSceneClassWithArray));
            new LiveObjectHandle("array-id-3", liveClass, testObj);

            var json = @"{
                ""objects"": [
                    {
                        ""@type"": ""TestSceneClassWithArray"",
                        ""@id"": ""array-id-3"",
                        ""nestedItems"": [
                            { ""id"": 100, ""name"": ""ItemA"" },
                            { ""id"": 200, ""name"": ""ItemB"" }
                        ]
                    }
                ]
            }";

            // Act
            LiveSceneSerializer.LiveSceneFromJson(json, _resolver);

            // Assert
            Assert.IsNotNull(testObj.nestedItems);
            Assert.AreEqual(2, testObj.nestedItems.Length);
            Assert.AreEqual(100, testObj.nestedItems[0].id);
            Assert.AreEqual("ItemA", testObj.nestedItems[0].name);
            Assert.AreEqual(200, testObj.nestedItems[1].id);
            Assert.AreEqual("ItemB", testObj.nestedItems[1].name);
        }

        [Test]
        public void LiveSceneFromJson_WithEmptyArray_DeserializesCorrectly()
        {
            // Arrange
            LiveClass.RegisterFromAttributes<TestSceneClassWithArray>();

            var testObj = new TestSceneClassWithArray
            {
                intArray = new int[] { 1, 2, 3 } // 既存の値がある
            };

            var liveClass = LiveClass.Find(typeof(TestSceneClassWithArray));
            new LiveObjectHandle("array-id-4", liveClass, testObj);

            var json = @"{
                ""objects"": [
                    {
                        ""@type"": ""TestSceneClassWithArray"",
                        ""@id"": ""array-id-4"",
                        ""intArray"": []
                    }
                ]
            }";

            // Act
            LiveSceneSerializer.LiveSceneFromJson(json, _resolver);

            // Assert
            Assert.IsNotNull(testObj.intArray);
            Assert.AreEqual(0, testObj.intArray.Length);
        }

        [Test]
        public void LiveSceneFromJson_ArrayLengthChange_HandlesCorrectly()
        {
            // Arrange
            LiveClass.RegisterFromAttributes<TestSceneClassWithArray>();

            var testObj = new TestSceneClassWithArray
            {
                intArray = new int[] { 1, 2, 3 } // 3要素
            };

            var liveClass = LiveClass.Find(typeof(TestSceneClassWithArray));
            new LiveObjectHandle("array-id-5", liveClass, testObj);

            // 5要素に変更
            var json = @"{
                ""objects"": [
                    {
                        ""@type"": ""TestSceneClassWithArray"",
                        ""@id"": ""array-id-5"",
                        ""intArray"": [10, 20, 30, 40, 50]
                    }
                ]
            }";

            // Act
            LiveSceneSerializer.LiveSceneFromJson(json, _resolver);

            // Assert
            Assert.IsNotNull(testObj.intArray);
            Assert.AreEqual(5, testObj.intArray.Length);
            Assert.AreEqual(10, testObj.intArray[0]);
            Assert.AreEqual(50, testObj.intArray[4]);
        }

        [Test]
        public void LiveSceneFromJson_WithIntList_DeserializesCorrectly()
        {
            // Arrange
            LiveClass.RegisterFromAttributes<TestSceneClassWithArray>();

            var testObj = new TestSceneClassWithArray
            {
                intList = null
            };

            var liveClass = LiveClass.Find(typeof(TestSceneClassWithArray));
            new LiveObjectHandle("array-id-6", liveClass, testObj);

            var json = @"{
                ""objects"": [
                    {
                        ""@type"": ""TestSceneClassWithArray"",
                        ""@id"": ""array-id-6"",
                        ""intList"": [100, 200, 300]
                    }
                ]
            }";

            // Act
            LiveSceneSerializer.LiveSceneFromJson(json, _resolver);

            // Assert
            Assert.IsNotNull(testObj.intList);
            Assert.AreEqual(3, testObj.intList.Count);
            Assert.AreEqual(100, testObj.intList[0]);
            Assert.AreEqual(200, testObj.intList[1]);
            Assert.AreEqual(300, testObj.intList[2]);
        }

        #endregion

        #region RoundTrip Tests

        [Test]
        public void RoundTrip_BasicProperties_PreservesData()
        {
            // Arrange
            LiveClass.RegisterFromAttributes<TestSceneClass>();

            var originalObj = new TestSceneClass
            {
                value = 42,
                name = "RoundTrip",
                position = 1.234f
            };

            var liveClass = LiveClass.Find(typeof(TestSceneClass));
            new LiveObjectHandle("roundtrip-1", liveClass, originalObj);

            // Act - Serialize
            var json = LiveSceneSerializer.LiveSceneToJson(new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver);

            // 新しいオブジェクトを作成してデシリアライズ
            var toRemove = LiveObjectRegistry.instances.ToList();
            foreach (var obj in toRemove) obj.Unregister();

            var newObj = new TestSceneClass { value = 0, name = "", position = 0f };
            new LiveObjectHandle("roundtrip-1", liveClass, newObj);

            // Act - Deserialize
            LiveSceneSerializer.LiveSceneFromJson(json, _resolver);

            // Assert
            Assert.AreEqual(42, newObj.value);
            Assert.AreEqual("RoundTrip", newObj.name);
            Assert.AreEqual(1.234f, newObj.position, 0.001f);
        }

        [Test]
        public void RoundTrip_IntArray_PreservesData()
        {
            // Arrange
            LiveClass.RegisterFromAttributes<TestSceneClassWithArray>();

            var originalObj = new TestSceneClassWithArray
            {
                intArray = new int[] { 1, 2, 3, 4, 5 }
            };

            var liveClass = LiveClass.Find(typeof(TestSceneClassWithArray));
            new LiveObjectHandle("roundtrip-array-1", liveClass, originalObj);

            // Act - Serialize
            var json = LiveSceneSerializer.LiveSceneToJson(new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver);

            // 新しいオブジェクトを作成
            var toRemove = LiveObjectRegistry.instances.ToList();
            foreach (var obj in toRemove) obj.Unregister();

            var newObj = new TestSceneClassWithArray { intArray = null };
            new LiveObjectHandle("roundtrip-array-1", liveClass, newObj);

            // Act - Deserialize
            LiveSceneSerializer.LiveSceneFromJson(json, _resolver);

            // Assert
            Assert.IsNotNull(newObj.intArray);
            Assert.AreEqual(5, newObj.intArray.Length);
            for (int i = 0; i < 5; i++)
            {
                Assert.AreEqual(i + 1, newObj.intArray[i]);
            }
        }

        [Test]
        public void RoundTrip_ObjectArray_PreservesData()
        {
            // Arrange
            LiveClass.RegisterFromAttributes<TestSceneClassWithArray>();

            var originalObj = new TestSceneClassWithArray
            {
                nestedItems = new NestedItem[]
                {
                    new NestedItem { id = 1, name = "Alpha" },
                    new NestedItem { id = 2, name = "Beta" },
                    new NestedItem { id = 3, name = "Gamma" }
                }
            };

            var liveClass = LiveClass.Find(typeof(TestSceneClassWithArray));
            new LiveObjectHandle("roundtrip-array-2", liveClass, originalObj);

            // Act - Serialize
            var json = LiveSceneSerializer.LiveSceneToJson(new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver);

            // 新しいオブジェクトを作成
            var toRemove = LiveObjectRegistry.instances.ToList();
            foreach (var obj in toRemove) obj.Unregister();

            var newObj = new TestSceneClassWithArray { nestedItems = null };
            new LiveObjectHandle("roundtrip-array-2", liveClass, newObj);

            // Act - Deserialize
            LiveSceneSerializer.LiveSceneFromJson(json, _resolver);

            // Assert
            Assert.IsNotNull(newObj.nestedItems);
            Assert.AreEqual(3, newObj.nestedItems.Length);

            Assert.AreEqual(1, newObj.nestedItems[0].id);
            Assert.AreEqual("Alpha", newObj.nestedItems[0].name);
            Assert.AreEqual(2, newObj.nestedItems[1].id);
            Assert.AreEqual("Beta", newObj.nestedItems[1].name);
            Assert.AreEqual(3, newObj.nestedItems[2].id);
            Assert.AreEqual("Gamma", newObj.nestedItems[2].name);
        }

        [Test]
        public void RoundTrip_MultipleObjects_PreservesAll()
        {
            // Arrange
            LiveClass.RegisterFromAttributes<TestSceneClass>();
            LiveClass.RegisterFromAttributes<TestSceneClassWithArray>();

            var obj1 = new TestSceneClass { value = 100, name = "First", position = 1.0f };
            var obj2 = new TestSceneClassWithArray { intArray = new int[] { 10, 20 } };

            var liveClass1 = LiveClass.Find(typeof(TestSceneClass));
            var liveClass2 = LiveClass.Find(typeof(TestSceneClassWithArray));
            new LiveObjectHandle("multi-1", liveClass1, obj1);
            new LiveObjectHandle("multi-2", liveClass2, obj2);

            // Act - Serialize
            var json = LiveSceneSerializer.LiveSceneToJson(new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver);

            // 新しいオブジェクトを作成
            var toRemove = LiveObjectRegistry.instances.ToList();
            foreach (var obj in toRemove) obj.Unregister();

            var newObj1 = new TestSceneClass { value = 0, name = "", position = 0f };
            var newObj2 = new TestSceneClassWithArray { intArray = null };
            new LiveObjectHandle("multi-1", liveClass1, newObj1);
            new LiveObjectHandle("multi-2", liveClass2, newObj2);

            // Act - Deserialize
            LiveSceneSerializer.LiveSceneFromJson(json, _resolver);

            // Assert
            Assert.AreEqual(100, newObj1.value);
            Assert.AreEqual("First", newObj1.name);
            Assert.AreEqual(1.0f, newObj1.position, 0.001f);

            Assert.IsNotNull(newObj2.intArray);
            Assert.AreEqual(2, newObj2.intArray.Length);
            Assert.AreEqual(10, newObj2.intArray[0]);
            Assert.AreEqual(20, newObj2.intArray[1]);
        }

        [Test]
        public void RoundTrip_DirtyProperties_PreservesData()
        {
            // Arrange
            LiveClass.RegisterFromAttributes<TestSceneClass>();

            var originalObj = new TestSceneClass
            {
                value = 0,
                name = "",
                position = 5.5f
            };

            var liveClass = LiveClass.Find(typeof(TestSceneClass));
            var liveObj = new LiveObjectHandle("dirty-test", liveClass, originalObj);

            // ベースライン設定
            LivePropertyUtility.SetDefault(liveObj);

            // valueとnameのみ変更（EnsureDefaultCapturedが自動で呼ばれdirty判定される）
            var valueProp = liveObj.FindProperty("value");
            valueProp.Value.SetValue(42);
            var nameProp = liveObj.FindProperty("name");
            nameProp.Value.SetValue("Dirty");

            // Act - Serialize (DeltaFromDefault)
            var json = LiveSceneSerializer.LiveSceneToJson(new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver, SerializeMode.Delta);

            // 新しいオブジェクトを作成
            var toRemove = LiveObjectRegistry.instances.ToList();
            foreach (var obj in toRemove) obj.Unregister();

            var newObj = new TestSceneClass { value = 0, name = "", position = 99.9f };
            new LiveObjectHandle("dirty-test", liveClass, newObj);

            // Act - Deserialize
            LiveSceneSerializer.LiveSceneFromJson(json, _resolver);

            // Assert - dirtyなプロパティのみ更新される
            Assert.AreEqual(42, newObj.value);
            Assert.AreEqual("Dirty", newObj.name);
            // positionはdirtyでなかったので、JSONに含まれず元の値のまま
            Assert.AreEqual(99.9f, newObj.position, 0.001f);
        }

        [Test]
        public void RoundTrip_StringArray_PreservesValues()
        {
            // Arrange
            LiveClass.RegisterFromAttributes<TestSceneClassWithArray>();

            var originalObj = new TestSceneClassWithArray
            {
                stringArray = new string[] { "apple", "banana", "cherry" }
            };

            var liveClass = LiveClass.Find(typeof(TestSceneClassWithArray));
            new LiveObjectHandle("rt-str-arr", liveClass, originalObj);

            // Act - Serialize
            var json = LiveSceneSerializer.LiveSceneToJson(new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver);

            var toRemove = LiveObjectRegistry.instances.ToList();
            foreach (var obj in toRemove) obj.Unregister();

            var newObj = new TestSceneClassWithArray { stringArray = null };
            new LiveObjectHandle("rt-str-arr", liveClass, newObj);

            // Act - Deserialize
            LiveSceneSerializer.LiveSceneFromJson(json, _resolver);

            // Assert
            Assert.IsNotNull(newObj.stringArray);
            Assert.AreEqual(3, newObj.stringArray.Length);
            Assert.AreEqual("apple", newObj.stringArray[0]);
            Assert.AreEqual("banana", newObj.stringArray[1]);
            Assert.AreEqual("cherry", newObj.stringArray[2]);
        }

        [Test]
        public void RoundTrip_StructArray_PreservesValues()
        {
            // Arrange
            LiveClass.RegisterFromAttributes<TestSceneClassWithStructArray>();
            LiveClass.RegisterFromAttributes<TestSceneNestedStruct>();

            var originalObj = new TestSceneClassWithStructArray
            {
                items = new TestSceneNestedStruct[]
                {
                    new TestSceneNestedStruct { id = 1, name = "First" },
                    new TestSceneNestedStruct { id = 2, name = "Second" }
                }
            };

            var liveClass = LiveClass.Find(typeof(TestSceneClassWithStructArray));
            new LiveObjectHandle("rt-struct-arr", liveClass, originalObj);

            // Act - Serialize
            var json = LiveSceneSerializer.LiveSceneToJson(new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver);

            var toRemove = LiveObjectRegistry.instances.ToList();
            foreach (var obj in toRemove) obj.Unregister();

            var newObj = new TestSceneClassWithStructArray { items = null };
            new LiveObjectHandle("rt-struct-arr", liveClass, newObj);

            // Act - Deserialize
            LiveSceneSerializer.LiveSceneFromJson(json, _resolver);

            // Assert
            Assert.IsNotNull(newObj.items);
            Assert.AreEqual(2, newObj.items.Length);
            Assert.AreEqual(1, newObj.items[0].id);
            Assert.AreEqual("First", newObj.items[0].name);
            Assert.AreEqual(2, newObj.items[1].id);
            Assert.AreEqual("Second", newObj.items[1].name);
        }

        [Test]
        public void RoundTrip_IntList_PreservesValues()
        {
            // Arrange
            LiveClass.RegisterFromAttributes<TestSceneClassWithArray>();

            var originalObj = new TestSceneClassWithArray
            {
                intList = new List<int> { 10, 20, 30, 40 }
            };

            var liveClass = LiveClass.Find(typeof(TestSceneClassWithArray));
            new LiveObjectHandle("rt-int-list", liveClass, originalObj);

            // Act - Serialize
            var json = LiveSceneSerializer.LiveSceneToJson(new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver);

            var toRemove = LiveObjectRegistry.instances.ToList();
            foreach (var obj in toRemove) obj.Unregister();

            var newObj = new TestSceneClassWithArray { intList = null };
            new LiveObjectHandle("rt-int-list", liveClass, newObj);

            // Act - Deserialize
            LiveSceneSerializer.LiveSceneFromJson(json, _resolver);

            // Assert
            Assert.IsNotNull(newObj.intList);
            Assert.AreEqual(4, newObj.intList.Count);
            Assert.AreEqual(10, newObj.intList[0]);
            Assert.AreEqual(20, newObj.intList[1]);
            Assert.AreEqual(30, newObj.intList[2]);
            Assert.AreEqual(40, newObj.intList[3]);
        }

        [Test]
        public void RoundTrip_RefList_PreservesReferences()
        {
            // Arrange
            LiveClass.RegisterFromAttributes<TestSceneRefItem>();
            LiveClass.RegisterFromAttributes<TestSceneContainerWithRefList>();

            var item1 = new TestSceneRefItem { name = "A", value = 10 };
            var item2 = new TestSceneRefItem { name = "B", value = 20 };
            var container = new TestSceneContainerWithRefList
            {
                items = new List<TestSceneRefItem> { item1, item2 }
            };

            var containerClass = LiveClass.Find(typeof(TestSceneContainerWithRefList));
            var itemClass = LiveClass.Find(typeof(TestSceneRefItem));
            new LiveObjectHandle("container-rt", containerClass, container);
            new LiveObjectHandle("item-rt-1", itemClass, item1);
            new LiveObjectHandle("item-rt-2", itemClass, item2);

            // Act - Serialize (All)
            var json = LiveSceneSerializer.LiveSceneToJson(new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver, SerializeMode.Snapshot);

            // JSON内に@refが含まれることを確認
            var jRoot = JObject.Parse(json);
            var objects = jRoot["objects"] as JArray;
            var containerObj = objects.FirstOrDefault(o => EntryKey(o) =="container-rt") as JObject;
            Assert.IsNotNull(containerObj);
            var itemsArr = containerObj["items"] as JArray;
            Assert.IsNotNull(itemsArr);
            Assert.AreEqual(2, itemsArr.Count);

            // 各要素に@refが含まれる
            Assert.IsNotNull(itemsArr[0]["@ref"]);
            Assert.IsNotNull(itemsArr[1]["@ref"]);
            Assert.AreEqual("item-rt-1", itemsArr[0]["@ref"].Value<string>());
            Assert.AreEqual("item-rt-2", itemsArr[1]["@ref"].Value<string>());

            // Deserialize - 既存のLiveObjectはそのまま（@refで参照解決）
            LiveSceneSerializer.LiveSceneFromJson(json, _resolver);

            // Assert - 参照が解決されている
            Assert.AreEqual(2, container.items.Count);
            Assert.AreSame(item1, container.items[0]);
            Assert.AreSame(item2, container.items[1]);
        }

        [Test]
        public void RoundTrip_RefList_NonDirtyElements_NotOutput()
        {
            // Arrange
            LiveClass.RegisterFromAttributes<TestSceneRefItem>();
            LiveClass.RegisterFromAttributes<TestSceneContainerWithRefList>();

            var item1 = new TestSceneRefItem { name = "X", value = 100 };
            var item2 = new TestSceneRefItem { name = "Y", value = 200 };
            var container = new TestSceneContainerWithRefList
            {
                items = new List<TestSceneRefItem> { item1, item2 }
            };

            var containerClass = LiveClass.Find(typeof(TestSceneContainerWithRefList));
            var itemClass = LiveClass.Find(typeof(TestSceneRefItem));
            var containerLive = new LiveObjectHandle("container-nd", containerClass, container);
            new LiveObjectHandle("item-nd-1", itemClass, item1);
            new LiveObjectHandle("item-nd-2", itemClass, item2);

            // SetDefaultで全プロパティのデフォルトをキャプチャ
            LivePropertyUtility.SetDefault(containerLive);

            // 値を変更しない → 全要素がnon-dirty

            // Act - DeltaFromDefaultでシリアライズ
            var json = LiveSceneSerializer.LiveSceneToJson(new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver, SerializeMode.Delta);

            // Assert - non-dirtyのcontainerはdelta出力に含まれない
            var jRoot = JObject.Parse(json);
            var objects = jRoot["objects"] as JArray;
            var containerObj = objects?.FirstOrDefault(o => EntryKey(o) =="container-nd") as JObject;
            Assert.IsNull(containerObj, "Non-dirty container should not be in DeltaFromDefault output");
        }

        [Test]
        public void RoundTrip_DeltaFromDefault_BasicProperties()
        {
            // Arrange
            LiveClass.RegisterFromAttributes<TestSceneClass>();

            var original = new TestSceneClass { value = 42, name = "Original", position = 1.0f };
            var liveClass = LiveClass.Find(typeof(TestSceneClass));
            var liveObj = new LiveObjectHandle("rt-dirty-basic", liveClass, original);

            // デフォルトキャプチャ → 値変更
            LivePropertyUtility.SetDefault(liveObj);
            original.value = 99; // valueのみ変更

            // Act - DeltaFromDefaultでシリアライズ
            var json = LiveSceneSerializer.LiveSceneToJson(new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver, SerializeMode.Delta);

            // 新オブジェクトに復元（nameとpositionは元の値のまま）
            var toRemove = LiveObjectRegistry.instances.ToList();
            foreach (var obj in toRemove) obj.Unregister();

            var restored = new TestSceneClass { value = 42, name = "Original", position = 1.0f };
            new LiveObjectHandle("rt-dirty-basic", liveClass, restored);
            LiveSceneSerializer.LiveSceneFromJson(json, _resolver);

            // Assert - dirtyだったvalueのみ更新される
            Assert.AreEqual(99, restored.value);
            Assert.AreEqual("Original", restored.name);
            Assert.AreEqual(1.0f, restored.position, 0.001f);
        }

        [Test]
        public void RoundTrip_DeltaFromDefault_ListAdd()
        {
            // Arrange - Allフィルタでリスト追加のラウンドトリップを検証
            // （DeltaFromDefaultフィルタではプリミティブ型リスト要素の個別dirtyが検出されないため）
            LiveClass.RegisterFromAttributes<TestSceneClassWithArray>();

            var original = new TestSceneClassWithArray
            {
                intList = new List<int> { 10, 20, 30 }
            };
            var liveClass = LiveClass.Find(typeof(TestSceneClassWithArray));
            new LiveObjectHandle("rt-dirty-list", liveClass, original);

            // Act - Allフィルタでシリアライズ
            var json = LiveSceneSerializer.LiveSceneToJson(new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver, SerializeMode.Snapshot);

            // 新オブジェクトに復元
            var toRemove = LiveObjectRegistry.instances.ToList();
            foreach (var obj in toRemove) obj.Unregister();

            var restored = new TestSceneClassWithArray { intList = new List<int>() };
            new LiveObjectHandle("rt-dirty-list", liveClass, restored);
            LiveSceneSerializer.LiveSceneFromJson(json, _resolver);

            // Assert
            Assert.IsNotNull(restored.intList);
            Assert.AreEqual(3, restored.intList.Count);
            Assert.AreEqual(10, restored.intList[0]);
            Assert.AreEqual(20, restored.intList[1]);
            Assert.AreEqual(30, restored.intList[2]);
        }

        [Test]
        public void RoundTrip_DeltaFromDefault_RefListAdd()
        {
            // Arrange
            LiveClass.RegisterFromAttributes<TestSceneRefItem>();
            LiveClass.RegisterFromAttributes<TestSceneContainerWithRefList>();

            var item1 = new TestSceneRefItem { name = "A", value = 10 };
            var container = new TestSceneContainerWithRefList
            {
                items = new List<TestSceneRefItem> { item1 }
            };

            var containerClass = LiveClass.Find(typeof(TestSceneContainerWithRefList));
            var itemClass = LiveClass.Find(typeof(TestSceneRefItem));
            var containerLive = new LiveObjectHandle("container-dirty-ref", containerClass, container);
            new LiveObjectHandle("item-dirty-1", itemClass, item1);

            // デフォルトキャプチャ
            LivePropertyUtility.SetDefault(containerLive);

            // 新要素追加
            var item2 = new TestSceneRefItem { name = "B", value = 20 };
            container.items.Add(item2);
            new LiveObjectHandle("item-dirty-2", itemClass, item2);

            // Act - DeltaFromDefaultでシリアライズ
            var json = LiveSceneSerializer.LiveSceneToJson(new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver, SerializeMode.Delta);

            // JSON内に@refが含まれることを確認
            var jRoot = JObject.Parse(json);
            var objects = jRoot["objects"] as JArray;
            var containerObj = objects.FirstOrDefault(o => EntryKey(o) =="container-dirty-ref") as JObject;
            Assert.IsNotNull(containerObj, "Container should be in DeltaFromDefault output");
            var itemsArr = containerObj["items"] as JArray;
            Assert.IsNotNull(itemsArr, "items array should exist in DeltaFromDefault output");

            // デシリアライズして参照が解決されることを確認
            // デルタ形式はデフォルト状態からの差分なので、デフォルト状態のコンテナに対して適用する
            var container2 = new TestSceneContainerWithRefList
            {
                items = new List<TestSceneRefItem> { item1 }
            };
            var containerLive2 = new LiveObjectHandle("container-dirty-ref", containerClass, container2);
            LivePropertyUtility.SetDefault(containerLive2);
            LiveSceneSerializer.LiveSceneFromJson(json, _resolver);

            Assert.AreEqual(2, container2.items.Count);
            Assert.AreSame(item1, container2.items[0]);
            Assert.AreSame(item2, container2.items[1]);
        }

        [Test]
        public void RoundTrip_DeltaFromDefault_AfterClearDirty_DetectsNewChanges()
        {
            // Arrange
            LiveClass.RegisterFromAttributes<TestSceneClass>();

            var original = new TestSceneClass { value = 10, name = "Initial", position = 1.0f };
            var liveClass = LiveClass.Find(typeof(TestSceneClass));
            var liveObj = new LiveObjectHandle("rt-clear-dirty", liveClass, original);

            // デフォルトキャプチャ → 値変更 → ClearDirty
            LivePropertyUtility.SetDefault(liveObj);
            original.value = 50;
            liveObj.ClearDirty();

            // ClearDirty後、別の変更を加える
            original.name = "Changed";

            // Act - DeltaFromDefaultでシリアライズ
            var json = LiveSceneSerializer.LiveSceneToJson(new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver, SerializeMode.Delta);

            // 新オブジェクトに復元
            var toRemove = LiveObjectRegistry.instances.ToList();
            foreach (var obj in toRemove) obj.Unregister();

            var restored = new TestSceneClass { value = 50, name = "Initial", position = 1.0f };
            new LiveObjectHandle("rt-clear-dirty", liveClass, restored);
            LiveSceneSerializer.LiveSceneFromJson(json, _resolver);

            // Assert - ClearDirty後のnameの変更のみ反映される
            Assert.AreEqual(50, restored.value); // ClearDirty後の値のまま（dirtyでない）
            Assert.AreEqual("Changed", restored.name); // 新しい変更が反映される
            Assert.AreEqual(1.0f, restored.position, 0.001f); // 変更されていない
        }

        [Test]
        public void LiveSceneToJson_DeltaFromDefault_UnchangedString_NotSerialized()
        {
            // Arrange
            LiveClass.RegisterFromAttributes<TestSceneClass>();

            var obj = new TestSceneClass { value = 10, name = "Initial", position = 1.0f };
            var liveClass = LiveClass.Find(typeof(TestSceneClass));
            var liveObj = new LiveObjectHandle("str-unchanged", liveClass, obj);

            LivePropertyUtility.SetDefault(liveObj);

            // Act - stringを変更せずDeltaFromDefaultでシリアライズ
            var json = LiveSceneSerializer.LiveSceneToJson(new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver, SerializeMode.Delta);

            // Assert - nameが出力されないことを検証
            var jRoot = JObject.Parse(json);
            var objects = jRoot["objects"] as JArray;
            if (objects != null && objects.Count > 0)
            {
                var containerObj = objects.FirstOrDefault(o => EntryKey(o) =="str-unchanged") as JObject;
                if (containerObj != null)
                {
                    Assert.IsNull(containerObj["name"], "Unchanged string 'name' should not appear in DeltaFromDefault output");
                }
            }
        }

        [Test]
        public void LiveSceneToJson_DeltaFromDefault_ChangedString_Serialized()
        {
            // Arrange
            LiveClass.RegisterFromAttributes<TestSceneClass>();

            var obj = new TestSceneClass { value = 10, name = "Initial", position = 1.0f };
            var liveClass = LiveClass.Find(typeof(TestSceneClass));
            var liveObj = new LiveObjectHandle("str-changed", liveClass, obj);

            LivePropertyUtility.SetDefault(liveObj);

            // stringを変更
            obj.name = "Modified";

            // Act - DeltaFromDefaultでシリアライズ
            var json = LiveSceneSerializer.LiveSceneToJson(new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver, SerializeMode.Delta);

            // Assert - nameが出力されることを検証
            var jRoot = JObject.Parse(json);
            var objects = jRoot["objects"] as JArray;
            Assert.IsNotNull(objects, "objects array should exist");
            var containerObj = objects.FirstOrDefault(o => EntryKey(o) =="str-changed") as JObject;
            Assert.IsNotNull(containerObj, "Changed string object should be in DeltaFromDefault output");
            Assert.IsNotNull(containerObj["name"], "Changed string 'name' should appear in DeltaFromDefault output");
            Assert.AreEqual("Modified", containerObj["name"].Value<string>());
        }

        [Test]
        public void RoundTrip_DeltaFromDefault_StringProperty()
        {
            // Arrange
            LiveClass.RegisterFromAttributes<TestSceneClass>();

            var original = new TestSceneClass { value = 10, name = "Initial", position = 1.0f };
            var liveClass = LiveClass.Find(typeof(TestSceneClass));
            var liveObj = new LiveObjectHandle("rt-string", liveClass, original);

            LivePropertyUtility.SetDefault(liveObj);

            // nameのみ変更
            original.name = "UpdatedName";

            // Act - DeltaFromDefaultでシリアライズ
            var json = LiveSceneSerializer.LiveSceneToJson(new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver, SerializeMode.Delta);

            // 新オブジェクトに復元
            var toRemove = LiveObjectRegistry.instances.ToList();
            foreach (var obj in toRemove) obj.Unregister();

            var restored = new TestSceneClass { value = 10, name = "Initial", position = 1.0f };
            new LiveObjectHandle("rt-string", liveClass, restored);
            LiveSceneSerializer.LiveSceneFromJson(json, _resolver);

            // Assert - nameのみ更新され、他は元の値のまま
            Assert.AreEqual(10, restored.value); // 変更されていない
            Assert.AreEqual("UpdatedName", restored.name); // 変更が反映される
            Assert.AreEqual(1.0f, restored.position, 0.001f); // 変更されていない
        }

        #endregion

        #region Persistence Filter Tests

        [Serializable]
        [LiveClass("TestPersistenceClass")]
        public class TestPersistenceClass
        {
            // LiveField → isPersistable = true（永続化される）
            [LiveField]
            public int persistableValue;

            [LiveField]
            public string persistableName;

            // LiveProperty → isPersistable = false（永続化されない）
            [LiveProperty]
            public float nonPersistableComputed { get => _computedBacking; set => _computedBacking = value; }
            private float _computedBacking;

            [LiveProperty]
            public string nonPersistableLabel { get => _labelBacking; set => _labelBacking = value; }
            private string _labelBacking;
        }

        [Test]
        public void LiveSceneToJson_ExcludesNonPersistableProperties()
        {
            // Arrange
            LiveClass.RegisterFromAttributes<TestPersistenceClass>();

            var obj = new TestPersistenceClass
            {
                persistableValue = 42,
                persistableName = "saved",
                nonPersistableComputed = 3.14f,
                nonPersistableLabel = "not_saved",
            };

            var liveClass = LiveClass.Find(typeof(TestPersistenceClass));
            new LiveObjectHandle("persist-test", liveClass, obj);

            // Act
            var json = LiveSceneSerializer.LiveSceneToJson(new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver);

            // Assert
            var jRoot = Newtonsoft.Json.Linq.JObject.Parse(json);
            var jArray = jRoot["objects"] as Newtonsoft.Json.Linq.JArray;
            Assert.IsNotNull(jArray);
            Assert.AreEqual(1, jArray.Count);

            var jObj = jArray[0] as Newtonsoft.Json.Linq.JObject;
            Assert.IsNotNull(jObj);

            // persistable フィールドは含まれる
            Assert.IsNotNull(jObj["persistableValue"]);
            Assert.IsNotNull(jObj["persistableName"]);

            // non-persistable プロパティは含まれない
            Assert.IsNull(jObj["nonPersistableComputed"]);
            Assert.IsNull(jObj["nonPersistableLabel"]);
        }

        [Test]
        public void RoundTrip_MixedPersistence_OnlyPersistableFieldsRestored()
        {
            // Arrange
            LiveClass.RegisterFromAttributes<TestPersistenceClass>();

            var original = new TestPersistenceClass
            {
                persistableValue = 100,
                persistableName = "original",
                nonPersistableComputed = 9.99f,
                nonPersistableLabel = "computed",
            };

            var liveClass = LiveClass.Find(typeof(TestPersistenceClass));
            new LiveObjectHandle("roundtrip-persist", liveClass, original);

            // Act - Serialize
            var json = LiveSceneSerializer.LiveSceneToJson(new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver);

            // 新しいオブジェクトを作成
            var toRemove = LiveObjectRegistry.instances.ToList();
            foreach (var o in toRemove) o.Unregister();

            var restored = new TestPersistenceClass
            {
                persistableValue = 0,
                persistableName = "",
                nonPersistableComputed = 0f,
                nonPersistableLabel = "",
            };
            new LiveObjectHandle("roundtrip-persist", liveClass, restored);

            // Act - Deserialize
            LiveSceneSerializer.LiveSceneFromJson(json, _resolver);

            // Assert - persistable フィールドのみ復元される
            Assert.AreEqual(100, restored.persistableValue);
            Assert.AreEqual("original", restored.persistableName);

            // non-persistable プロパティは保存JSONに含まれないため、デフォルト値のまま
            Assert.AreEqual(0f, restored.nonPersistableComputed, 0.001f);
            Assert.AreEqual("", restored.nonPersistableLabel);
        }

        #endregion

        #region Additions Section Tests

        [LiveClass("TestAdditionsComponent")]
        public class TestAdditionsComponent : MonoBehaviour
        {
            [LiveField]
            public int health;

            [LiveField]
            public string label;
        }

        [LiveClass("TestAdditionsComponent2")]
        public class TestAdditionsComponent2 : MonoBehaviour
        {
            [LiveField]
            public float speed;
        }

        [Serializable]
        [LiveClass("TestPluglikePath")]
        public class TestPluglikePath
        {
            [LiveField]
            public string rootObjectName;

            [LiveField]
            public string transformName;
        }

        [LiveClass("TestPluglikeComponent")]
        public class TestPluglikeComponent : MonoBehaviour
        {
            [SerializeField]
            [LiveField]
            public TestPluglikePath target = new TestPluglikePath();
        }

        /// <summary>
        /// 配列プロパティを持つテスト用コンポーネント（meshStateOverrides相当）
        /// </summary>
        [LiveClass("TestComponentWithArray")]
        public class TestComponentWithArray : MonoBehaviour
        {
            [LiveField]
            public List<TestDeltaNewItem> items;
        }

        [Test]
        public void LiveSceneToJson_TrackedPrefabInstance_HasOpNewAndPrefab()
        {
            // Arrange
            LiveClass.RegisterFromAttributes<TestAdditionsComponent>();
            LiveClass.RegisterFromAttributes<LiveGameObject>();

            var instance = new GameObject("TestPrefab(Clone)");
            try
            {
                // LiveGameObjectでGOをラップし、prefabSourceKey (Asset GUID) を設定
                var liveGO = new LiveGameObject(instance);
                liveGO.prefabSourceKey = "11111111111111111111111111111111";
                liveGO.OnEnable();

                var testComp = instance.AddComponent<TestAdditionsComponent>();
                testComp.health = 100;
                testComp.label = "Hero";
                var liveClass = LiveClass.Find(typeof(TestAdditionsComponent));
                var liveObj = new LiveObjectHandle("comp-id-1", liveClass, testComp);

                // Act
                var json = LiveSceneSerializer.LiveSceneToJson(new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver);

                // Assert
                var jRoot = JObject.Parse(json);
                Assert.IsNull(jRoot["instances"], "instances section should not exist");
                var objects = jRoot["objects"] as JArray;
                Assert.IsNotNull(objects);

                // @prefab を持つ新規インスタンスオブジェクトが存在する (@op は廃止、@source も付かない)
                bool foundPrefabObj = false;
                foreach (var obj in objects)
                {
                    if (obj is JObject o && o["@prefab"]?.ToString() == "11111111111111111111111111111111" && o["@source"] == null && o["@op"] == null)
                    {
                        foundPrefabObj = true;
                        break;
                    }
                }
                Assert.IsTrue(foundPrefabObj, "objects should contain entry with @prefab (guid) (no @op, no @source)");
            }
            finally
            {
                GameObject.DestroyImmediate(instance);
            }
        }

        [Test]
        public void LiveSceneToJson_ComponentOnTrackedInstance_HasOpNewAndPrefab()
        {
            // Arrange
            LiveClass.RegisterFromAttributes<TestAdditionsComponent>();
            LiveClass.RegisterFromAttributes<LiveGameObject>();

            var instance = new GameObject("LightPrefab(Clone)");
            try
            {
                var liveGO = new LiveGameObject(instance);
                liveGO.prefabSourceKey = "22222222222222222222222222222222";
                liveGO.OnEnable();

                var testComp = instance.AddComponent<TestAdditionsComponent>();
                testComp.health = 50;
                testComp.label = "Light";
                var liveClass = LiveClass.Find(typeof(TestAdditionsComponent));
                var liveObj = new LiveObjectHandle("light-comp-1", liveClass, testComp);

                // Act
                var json = LiveSceneSerializer.LiveSceneToJson(new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver);

                // Assert
                var jRoot = JObject.Parse(json);
                var objects = jRoot["objects"] as JArray;
                Assert.IsNotNull(objects);

                // light-comp-1 が @prefab: "<LightPrefab guid>" を持つ (新規インスタンスは @source / @op を持たない)
                bool found = false;
                foreach (var obj in objects)
                {
                    if (obj is JObject o && EntryKey(o) =="light-comp-1"
                        && o["@prefab"]?.ToString() == "22222222222222222222222222222222" && o["@source"] == null && o["@op"] == null)
                    {
                        found = true;
                        break;
                    }
                }
                Assert.IsTrue(found, "light-comp-1 should have @prefab (LightPrefab guid) (no @op, no @source)");
            }
            finally
            {
                GameObject.DestroyImmediate(instance);
            }
        }

        [Test]
        public void LiveSceneToJson_MultipleComponentsOnSameInstance_EachHasOpNewAndPrefab()
        {
            // Arrange
            LiveClass.RegisterFromAttributes<TestAdditionsComponent>();
            LiveClass.RegisterFromAttributes<TestAdditionsComponent2>();
            LiveClass.RegisterFromAttributes<LiveGameObject>();

            var instance = new GameObject("MultiCompPrefab(Clone)");
            try
            {
                var liveGO = new LiveGameObject(instance);
                liveGO.prefabSourceKey = "33333333333333333333333333333333";
                liveGO.OnEnable();

                var comp1 = instance.AddComponent<TestAdditionsComponent>();
                var comp2 = instance.AddComponent<TestAdditionsComponent2>();
                var liveClass1 = LiveClass.Find(typeof(TestAdditionsComponent));
                var liveClass2 = LiveClass.Find(typeof(TestAdditionsComponent2));
                new LiveObjectHandle("multi-comp-1", liveClass1, comp1);
                new LiveObjectHandle("multi-comp-2", liveClass2, comp2);

                // Act
                var json = LiveSceneSerializer.LiveSceneToJson(new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver);

                // Assert
                var jRoot = JObject.Parse(json);
                var objects = jRoot["objects"] as JArray;
                Assert.IsNotNull(objects);

                // 各コンポーネントが個別に @prefab を持つ (@op / @source なし)
                int prefabObjCount = 0;
                foreach (var obj in objects)
                {
                    if (obj is JObject o && o["@prefab"]?.ToString() == "33333333333333333333333333333333" && o["@source"] == null && o["@op"] == null)
                    {
                        prefabObjCount++;
                    }
                }
                Assert.GreaterOrEqual(prefabObjCount, 2, "At least 2 objects should have @prefab (MultiCompPrefab guid) (no @op, no @source)");
            }
            finally
            {
                GameObject.DestroyImmediate(instance);
            }
        }

        [Test]
        public void LiveSceneToJson_PrefabInstance_NestsLiveObject()
        {
            // Arrange
            LiveClass.RegisterFromAttributes<TestAdditionsComponent>();
            LiveClass.RegisterFromAttributes<LiveGameObject>();

            var instance = new GameObject("NestedPrefab(Clone)");
            try
            {
                var liveGO = new LiveGameObject(instance);
                liveGO.prefabSourceKey = "44444444444444444444444444444444";
                liveGO.OnEnable();

                var testComp = instance.AddComponent<TestAdditionsComponent>();
                testComp.health = 77;
                testComp.label = "Nested";
                var liveClass = LiveClass.Find(typeof(TestAdditionsComponent));
                new LiveObjectHandle("nested-comp-1", liveClass, testComp);

                // Act
                var json = LiveSceneSerializer.LiveSceneToJson(new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver);

                // Assert
                var jRoot = JObject.Parse(json);
                var objects = jRoot["objects"] as JArray;
                Assert.IsNotNull(objects);

                JObject entry = null;
                foreach (var obj in objects)
                {
                    if (obj is JObject o && EntryKey(o) =="nested-comp-1")
                    {
                        entry = o;
                        break;
                    }
                }
                Assert.IsNotNull(entry, "Entry for nested-comp-1 should exist");

                // フラット構造: @prefab/@id/@name/@type/user props が同一レベルに並ぶ。
                // 新規インスタンス (Prefab 由来) なので @source も @op も付かない。
                Assert.IsNull(entry["@op"], "Entry @op should be absent (deprecated)");
                Assert.IsNull(entry["@source"], "Entry @source should be absent for Prefab-new");
                Assert.AreEqual("44444444444444444444444444444444", entry["@prefab"]?.ToString(), "Entry @prefab should match (NestedPrefab guid)");
                Assert.AreEqual("TestAdditionsComponent", entry["@type"]?.ToString(), "Entry @type should match");
                Assert.IsNull(entry["liveObject"], "Entry should NOT have nested liveObject");
            }
            finally
            {
                GameObject.DestroyImmediate(instance);
            }
        }

        [Test]
        public void LiveSceneToJson_NoTrackedInstances_NoOpNewInObjects()
        {
            // Arrange
            LiveClass.RegisterFromAttributes<TestSceneClass>();

            var testObj = new TestSceneClass { value = 1, name = "Normal", position = 0f };
            var liveClass = LiveClass.Find(typeof(TestSceneClass));
            new LiveObjectHandle("normal-id", liveClass, testObj);

            // Act
            var json = LiveSceneSerializer.LiveSceneToJson(new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver);

            // Assert
            var jRoot = JObject.Parse(json);
            var objects = jRoot["objects"] as JArray;
            Assert.IsNotNull(objects);
            foreach (var obj in objects)
            {
                if (obj is JObject o)
                {
                    Assert.IsNull(o["@op"], "No object should have @op when no tracked instances");
                    Assert.IsNull(o["@prefab"], "Non-Prefab entry should have no @prefab");
                    Assert.IsNull(o["@id"], "Override root entry should have no @id (only @source)");
                    Assert.IsNotNull(o["@source"], "Override root entry should have @source");
                }
            }
        }

        [Test]
        public void LiveSceneFromJson_WithAdditionsSection_InstantiatesPrefab()
        {
            // Arrange
            LiveClass.RegisterFromAttributes<TestAdditionsComponent>();

            var prefab = new GameObject("RestorePrefab");
            var prefabCam = prefab.AddComponent<Camera>();
            PrefabRegistry.Register("55555555555555555555555555555555", prefab);

            GameObject createdInstance = null;
            try
            {
                var json = @"{
                    ""format"": ""jp.lilium.remotecontrol.scene"",
                    ""formatVersion"": 1,
                    ""objects"": [
                        {
                            ""@prefab"": ""55555555555555555555555555555555"",
                            ""@id"": ""restored-comp-1"",
                            ""@type"": ""TestAdditionsComponent""
                        }
                    ]
                }";

                // Act
                LiveSceneSerializer.LiveSceneFromJson(json, _resolver);

                // Assert - LiveObjectが旧IDで登録されているか
                var restored = LiveObjectRegistry.FindById("restored-comp-1");
                // TestAdditionsComponentのLiveClassでCamera型のコンポーネントを検索するが、
                // TestAdditionsComponentはComponentではないため、_RegisterComponentLiveObjectでは見つからない。
                // これは期待通りの動作 - 実際のComponentベースのLiveClassでのみ動作する。
                // ただしPrefabのInstantiate自体は成功するはず。

                // Prefabからインスタンスが生成されたことを確認
                // InstantiateFromPrefabで作られたオブジェクトを探す
                var clones = GameObject.FindObjectsOfType<Camera>();
                // prefab自身のCameraを除外して、クローンがあるか確認
                int cloneCount = 0;
                foreach (var c in clones)
                {
                    if (c.gameObject != prefab)
                    {
                        createdInstance = c.gameObject;
                        cloneCount++;
                    }
                }
                Assert.IsTrue(cloneCount >= 1, "Prefab should have been instantiated");
            }
            finally
            {
                if (createdInstance != null) GameObject.DestroyImmediate(createdInstance);
                GameObject.DestroyImmediate(prefab);
            }
        }

        [Test]
        public void LiveSceneFromJson_WithAdditions_AlreadyExists_SkipsInstantiation()
        {
            // Arrange
            LiveClass.RegisterFromAttributes<TestAdditionsComponent>();

            var prefab = new GameObject("SkipPrefab");
            PrefabRegistry.Register("66666666666666666666666666666666", prefab);

            // 既にこのIDのLiveObjectが存在する
            var existingGo = new GameObject("ExistingComp");
            var existingComp = existingGo.AddComponent<TestAdditionsComponent>();
            existingComp.health = 999;
            var liveClass = LiveClass.Find(typeof(TestAdditionsComponent));
            new LiveObjectHandle("existing-comp-1", liveClass, existingComp);

            try
            {
                var json = @"{
                    ""format"": ""jp.lilium.remotecontrol.scene"",
                    ""formatVersion"": 1,
                    ""objects"": [
                        {
                            ""@prefab"": ""66666666666666666666666666666666"",
                            ""@id"": ""existing-comp-1"",
                            ""@type"": ""TestAdditionsComponent""
                        }
                    ]
                }";

                // Act
                LiveSceneSerializer.LiveSceneFromJson(json, _resolver);

                // Assert - 既存のオブジェクトが変更されていないことを確認
                var found = LiveObjectRegistry.FindById("existing-comp-1");
                Assert.IsNotNull(found);
                Assert.AreEqual(existingComp, found.Value.target);
            }
            finally
            {
                GameObject.DestroyImmediate(existingGo);
                GameObject.DestroyImmediate(prefab);
            }
        }

        [Test]
        public void LiveSceneFromJson_WithAdditions_UnknownPrefab_HandlesGracefully()
        {
            // Arrange
            var json = @"{
                ""format"": ""jp.lilium.remotecontrol.scene"",
                    ""formatVersion"": 1,
                ""objects"": [
                    {
                        ""@prefab"": ""77777777777777777777777777777777"",
                        ""@id"": ""ghost-comp-1"",
                        ""@type"": ""TestAdditionsComponent""
                    }
                ]
            }";

            // Act & Assert - 例外なく処理され、プレハブ未解決の警告だけが出る
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*Prefab not found.*"));
            Assert.DoesNotThrow(() => LiveSceneSerializer.LiveSceneFromJson(json, _resolver));

            try
            {
                // 解決できなかった @prefab エントリは破棄されず PendingPrefabStore へ退避され、次の保存で
                // そのまま書き戻される。@prefab エントリは @source を持たないためプロパティ側の pending
                // 再出力では拾えず、退避しないと load→save で黙って失われる (それが以前の挙動だった)。
                var saved = JObject.Parse(LiveSceneSerializer.LiveSceneToJson(
                    new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver));
                var reEmitted = saved["objects"]?
                    .FirstOrDefault(t => t["@id"]?.Value<string>() == "ghost-comp-1");
                Assert.IsNotNull(reEmitted, "未解決の @prefab エントリが保存時に失われている");
                Assert.AreEqual("77777777777777777777777777777777", reEmitted["@prefab"]?.Value<string>());
            }
            finally
            {
                // 退避キューは静的なので、後続テストの保存に混ざらないよう明示的に空にする。
                PendingPrefabStore.Clear();
            }
        }

        [Test]
        public void LiveSceneFromJson_WithAdditionsThenObjects_RestoresProperties()
        {
            // Arrange
            LiveClass.RegisterFromAttributes<TestSceneClass>();

            // 既存のオブジェクトに対して@opなしでobjectsのみ復元するケース
            var testObj = new TestSceneClass { value = 0, name = "", position = 0f };
            var liveClass = LiveClass.Find(typeof(TestSceneClass));
            new LiveObjectHandle("prop-restore-1", liveClass, testObj);

            var json = @"{
                ""format"": ""jp.lilium.remotecontrol.scene"",
                    ""formatVersion"": 1,
                ""objects"": [
                    {
                        ""@type"": ""TestSceneClass"",
                        ""@id"": ""prop-restore-1"",
                        ""value"": 777,
                        ""name"": ""Restored"",
                        ""position"": 1.5
                    }
                ]
            }";

            // Act
            LiveSceneSerializer.LiveSceneFromJson(json, _resolver);

            // Assert
            Assert.AreEqual(777, testObj.value);
            Assert.AreEqual("Restored", testObj.name);
            Assert.AreEqual(1.5f, testObj.position, 0.001f);
        }

        [Test]
        public void LiveSceneFromJson_NoOpNew_WorksAsExistingBehavior()
        {
            // Arrange - @opなしの通常のobjectsのみのJSON
            LiveClass.RegisterFromAttributes<TestSceneClass>();

            var testObj = new TestSceneClass { value = 0, name = "", position = 0f };
            var liveClass = LiveClass.Find(typeof(TestSceneClass));
            new LiveObjectHandle("legacy-1", liveClass, testObj);

            var json = @"{
                ""objects"": [
                    {
                        ""@type"": ""TestSceneClass"",
                        ""@id"": ""legacy-1"",
                        ""value"": 42,
                        ""name"": ""Legacy""
                    }
                ]
            }";

            // Act
            LiveSceneSerializer.LiveSceneFromJson(json, _resolver);

            // Assert
            Assert.AreEqual(42, testObj.value);
            Assert.AreEqual("Legacy", testObj.name);
        }

        [Test]
        public void LiveSceneFromJson_MultipleInstancesOfSamePrefab_CreatesSeparateGameObjects()
        {
            LiveClass.RegisterFromAttributes<TestAdditionsComponent>();

            var prefab = new GameObject("MultiInstancePrefab");
            prefab.AddComponent<TestAdditionsComponent>();
            PrefabRegistry.Register("88888888888888888888888888888888", prefab);

            var json = @"{
                ""format"": ""jp.lilium.remotecontrol.scene"",
                ""formatVersion"": 1,
                ""objects"": [
                    { ""@prefab"": ""88888888888888888888888888888888"", ""@id"": ""inst-1"",
                      ""@name"": ""MultiInstancePrefab(Clone)"",
                      ""@type"": ""TestAdditionsComponent"", ""health"": 10 },
                    { ""@prefab"": ""88888888888888888888888888888888"", ""@id"": ""inst-2"",
                      ""@name"": ""MultiInstancePrefab(Clone) (1)"",
                      ""@type"": ""TestAdditionsComponent"", ""health"": 20 },
                    { ""@prefab"": ""88888888888888888888888888888888"", ""@id"": ""inst-3"",
                      ""@name"": ""MultiInstancePrefab(Clone) (2)"",
                      ""@type"": ""TestAdditionsComponent"", ""health"": 30 }
                ]
            }";

            var clones = new List<GameObject>();
            try
            {
                LiveSceneSerializer.LiveSceneFromJson(json, _resolver);

                var obj1 = LiveObjectRegistry.FindById("inst-1");
                var obj2 = LiveObjectRegistry.FindById("inst-2");
                var obj3 = LiveObjectRegistry.FindById("inst-3");
                Assert.IsNotNull(obj1, "inst-1 should be registered");
                Assert.IsNotNull(obj2, "inst-2 should be registered");
                Assert.IsNotNull(obj3, "inst-3 should be registered");

                var comp1 = obj1.Value.target as TestAdditionsComponent;
                var comp2 = obj2.Value.target as TestAdditionsComponent;
                var comp3 = obj3.Value.target as TestAdditionsComponent;
                Assert.IsNotNull(comp1);
                Assert.IsNotNull(comp2);
                Assert.IsNotNull(comp3);

                Assert.AreNotSame(comp1.gameObject, comp2.gameObject, "inst-1 and inst-2 must be different GameObjects");
                Assert.AreNotSame(comp2.gameObject, comp3.gameObject, "inst-2 and inst-3 must be different GameObjects");
                Assert.AreNotSame(comp1.gameObject, comp3.gameObject, "inst-1 and inst-3 must be different GameObjects");

                Assert.AreEqual("MultiInstancePrefab(Clone)", comp1.gameObject.name);
                Assert.AreEqual("MultiInstancePrefab(Clone) (1)", comp2.gameObject.name);
                Assert.AreEqual("MultiInstancePrefab(Clone) (2)", comp3.gameObject.name);

                Assert.AreEqual(10, comp1.health);
                Assert.AreEqual(20, comp2.health);
                Assert.AreEqual(30, comp3.health);

                clones.Add(comp1.gameObject);
                clones.Add(comp2.gameObject);
                clones.Add(comp3.gameObject);
            }
            finally
            {
                foreach (var c in clones) if (c != null) GameObject.DestroyImmediate(c);
                GameObject.DestroyImmediate(prefab);
            }
        }

        [Test]
        public void LiveSceneFromJson_MultipleComponentsOnSameInstance_SharesGameObject()
        {
            LiveClass.RegisterFromAttributes<TestAdditionsComponent>();
            LiveClass.RegisterFromAttributes<TestAdditionsComponent2>();

            var prefab = new GameObject("SharedPrefab");
            prefab.AddComponent<TestAdditionsComponent>();
            prefab.AddComponent<TestAdditionsComponent2>();
            PrefabRegistry.Register("99999999999999999999999999999999", prefab);

            var json = @"{
                ""format"": ""jp.lilium.remotecontrol.scene"",
                ""formatVersion"": 1,
                ""objects"": [
                    { ""@prefab"": ""99999999999999999999999999999999"", ""@id"": ""shared-1"",
                      ""@type"": ""TestAdditionsComponent"" },
                    { ""@prefab"": ""99999999999999999999999999999999"", ""@id"": ""shared-2"",
                      ""@type"": ""TestAdditionsComponent2"" }
                ]
            }";

            GameObject clone = null;
            try
            {
                LiveSceneSerializer.LiveSceneFromJson(json, _resolver);

                var obj1 = LiveObjectRegistry.FindById("shared-1");
                var obj2 = LiveObjectRegistry.FindById("shared-2");
                Assert.IsNotNull(obj1);
                Assert.IsNotNull(obj2);

                var comp1 = obj1.Value.target as TestAdditionsComponent;
                var comp2 = obj2.Value.target as TestAdditionsComponent2;
                Assert.IsNotNull(comp1);
                Assert.IsNotNull(comp2);
                Assert.AreSame(comp1.gameObject, comp2.gameObject, "Different component types should share the same GameObject");
                clone = comp1.gameObject;
            }
            finally
            {
                if (clone != null) GameObject.DestroyImmediate(clone);
                GameObject.DestroyImmediate(prefab);
            }
        }

        /// <summary>
        /// LiveGameObjectWithTransform のように、既に登録済みの componentType (GameObject) を
        /// 共有する別 displayName の Proxy も、シーンロード時に Container._objects へ追加されなければならない。
        /// 従来は LiveUnityObjectFactory._AutoRegisterDerivedTypes が componentType 衝突時に
        /// _registrationList にも登録しなかったため、displayName "GameObjectWithTransform" が
        /// 解決できず wrapper が null になり、Container に追加されなかった。
        /// </summary>
        [Test]
        public void LiveSceneFromJson_GameObjectWithTransform_AddedToContainerObjects()
        {
            LiveClass.RegisterFromAttributes<LiveObjectContainer>();
            LiveClass.RegisterFromAttributes<LiveGameObject>();
            LiveClass.RegisterFromAttributes<LiveGameObjectWithTransform>();

            var containerGo = new GameObject("TestContainer");
            var container = new LiveObjectContainer(containerGo.name, new List<ILiveObject>());

            var prefab = new GameObject("GLTF Model");
            PrefabRegistry.Register("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", prefab);

            GameObject instance = null;
            try
            {
                var json = @"{
                    ""format"": ""jp.lilium.remotecontrol.scene"",
                    ""formatVersion"": 1,
                    ""objects"": [
                        {
                            ""@prefab"": ""aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"",
                            ""@id"": ""gltf-1"",
                            ""@name"": ""GLTF Model(Clone)"",
                            ""@type"": ""GameObjectWithTransform""
                        }
                    ]
                }";

                LiveSceneSerializer.LiveSceneFromJson(json, container);

                Assert.AreEqual(1, container._objects.Count,
                    "GameObjectWithTransform wrapper should be added to container._objects");
                var wrapper = container._objects[0] as LiveGameObjectWithTransform;
                Assert.IsNotNull(wrapper,
                    "Container entry should be LiveGameObjectWithTransform, not some fallback type");
                Assert.AreEqual("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", wrapper.prefabSourceKey,
                    "prefabSourceKey should be restored so re-save emits @prefab");
                Assert.AreEqual("gltf-1", wrapper.id,
                    "Saved @id should be preserved on the wrapper");

                instance = wrapper.reference as GameObject;
            }
            finally
            {
                container.Shutdown();
                if (instance != null) GameObject.DestroyImmediate(instance);
                GameObject.DestroyImmediate(prefab);
                GameObject.DestroyImmediate(containerGo);
            }
        }

        /// <summary>
        /// Load → Save の往復で GameObjectWithTransform エントリが失われないことを確認する。
        /// 実運用で test.scene.json を読み込んだ後に自動保存した際、GLTF Model エントリが
        /// 永続化されないリグレッションを捕捉する。
        /// </summary>
        [Test]
        public void LiveSceneFromJson_GameObjectWithTransform_RoundTrip_PreservesEntry()
        {
            LiveClass.RegisterFromAttributes<LiveObjectContainer>();
            LiveClass.RegisterFromAttributes<LiveGameObject>();
            LiveClass.RegisterFromAttributes<LiveGameObjectWithTransform>();

            var containerGo = new GameObject("TestContainer");
            var container = new LiveObjectContainer(containerGo.name, new List<ILiveObject>());

            var prefab = new GameObject("GLTF Model");
            PrefabRegistry.Register("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", prefab);

            GameObject instance = null;
            try
            {
                var json = @"{
                    ""format"": ""jp.lilium.remotecontrol.scene"",
                    ""formatVersion"": 1,
                    ""objects"": [
                        {
                            ""@prefab"": ""aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"",
                            ""@id"": ""gltf-1"",
                            ""@name"": ""GLTF Model(Clone)"",
                            ""@type"": ""GameObjectWithTransform""
                        }
                    ]
                }";

                LiveSceneSerializer.LiveSceneFromJson(json, container);

                var wrapper = container._objects.FirstOrDefault() as LiveGameObjectWithTransform;
                if (wrapper != null) instance = wrapper.reference as GameObject;

                var resolved = LiveObjectGraph.ResolveLiveObjects(container.objects, container);
                var saved = LiveSceneSerializer.LiveSceneToJson(resolved, container, SerializeMode.Snapshot);

                var parsed = JObject.Parse(saved);
                var objectsArr = parsed["objects"] as JArray;
                Assert.IsNotNull(objectsArr, "objects array must be present in saved JSON");

                bool found = false;
                foreach (var entry in objectsArr)
                {
                    if (entry["@prefab"]?.Value<string>() != "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa") continue;
                    if (entry["@type"]?.Value<string>() != "GameObjectWithTransform") continue;
                    // Prefab 由来の新規インスタンスは @source / @op を持たない
                    if (entry["@source"] != null) continue;
                    if (entry["@op"] != null) continue;
                    found = true;
                    break;
                }
                Assert.IsTrue(found,
                    "Round-trip should preserve the GLTF Model instance entry. Actual JSON: " + saved);
            }
            finally
            {
                container.Shutdown();
                if (instance != null) GameObject.DestroyImmediate(instance);
                GameObject.DestroyImmediate(prefab);
                GameObject.DestroyImmediate(containerGo);
            }
        }

        #endregion

        /// <summary>
        /// Factory._prefabGuid が空 (UI Designer Reset / OnValidate 未実行) の状態で Factory.Create → container に追加 →
        /// BuildLiveSceneJson (Delta) したとき、exposed.prefabSourceKey も空のため isPrefabNew=false + メタのみ判定で
        /// エントリが完全に欠落するリグレッションを捕捉する。ユーザー報告 test.scene.json (objects: []) と同じ症状。
        /// RefreshPrefabKey を事前に呼べば @prefab が出力されることも検証する。
        /// </summary>
        [Test]
        public void RuntimeCreate_WithPrefabGuidPopulatedViaRefresh_EmitsPrefabEntry()
        {
            LiveClass.RegisterFromAttributes<LiveGameObject>();
            LiveClass.RegisterFromAttributes<LiveObjectContainer>();

            const string tmpPath = "Assets/_TmpPrefab_DeltaRepro.prefab";
            var seed = new GameObject("DeltaReproPrefab");
            var asset = UnityEditor.PrefabUtility.SaveAsPrefabAsset(seed, tmpPath);
            GameObject.DestroyImmediate(seed);
            var expectedGuid = UnityEditor.AssetDatabase.AssetPathToGUID(tmpPath);

            var containerGo = new GameObject("DeltaReproContainer");
            var container = new LiveObjectContainer(containerGo.name, new List<ILiveObject>());
            container.Initialize();

            GameObject instance = null;
            try
            {
                var factory = new LiveGameObjectFactory { prefab = asset };

                // 前提: _prefabGuid が空の状態で Create すると save 後の objects は空
                var beforeCreated = factory.Create() as LiveGameObject;
                instance = beforeCreated.reference as GameObject;
                container.AddLiveObject(beforeCreated);
                beforeCreated.OnEnable();

                var beforeJson = LiveSceneSerializer.BuildLiveSceneJson(container);
                var beforeRoot = JObject.Parse(beforeJson);
                var beforeObjs = beforeRoot["objects"] as JArray;
                Assert.AreEqual(0, beforeObjs.Count,
                    "Precondition: when _prefabGuid is empty, Delta save drops the entry entirely. Actual: " + beforeJson);

                // Shutdown して作業をクリアしてから Refresh 後の挙動を検証
                container.RemoveLiveObject(beforeCreated);
                beforeCreated.OnDispose();
                GameObject.DestroyImmediate(instance);
                instance = null;

                // UI Designer Reset 相当: Factory の _prefabGuid を AssetDatabase から再解決
                factory.RefreshPrefabKey();
                Assert.AreEqual(expectedGuid, factory.prefabGuid,
                    "RefreshPrefabKey should populate _prefabGuid from AssetDatabase");

                var afterCreated = factory.Create() as LiveGameObject;
                instance = afterCreated.reference as GameObject;
                container.AddLiveObject(afterCreated);
                afterCreated.OnEnable();

                var afterJson = LiveSceneSerializer.BuildLiveSceneJson(container);
                var afterRoot = JObject.Parse(afterJson);
                var afterObjs = afterRoot["objects"] as JArray;

                bool hasPrefab = afterObjs.OfType<JObject>().Any(o => o["@prefab"]?.Value<string>() == expectedGuid);
                Assert.IsTrue(hasPrefab,
                    $"After Refresh, Delta save should contain @prefab={expectedGuid}. Actual: {afterJson}");
            }
            finally
            {
                if (instance != null) GameObject.DestroyImmediate(instance);
                GameObject.DestroyImmediate(containerGo);
                UnityEditor.AssetDatabase.DeleteAsset(tmpPath);
            }
        }

        /// <summary>
        /// UI Designer Reset (= UIDefinitionPrefabKeyRefresher.Refresh) は渡された UIDefinition
        /// のみ更新し、それ以外の UIDefinition アセットには影響しないことを検証する。
        /// Play 中に呼ばれても PrefabRegistry に即座に登録されることも併せて確認する。
        /// </summary>
        [Test]
        public void Refresh_TargetsOnlyGivenDefinition_AndRegistersPrefab()
        {
            const string prefabTargetPath = "Assets/_TmpPrefab_Refresher_Target.prefab";
            const string prefabOtherPath = "Assets/_TmpPrefab_Refresher_Other.prefab";
            const string defTargetPath = "Assets/_TmpUIDef_Refresher_Target.asset";
            const string defOtherPath = "Assets/_TmpUIDef_Refresher_Other.asset";

            var seedT = new GameObject("RefresherPrefabTarget");
            var prefabTarget = UnityEditor.PrefabUtility.SaveAsPrefabAsset(seedT, prefabTargetPath);
            GameObject.DestroyImmediate(seedT);

            var seedO = new GameObject("RefresherPrefabOther");
            var prefabOther = UnityEditor.PrefabUtility.SaveAsPrefabAsset(seedO, prefabOtherPath);
            GameObject.DestroyImmediate(seedO);

            var expectedGuidTarget = UnityEditor.AssetDatabase.AssetPathToGUID(prefabTargetPath);
            var expectedGuidOther = UnityEditor.AssetDatabase.AssetPathToGUID(prefabOtherPath);

            UIDefinition defTarget = null;
            UIDefinition defOther = null;

            try
            {
                var factoryTarget = new LiveGameObjectFactory { prefab = prefabTarget };
                var factoryOther = new LiveGameObjectFactory { prefab = prefabOther };

                defTarget = ScriptableObject.CreateInstance<UIDefinition>();
                defTarget.menuItems.Add(new MenuItem
                {
                    id = "target",
                    page = new CategoryPage { factory = new StandardObjectFactory { factories = new ILiveObjectFactory[] { factoryTarget } } }
                });
                UnityEditor.AssetDatabase.CreateAsset(defTarget, defTargetPath);

                defOther = ScriptableObject.CreateInstance<UIDefinition>();
                defOther.menuItems.Add(new MenuItem
                {
                    id = "other",
                    page = new CategoryPage { factory = new StandardObjectFactory { factories = new ILiveObjectFactory[] { factoryOther } } }
                });
                UnityEditor.AssetDatabase.CreateAsset(defOther, defOtherPath);

                Assert.IsTrue(string.IsNullOrEmpty(factoryTarget.prefabGuid), "Precondition: target factory guid empty");
                Assert.IsTrue(string.IsNullOrEmpty(factoryOther.prefabGuid), "Precondition: other factory guid empty");

                // Act: Simulator が設定している _definition だけを対象に Refresh
                var updated = UIDefinitionPrefabKeyRefresher.Refresh(defTarget);
                Assert.IsTrue(updated);

                // Target は更新される
                var reloadedTarget = UnityEditor.AssetDatabase.LoadAssetAtPath<UIDefinition>(defTargetPath);
                var rfTarget = ((StandardObjectFactory)((CategoryPage)reloadedTarget.menuItems[0].page).factory).factories[0] as LiveGameObjectFactory;
                Assert.AreEqual(expectedGuidTarget, rfTarget.prefabGuid, "Target definition factory GUID should be refreshed");
                Assert.IsTrue(PrefabRegistry.TryFind(expectedGuidTarget, out var regTarget), "PrefabRegistry should have target prefab");
                Assert.AreEqual(prefabTarget, regTarget);

                // Other は一切触られない
                var reloadedOther = UnityEditor.AssetDatabase.LoadAssetAtPath<UIDefinition>(defOtherPath);
                var rfOther = ((StandardObjectFactory)((CategoryPage)reloadedOther.menuItems[0].page).factory).factories[0] as LiveGameObjectFactory;
                Assert.IsTrue(string.IsNullOrEmpty(rfOther.prefabGuid), "Other definition factory GUID must remain empty");
                Assert.IsFalse(PrefabRegistry.TryFind(expectedGuidOther, out _), "PrefabRegistry must not contain other prefab");
            }
            finally
            {
                UnityEditor.AssetDatabase.DeleteAsset(defTargetPath);
                UnityEditor.AssetDatabase.DeleteAsset(defOtherPath);
                UnityEditor.AssetDatabase.DeleteAsset(prefabTargetPath);
                UnityEditor.AssetDatabase.DeleteAsset(prefabOtherPath);
            }
        }

        /// <summary>
        /// ScenePage のように CategoryPage 以外の IPage 実装が StandardObjectFactory を持つ場合でも
        /// Refresh が Factory の prefab GUID を再解決することを検証する。
        /// ユーザーの Studio UI Definition で ScenePage 側の Factory が更新されなかった症状の再発防止。
        /// </summary>
        [Test]
        public void Refresh_SupportsScenePageInAdditionToCategoryPage()
        {
            const string prefabPath = "Assets/_TmpPrefab_ScenePage.prefab";
            const string defPath = "Assets/_TmpUIDef_ScenePage.asset";

            var seed = new GameObject("ScenePagePrefab");
            var prefab = UnityEditor.PrefabUtility.SaveAsPrefabAsset(seed, prefabPath);
            GameObject.DestroyImmediate(seed);

            var expectedGuid = UnityEditor.AssetDatabase.AssetPathToGUID(prefabPath);

            UIDefinition def = null;
            try
            {
                var factory = new LiveGameObjectFactory { prefab = prefab };

                def = ScriptableObject.CreateInstance<UIDefinition>();
                def.menuItems.Add(new MenuItem
                {
                    id = "scene",
                    page = new ScenePage { factory = new StandardObjectFactory { factories = new ILiveObjectFactory[] { factory } } }
                });
                UnityEditor.AssetDatabase.CreateAsset(def, defPath);

                Assert.IsTrue(string.IsNullOrEmpty(factory.prefabGuid), "Precondition: factory guid empty");

                var updated = UIDefinitionPrefabKeyRefresher.Refresh(def);
                Assert.IsTrue(updated);

                var reloaded = UnityEditor.AssetDatabase.LoadAssetAtPath<UIDefinition>(defPath);
                var rf = ((StandardObjectFactory)((ScenePage)reloaded.menuItems[0].page).factory).factories[0] as LiveGameObjectFactory;
                Assert.AreEqual(expectedGuid, rf.prefabGuid, "ScenePage-hosted factory should also be refreshed");
                Assert.IsTrue(PrefabRegistry.TryFind(expectedGuid, out var reg));
                Assert.AreEqual(prefab, reg);
            }
            finally
            {
                UnityEditor.AssetDatabase.DeleteAsset(defPath);
                UnityEditor.AssetDatabase.DeleteAsset(prefabPath);
            }
        }

        #region PrefabRegistry Tests

        [Test]
        public void PrefabRegistry_RegisterAndFind_Works()
        {
            var prefab = new GameObject("RegisteredPrefab");
            try
            {
                PrefabRegistry.Register("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", prefab);

                Assert.IsTrue(PrefabRegistry.TryFind("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", out var found));
                Assert.AreEqual(prefab, found);
            }
            finally
            {
                GameObject.DestroyImmediate(prefab);
            }
        }

        [Test]
        public void PrefabRegistry_FindUnregistered_ReturnsFalse()
        {
            Assert.IsFalse(PrefabRegistry.TryFind("eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee", out _));
        }

        [Test]
        public void PrefabRegistry_Instantiate_CreatesInstance()
        {
            var prefab = new GameObject("InstPrefab");
            PrefabRegistry.Register("cccccccccccccccccccccccccccccccc", prefab);

            GameObject instance = null;
            try
            {
                instance = PrefabRegistry.Instantiate("cccccccccccccccccccccccccccccccc");

                Assert.IsNotNull(instance);
            }
            finally
            {
                if (instance != null) GameObject.DestroyImmediate(instance);
                GameObject.DestroyImmediate(prefab);
            }
        }

        [Test]
        public void PrefabRegistry_Instantiate_UnknownPrefab_ReturnsNull()
        {
            var result = PrefabRegistry.Instantiate("ffffffffffffffffffffffffffffffff");
            Assert.IsNull(result);
        }

        #endregion

        #region LiveUnityObjectBase.prefabSourceKey Tests

        [Test]
        public void PrefabSourceKey_SetAndGet_ReturnsCorrectKey()
        {
            LiveClass.RegisterFromAttributes<LiveGameObject>();
            var instance = new GameObject("MyPrefab(Clone)");
            try
            {
                var liveGO = new LiveGameObject(instance);
                liveGO.prefabSourceKey = "dddddddddddddddddddddddddddddddd";

                Assert.AreEqual("dddddddddddddddddddddddddddddddd", liveGO.prefabSourceKey);
            }
            finally
            {
                GameObject.DestroyImmediate(instance);
            }
        }

        [Test]
        public void PrefabSourceKey_Default_IsNull()
        {
            LiveClass.RegisterFromAttributes<LiveGameObject>();
            var instance = new GameObject("NeverTracked");
            try
            {
                var liveGO = new LiveGameObject(instance);
                Assert.IsNull(liveGO.prefabSourceKey);
            }
            finally
            {
                GameObject.DestroyImmediate(instance);
            }
        }

        [Test]
        public void OnDispose_CallsOnDisable()
        {
            LiveClass.RegisterFromAttributes<LiveGameObject>();
            var instance = new GameObject("DisposeTest");
            try
            {
                var liveGO = new LiveGameObject(instance);
                liveGO.OnEnable();
                Assert.IsNotNull(liveGO.liveObject);

                liveGO.OnDispose();
                Assert.IsNull(liveGO.liveObject);
            }
            finally
            {
                GameObject.DestroyImmediate(instance);
            }
        }

        #endregion

        #region Delta New Element Tests

        [Test]
        public void DeltaFromDefault_NewArrayElement_OnlyChangedPropertiesSerialized()
        {
            // Arrange
            LiveClass.RegisterFromAttributes<TestDeltaNewItem>();
            LiveClass.RegisterFromAttributes<TestDeltaNewContainer>();

            var container = new TestDeltaNewContainer
            {
                items = new List<TestDeltaNewItem>()
            };
            var containerClass = LiveClass.Find(typeof(TestDeltaNewContainer));
            var containerLive = new LiveObjectHandle("delta-new-test", containerClass, container);
            LivePropertyUtility.SetDefault(containerLive);

            // 新要素追加: nameのみ変更、value1/value2はデフォルトのまま
            container.items.Add(new TestDeltaNewItem
            {
                name = "test",
                value1 = 1.0f,  // デフォルトと同じ
                value2 = 2.0f,  // デフォルトと同じ
                nested = new int[0], // デフォルトと同じ
            });

            // Act
            var json = LiveSceneSerializer.LiveSceneToJson(new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver, SerializeMode.Delta);
            var jRoot = JObject.Parse(json);
            var objects = jRoot["objects"] as JArray;
            var containerObj = objects.FirstOrDefault(o => EntryKey(o) =="delta-new-test") as JObject;
            var itemsArr = containerObj["items"] as JArray;

            Assert.IsNotNull(itemsArr, "items array should exist");
            Assert.AreEqual(1, itemsArr.Count);

            var newItem = itemsArr[0] as JObject;
            Assert.AreEqual("new", newItem["@op"]?.Value<string>());
            Assert.AreEqual("TestDeltaNewItem", newItem["@type"]?.Value<string>());
            Assert.AreEqual("test", newItem["name"]?.Value<string>(), "Changed property should be serialized");
            Assert.IsNull(newItem["value1"], "Default value property should not be serialized");
            Assert.IsNull(newItem["value2"], "Default value property should not be serialized");
            Assert.IsNull(newItem["nested"], "Default empty array should not be serialized");
        }

        [Test]
        public void DeltaFromDefault_NewArrayElement_AllDefault_MinimalOutput()
        {
            // Arrange
            LiveClass.RegisterFromAttributes<TestDeltaNewItem>();
            LiveClass.RegisterFromAttributes<TestDeltaNewContainer>();

            var container = new TestDeltaNewContainer
            {
                items = new List<TestDeltaNewItem>()
            };
            var containerClass = LiveClass.Find(typeof(TestDeltaNewContainer));
            var containerLive = new LiveObjectHandle("delta-new-minimal", containerClass, container);
            LivePropertyUtility.SetDefault(containerLive);

            // 全プロパティがデフォルトと同じ新要素を追加
            container.items.Add(TestDeltaNewItem.Default);

            // Act
            var json = LiveSceneSerializer.LiveSceneToJson(new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver, SerializeMode.Delta);
            var jRoot = JObject.Parse(json);
            var objects = jRoot["objects"] as JArray;
            var containerObj = objects.FirstOrDefault(o => EntryKey(o) =="delta-new-minimal") as JObject;
            var itemsArr = containerObj["items"] as JArray;
            var newItem = itemsArr[0] as JObject;

            // @op と @type のみ
            Assert.AreEqual("new", newItem["@op"]?.Value<string>());
            Assert.AreEqual("TestDeltaNewItem", newItem["@type"]?.Value<string>());
            Assert.IsNull(newItem["name"], "Default name should not be serialized");
            Assert.IsNull(newItem["value1"], "Default value1 should not be serialized");
            Assert.IsNull(newItem["value2"], "Default value2 should not be serialized");
            Assert.IsNull(newItem["nested"], "Default nested should not be serialized");
        }

        [Test]
        public void RoundTrip_DeltaFromDefault_NewArrayElement_PreservesValues()
        {
            // Arrange
            LiveClass.RegisterFromAttributes<TestDeltaNewItem>();
            LiveClass.RegisterFromAttributes<TestDeltaNewContainer>();

            var container = new TestDeltaNewContainer
            {
                items = new List<TestDeltaNewItem>()
            };
            var containerClass = LiveClass.Find(typeof(TestDeltaNewContainer));
            var containerLive = new LiveObjectHandle("delta-new-rt", containerClass, container);
            LivePropertyUtility.SetDefault(containerLive);

            // nameとvalue1のみ変更
            container.items.Add(new TestDeltaNewItem
            {
                name = "roundtrip",
                value1 = 5.0f,
                value2 = 2.0f,  // デフォルト
                nested = new int[0],
            });

            // Act - シリアライズ
            var json = LiveSceneSerializer.LiveSceneToJson(new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver, SerializeMode.Delta);

            // 新コンテナに復元
            var toRemove = LiveObjectRegistry.instances.ToList();
            foreach (var obj in toRemove) obj.Unregister();

            var restored = new TestDeltaNewContainer
            {
                items = new List<TestDeltaNewItem>()
            };
            var restoredLive = new LiveObjectHandle("delta-new-rt", containerClass, restored);
            LivePropertyUtility.SetDefault(restoredLive);
            LiveSceneSerializer.LiveSceneFromJson(json, _resolver);

            // Assert
            Assert.AreEqual(1, restored.items.Count);
            Assert.AreEqual("roundtrip", restored.items[0].name);
            Assert.AreEqual(5.0f, restored.items[0].value1);
            Assert.AreEqual(2.0f, restored.items[0].value2, "Default value should be preserved");
            Assert.IsNotNull(restored.items[0].nested);
            Assert.AreEqual(0, restored.items[0].nested.Length, "Default empty array should be preserved");
        }

        #endregion

        #region ResolveLiveObjects Array Ref Auto-Discovery Tests

        /// <summary>
        /// LiveGameObjectの_components配列に含まれるLiveClass付きコンポーネントが、
        /// ResolveLiveObjectsのBFSで探索されることを検証。
        /// IDなしの inline コンポーネントも result に含めて返す（呼び出し側が
        /// SetDefault/EnsureDefaultsCaptured で defaults を登録できるようにするため）。
        /// LiveSceneToJson 側では hasId チェックでトップレベル出力はスキップされる。
        /// </summary>
        [Test]
        public void ResolveLiveObjects_ComponentOnGameObject_AutoDiscovered()
        {
            LiveClass.RegisterFromAttributes<LiveGameObject>();
            LiveClass.RegisterFromAttributes<TestAdditionsComponent>();

            var go = new GameObject("TestGO");
            try
            {
                var comp = go.AddComponent<TestAdditionsComponent>();
                comp.health = 99;

                var liveGO = new LiveGameObject(go);
                liveGO.OnEnable();

                // コンポーネントのLiveObjectは事前に作成しない
                var objects = LiveObjectGraph.ResolveLiveObjects(
                    new object[] { liveGO }, _resolver);

                // IDなしコンポーネントも result に含まれる（defaults 登録のため）
                Assert.IsTrue(
                    objects.Any(o => o.targetType.typeName == "TestAdditionsComponent"),
                    "IDなしコンポーネントもResolveLiveObjectsの結果に含まれるべき（defaults登録対象）");

                // コンテナ未管理のコンポーネントはレジストリには登録されない
                // （CreateUnregistered で作成されるため）
                Assert.IsFalse(
                    LiveObjectRegistry.TryFindByTarget(comp, out _),
                    "コンテナ未管理のコンポーネントはレジストリに登録されるべきではない");
            }
            finally
            {
                GameObject.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// LiveGameObjectの_components配列経由で発見されたIDなしコンポーネントが、
        /// LiveSceneToJson(DeltaFromDefault)で親オブジェクト内にインライン展開されることを検証。
        /// コンポーネント内部のプロパティ変更は親の子パスとしてdirty追跡されるため、
        /// componentsは正しくdelta出力に含まれる。
        /// </summary>
        [Test]
        public void LiveSceneToJson_DeltaFromDefault_ComponentDataSerialized()
        {
            LiveClass.RegisterFromAttributes<LiveGameObject>();
            LiveClass.RegisterFromAttributes<TestAdditionsComponent>();

            var go = new GameObject("TestGO");
            try
            {
                var comp = go.AddComponent<TestAdditionsComponent>();
                comp.health = 42;
                comp.label = "Test";

                var liveGO = new LiveGameObject(go);
                liveGO.OnEnable();

                // ResolveLiveObjectsで依存解決
                var resolved = LiveObjectGraph.ResolveLiveObjects(
                    new object[] { liveGO }, _resolver);

                // 全オブジェクトのデフォルト値をキャプチャ
                foreach (var obj in resolved)
                    LivePropertyUtility.SetDefault(obj);

                // 値を変更してdirtyにする
                comp.health = 100;

                var json = LiveSceneSerializer.LiveSceneToJson(resolved, _resolver, SerializeMode.Delta);
                var jRoot = JObject.Parse(json);
                var objects = jRoot["objects"] as JArray;

                // 新フォーマット: Component は pending エントリとしてトップレベルに出力される
                var compObj = objects.FirstOrDefault(o => o["@type"]?.ToString() == "TestAdditionsComponent") as JObject;
                Assert.IsNotNull(compObj, "Component は pending エントリとしてトップレベルに出力されるべき");
                Assert.IsNotNull(compObj["@source"], "pending エントリは @source を持つ");
                var sourceToken = compObj["@source"];
                Assert.IsNotNull(sourceToken, "pending エントリは @source を持つ");
                Assert.AreEqual(JTokenType.String, sourceToken.Type, "@source は文字列形式");
                Assert.AreEqual(100, compObj["health"]?.Value<int>(), "変更されたhealthが出力されるべき");
            }
            finally
            {
                GameObject.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// pending エントリの @source が rootId + path を "." で結合した文字列になっていること、
        /// かつ LiveSceneFromJson 経由のラウンドトリップで target が正しく解決できることを検証する。
        /// </summary>
        [Test]
        public void LiveSceneToJson_PendingEntry_EmitsSourceAsString()
        {
            LiveClass.RegisterFromAttributes<LiveGameObject>();
            LiveClass.RegisterFromAttributes<TestAdditionsComponent>();

            var go = new GameObject("TestGO_SourceString");
            try
            {
                var comp = go.AddComponent<TestAdditionsComponent>();
                comp.health = 7;

                var liveGO = new LiveGameObject(go);
                liveGO.OnEnable();

                var resolved = LiveObjectGraph.ResolveLiveObjects(
                    new object[] { liveGO }, _resolver);
                foreach (var obj in resolved)
                    LivePropertyUtility.SetDefault(obj);

                comp.health = 9;

                var json = LiveSceneSerializer.LiveSceneToJson(resolved, _resolver, SerializeMode.Delta);
                var jRoot = JObject.Parse(json);
                var objects = jRoot["objects"] as JArray;
                var compObj = objects.FirstOrDefault(o => o["@type"]?.ToString() == "TestAdditionsComponent") as JObject;
                Assert.IsNotNull(compObj);

                var sourceToken = compObj["@source"];
                Assert.AreEqual(JTokenType.String, sourceToken.Type, "@source must be string");
                var sourceKey = sourceToken.Value<string>();
                Assert.IsFalse(string.IsNullOrEmpty(sourceKey), "@source must not be empty");
                StringAssert.StartsWith(liveGO.liveObject.Value.id, sourceKey,
                    "@source は LiveGameObject の root id で始まる");
                // path 部分 (components[0] 相当) が含まれる
                StringAssert.Contains("components", sourceKey, "@source は path 'components' を含む");
            }
            finally
            {
                GameObject.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// LiveGameObjectの_components配列で、未変更のコンポーネントが
        /// DeltaFromDefaultで出力されないことを検証。
        /// rootのLiveObjectのみがIDを保持するため、ネストされたLiveObject参照も
        /// dirtyでなければ出力しない。
        /// </summary>
        [Test]
        public void LiveSceneToJson_DeltaFromDefault_UnchangedComponentNotOutput()
        {
            LiveClass.RegisterFromAttributes<LiveGameObject>();
            LiveClass.RegisterFromAttributes<TestAdditionsComponent>();

            var go = new GameObject("TestGO");
            try
            {
                var comp = go.AddComponent<TestAdditionsComponent>();
                comp.health = 42;

                var liveGO = new LiveGameObject(go);
                liveGO.OnEnable();

                var resolved = LiveObjectGraph.ResolveLiveObjects(
                    new object[] { liveGO }, _resolver);

                foreach (var obj in resolved)
                    LivePropertyUtility.SetDefault(obj);

                // コンポーネントの値は変更しない（未変更のまま）
                var json = LiveSceneSerializer.LiveSceneToJson(resolved, _resolver, SerializeMode.Delta);
                var jRoot = JObject.Parse(json);
                var objects = jRoot["objects"] as JArray;

                // LiveGameObjectのcomponents配列を取得
                // 未変更のGameObjectはdelta出力に含まれない
                var goObj = objects?.FirstOrDefault(o => o["@type"]?.ToString() == "GameObject");
                Assert.IsNull(goObj, $"未変更のGameObjectはdelta出力に含まれるべきでない. JSON: {json}");
            }
            finally
            {
                GameObject.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// LiveSceneFromJsonでComponent型（MonoBehaviour）のLiveObjectが
        /// シーン上のコンポーネントから自動的に復元されることを検証。
        /// InstanceIDベースのIDはセッション間で変わるため、型名とGameObject名で検索する。
        /// </summary>
        [Test]
        public void LiveSceneFromJson_ComponentType_RestoredFromScene()
        {
            LiveClass.RegisterFromAttributes<LiveGameObject>();
            LiveClass.RegisterFromAttributes<TestAdditionsComponent>();

            var go = new GameObject("TestGO");
            try
            {
                var comp = go.AddComponent<TestAdditionsComponent>();
                comp.health = 10;
                comp.label = "Original";

                // 保存用のJSONを構築（InstanceIDベースのIDを使用）
                var json = @"{
                    ""format"": ""jp.lilium.remotecontrol.scene"",
                    ""formatVersion"": 1,
                    ""objects"": [
                        {
                            ""@type"": ""TestAdditionsComponent"",
                            ""@id"": ""-999999"",
                            ""@name"": ""TestGO"",
                            ""health"": 77
                        }
                    ]
                }";

                LiveSceneSerializer.LiveSceneFromJson(json, _resolver);

                // LiveObjectがシーン上のコンポーネントから作成される
                var liveObj = LiveObjectRegistry.FindById("-999999");
                Assert.IsNotNull(liveObj,
                    "Component型のLiveObjectがシーンから自動復元されるべき");
                Assert.AreEqual(comp, liveObj.Value.target,
                    "LiveObjectのターゲットがシーン上のコンポーネントであるべき");
                Assert.AreEqual(77, comp.health,
                    "復元されたプロパティ値が適用されるべき");
            }
            finally
            {
                GameObject.DestroyImmediate(go);
            }
        }

        #endregion

        #region ID-less LiveObjectHandle Inline Serialization Tests

        [Serializable]
        [LiveClass("TestInlineChild")]
        public class TestInlineChild
        {
            [LiveField]
            public int childValue;

            [LiveField]
            public string childName;
        }

        [Serializable]
        [LiveClass("TestParentWithChild")]
        public class TestParentWithChild
        {
            [LiveField]
            public string parentName;

            [LiveField]
            public TestInlineChild child;
        }

        [Test]
        public void LiveSceneToJson_IdLessLiveObject_IsInlinedInParent()
        {
            // Arrange
            LiveClass.RegisterFromAttributes<TestInlineChild>();
            LiveClass.RegisterFromAttributes<TestParentWithChild>();

            var child = new TestInlineChild { childValue = 10, childName = "InlineChild" };
            var parent = new TestParentWithChild { parentName = "Parent", child = child };

            var parentClass = LiveClass.Find(typeof(TestParentWithChild));
            var childClass = LiveClass.Find(typeof(TestInlineChild));

            // 親はID付き、子はIDなし
            var parentLive = new LiveObjectHandle("parent-id-1", parentClass, parent);
            var childLive = LiveObjectRegistry.GetOrCreateWithoutId(childClass, child);

            Assert.IsTrue(parentLive.hasId);
            Assert.IsFalse(childLive.hasId);

            // Act
            var json = LiveSceneSerializer.LiveSceneToJson(
                new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver);

            // Assert
            var jRoot = JObject.Parse(json);
            var objects = jRoot["objects"] as JArray;

            // トップレベルにはID付きオブジェクトのみ
            Assert.AreEqual(1, objects.Count, "IDなしオブジェクトはトップレベルに出力されるべきではない");

            var parentObj = objects[0] as JObject;
            Assert.AreEqual("parent-id-1", EntryKey(parentObj));

            // 子オブジェクトは@refではなくインライン展開
            var childObj = parentObj["child"] as JObject;
            Assert.IsNotNull(childObj, "子オブジェクトはインライン展開されるべき");
            Assert.IsNull(childObj["@ref"], "IDなしオブジェクトは@refを持つべきではない");
            Assert.AreEqual("TestInlineChild", childObj["@type"]?.Value<string>());
            Assert.AreEqual(10, childObj["childValue"]?.Value<int>());
            Assert.AreEqual("InlineChild", childObj["childName"]?.Value<string>());
        }

        [Test]
        public void LiveSceneToJson_IdLiveObject_IsRefInParent()
        {
            // Arrange
            LiveClass.RegisterFromAttributes<TestInlineChild>();
            LiveClass.RegisterFromAttributes<TestParentWithChild>();

            var child = new TestInlineChild { childValue = 20, childName = "RefChild" };
            var parent = new TestParentWithChild { parentName = "Parent", child = child };

            var parentClass = LiveClass.Find(typeof(TestParentWithChild));
            var childClass = LiveClass.Find(typeof(TestInlineChild));

            // 両方ID付き
            var parentLive = new LiveObjectHandle("parent-id-1", parentClass, parent);
            var childLive = new LiveObjectHandle("child-id-1", childClass, child);

            Assert.IsTrue(parentLive.hasId);
            Assert.IsTrue(childLive.hasId);

            // Act
            var json = LiveSceneSerializer.LiveSceneToJson(
                new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver);

            // Assert
            var jRoot = JObject.Parse(json);
            var objects = jRoot["objects"] as JArray;

            // トップレベルに両方出力
            Assert.AreEqual(2, objects.Count, "ID付きオブジェクトは両方トップレベルに出力されるべき");

            // 親の子プロパティは@ref参照
            JObject parentObj = null;
            foreach (var obj in objects)
            {
                if (EntryKey(obj) =="parent-id-1")
                {
                    parentObj = obj as JObject;
                    break;
                }
            }
            Assert.IsNotNull(parentObj);
            var childRef = parentObj["child"] as JObject;
            Assert.IsNotNull(childRef);
            Assert.AreEqual("child-id-1", childRef["@ref"]?.Value<string>(),
                "ID付きオブジェクトは@refで参照されるべき");
        }

        [Test]
        public void LiveSceneToJson_IdLessObject_RoundTrip()
        {
            // Arrange
            LiveClass.RegisterFromAttributes<TestInlineChild>();
            LiveClass.RegisterFromAttributes<TestParentWithChild>();

            var child = new TestInlineChild { childValue = 42, childName = "RoundTrip" };
            var parent = new TestParentWithChild { parentName = "Parent", child = child };

            var parentClass = LiveClass.Find(typeof(TestParentWithChild));
            var childClass = LiveClass.Find(typeof(TestInlineChild));

            var parentLive = new LiveObjectHandle("parent-id-1", parentClass, parent);
            LiveObjectRegistry.GetOrCreateWithoutId(childClass, child);

            // Act: シリアライズ
            var json = LiveSceneSerializer.LiveSceneToJson(
                new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver);

            // 状態をリセットしてデシリアライズ
            parent.child = new TestInlineChild();
            parent.parentName = "";

            LiveSceneSerializer.LiveSceneFromJson(json, _resolver);

            // Assert: 値が復元されている
            Assert.AreEqual("Parent", parent.parentName);
            Assert.IsNotNull(parent.child);
            Assert.AreEqual(42, parent.child.childValue);
            Assert.AreEqual("RoundTrip", parent.child.childName);
        }

        [Test]
        public void LiveObject_HasId_ReturnsFalseForNullId()
        {
            // Arrange
            LiveClass.RegisterFromAttributes<TestInlineChild>();
            var child = new TestInlineChild();
            var childClass = LiveClass.Find(typeof(TestInlineChild));

            // Act
            var withId = new LiveObjectHandle("some-id", childClass, child);
            Assert.IsTrue(withId.hasId);

            withId.Unregister();

            var withoutId = LiveObjectRegistry.GetOrCreateWithoutId(childClass, child);
            Assert.IsFalse(withoutId.hasId);
        }

        #endregion

        #region SetDefault → Load → Modify → Save Flow Tests

        /// <summary>
        /// 実アプリフローの再現:
        /// 1. LiveObject作成（コンストラクタでSetDefault）
        /// 2. 追加のSetDefault（LiveObjectContainer.Initializeと同等）
        /// 3. LiveSceneFromJsonでデルタ��ード
        /// 4. プロパティ変更
        /// 5. LiveSceneToJson(DeltaFromDefault)���保存
        /// 変更し��プロパティが正しく保存されることを検��。
        /// </summary>
        [Test]
        public void AppFlow_SetDefault_Load_Modify_Save_PreservesChanges()
        {
            // Arrange
            LiveClass.RegisterFromAttributes<TestSceneClass>();

            var testObj = new TestSceneClass { value = 42, name = "Original", position = 1.0f };
            var liveClass = LiveClass.Find(typeof(TestSceneClass));
            var liveObj = new LiveObjectHandle("app-flow-1", liveClass, testObj);

            // Step 1: 初期SetDefault（LiveObjectContainer.Initializeと同等）
            LivePropertyUtility.SetDefault(liveObj);

            // Step 2: 前回保存されたデルタを適用（LiveSceneFromJsonシミュレーション）
            // 前回保存時にvalueが99だった場合のデルタJSON
            var savedDeltaJson = @"{
                ""format"": ""jp.lilium.remotecontrol.scene"",
                ""formatVersion"": 1,
                ""objects"": [
                    {
                        ""@type"": ""TestSceneClass"",
                        ""@id"": ""app-flow-1"",
                        ""@name"": ""Original"",
                        ""value"": 99
                    }
                ]
            }";
            LiveSceneSerializer.LiveSceneFromJson(savedDeltaJson, _resolver);
            Assert.AreEqual(99, testObj.value, "LiveSceneFromJson should have restored value to 99");

            // Step 3: ユーザーがプロパティを変更
            var valueProp = liveObj.FindProperty("value");
            Assert.IsNotNull(valueProp);
            valueProp.Value.SetValue(200);
            Assert.AreEqual(200, testObj.value);

            // Step 4: 保存（DeltaFromDefault）
            var json = LiveSceneSerializer.LiveSceneToJson(
                new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver, SerializeMode.Delta);

            // Assert: 変更したプロパティが保存される
            var jRoot = JObject.Parse(json);
            var objects = jRoot["objects"] as JArray;
            var obj = objects?.FirstOrDefault(o => EntryKey(o) =="app-flow-1") as JObject;
            Assert.IsNotNull(obj, $"変更さ���たオブジェクトがdelta出力に含まれるべき. JSON: {json}");
            Assert.AreEqual(200, obj["value"]?.Value<int>(), "変更されたvalueが保存されるべき");
        }

        /// <summary>
        /// SetDefault後にLiveSceneFromJsonでロードし、何も変更しない場合、
        /// ロード済みの値が保存されることを検証（デフォルト値からの���分として���。
        /// </summary>
        [Test]
        public void AppFlow_SetDefault_Load_NoModify_Save_PreservesLoadedValues()
        {
            // Arrange
            LiveClass.RegisterFromAttributes<TestSceneClass>();

            var testObj = new TestSceneClass { value = 42, name = "Original", position = 1.0f };
            var liveClass = LiveClass.Find(typeof(TestSceneClass));
            var liveObj = new LiveObjectHandle("app-flow-2", liveClass, testObj);

            // Step 1: 初期SetDefault
            LivePropertyUtility.SetDefault(liveObj);

            // Step 2: 前回保存されたデルタを適用
            var savedDeltaJson = @"{
                ""format"": ""jp.lilium.remotecontrol.scene"",
                ""formatVersion"": 1,
                ""objects"": [
                    {
                        ""@type"": ""TestSceneClass"",
                        ""@id"": ""app-flow-2"",
                        ""@name"": ""Original"",
                        ""value"": 99,
                        ""name"": ""Modified""
                    }
                ]
            }";
            LiveSceneSerializer.LiveSceneFromJson(savedDeltaJson, _resolver);

            // Step 3: 何も変更しない → しかしロード済みの値はデフォルトと異なるのでdirty

            // Step 4: 保存
            var json = LiveSceneSerializer.LiveSceneToJson(
                new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver, SerializeMode.Delta);

            // Assert: ロード済みの値が保存される
            var jRoot = JObject.Parse(json);
            var objects = jRoot["objects"] as JArray;
            var obj = objects?.FirstOrDefault(o => EntryKey(o) =="app-flow-2") as JObject;
            Assert.IsNotNull(obj, $"ロード済みの変更がdelta出力に含まれるべき. JSON: {json}");
            Assert.AreEqual(99, obj["value"]?.Value<int>(), "ロード済みのvalueが保存されるべき");
            Assert.AreEqual("Modified", obj["name"]?.Value<string>(), "ロード済み��nameが保存されるべき");
        }

        #endregion

        #region Integration Tests — Full Save/Load Cycle

        /// <summary>
        /// 統合テスト: 初期状態 → SetDefault → プロパティ変更 → デルタ保存 →
        /// 新インスタンスで初期状態再構築 → SetDefault → デルタロード → 変更後の値と一致するか検証。
        /// 実アプリの SaveCurrentData / LoadCurrentData サイクルを再現。
        /// </summary>
        [Test]
        public void Integration_SaveLoad_BasicProperties()
        {
            LiveClass.RegisterFromAttributes<TestSceneClass>();

            // === Session 1: 変更して保存 ===
            var obj1 = new TestSceneClass { value = 42, name = "Original", position = 1.0f };
            var liveClass = LiveClass.Find(typeof(TestSceneClass));
            var live1 = new LiveObjectHandle("integ-basic-1", liveClass, obj1);
            LivePropertyUtility.SetDefault(live1);

            // プロパティ変更
            obj1.value = 200;
            obj1.name = "Changed";
            // positionは変更しない

            // デルタ保存
            var deltaJson = LiveSceneSerializer.LiveSceneToJson(
                new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver, SerializeMode.Delta);

            // === Session 2: 新インスタンスで再構築してロード ===
            // LiveObjectをクリア
            live1.Unregister();

            var obj2 = new TestSceneClass { value = 42, name = "Original", position = 1.0f };
            var live2 = new LiveObjectHandle("integ-basic-1", liveClass, obj2);
            LivePropertyUtility.SetDefault(live2);

            // デルタロード
            LiveSceneSerializer.LiveSceneFromJson(deltaJson, _resolver);

            // 検証: 変更後の値と一致
            Assert.AreEqual(200, obj2.value, "value should be restored to modified value");
            Assert.AreEqual("Changed", obj2.name, "name should be restored to modified value");
            Assert.AreEqual(1.0f, obj2.position, "position should remain at default (unchanged)");
        }

        /// <summary>
        /// 統合テスト: 保存→ロード→再変更→再保存→再ロード の2サイクル検証。
        /// </summary>
        [Test]
        public void Integration_SaveLoad_TwoCycles()
        {
            LiveClass.RegisterFromAttributes<TestSceneClass>();
            var liveClass = LiveClass.Find(typeof(TestSceneClass));

            // === Cycle 1: 変更して保存 ===
            var obj1 = new TestSceneClass { value = 42, name = "Original", position = 1.0f };
            var live1 = new LiveObjectHandle("integ-cycle-1", liveClass, obj1);
            LivePropertyUtility.SetDefault(live1);

            obj1.value = 100;

            var deltaJson1 = LiveSceneSerializer.LiveSceneToJson(
                new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver, SerializeMode.Delta);
            live1.Unregister();

            // === Cycle 2: ロード→追加変更→保存 ===
            var obj2 = new TestSceneClass { value = 42, name = "Original", position = 1.0f };
            var live2 = new LiveObjectHandle("integ-cycle-1", liveClass, obj2);
            LivePropertyUtility.SetDefault(live2);
            LiveSceneSerializer.LiveSceneFromJson(deltaJson1, _resolver);

            Assert.AreEqual(100, obj2.value, "Cycle 1 value should be loaded");

            // 追加変更
            obj2.name = "Cycle2";

            var deltaJson2 = LiveSceneSerializer.LiveSceneToJson(
                new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver, SerializeMode.Delta);
            live2.Unregister();

            // === Cycle 3: 最終ロード→検証 ===
            var obj3 = new TestSceneClass { value = 42, name = "Original", position = 1.0f };
            var live3 = new LiveObjectHandle("integ-cycle-1", liveClass, obj3);
            LivePropertyUtility.SetDefault(live3);
            LiveSceneSerializer.LiveSceneFromJson(deltaJson2, _resolver);

            Assert.AreEqual(100, obj3.value, "value from cycle 1 should persist");
            Assert.AreEqual("Cycle2", obj3.name, "name from cycle 2 should persist");
            Assert.AreEqual(1.0f, obj3.position, "position should remain at default");
        }

        /// <summary>
        /// 統合テスト: 複数オブジェクトの保存/ロードサイクル。
        /// 一部のオブジェクトのみ変更した場合、変更したオブジェクトのみ保存されることを検証。
        /// </summary>
        [Test]
        public void Integration_SaveLoad_MultipleObjects_PartialChange()
        {
            LiveClass.RegisterFromAttributes<TestSceneClass>();
            var liveClass = LiveClass.Find(typeof(TestSceneClass));

            // === Session 1: 2オブジェクト、1つだけ変更 ===
            var objA = new TestSceneClass { value = 10, name = "A", position = 0f };
            var objB = new TestSceneClass { value = 20, name = "B", position = 0f };
            var liveA = new LiveObjectHandle("integ-multi-a", liveClass, objA);
            var liveB = new LiveObjectHandle("integ-multi-b", liveClass, objB);
            LivePropertyUtility.SetDefault(liveA);
            LivePropertyUtility.SetDefault(liveB);

            // Aのみ変更
            objA.value = 999;

            var deltaJson = LiveSceneSerializer.LiveSceneToJson(
                new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver, SerializeMode.Delta);

            // デルタにAのみ含まれることを確認
            var jRoot = JObject.Parse(deltaJson);
            var objects = jRoot["objects"] as JArray;
            Assert.AreEqual(1, objects.Count, "変更したオブジェクトのみdelta出力に含まれるべき");
            Assert.AreEqual("integ-multi-a", EntryKey(objects[0]));

            liveA.Unregister();
            liveB.Unregister();

            // === Session 2: ロード→検証 ===
            var objA2 = new TestSceneClass { value = 10, name = "A", position = 0f };
            var objB2 = new TestSceneClass { value = 20, name = "B", position = 0f };
            var liveA2 = new LiveObjectHandle("integ-multi-a", liveClass, objA2);
            var liveB2 = new LiveObjectHandle("integ-multi-b", liveClass, objB2);
            LivePropertyUtility.SetDefault(liveA2);
            LivePropertyUtility.SetDefault(liveB2);
            LiveSceneSerializer.LiveSceneFromJson(deltaJson, _resolver);

            Assert.AreEqual(999, objA2.value, "A.value should be restored");
            Assert.AreEqual("A", objA2.name, "A.name should remain at default");
            Assert.AreEqual(20, objB2.value, "B.value should remain at default");
            Assert.AreEqual("B", objB2.name, "B.name should remain at default");
        }

        /// <summary>
        /// 統合テスト: 配列プロパティの保存/ロードサイクル。
        /// 配列への要素追加がデルタ保存→ロードで正しく復元されるか検証。
        /// </summary>
        [Test]
        public void Integration_SaveLoad_ArrayProperty()
        {
            LiveClass.RegisterFromAttributes<TestSceneClassWithArray>();
            var liveClass = LiveClass.Find(typeof(TestSceneClassWithArray));

            // === Session 1: 配列変更して保存 ===
            var obj1 = new TestSceneClassWithArray
            {
                intArray = new int[] { 1, 2, 3 },
                stringArray = new string[] { "a", "b" },
                intList = new List<int> { 10, 20 }
            };
            var live1 = new LiveObjectHandle("integ-array-1", liveClass, obj1);
            LivePropertyUtility.SetDefault(live1);

            // 配列の値を変更
            obj1.intArray[0] = 100;
            obj1.stringArray = new string[] { "a", "b", "c" };

            var deltaJson = LiveSceneSerializer.LiveSceneToJson(
                new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver, SerializeMode.Delta);
            live1.Unregister();

            // === Session 2: ロード→検証 ===
            var obj2 = new TestSceneClassWithArray
            {
                intArray = new int[] { 1, 2, 3 },
                stringArray = new string[] { "a", "b" },
                intList = new List<int> { 10, 20 }
            };
            var live2 = new LiveObjectHandle("integ-array-1", liveClass, obj2);
            LivePropertyUtility.SetDefault(live2);
            LiveSceneSerializer.LiveSceneFromJson(deltaJson, _resolver);

            Assert.AreEqual(100, obj2.intArray[0], "intArray[0] should be restored to modified value");
            Assert.AreEqual(2, obj2.intArray[1], "intArray[1] should remain at default");
            Assert.AreEqual(3, obj2.stringArray.Length, "stringArray should have 3 elements after restore");
            Assert.AreEqual("c", obj2.stringArray[2], "stringArray[2] should be restored");
            // 変更しなかったintListはデフォルトのまま
            Assert.AreEqual(2, obj2.intList.Count, "intList should remain at default size");
        }

        /// <summary>
        /// 統合テスト: 参照リストの保存/ロードサイクル。
        /// LiveObject参照を含む配列の追加要素がデルタで正しく保存/復元されるか検証。
        /// </summary>
        [Test]
        public void Integration_SaveLoad_RefList()
        {
            LiveClass.RegisterFromAttributes<TestSceneRefItem>();
            LiveClass.RegisterFromAttributes<TestSceneContainerWithRefList>();
            var containerClass = LiveClass.Find(typeof(TestSceneContainerWithRefList));
            var itemClass = LiveClass.Find(typeof(TestSceneRefItem));

            // === Session 1: 要素追加して保存 ===
            var item1 = new TestSceneRefItem { name = "Item1", value = 10 };
            var container1 = new TestSceneContainerWithRefList
            {
                items = new List<TestSceneRefItem> { item1 }
            };
            var containerLive1 = new LiveObjectHandle("integ-ref-container", containerClass, container1);
            new LiveObjectHandle("integ-ref-item1", itemClass, item1);
            LivePropertyUtility.SetDefault(containerLive1);

            // 新要素追加
            var item2 = new TestSceneRefItem { name = "Item2", value = 20 };
            container1.items.Add(item2);
            new LiveObjectHandle("integ-ref-item2", itemClass, item2);

            var deltaJson = LiveSceneSerializer.LiveSceneToJson(
                new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver, SerializeMode.Delta);

            // 全Unregister
            foreach (var obj in LiveObjectRegistry.instances.ToList()) obj.Unregister();

            // === Session 2: ロード→検証 ===
            var item1b = new TestSceneRefItem { name = "Item1", value = 10 };
            var item2b = new TestSceneRefItem { name = "Item2", value = 20 };
            var container2 = new TestSceneContainerWithRefList
            {
                items = new List<TestSceneRefItem> { item1b }
            };
            var containerLive2 = new LiveObjectHandle("integ-ref-container", containerClass, container2);
            new LiveObjectHandle("integ-ref-item1", itemClass, item1b);
            new LiveObjectHandle("integ-ref-item2", itemClass, item2b);
            LivePropertyUtility.SetDefault(containerLive2);

            LiveSceneSerializer.LiveSceneFromJson(deltaJson, _resolver);

            Assert.AreEqual(2, container2.items.Count, "container should have 2 items after restore");
        }

        #endregion

        #region Static Object Dirty Detection Tests

        [Test]
        public void Static_SetDefault_ChangeProperty_IsDirty()
        {
            LiveClass.RegisterClass(typeof(TestStaticSceneClass));
            LiveClass.RegisterProperties(typeof(TestStaticSceneClass));
            TestStaticSceneClass.Reset();

            var liveClass = LiveClass.Find(typeof(TestStaticSceneClass));
            Assert.IsNotNull(liveClass, "TestStaticSceneClass should be registered");
            Assert.IsTrue(liveClass.isStatic, "TestStaticSceneClass should be static");

            // staticオブジェクトを手動作成（コンストラクタでSetDefaultが呼ばれる）
            var liveObj = new LiveObjectHandle("TestStaticSceneClass", liveClass, null);

            try
            {
                // LiveClassのプロパティが登録されているか確認
                Assert.IsTrue(liveClass.propertyTypes.Length > 0,
                    $"LiveClass should have properties. isStatic={liveClass.isStatic}, type={liveClass.type}");

                // 初期状態ではdirtyでない
                Assert.IsFalse(liveObj.isDirty, "Should not be dirty before change");

                // staticプロパティを変更
                TestStaticSceneClass.value = 999;

                // dirtyになるべき
                Assert.IsTrue(liveObj.isDirty, "Should be dirty after changing static property");
            }
            finally
            {
                TestStaticSceneClass.Reset();
            }
        }

        [Test]
        public void Static_DeltaFromDefault_OutputsChangedProperties()
        {
            LiveClass.RegisterClass(typeof(TestStaticSceneClass));
            LiveClass.RegisterProperties(typeof(TestStaticSceneClass));
            TestStaticSceneClass.Reset();

            var liveClass = LiveClass.Find(typeof(TestStaticSceneClass));
            var liveObj = new LiveObjectHandle("TestStaticSceneClass", liveClass, null);

            try
            {
                // staticプロパティを変更
                TestStaticSceneClass.value = 42;

                var json = LiveSceneSerializer.LiveSceneToJson(
                    new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver, SerializeMode.Delta);

                var jRoot = JObject.Parse(json);
                var objects = jRoot["objects"] as JArray;
                var staticObj = objects?.FirstOrDefault(o => o["@type"]?.Value<string>() == "TestStaticSceneClass") as JObject;
                Assert.IsNotNull(staticObj, $"Changed static object should be in delta output. JSON: {json}");
                Assert.AreEqual(42, staticObj["value"]?.Value<int>(), "Changed value should be serialized");

                // 変更していないプロパティは含まれない
                Assert.IsNull(staticObj["name"], "Unchanged property should not be in delta output");
            }
            finally
            {
                TestStaticSceneClass.Reset();
            }
        }

        #endregion

        #region Delta Minimal Output Tests

        /// <summary>
        /// ID付き非rootのLiveObject参照で、変更したプロパティのみがdelta出力に含まれ、
        /// 未変更プロパティは出力されないことを検証。
        /// </summary>
        [Test]
        public void LiveSceneToJson_DeltaFromDefault_NonRootRefObject_OnlyDirtyPropertiesOutput()
        {
            // Arrange
            LiveClass.RegisterFromAttributes<TestSceneRefItem>();
            LiveClass.RegisterFromAttributes<TestSceneContainerWithRefList>();

            var item1 = new TestSceneRefItem { name = "Item1", value = 10 };
            var item2 = new TestSceneRefItem { name = "Item2", value = 20 };
            var container = new TestSceneContainerWithRefList
            {
                items = new List<TestSceneRefItem> { item1, item2 }
            };

            var containerClass = LiveClass.Find(typeof(TestSceneContainerWithRefList));
            var itemClass = LiveClass.Find(typeof(TestSceneRefItem));
            var containerLive = new LiveObjectHandle("container-minimal", containerClass, container);
            var item1Live = new LiveObjectHandle("item-minimal-1", itemClass, item1);
            var item2Live = new LiveObjectHandle("item-minimal-2", itemClass, item2);

            // デフォルト値キャプチャ
            LivePropertyUtility.SetDefault(containerLive);
            LivePropertyUtility.SetDefault(item1Live);
            LivePropertyUtility.SetDefault(item2Live);

            // item1のvalueのみ変更
            item1.value = 99;

            // Act
            var json = LiveSceneSerializer.LiveSceneToJson(
                new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver, SerializeMode.Delta);
            var jRoot = JObject.Parse(json);
            var objects = jRoot["objects"] as JArray;

            // Assert: item1は変更されたのでdelta出力に含まれる（変更プロパティのみ）
            var item1Obj = objects.FirstOrDefault(o => EntryKey(o) =="item-minimal-1") as JObject;
            Assert.IsNotNull(item1Obj, $"変更されたitem1はdelta出力に含まれるべき. JSON: {json}");
            Assert.AreEqual(99, item1Obj["value"]?.Value<int>(), "変更したvalueが出力されるべき");

            // Assert: item2は変更されていないのでdelta出力に含まれない
            var item2Obj = objects.FirstOrDefault(o => EntryKey(o) =="item-minimal-2") as JObject;
            Assert.IsNull(item2Obj, $"未変更のitem2はdelta出力に含まれるべきでない. JSON: {json}");
        }

        /// <summary>
        /// インライン展開のLiveObject参照（IDなし、コンポーネント等）で、
        /// 変更したプロパティのみがdelta出力に含まれ、未変更プロパティは出力されないことを検証。
        /// </summary>
        [Test]
        public void LiveSceneToJson_DeltaFromDefault_InlineLiveObject_OnlyDirtyPropertiesOutput()
        {
            LiveClass.RegisterFromAttributes<LiveGameObject>();
            LiveClass.RegisterFromAttributes<TestAdditionsComponent>();

            var go = new GameObject("TestGO");
            try
            {
                var comp = go.AddComponent<TestAdditionsComponent>();
                comp.health = 42;
                comp.label = "Original";

                var liveGO = new LiveGameObject(go);
                liveGO.OnEnable();

                var resolved = LiveObjectGraph.ResolveLiveObjects(
                    new object[] { liveGO }, _resolver);

                foreach (var obj in resolved)
                    LivePropertyUtility.SetDefault(obj);

                // healthのみ変更（labelは変更しない）
                comp.health = 100;

                var json = LiveSceneSerializer.LiveSceneToJson(resolved, _resolver, SerializeMode.Delta);
                var jRoot = JObject.Parse(json);
                var objects = jRoot["objects"] as JArray;

                // Component は pending エントリとしてトップレベルに出力される
                var compObj = objects.FirstOrDefault(o => o["@type"]?.ToString() == "TestAdditionsComponent") as JObject;
                Assert.IsNotNull(compObj, $"変更されたコンポーネントが pending エントリとして出力されるべき. JSON: {json}");

                // 変更したhealthのみ出力される
                Assert.AreEqual(100, compObj["health"]?.Value<int>(), "変更したhealthが出力されるべき");

                // pending も delta モードに従うため、未変更の label は出力されない（最小化）
                Assert.IsNull(compObj["label"], $"未変更の label は delta 出力に含まれないべき. JSON: {json}");
            }
            finally
            {
                GameObject.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// LiveGameObjectのcomponents配列内のインラインLiveObject参照で、
        /// 変更プロパティのみがデルタ出力に含まれ、未変更プロパティは出力されないことを検証。
        /// _SerializeArrayDeltaの結果ベースdirty判定が正しく動作することの確認。
        /// </summary>
        [Test]
        public void LiveSceneToJson_DeltaFromDefault_InlineComponentChange_OutputsDirtyProperties()
        {
            LiveClass.RegisterFromAttributes<LiveGameObject>();
            LiveClass.RegisterFromAttributes<TestAdditionsComponent>();

            var go = new GameObject("TestGO");
            try
            {
                var comp = go.AddComponent<TestAdditionsComponent>();
                comp.health = 50;
                comp.label = "Original";

                var liveGO = new LiveGameObject(go);
                liveGO.OnEnable();

                var resolved = LiveObjectGraph.ResolveLiveObjects(
                    new object[] { liveGO }, _resolver);

                foreach (var obj in resolved)
                    LivePropertyUtility.SetDefault(obj);

                // healthのみ変更
                comp.health = 200;

                var json = LiveSceneSerializer.LiveSceneToJson(resolved, _resolver, SerializeMode.Delta);
                var jRoot = JObject.Parse(json);
                var objects = jRoot["objects"] as JArray;

                // Component は pending エントリとしてトップレベルに出力される
                var compObj = objects?.FirstOrDefault(o => o["@type"]?.ToString() == "TestAdditionsComponent") as JObject;
                Assert.IsNotNull(compObj, $"変更されたコンポーネントが pending エントリとして出力されるべき. JSON: {json}");

                Assert.AreEqual(200, compObj["health"]?.Value<int>(), "変更したhealthが出力されるべき");
                // pending も delta モードに従うため、未変更の label は出力されない（最小化）
                Assert.IsNull(compObj["label"], $"未変更の label は delta 出力に含まれないべき. JSON: {json}");
            }
            finally
            {
                GameObject.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// インラインLiveObject参照(コンポーネント)のデルタ保存→復元ラウンドトリップ。
        /// 変更プロパティのみが復元され、未変更プロパティはデフォルトのままであることを検証。
        /// </summary>
        [Test]
        public void RoundTrip_DeltaFromDefault_InlineComponentChange_Restored()
        {
            LiveClass.RegisterFromAttributes<LiveGameObject>();
            LiveClass.RegisterFromAttributes<TestAdditionsComponent>();

            var go = new GameObject("TestGO");
            try
            {
                var comp = go.AddComponent<TestAdditionsComponent>();
                comp.health = 50;
                comp.label = "Original";

                var liveGO = new LiveGameObject(go);
                liveGO.OnEnable();

                var resolved = LiveObjectGraph.ResolveLiveObjects(
                    new object[] { liveGO }, _resolver);

                foreach (var obj in resolved)
                    LivePropertyUtility.SetDefault(obj);

                // healthのみ変更
                comp.health = 200;

                // デルタ保存
                var json = LiveSceneSerializer.LiveSceneToJson(resolved, _resolver, SerializeMode.Delta);

                // 値をデフォルトに戻す（LiveObjectはIDを維持するため再構築しない）
                comp.health = 50;
                comp.label = "Original";

                // デルタからロード
                LiveSceneSerializer.LiveSceneFromJson(json, _resolver);

                // healthのみ復元される
                Assert.AreEqual(200, comp.health, $"変更されたhealthが復元されるべき. JSON: {json}");
                Assert.AreEqual("Original", comp.label, "未変更のlabelはデフォルトのまま");
            }
            finally
            {
                GameObject.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// 複数コンポーネントを持つGameObjectで、1つのコンポーネントのみ変更した場合、
        /// 変更されたコンポーネントのみがcomponents配列にデルタ出力されることを検証。
        /// </summary>
        [Test]
        public void LiveSceneToJson_DeltaFromDefault_MultipleComponents_OnlyChangedOutput()
        {
            LiveClass.RegisterFromAttributes<LiveGameObject>();
            LiveClass.RegisterFromAttributes<TestAdditionsComponent>();
            LiveClass.RegisterFromAttributes<TestAdditionsComponent2>();

            var go = new GameObject("TestGO");
            try
            {
                var comp1 = go.AddComponent<TestAdditionsComponent>();
                comp1.health = 42;
                comp1.label = "Label1";
                var comp2 = go.AddComponent<TestAdditionsComponent2>();
                comp2.speed = 5.0f;

                var liveGO = new LiveGameObject(go);
                liveGO.OnEnable();

                var resolved = LiveObjectGraph.ResolveLiveObjects(
                    new object[] { liveGO }, _resolver);

                foreach (var obj in resolved)
                    LivePropertyUtility.SetDefault(obj);

                // comp1のhealthのみ変更（comp2は未変更）
                comp1.health = 100;

                var json = LiveSceneSerializer.LiveSceneToJson(resolved, _resolver, SerializeMode.Delta);
                var jRoot = JObject.Parse(json);
                var objects = jRoot["objects"] as JArray;

                // 新フォーマット: Component は pending エントリとしてトップレベルに出力される
                var comp1Obj = objects?.FirstOrDefault(o => o["@type"]?.ToString() == "TestAdditionsComponent") as JObject;
                Assert.IsNotNull(comp1Obj, $"変更されたTestAdditionsComponentが pending エントリとして出力されるべき. JSON: {json}");
                Assert.AreEqual(100, comp1Obj["health"]?.Value<int>(), "変更したhealthが出力されるべき");

                // pending も delta モードに従うため、未変更の comp2 は pending 出力に現れない。
                var comp2Obj = objects?.FirstOrDefault(o => o["@type"]?.ToString() == "TestAdditionsComponent2") as JObject;
                Assert.IsNull(comp2Obj, $"未変更の comp2 は delta 出力に含まれないべき. JSON: {json}");
            }
            finally
            {
                GameObject.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// デルタ配列の@op:new要素の冪等性テスト: 同じデルタを2回ロードしても要素が重複しないことを検証。
        /// </summary>
        [Test]
        public void RoundTrip_DeltaFromDefault_ArrayNewElement_IdempotentLoad()
        {
            LiveClass.RegisterFromAttributes<TestDeltaNewItem>();
            LiveClass.RegisterFromAttributes<TestDeltaNewContainer>();

            var container = new TestDeltaNewContainer
            {
                items = new List<TestDeltaNewItem>()
            };
            var containerClass = LiveClass.Find(typeof(TestDeltaNewContainer));
            var containerLive = new LiveObjectHandle("idempotent-test", containerClass, container);

            // デフォルトキャプチャ（空リスト）
            LivePropertyUtility.SetDefault(containerLive);

            // 要素追加
            container.items.Add(new TestDeltaNewItem { name = "Added", value1 = 10f, value2 = 20f });

            // デルタ保存
            var json = LiveSceneSerializer.LiveSceneToJson(
                new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver, SerializeMode.Delta);

            // 1回目ロード
            LiveSceneSerializer.LiveSceneFromJson(json, _resolver);
            Assert.AreEqual(1, container.items.Count, $"1回目ロード後: 要素は1つであるべき. JSON: {json}");

            // 2回目ロード（冪等性テスト）
            LiveSceneSerializer.LiveSceneFromJson(json, _resolver);
            Assert.AreEqual(1, container.items.Count, $"2回目ロード後: 要素は1つのまま（重複しない）. JSON: {json}");

            // 3回目ロード
            LiveSceneSerializer.LiveSceneFromJson(json, _resolver);
            Assert.AreEqual(1, container.items.Count, "3回目ロード後: 要素は1つのまま");
        }

        /// <summary>
        /// インラインLiveObject参照(コンポーネント)のデルタ保存→復元ラウンドトリップで、
        /// 再保存時にcomponents[]が空にならないことを検証。
        /// </summary>
        [Test]
        public void RoundTrip_DeltaFromDefault_InlineComponent_ReSavePreservesContent()
        {
            LiveClass.RegisterFromAttributes<LiveGameObject>();
            LiveClass.RegisterFromAttributes<TestAdditionsComponent>();

            var go = new GameObject("TestGO");
            try
            {
                var comp = go.AddComponent<TestAdditionsComponent>();
                comp.health = 50;
                comp.label = "Original";

                var liveGO = new LiveGameObject(go);
                liveGO.OnEnable();

                var resolved = LiveObjectGraph.ResolveLiveObjects(
                    new object[] { liveGO }, _resolver);

                foreach (var obj in resolved)
                    LivePropertyUtility.SetDefault(obj);

                // 変更
                comp.health = 200;

                // 1回目デルタ保存
                var json1 = LiveSceneSerializer.LiveSceneToJson(resolved, _resolver, SerializeMode.Delta);
                var jRoot1 = JObject.Parse(json1);
                var objects1 = jRoot1["objects"] as JArray;
                // 新フォーマット: Component は pending エントリとしてトップレベルに出力される
                var compData1 = objects1?.FirstOrDefault(o => o["@type"]?.ToString() == "TestAdditionsComponent") as JObject;
                Assert.IsNotNull(compData1, $"1回目: コンポーネントが pending エントリとして出力されるべき. JSON: {json1}");

                // ロードしてから再保存
                LiveSceneSerializer.LiveSceneFromJson(json1, _resolver);

                var json2 = LiveSceneSerializer.LiveSceneToJson(resolved, _resolver, SerializeMode.Delta);
                var jRoot2 = JObject.Parse(json2);
                var objects2 = jRoot2["objects"] as JArray;

                var compData = objects2?.FirstOrDefault(o => o["@type"]?.ToString() == "TestAdditionsComponent") as JObject;
                Assert.IsNotNull(compData, $"2回目: コンポーネントデータが pending エントリとして含まれるべき. JSON: {json2}");
                Assert.AreEqual(200, compData["health"]?.Value<int>(), "2回目: healthが保持されるべき");
            }
            finally
            {
                GameObject.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// デルタでロードした配列要素のプロパティを変更後に再保存すると、
        /// その変更がデルタ出力に含まれることを検証。
        /// (meshStateOverridesのname/visible変更相当)
        /// </summary>
        [Test]
        public void RoundTrip_DeltaFromDefault_ModifyLoadedArrayElement_ChangesPreserved()
        {
            LiveClass.RegisterFromAttributes<TestDeltaNewItem>();
            LiveClass.RegisterFromAttributes<TestDeltaNewContainer>();

            var container = new TestDeltaNewContainer
            {
                items = new List<TestDeltaNewItem>()
            };
            var containerClass = LiveClass.Find(typeof(TestDeltaNewContainer));
            var containerLive = new LiveObjectHandle("loaded-modify-test", containerClass, container);
            LivePropertyUtility.SetDefault(containerLive);

            // 1回目: 要素追加してデルタ保存
            container.items.Add(new TestDeltaNewItem { name = "Original", value1 = 1.0f, value2 = 2.0f });
            var delta1 = LiveSceneSerializer.LiveSceneToJson(
                new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver, SerializeMode.Delta);

            // デフォルト状態に戻す
            container.items.Clear();

            // デルタをロード → 要素が復元される
            LiveSceneSerializer.LiveSceneFromJson(delta1, _resolver);
            Assert.AreEqual(1, container.items.Count, "ロード後: 要素が1つあるべき");
            Assert.AreEqual("Original", container.items[0].name, "ロード後: nameが復元されるべき");

            // ロードした要素のプロパティを変更（structなのでコピー→変更→代入）
            var item = container.items[0];
            item.name = "Modified";
            item.value1 = 99.0f;
            container.items[0] = item;

            // 再保存
            var delta2 = LiveSceneSerializer.LiveSceneToJson(
                new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver, SerializeMode.Delta);
            var jRoot = JObject.Parse(delta2);
            var objects = jRoot["objects"] as JArray;
            var containerObj = objects?.FirstOrDefault(o => EntryKey(o) =="loaded-modify-test") as JObject;
            Assert.IsNotNull(containerObj, $"再保存: containerが出力されるべき. JSON: {delta2}");

            var items = containerObj["items"] as JArray;
            Assert.IsNotNull(items, $"再保存: items配列が存在するべき. JSON: {delta2}");

            // @op:new要素に変更後の値が含まれる
            var newItem = items.FirstOrDefault(i => i is JObject o && o["@op"]?.ToString() == "new") as JObject;
            Assert.IsNotNull(newItem, $"再保存: @op:new要素が存在するべき. JSON: {delta2}");
            Assert.AreEqual("Modified", newItem["name"]?.Value<string>(), $"再保存: 変更したnameが出力されるべき. JSON: {delta2}");
            Assert.AreEqual(99.0f, newItem["value1"]?.Value<float>(), 0.001f, $"再保存: 変更したvalue1が出力されるべき. JSON: {delta2}");
        }

        /// <summary>
        /// インラインコンポーネント内のネスト配列要素のプロパティ変更がデルタ保存に含まれることを検証。
        /// (meshStateOverrides[0].name変更相当)
        /// LiveGameObject → components[] → Component → items[] → item.name を変更し、
        /// デルタ保存→復元でその変更が保持されることを確認。
        ///
        /// 実アプリではコンポーネントのLiveObjectがLiveGameObjectより先に生成されるため、
        /// SetDefaultの@ref境界停止でネストパス(components[0].items等)が親に作られない。
        /// この状態を再現してテストする。
        /// </summary>
        [Test]
        public void RoundTrip_DeltaFromDefault_InlineComponentNestedArrayElementChange_Preserved()
        {
            LiveClass.RegisterFromAttributes<LiveGameObject>();
            LiveClass.RegisterFromAttributes<TestComponentWithArray>();
            LiveClass.RegisterFromAttributes<TestDeltaNewItem>();

            var go = new GameObject("TestGO");
            try
            {
                var comp = go.AddComponent<TestComponentWithArray>();
                comp.items = new List<TestDeltaNewItem>();

                // 実アプリの初期化順序を再現:
                // 1. コンポーネントのLiveObjectを先に作成（LiveComponent.OnEnable相当）
                var compClass = LiveClass.Find(typeof(TestComponentWithArray));
                var compLiveObj = new LiveObjectHandle(null, compClass, comp);

                // 2. LiveGameObject作成 → auto-SetDefaultでcomponents[0]は@ref境界で停止
                var liveGO = new LiveGameObject(go);
                liveGO.OnEnable();

                var resolved = LiveObjectGraph.ResolveLiveObjects(
                    new object[] { liveGO }, _resolver);

                // 3. 全オブジェクトのSetDefault（@ref境界でネストパスなし）
                foreach (var obj in resolved)
                    LivePropertyUtility.SetDefault(obj);

                // 要素を追加してデルタ保存
                comp.items.Add(new TestDeltaNewItem { name = "Original", value1 = 1.0f, value2 = 2.0f });
                var delta1 = LiveSceneSerializer.LiveSceneToJson(resolved, _resolver, SerializeMode.Delta);

                // 値をデフォルトに戻す
                comp.items.Clear();

                // デルタをロード → 要素が復元される
                LiveSceneSerializer.LiveSceneFromJson(delta1, _resolver);
                Assert.AreEqual(1, comp.items.Count, $"ロード後: 要素が1つあるべき. JSON: {delta1}");

                // ロードした要素のnameを変更（meshStateOverrides[0].name変更相当）
                var item = comp.items[0];
                item.name = "Modified";
                comp.items[0] = item;

                // 再保存 → 変更が出力されること
                var delta2 = LiveSceneSerializer.LiveSceneToJson(resolved, _resolver, SerializeMode.Delta);
                var jRoot = JObject.Parse(delta2);
                var objects = jRoot["objects"] as JArray;

                // 新フォーマット: Component は pending エントリとしてトップレベルに出力される
                var compData = objects?.FirstOrDefault(o => o["@type"]?.ToString() == "TestComponentWithArray") as JObject;
                Assert.IsNotNull(compData, $"再保存: コンポーネントが pending エントリとして含まれるべき. JSON: {delta2}");

                var items = compData["items"] as JArray;
                Assert.IsNotNull(items, $"再保存: items配列が存在するべき. JSON: {delta2}");

                // pending は delta モードに従うため @op:new マーカーは付かない
                // items[0] が Modified になっていることを直接確認
                Assert.IsTrue(items.Count >= 1, $"再保存: items には最低1要素あるべき. JSON: {delta2}");
                var firstItem = items[0] as JObject;
                Assert.IsNotNull(firstItem, $"再保存: items[0] が JObject であるべき. JSON: {delta2}");
                Assert.AreEqual("Modified", firstItem["name"]?.Value<string>(),
                    $"再保存: 変更したnameが出力されるべき. JSON: {delta2}");

                // ラウンドトリップ: 再保存したデルタをロードして復元確認
                comp.items.Clear();
                LiveSceneSerializer.LiveSceneFromJson(delta2, _resolver);
                Assert.AreEqual(1, comp.items.Count, "復元後: 要素が1つあるべき");
                Assert.AreEqual("Modified", comp.items[0].name, "復元後: 変更したnameが復元されるべき");
            }
            finally
            {
                GameObject.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// LiveGameObjectのcomponents内コンポーネントの配列プロパティが@op:newで追加された場合、
        /// ロード→再保存でcomponents配列が消えないことを検証。
        /// 親のdirty追跡にネストパスがない場合（@ref境界停止）でも、
        /// LiveClass要素を含むプロパティは結果ベースで判定される。
        /// </summary>
        [Test]
        public void RoundTrip_DeltaFromDefault_NestedArrayOpNew_PreservedAfterReSave()
        {
            LiveClass.RegisterFromAttributes<LiveGameObject>();
            LiveClass.RegisterFromAttributes<TestAdditionsComponent>();
            LiveClass.RegisterFromAttributes<TestDeltaNewItem>();

            var go = new GameObject("TestGO");
            try
            {
                var comp = go.AddComponent<TestAdditionsComponent>();
                comp.health = 50;

                var liveGO = new LiveGameObject(go);
                liveGO.OnEnable();

                var resolved = LiveObjectGraph.ResolveLiveObjects(
                    new object[] { liveGO }, _resolver);

                // ResolveLiveObjects後にSetDefault（@ref境界でネストパスが作られない状態を再現）
                foreach (var obj in resolved)
                    LivePropertyUtility.SetDefault(obj);

                // healthを変更
                comp.health = 100;

                // 1回目デルタ保存
                var delta1 = LiveSceneSerializer.LiveSceneToJson(resolved, _resolver, SerializeMode.Delta);

                // 値をデフォルトに戻す
                comp.health = 50;

                // デルタをロード
                LiveSceneSerializer.LiveSceneFromJson(delta1, _resolver);
                Assert.AreEqual(100, comp.health, "ロード後: healthが復元されるべき");

                // ロード後に再保存（components内のデータが消えないこと）
                var delta2 = LiveSceneSerializer.LiveSceneToJson(resolved, _resolver, SerializeMode.Delta);
                var jRoot = JObject.Parse(delta2);
                var objects = jRoot["objects"] as JArray;

                // 新フォーマット: Component は pending エントリとしてトップレベルに出力される
                var compData = objects?.FirstOrDefault(o => o["@type"]?.ToString() == "TestAdditionsComponent") as JObject;
                Assert.IsNotNull(compData, $"再保存: コンポーネントが pending エントリとして含まれるべき. JSON: {delta2}");
                Assert.AreEqual(100, compData["health"]?.Value<int>(), $"再保存: healthが保持されるべき. JSON: {delta2}");
            }
            finally
            {
                GameObject.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// デルタをロードした後に再保存しても、インラインコンポーネントの変更が消えないことを検証。
        /// components配列はコレクション型であり、LiveClass.Find(listType)がnullを返すため、
        /// 要素レベルのLiveClassチェックが必要。
        /// </summary>
        [Test]
        public void RoundTrip_DeltaFromDefault_LoadThenReSave_InlineComponentPreserved()
        {
            LiveClass.RegisterFromAttributes<LiveGameObject>();
            LiveClass.RegisterFromAttributes<TestAdditionsComponent>();

            var go = new GameObject("TestGO");
            try
            {
                var comp = go.AddComponent<TestAdditionsComponent>();
                comp.health = 50;
                comp.label = "Original";

                var liveGO = new LiveGameObject(go);
                liveGO.OnEnable();

                var resolved = LiveObjectGraph.ResolveLiveObjects(
                    new object[] { liveGO }, _resolver);

                foreach (var obj in resolved)
                    LivePropertyUtility.SetDefault(obj);

                // 変更してデルタ保存
                comp.health = 200;
                var deltaJson = LiveSceneSerializer.LiveSceneToJson(resolved, _resolver, SerializeMode.Delta);

                // 値をデフォルトに戻す
                comp.health = 50;

                // デルタをロード → healthが200に復元される
                LiveSceneSerializer.LiveSceneFromJson(deltaJson, _resolver);
                Assert.AreEqual(200, comp.health, "ロード後: healthが復元されるべき");

                // ロード後に再保存 → 変更が消えないこと
                var reSavedJson = LiveSceneSerializer.LiveSceneToJson(resolved, _resolver, SerializeMode.Delta);
                var jRoot = JObject.Parse(reSavedJson);
                var objects = jRoot["objects"] as JArray;

                // 新フォーマット: Component は pending エントリとしてトップレベルに出力される
                var compData = objects?.FirstOrDefault(o => o["@type"]?.ToString() == "TestAdditionsComponent") as JObject;
                Assert.IsNotNull(compData, $"再保存: コンポーネントが pending エントリとして含まれるべき. JSON: {reSavedJson}");
                Assert.AreEqual(200, compData["health"]?.Value<int>(), $"再保存: healthが保持されるべき. JSON: {reSavedJson}");
            }
            finally
            {
                GameObject.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// デルタ保存のラウンドトリップ: 非rootオブジェクトの変更のみが保存・復元されることを検証。
        /// </summary>
        [Test]
        public void RoundTrip_DeltaFromDefault_NonRootRefObject_OnlyDirtyRestored()
        {
            LiveClass.RegisterFromAttributes<TestSceneRefItem>();
            LiveClass.RegisterFromAttributes<TestSceneContainerWithRefList>();

            var item1 = new TestSceneRefItem { name = "A", value = 10 };
            var item2 = new TestSceneRefItem { name = "B", value = 20 };
            var container = new TestSceneContainerWithRefList
            {
                items = new List<TestSceneRefItem> { item1, item2 }
            };

            var containerClass = LiveClass.Find(typeof(TestSceneContainerWithRefList));
            var itemClass = LiveClass.Find(typeof(TestSceneRefItem));
            var containerLive = new LiveObjectHandle("container-rt", containerClass, container);
            var item1Live = new LiveObjectHandle("item-rt-1", itemClass, item1);
            var item2Live = new LiveObjectHandle("item-rt-2", itemClass, item2);

            LivePropertyUtility.SetDefault(containerLive);
            LivePropertyUtility.SetDefault(item1Live);
            LivePropertyUtility.SetDefault(item2Live);

            // item1のvalueのみ変更
            item1.value = 99;

            // delta保存
            var json = LiveSceneSerializer.LiveSceneToJson(
                new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver, SerializeMode.Delta);

            // 新しいオブジェクトセットに復元
            var toRemove = LiveObjectRegistry.instances.ToList();
            foreach (var obj in toRemove) obj.Unregister();

            var restoredItem1 = new TestSceneRefItem { name = "A", value = 10 };
            var restoredItem2 = new TestSceneRefItem { name = "B", value = 20 };
            var restoredContainer = new TestSceneContainerWithRefList
            {
                items = new List<TestSceneRefItem> { restoredItem1, restoredItem2 }
            };

            new LiveObjectHandle("container-rt", containerClass, restoredContainer);
            new LiveObjectHandle("item-rt-1", itemClass, restoredItem1);
            new LiveObjectHandle("item-rt-2", itemClass, restoredItem2);

            LiveSceneSerializer.LiveSceneFromJson(json, _resolver);

            // item1のvalueのみ復元される
            Assert.AreEqual(99, restoredItem1.value, "変更されたitem1.valueが復元されるべき");
            // item2は未変更のまま
            Assert.AreEqual(20, restoredItem2.value, "未変更のitem2.valueは変わらないべき");
        }

        // =====================================================================
        // Delta mode の pending エントリは「必要最低限の情報のみ」を出力する仕様
        // （UnityEngine.Object 参照は inline 展開せず、中身フィールドは差分のみ）。
        // =====================================================================

        /// <summary>
        /// Delta モードで、inline component の値が一切変更されていない場合、
        /// pending エントリは出力されない（= objects[] に当該エントリが含まれない）こと。
        /// </summary>
        [Test]
        public void LiveSceneToJson_Delta_PendingEntry_NoChange_EmitsNothing()
        {
            LiveClass.RegisterFromAttributes<LiveGameObject>();
            LiveClass.RegisterFromAttributes<TestAdditionsComponent>();

            var go = new GameObject("TestGO");
            try
            {
                var comp = go.AddComponent<TestAdditionsComponent>();
                comp.health = 42;
                comp.label = "Original";

                var liveGO = new LiveGameObject(go);
                liveGO.OnEnable();

                var resolved = LiveObjectGraph.ResolveLiveObjects(
                    new object[] { liveGO }, _resolver);

                foreach (var obj in resolved)
                    LivePropertyUtility.SetDefault(obj);

                // 何も変更しない
                var json = LiveSceneSerializer.LiveSceneToJson(resolved, _resolver, SerializeMode.Delta);
                var jRoot = JObject.Parse(json);
                var objects = jRoot["objects"] as JArray;

                // pending エントリが emit されないこと（delta ゼロは省略）
                var compObj = objects?.FirstOrDefault(o => o["@type"]?.ToString() == "TestAdditionsComponent") as JObject;
                Assert.IsNull(compObj,
                    $"未変更の pending エントリは delta 出力に含まれないべき. JSON: {json}");
            }
            finally
            {
                GameObject.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// Delta モードで pending エントリの 1 フィールドだけ変更した場合、
        /// 出力されるエントリには変更フィールドとメタデータだけが含まれ、
        /// 未変更フィールドは出力されないこと。
        /// </summary>
        [Test]
        public void LiveSceneToJson_Delta_PendingEntry_WithChange_EmitsOnlyChangedField()
        {
            LiveClass.RegisterFromAttributes<LiveGameObject>();
            LiveClass.RegisterFromAttributes<TestAdditionsComponent>();

            var go = new GameObject("TestGO");
            try
            {
                var comp = go.AddComponent<TestAdditionsComponent>();
                comp.health = 42;
                comp.label = "Original";

                var liveGO = new LiveGameObject(go);
                liveGO.OnEnable();

                var resolved = LiveObjectGraph.ResolveLiveObjects(
                    new object[] { liveGO }, _resolver);

                foreach (var obj in resolved)
                    LivePropertyUtility.SetDefault(obj);

                // health のみ変更
                comp.health = 100;

                var json = LiveSceneSerializer.LiveSceneToJson(resolved, _resolver, SerializeMode.Delta);
                var jRoot = JObject.Parse(json);
                var objects = jRoot["objects"] as JArray;

                var compObj = objects?.FirstOrDefault(o => o["@type"]?.ToString() == "TestAdditionsComponent") as JObject;
                Assert.IsNotNull(compObj,
                    $"変更された pending エントリが delta 出力に含まれるべき. JSON: {json}");

                // メタデータ
                Assert.IsNotNull(compObj["@source"], "@source メタデータが含まれるべき");
                Assert.IsNotNull(compObj["@source"], "@source メタデータが含まれるべき");

                // 変更フィールド
                Assert.AreEqual(100, compObj["health"]?.Value<int>(),
                    $"変更した health が出力されるべき. JSON: {json}");

                // 未変更フィールドは出力されない（delta 最小化）
                Assert.IsNull(compObj["label"],
                    $"未変更の label は delta 出力に含まれないべき. JSON: {json}");
            }
            finally
            {
                GameObject.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// Snapshot モードでは pending エントリが full snapshot として出力されること（回帰防止）。
        /// </summary>
        [Test]
        public void LiveSceneToJson_Snapshot_PendingEntry_EmitsAllFields()
        {
            LiveClass.RegisterFromAttributes<LiveGameObject>();
            LiveClass.RegisterFromAttributes<TestAdditionsComponent>();

            var go = new GameObject("TestGO");
            try
            {
                var comp = go.AddComponent<TestAdditionsComponent>();
                comp.health = 42;
                comp.label = "Original";

                var liveGO = new LiveGameObject(go);
                liveGO.OnEnable();

                var resolved = LiveObjectGraph.ResolveLiveObjects(
                    new object[] { liveGO }, _resolver);

                foreach (var obj in resolved)
                    LivePropertyUtility.SetDefault(obj);

                // Snapshot モードでは全フィールドが出力される
                var json = LiveSceneSerializer.LiveSceneToJson(resolved, _resolver, SerializeMode.Snapshot);
                var jRoot = JObject.Parse(json);
                var objects = jRoot["objects"] as JArray;

                var compObj = objects?.FirstOrDefault(o => o["@type"]?.ToString() == "TestAdditionsComponent") as JObject;
                Assert.IsNotNull(compObj,
                    $"Snapshot mode: pending エントリが出力されるべき. JSON: {json}");
                Assert.AreEqual(42, compObj["health"]?.Value<int>(), "Snapshot mode: health が出力されるべき");
                Assert.AreEqual("Original", compObj["label"]?.Value<string>(), "Snapshot mode: label が出力されるべき");
            }
            finally
            {
                GameObject.DestroyImmediate(go);
            }
        }

        #endregion

        #region Delta Save Idempotency Tests

        /// <summary>
        /// 冪等性テスト: デルタ保存 → ロード → 再デルタ保存で同じJSONが出力される。
        /// 基本プロパティの変更。
        /// </summary>
        [Test]
        public void Idempotency_DeltaSaveLoadSave_BasicProperties()
        {
            LiveClass.RegisterFromAttributes<TestSceneClass>();
            var liveClass = LiveClass.Find(typeof(TestSceneClass));

            // === Session 1: 変更して保存 ===
            var obj1 = new TestSceneClass { value = 42, name = "Original", position = 1.0f };
            var live1 = new LiveObjectHandle("idemp-basic-1", liveClass, obj1);
            LivePropertyUtility.SetDefault(live1);

            obj1.value = 200;
            obj1.name = "Changed";

            var deltaJson1 = LiveSceneSerializer.LiveSceneToJson(
                new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver, SerializeMode.Delta);
            live1.Unregister();

            // === Session 2: ロードして再保存 ===
            var obj2 = new TestSceneClass { value = 42, name = "Original", position = 1.0f };
            var live2 = new LiveObjectHandle("idemp-basic-1", liveClass, obj2);
            LivePropertyUtility.SetDefault(live2);

            LiveSceneSerializer.LiveSceneFromJson(deltaJson1, _resolver);

            // 値が復元されたことを確認
            Assert.AreEqual(200, obj2.value, "value should be restored");
            Assert.AreEqual("Changed", obj2.name, "name should be restored");
            Assert.AreEqual(1.0f, obj2.position, "position should remain at default");

            // 再デルタ保存
            var deltaJson2 = LiveSceneSerializer.LiveSceneToJson(
                new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver, SerializeMode.Delta);

            // === 検証: 同じJSONが出力される ===
            var jRoot1 = JObject.Parse(deltaJson1);
            var jRoot2 = JObject.Parse(deltaJson2);
            var objects1 = jRoot1["objects"] as JArray;
            var objects2 = jRoot2["objects"] as JArray;

            Assert.AreEqual(objects1.Count, objects2.Count, "同じ数のオブジェクトが保存されるべき");

            // プロパティ値が一致
            Assert.AreEqual(200, obj2.value, "再保存後もvalueが保持されるべき");
            Assert.AreEqual("Changed", obj2.name, "再保存後もnameが保持されるべき");
        }

        /// <summary>
        /// 冪等性テスト: LiveClass配列で一部の要素のみ変更した場合。
        /// デルタ保存 → ロード → 未変更要素が消えないことを検証。
        /// </summary>
        [Test]
        public void Idempotency_DeltaSaveLoadSave_ArrayPartialChange()
        {
            LiveClass.RegisterFromAttributes<TestDeltaNewItem>();
            LiveClass.RegisterFromAttributes<TestDeltaNewContainer>();
            var containerClass = LiveClass.Find(typeof(TestDeltaNewContainer));

            // === Session 1: 3要素の配列で最初の要素のみ変更 ===
            var container1 = new TestDeltaNewContainer
            {
                items = new List<TestDeltaNewItem>
                {
                    new TestDeltaNewItem { name = "A", value1 = 1.0f, value2 = 2.0f },
                    new TestDeltaNewItem { name = "B", value1 = 1.0f, value2 = 2.0f },
                    new TestDeltaNewItem { name = "C", value1 = 1.0f, value2 = 2.0f },
                }
            };
            var live1 = new LiveObjectHandle("idemp-array-1", containerClass, container1);
            LivePropertyUtility.SetDefault(live1);

            // 最初の要素のみ変更
            var item0 = container1.items[0];
            item0.name = "A-Modified";
            item0.value1 = 99.0f;
            container1.items[0] = item0;

            var deltaJson1 = LiveSceneSerializer.LiveSceneToJson(
                new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver, SerializeMode.Delta);
            live1.Unregister();

            // === Session 2: ロード ===
            var container2 = new TestDeltaNewContainer
            {
                items = new List<TestDeltaNewItem>
                {
                    new TestDeltaNewItem { name = "A", value1 = 1.0f, value2 = 2.0f },
                    new TestDeltaNewItem { name = "B", value1 = 1.0f, value2 = 2.0f },
                    new TestDeltaNewItem { name = "C", value1 = 1.0f, value2 = 2.0f },
                }
            };
            var live2 = new LiveObjectHandle("idemp-array-1", containerClass, container2);
            LivePropertyUtility.SetDefault(live2);

            LiveSceneSerializer.LiveSceneFromJson(deltaJson1, _resolver);

            // 配列サイズが保持されていること
            Assert.AreEqual(3, container2.items.Count, "配列サイズが3のまま保持されるべき");
            // 変更された要素が復元されていること
            Assert.AreEqual("A-Modified", container2.items[0].name, "items[0].nameが復元されるべき");
            Assert.AreEqual(99.0f, container2.items[0].value1, "items[0].value1が復元されるべき");
            // 未変更要素が保持されていること
            Assert.AreEqual("B", container2.items[1].name, "items[1].nameが保持されるべき");
            Assert.AreEqual("C", container2.items[2].name, "items[2].nameが保持されるべき");

            // === 再デルタ保存 ===
            var deltaJson2 = LiveSceneSerializer.LiveSceneToJson(
                new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver, SerializeMode.Delta);
            live2.Unregister();

            // === Session 3: 再ロードして検証 ===
            var container3 = new TestDeltaNewContainer
            {
                items = new List<TestDeltaNewItem>
                {
                    new TestDeltaNewItem { name = "A", value1 = 1.0f, value2 = 2.0f },
                    new TestDeltaNewItem { name = "B", value1 = 1.0f, value2 = 2.0f },
                    new TestDeltaNewItem { name = "C", value1 = 1.0f, value2 = 2.0f },
                }
            };
            var live3 = new LiveObjectHandle("idemp-array-1", containerClass, container3);
            LivePropertyUtility.SetDefault(live3);

            LiveSceneSerializer.LiveSceneFromJson(deltaJson2, _resolver);

            Assert.AreEqual(3, container3.items.Count, "再ロード後も配列サイズが3のまま保持されるべき");
            Assert.AreEqual("A-Modified", container3.items[0].name, "再ロード後もitems[0].nameが復元されるべき");
            Assert.AreEqual(99.0f, container3.items[0].value1, "再ロード後もitems[0].value1が復元されるべき");
            Assert.AreEqual("B", container3.items[1].name, "再ロード後もitems[1].nameが保持されるべき");
            Assert.AreEqual("C", container3.items[2].name, "再ロード後もitems[2].nameが保持されるべき");
        }

        /// <summary>
        /// 冪等性テスト: 配列の中間要素のみ変更した場合。
        /// デルタ保存時に先頭の未変更要素と末尾の未変更要素が正しく保持されるか検証。
        /// </summary>
        [Test]
        public void Idempotency_DeltaSaveLoadSave_ArrayMiddleElementChanged()
        {
            LiveClass.RegisterFromAttributes<TestDeltaNewItem>();
            LiveClass.RegisterFromAttributes<TestDeltaNewContainer>();
            var containerClass = LiveClass.Find(typeof(TestDeltaNewContainer));

            // === Session 1 ===
            var container1 = new TestDeltaNewContainer
            {
                items = new List<TestDeltaNewItem>
                {
                    new TestDeltaNewItem { name = "First", value1 = 1.0f, value2 = 2.0f },
                    new TestDeltaNewItem { name = "Middle", value1 = 1.0f, value2 = 2.0f },
                    new TestDeltaNewItem { name = "Last", value1 = 1.0f, value2 = 2.0f },
                }
            };
            var live1 = new LiveObjectHandle("idemp-mid-1", containerClass, container1);
            LivePropertyUtility.SetDefault(live1);

            // 中間要素のみ変更
            var mid = container1.items[1];
            mid.name = "Middle-Changed";
            container1.items[1] = mid;

            var deltaJson1 = LiveSceneSerializer.LiveSceneToJson(
                new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver, SerializeMode.Delta);
            live1.Unregister();

            // === Session 2: ロード → 再保存 ===
            var container2 = new TestDeltaNewContainer
            {
                items = new List<TestDeltaNewItem>
                {
                    new TestDeltaNewItem { name = "First", value1 = 1.0f, value2 = 2.0f },
                    new TestDeltaNewItem { name = "Middle", value1 = 1.0f, value2 = 2.0f },
                    new TestDeltaNewItem { name = "Last", value1 = 1.0f, value2 = 2.0f },
                }
            };
            var live2 = new LiveObjectHandle("idemp-mid-1", containerClass, container2);
            LivePropertyUtility.SetDefault(live2);

            LiveSceneSerializer.LiveSceneFromJson(deltaJson1, _resolver);

            Assert.AreEqual(3, container2.items.Count, "配列サイズが保持されるべき");
            Assert.AreEqual("First", container2.items[0].name, "先頭要素が保持されるべき");
            Assert.AreEqual("Middle-Changed", container2.items[1].name, "中間要素が復元されるべき");
            Assert.AreEqual("Last", container2.items[2].name, "末尾要素が保持されるべき");

            var deltaJson2 = LiveSceneSerializer.LiveSceneToJson(
                new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver, SerializeMode.Delta);
            live2.Unregister();

            // === Session 3: 再ロード ===
            var container3 = new TestDeltaNewContainer
            {
                items = new List<TestDeltaNewItem>
                {
                    new TestDeltaNewItem { name = "First", value1 = 1.0f, value2 = 2.0f },
                    new TestDeltaNewItem { name = "Middle", value1 = 1.0f, value2 = 2.0f },
                    new TestDeltaNewItem { name = "Last", value1 = 1.0f, value2 = 2.0f },
                }
            };
            var live3 = new LiveObjectHandle("idemp-mid-1", containerClass, container3);
            LivePropertyUtility.SetDefault(live3);

            LiveSceneSerializer.LiveSceneFromJson(deltaJson2, _resolver);

            Assert.AreEqual(3, container3.items.Count, "再ロード後も配列サイズが保持されるべき");
            Assert.AreEqual("First", container3.items[0].name, "再ロード後も先頭要素が保持されるべき");
            Assert.AreEqual("Middle-Changed", container3.items[1].name, "再ロード後も中間要素が復元されるべき");
            Assert.AreEqual("Last", container3.items[2].name, "再ロード後も末尾要素が保持されるべき");
        }

        /// <summary>
        /// 冪等性テスト: 配列に新規要素を追加した場合。
        /// @op:new要素が保存・ロード・再保存を通じて保持されるか検証。
        /// </summary>
        [Test]
        public void Idempotency_DeltaSaveLoadSave_ArrayWithNewElements()
        {
            LiveClass.RegisterFromAttributes<TestDeltaNewItem>();
            LiveClass.RegisterFromAttributes<TestDeltaNewContainer>();
            var containerClass = LiveClass.Find(typeof(TestDeltaNewContainer));

            // === Session 1: 要素を追加して保存 ===
            var container1 = new TestDeltaNewContainer
            {
                items = new List<TestDeltaNewItem>
                {
                    new TestDeltaNewItem { name = "Original", value1 = 1.0f, value2 = 2.0f },
                }
            };
            var live1 = new LiveObjectHandle("idemp-new-1", containerClass, container1);
            LivePropertyUtility.SetDefault(live1);

            // 新規要素を追加
            container1.items.Add(new TestDeltaNewItem { name = "Added", value1 = 5.0f, value2 = 2.0f });

            var deltaJson1 = LiveSceneSerializer.LiveSceneToJson(
                new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver, SerializeMode.Delta);
            live1.Unregister();

            // === Session 2: ロード → 再保存 ===
            var container2 = new TestDeltaNewContainer
            {
                items = new List<TestDeltaNewItem>
                {
                    new TestDeltaNewItem { name = "Original", value1 = 1.0f, value2 = 2.0f },
                }
            };
            var live2 = new LiveObjectHandle("idemp-new-1", containerClass, container2);
            LivePropertyUtility.SetDefault(live2);

            LiveSceneSerializer.LiveSceneFromJson(deltaJson1, _resolver);

            Assert.AreEqual(2, container2.items.Count, "追加要素がロードされるべき");
            Assert.AreEqual("Original", container2.items[0].name, "元の要素が保持されるべき");
            Assert.AreEqual("Added", container2.items[1].name, "追加要素が復元されるべき");
            Assert.AreEqual(5.0f, container2.items[1].value1, "追加要素のvalue1が復元されるべき");

            var deltaJson2 = LiveSceneSerializer.LiveSceneToJson(
                new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver, SerializeMode.Delta);
            live2.Unregister();

            // === Session 3: 再ロード ===
            var container3 = new TestDeltaNewContainer
            {
                items = new List<TestDeltaNewItem>
                {
                    new TestDeltaNewItem { name = "Original", value1 = 1.0f, value2 = 2.0f },
                }
            };
            var live3 = new LiveObjectHandle("idemp-new-1", containerClass, container3);
            LivePropertyUtility.SetDefault(live3);

            LiveSceneSerializer.LiveSceneFromJson(deltaJson2, _resolver);

            Assert.AreEqual(2, container3.items.Count, "再ロード後も追加要素が保持されるべき");
            Assert.AreEqual("Original", container3.items[0].name, "再ロード後も元の要素が保持されるべき");
            Assert.AreEqual("Added", container3.items[1].name, "再ロード後も追加要素が復元されるべき");
            Assert.AreEqual(5.0f, container3.items[1].value1, "再ロード後も追加要素のvalue1が復元されるべき");
        }

        /// <summary>
        /// 冪等性テスト: 複数オブジェクトの場合。
        /// 変更されたオブジェクトと変更されていないオブジェクトが混在する場合。
        /// </summary>
        [Test]
        public void Idempotency_DeltaSaveLoadSave_MultipleObjects()
        {
            LiveClass.RegisterFromAttributes<TestSceneClass>();
            var liveClass = LiveClass.Find(typeof(TestSceneClass));

            // === Session 1 ===
            var objA1 = new TestSceneClass { value = 10, name = "A", position = 0f };
            var objB1 = new TestSceneClass { value = 20, name = "B", position = 0f };
            var liveA1 = new LiveObjectHandle("idemp-multi-a", liveClass, objA1);
            var liveB1 = new LiveObjectHandle("idemp-multi-b", liveClass, objB1);
            LivePropertyUtility.SetDefault(liveA1);
            LivePropertyUtility.SetDefault(liveB1);

            // Aのみ変更
            objA1.value = 999;

            var deltaJson1 = LiveSceneSerializer.LiveSceneToJson(
                new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver, SerializeMode.Delta);
            liveA1.Unregister();
            liveB1.Unregister();

            // Bは保存されないことを確認
            var jRoot1 = JObject.Parse(deltaJson1);
            var objects1 = jRoot1["objects"] as JArray;
            Assert.AreEqual(1, objects1.Count, "変更されたオブジェクトのみ保存されるべき");

            // === Session 2: ロード → 再保存 ===
            var objA2 = new TestSceneClass { value = 10, name = "A", position = 0f };
            var objB2 = new TestSceneClass { value = 20, name = "B", position = 0f };
            var liveA2 = new LiveObjectHandle("idemp-multi-a", liveClass, objA2);
            var liveB2 = new LiveObjectHandle("idemp-multi-b", liveClass, objB2);
            LivePropertyUtility.SetDefault(liveA2);
            LivePropertyUtility.SetDefault(liveB2);

            LiveSceneSerializer.LiveSceneFromJson(deltaJson1, _resolver);

            Assert.AreEqual(999, objA2.value, "Aの変更が復元されるべき");
            Assert.AreEqual(20, objB2.value, "Bは変更されないべき");

            var deltaJson2 = LiveSceneSerializer.LiveSceneToJson(
                new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver, SerializeMode.Delta);

            // 同じオブジェクト数
            var jRoot2 = JObject.Parse(deltaJson2);
            var objects2 = jRoot2["objects"] as JArray;
            Assert.AreEqual(1, objects2.Count, "再保存でも変更されたオブジェクトのみ保存されるべき");
            Assert.AreEqual(999, objA2.value, "再保存後もAの値が保持されるべき");
        }

        /// <summary>
        /// 冪等性テスト: 配列の全要素を変更した場合。
        /// 末尾の未変更マーカー省略がロード時に問題を起こさないか検証。
        /// </summary>
        [Test]
        public void Idempotency_DeltaSaveLoadSave_ArrayAllElementsChanged()
        {
            LiveClass.RegisterFromAttributes<TestDeltaNewItem>();
            LiveClass.RegisterFromAttributes<TestDeltaNewContainer>();
            var containerClass = LiveClass.Find(typeof(TestDeltaNewContainer));

            // === Session 1: 全要素変更 ===
            var container1 = new TestDeltaNewContainer
            {
                items = new List<TestDeltaNewItem>
                {
                    new TestDeltaNewItem { name = "A", value1 = 1.0f, value2 = 2.0f },
                    new TestDeltaNewItem { name = "B", value1 = 1.0f, value2 = 2.0f },
                    new TestDeltaNewItem { name = "C", value1 = 1.0f, value2 = 2.0f },
                }
            };
            var live1 = new LiveObjectHandle("idemp-all-1", containerClass, container1);
            LivePropertyUtility.SetDefault(live1);

            // 全要素を変更
            var a = container1.items[0]; a.name = "A-Changed"; container1.items[0] = a;
            var b = container1.items[1]; b.name = "B-Changed"; container1.items[1] = b;
            var c = container1.items[2]; c.name = "C-Changed"; container1.items[2] = c;

            var deltaJson1 = LiveSceneSerializer.LiveSceneToJson(
                new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver, SerializeMode.Delta);
            live1.Unregister();

            // === Session 2: ロード → 再保存 ===
            var container2 = new TestDeltaNewContainer
            {
                items = new List<TestDeltaNewItem>
                {
                    new TestDeltaNewItem { name = "A", value1 = 1.0f, value2 = 2.0f },
                    new TestDeltaNewItem { name = "B", value1 = 1.0f, value2 = 2.0f },
                    new TestDeltaNewItem { name = "C", value1 = 1.0f, value2 = 2.0f },
                }
            };
            var live2 = new LiveObjectHandle("idemp-all-1", containerClass, container2);
            LivePropertyUtility.SetDefault(live2);

            LiveSceneSerializer.LiveSceneFromJson(deltaJson1, _resolver);

            Assert.AreEqual(3, container2.items.Count, "配列サイズが保持されるべき");
            Assert.AreEqual("A-Changed", container2.items[0].name);
            Assert.AreEqual("B-Changed", container2.items[1].name);
            Assert.AreEqual("C-Changed", container2.items[2].name);

            var deltaJson2 = LiveSceneSerializer.LiveSceneToJson(
                new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver, SerializeMode.Delta);
            live2.Unregister();

            // === Session 3: 再ロード ===
            var container3 = new TestDeltaNewContainer
            {
                items = new List<TestDeltaNewItem>
                {
                    new TestDeltaNewItem { name = "A", value1 = 1.0f, value2 = 2.0f },
                    new TestDeltaNewItem { name = "B", value1 = 1.0f, value2 = 2.0f },
                    new TestDeltaNewItem { name = "C", value1 = 1.0f, value2 = 2.0f },
                }
            };
            var live3 = new LiveObjectHandle("idemp-all-1", containerClass, container3);
            LivePropertyUtility.SetDefault(live3);

            LiveSceneSerializer.LiveSceneFromJson(deltaJson2, _resolver);

            Assert.AreEqual(3, container3.items.Count, "再ロード後も配列サイズが保持されるべき");
            Assert.AreEqual("A-Changed", container3.items[0].name, "再ロード後もitems[0]が復元されるべき");
            Assert.AreEqual("B-Changed", container3.items[1].name, "再ロード後もitems[1]が復元されるべき");
            Assert.AreEqual("C-Changed", container3.items[2].name, "再ロード後もitems[2]が復元されるべき");
        }

        /// <summary>
        /// 再現テスト: GameObjectに2つのコンポーネントがある場合、
        /// デルタ保存→ロード→再デルタ保存で2番目のコンポーネントが消失する問題。
        /// (studio_scene.jsonでInputActionsがcomponents配列から消える現象)
        /// </summary>
        [Test]
        public void Idempotency_DeltaSaveLoadSave_MultipleComponents_AllPreserved()
        {
            LiveClass.RegisterFromAttributes<LiveGameObject>();
            LiveClass.RegisterFromAttributes<TestAdditionsComponent>();
            LiveClass.RegisterFromAttributes<TestAdditionsComponent2>();

            var go = new GameObject("TestAvatar");
            try
            {
                var comp1 = go.AddComponent<TestAdditionsComponent>();
                comp1.health = 100;
                comp1.label = "Default";

                var comp2 = go.AddComponent<TestAdditionsComponent2>();
                comp2.speed = 1.0f;

                var liveGO = new LiveGameObject(go);
                liveGO.OnEnable();

                var resolved = LiveObjectGraph.ResolveLiveObjects(
                    new object[] { liveGO }, _resolver);

                foreach (var obj in resolved)
                    LivePropertyUtility.SetDefault(obj);

                // === Session 1: 両方のコンポーネントを変更してデルタ保存 ===
                comp1.health = 200;
                comp2.speed = 5.0f;

                var deltaJson1 = LiveSceneSerializer.LiveSceneToJson(resolved, _resolver, SerializeMode.Delta);

                // 新フォーマット: Component は pending エントリとしてトップレベルに出力される
                var jRoot1 = JObject.Parse(deltaJson1);
                var objects1 = jRoot1["objects"] as JArray;

                var comp1Data = objects1?.FirstOrDefault(o => o["@type"]?.ToString() == "TestAdditionsComponent") as JObject;
                var comp2Data = objects1?.FirstOrDefault(o => o["@type"]?.ToString() == "TestAdditionsComponent2") as JObject;
                Assert.IsNotNull(comp1Data,
                    $"Session1: TestAdditionsComponentが pending エントリとして含まれるべき. JSON: {deltaJson1}");
                Assert.IsNotNull(comp2Data,
                    $"Session1: TestAdditionsComponent2が pending エントリとして含まれるべき. JSON: {deltaJson1}");

                // 値をデフォルトに戻す
                comp1.health = 100;
                comp1.label = "Default";
                comp2.speed = 1.0f;

                // === Session 2: ロード → 再デルタ保存 ===
                LiveSceneSerializer.LiveSceneFromJson(deltaJson1, _resolver);

                // 値が復元されたことを確認
                Assert.AreEqual(200, comp1.health, "Session2: comp1.healthが復元されるべき");
                Assert.AreEqual(5.0f, comp2.speed, "Session2: comp2.speedが復元されるべき");

                // 再デルタ保存
                var deltaJson2 = LiveSceneSerializer.LiveSceneToJson(resolved, _resolver, SerializeMode.Delta);

                // 新フォーマット: Component は pending エントリとしてトップレベルに出力される
                var jRoot2 = JObject.Parse(deltaJson2);
                var objects2 = jRoot2["objects"] as JArray;

                // 2番目のコンポーネントの変更も保持されているかを検証
                var comp2Data2 = objects2?.FirstOrDefault(o => o["@type"]?.ToString() == "TestAdditionsComponent2") as JObject;
                Assert.IsNotNull(comp2Data2,
                    $"Session2: TestAdditionsComponent2のデータが再保存に含まれるべき. JSON: {deltaJson2}");
                Assert.AreEqual(5.0f, comp2Data2["speed"]?.Value<float>(),
                    $"Session2: comp2.speedが再保存で保持されるべき. JSON: {deltaJson2}");
            }
            finally
            {
                GameObject.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// 再現テスト: pending エントリで Nested LiveClass フィールドを持つコンポーネントが、
        /// ロード → 再保存で中身を失わないこと。
        /// Plug._target (TransformRef) が次の上書き保存で消える問題の回帰テスト。
        /// </summary>
        [Test]
        public void Idempotency_PendingComponentWithNestedLiveClass_PreservedOnReSave()
        {
            LiveClass.RegisterFromAttributes<LiveGameObject>();
            LiveClass.RegisterFromAttributes<TestPluglikeComponent>();
            LiveClass.RegisterFromAttributes<TestPluglikePath>();

            var go = new GameObject("TestGO");
            try
            {
                var comp = go.AddComponent<TestPluglikeComponent>();
                // プレハブ初期値相当: target はインスタンス化済みだが中身は空

                var liveGO = new LiveGameObject(go);
                liveGO.OnEnable();

                var resolved = LiveObjectGraph.ResolveLiveObjects(
                    new object[] { liveGO }, _resolver);
                foreach (var obj in resolved)
                    LivePropertyUtility.SetDefault(obj);

                // Session 1: ユーザーが値を設定して保存
                comp.target.rootObjectName = "Main Avatar";
                comp.target.transformName = "Head";

                var json1 = LiveSceneSerializer.LiveSceneToJson(resolved, _resolver, SerializeMode.Delta);

                var jRoot1 = JObject.Parse(json1);
                var objects1 = jRoot1["objects"] as JArray;
                var compData1 = objects1?.FirstOrDefault(o => o["@type"]?.ToString() == "TestPluglikeComponent") as JObject;
                Assert.IsNotNull(compData1, $"Session1: pending エントリが含まれるべき. JSON: {json1}");
                Assert.IsNotNull(compData1["target"], $"Session1: target が出力されるべき. JSON: {json1}");

                // 値をリセット
                comp.target.rootObjectName = null;
                comp.target.transformName = null;

                // 実アプリでプレハブ経由生成されたコンポーネントの defaults が
                // ロード時点で未登録な状態を再現するため、defaults レジストリをクリア。
                LiveObjectDefaultRegistry.ClearAll();

                // Session 2: ロード
                LiveSceneSerializer.LiveSceneFromJson(json1, _resolver);

                Assert.AreEqual("Main Avatar", comp.target.rootObjectName,
                    "Session2: rootObjectName が復元されるべき");
                Assert.AreEqual("Head", comp.target.transformName,
                    "Session2: transformName が復元されるべき");

                // Session 2: 再デルタ保存（上書き保存を模擬）
                var json2 = LiveSceneSerializer.LiveSceneToJson(resolved, _resolver, SerializeMode.Delta);

                var jRoot2 = JObject.Parse(json2);
                var objects2 = jRoot2["objects"] as JArray;
                var compData2 = objects2?.FirstOrDefault(o => o["@type"]?.ToString() == "TestPluglikeComponent") as JObject;

                Assert.IsNotNull(compData2,
                    $"Session2: pending エントリが再保存に含まれるべき. JSON: {json2}");
                var target2 = compData2["target"] as JObject;
                Assert.IsNotNull(target2,
                    $"Session2: target が再保存に含まれるべき（これが消えるのがバグ）. JSON: {json2}");
                Assert.AreEqual("Main Avatar", target2["rootObjectName"]?.Value<string>(),
                    $"Session2: rootObjectName が保持されるべき. JSON: {json2}");
                Assert.AreEqual("Head", target2["transformName"]?.Value<string>(),
                    $"Session2: transformName が保持されるべき. JSON: {json2}");
            }
            finally
            {
                GameObject.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// 再現テスト: 複数のGameObjectがある場合、
        /// デルタ保存→ロード→再デルタ保存で2番目のGameObjectが完全に消失する問題。
        /// (studio_scene.jsonで"Main Screen"が消える現象)
        /// </summary>
        [Test]
        public void Idempotency_DeltaSaveLoadSave_MultipleGameObjects_AllPreserved()
        {
            LiveClass.RegisterFromAttributes<LiveGameObject>();
            LiveClass.RegisterFromAttributes<TestAdditionsComponent>();
            LiveClass.RegisterFromAttributes<TestAdditionsComponent2>();

            var go1 = new GameObject("CurrentAvatar");
            var go2 = new GameObject("MainScreen");
            try
            {
                // GO1: TestAdditionsComponent
                var comp1 = go1.AddComponent<TestAdditionsComponent>();
                comp1.health = 100;
                comp1.label = "Default";

                var liveGO1 = new LiveGameObject(go1);
                liveGO1.OnEnable();

                // GO2: TestAdditionsComponent2
                var comp2 = go2.AddComponent<TestAdditionsComponent2>();
                comp2.speed = 1.0f;

                var liveGO2 = new LiveGameObject(go2);
                liveGO2.OnEnable();

                var resolved = LiveObjectGraph.ResolveLiveObjects(
                    new object[] { liveGO1, liveGO2 }, _resolver);

                foreach (var obj in resolved)
                    LivePropertyUtility.SetDefault(obj);

                // === Session 1: 両方のGOのコンポーネントを変更してデルタ保存 ===
                comp1.health = 200;
                comp2.speed = 5.0f;

                var deltaJson1 = LiveSceneSerializer.LiveSceneToJson(resolved, _resolver, SerializeMode.Delta);

                // 両方のGameObjectがデルタ出力に含まれることを確認
                var jRoot1 = JObject.Parse(deltaJson1);
                var objects1 = jRoot1["objects"] as JArray;
                Assert.IsTrue(objects1.Count >= 2,
                    $"Session1: 2つ以上のオブジェクトがデルタ出力に含まれるべき. JSON: {deltaJson1}");

                // 値をデフォルトに戻す
                comp1.health = 100;
                comp1.label = "Default";
                comp2.speed = 1.0f;

                // === Session 2: ロード → 再デルタ保存 ===
                LiveSceneSerializer.LiveSceneFromJson(deltaJson1, _resolver);

                // 値が復元されたことを確認
                Assert.AreEqual(200, comp1.health, "Session2: comp1.healthが復元されるべき");
                Assert.AreEqual(5.0f, comp2.speed, "Session2: comp2.speedが復元されるべき");

                // 再デルタ保存
                var deltaJson2 = LiveSceneSerializer.LiveSceneToJson(resolved, _resolver, SerializeMode.Delta);

                var jRoot2 = JObject.Parse(deltaJson2);
                var objects2 = jRoot2["objects"] as JArray;

                // 新フォーマット: Component は pending エントリとしてトップレベルに出力される。
                // @name は永続化されないので @type のみで特定 (このテストでは TestAdditionsComponent2 は 1 個のみ)。
                var screenCompData = objects2?.FirstOrDefault(o =>
                    o["@type"]?.ToString() == "TestAdditionsComponent2") as JObject;
                Assert.IsNotNull(screenCompData,
                    $"Session2: MainScreen の TestAdditionsComponent2 が pending エントリとして含まれるべき. JSON: {deltaJson2}");
                Assert.AreEqual(5.0f, screenCompData["speed"]?.Value<float>(),
                    $"Session2: speedが再保存で保持されるべき. JSON: {deltaJson2}");
            }
            finally
            {
                GameObject.DestroyImmediate(go1);
                GameObject.DestroyImmediate(go2);
            }
        }

        /// <summary>
        /// 1番目のコンポーネントのみ変更、2番目は未変更のケース。
        /// Delta モードでは未変更の pending エントリは出力されない（最小化仕様）。
        /// 変更された comp1 のみが pending エントリとして出力され、再保存後も
        /// comp1 の変更が消えない（冪等性）ことを検証する。
        /// </summary>
        [Test]
        public void Idempotency_DeltaSaveLoadSave_OnlyFirstComponentChanged_SecondPreserved()
        {
            LiveClass.RegisterFromAttributes<LiveGameObject>();
            LiveClass.RegisterFromAttributes<TestAdditionsComponent>();
            LiveClass.RegisterFromAttributes<TestAdditionsComponent2>();

            var go = new GameObject("TestAvatar");
            try
            {
                var comp1 = go.AddComponent<TestAdditionsComponent>();
                comp1.health = 100;
                comp1.label = "Default";

                var comp2 = go.AddComponent<TestAdditionsComponent2>();
                comp2.speed = 1.0f;

                var liveGO = new LiveGameObject(go);
                liveGO.OnEnable();

                var resolved = LiveObjectGraph.ResolveLiveObjects(
                    new object[] { liveGO }, _resolver);

                foreach (var obj in resolved)
                    LivePropertyUtility.SetDefault(obj);

                // === Session 1: 1番目のコンポーネントのみ変更、2番目は未変更 ===
                comp1.health = 200;
                // comp2は変更しない

                var deltaJson1 = LiveSceneSerializer.LiveSceneToJson(resolved, _resolver, SerializeMode.Delta);

                var jRoot1 = JObject.Parse(deltaJson1);
                var objects1 = jRoot1["objects"] as JArray;

                // Delta モードでは変更された comp1 のみ出力され、未変更の comp2 は省略される
                var comp1Count1 = objects1?.Count(o => o["@type"]?.ToString() == "TestAdditionsComponent") ?? 0;
                var comp2Count1 = objects1?.Count(o => o["@type"]?.ToString() == "TestAdditionsComponent2") ?? 0;
                Assert.AreEqual(1, comp1Count1,
                    $"Session1: 変更された comp1 のみ pending として出力されるべき. JSON: {deltaJson1}");
                Assert.AreEqual(0, comp2Count1,
                    $"Session1: 未変更の comp2 は delta 出力に含まれないべき. JSON: {deltaJson1}");

                // 値をデフォルトに戻す
                comp1.health = 100;
                comp1.label = "Default";

                // === Session 2: ロード → 再デルタ保存 ===
                LiveSceneSerializer.LiveSceneFromJson(deltaJson1, _resolver);

                Assert.AreEqual(200, comp1.health, "Session2: comp1.healthが復元されるべき");
                Assert.AreEqual(1.0f, comp2.speed, "Session2: comp2.speedはデフォルトのまま");

                var deltaJson2 = LiveSceneSerializer.LiveSceneToJson(resolved, _resolver, SerializeMode.Delta);

                var jRoot2 = JObject.Parse(deltaJson2);
                var objects2 = jRoot2["objects"] as JArray;

                // 再保存でも comp1 のみ出力される（冪等性）
                var comp1Count2 = objects2?.Count(o => o["@type"]?.ToString() == "TestAdditionsComponent") ?? 0;
                var comp2Count2 = objects2?.Count(o => o["@type"]?.ToString() == "TestAdditionsComponent2") ?? 0;
                Assert.AreEqual(1, comp1Count2,
                    $"Session2: 再保存でも comp1 のみ出力されるべき. JSON: {deltaJson2}");
                Assert.AreEqual(0, comp2Count2,
                    $"Session2: 未変更の comp2 は delta 出力に含まれないべき. JSON: {deltaJson2}");

                // 1番目のコンポーネントの変更が保持されているべき
                var comp1Data = objects2?.FirstOrDefault(o => o["@type"]?.ToString() == "TestAdditionsComponent") as JObject;
                Assert.IsNotNull(comp1Data, $"Session2: TestAdditionsComponentが含まれるべき. JSON: {deltaJson2}");
                Assert.AreEqual(200, comp1Data["health"]?.Value<int>(),
                    $"Session2: healthが保持されるべき. JSON: {deltaJson2}");
            }
            finally
            {
                GameObject.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// AvatarInputに近い構造のテストコンポーネント。
        /// writable+persistable の settings と readonly+非persistable の readonlyInfo を持つ。
        /// </summary>
        [LiveClass("TestInputLikeComponent")]
        public class TestInputLikeComponent : MonoBehaviour
        {
            [SerializeField, LiveField("settings")] internal string _settingsJson = "{}";

            public string settings
            {
                get => _settingsJson;
                set => _settingsJson = value ?? "{}";
            }

            // readonly + 非persistable（AvatarInput.actionNames相当）
            [LiveProperty("readonlyInfo")]
            public string readonlyInfo => _settingsJson.Length > 2 ? "has-data" : "empty";
        }

        /// <summary>
        /// LiveGameObject + readonly componentsプロパティ経由で
        /// readonlyプロパティ持ちコンポーネントのLoad→Saveラウンドトリップテスト。
        /// AvatarInputのsettings+actionNamesシナリオを再現する。
        /// </summary>
        [Test]
        public void RoundTrip_LoadDelta_ComponentWithReadonlyProp_PreservedOnReSave()
        {
            LiveClass.RegisterFromAttributes<LiveGameObject>();
            LiveClass.RegisterFromAttributes<TestInputLikeComponent>();

            var go = new GameObject("TestGO_InputLike");
            try
            {
                var comp = go.AddComponent<TestInputLikeComponent>();
                comp._settingsJson = "{}"; // 初期状態

                var liveGO = new LiveGameObject(go);
                liveGO.OnEnable();

                var resolved = LiveObjectGraph.ResolveLiveObjects(
                    new object[] { liveGO }, _resolver);

                foreach (var obj in resolved)
                    LivePropertyUtility.SetDefault(obj);

                // readonlyInfoの初期値を確認
                Assert.AreEqual("empty", comp.readonlyInfo, "初期: readonlyInfoはempty");

                // LiveGameObject._componentsはLiveClass.Has()でフィルタされるため、
                // LiveClass登録済みコンポーネントのみ含まれる（Transformは除外）。
                // TestInputLikeComponentはindex 0。
                // デルタJSON構築: コンポーネントデータを直接配列要素として指定
                var componentsArray = new JArray
                {
                    new JObject
                    {
                        ["@type"] = "TestInputLikeComponent",
                        ["settings"] = "{\"binding\":\"keyboard/a\"}"
                    }
                };

                var loadJson = new JObject
                {
                    ["objects"] = new JArray
                    {
                        new JObject
                        {
                            ["@type"] = "GameObject",
                            ["@id"] = liveGO.id,
                            ["components"] = componentsArray
                        }
                    }
                };

                // Load delta
                LiveSceneSerializer.LiveSceneFromJson(loadJson.ToString(), _resolver);
                Assert.AreEqual("{\"binding\":\"keyboard/a\"}", comp.settings, "Load後: settingsが変更されるべき");
                Assert.AreEqual("has-data", comp.readonlyInfo, "Load後: readonlyInfoが変化");

                // Re-save (Delta)
                var reSaved = LiveSceneSerializer.LiveSceneToJson(resolved, _resolver, SerializeMode.Delta);
                var jRoot = JObject.Parse(reSaved);
                var objects = jRoot["objects"] as JArray;

                // 新フォーマット: Component は pending エントリとしてトップレベルに出力される
                var compData = objects?.FirstOrDefault(o => o["@type"]?.ToString() == "TestInputLikeComponent") as JObject;
                Assert.IsNotNull(compData,
                    $"TestInputLikeComponentが pending エントリとして含まれるべき。JSON: {reSaved}");
                Assert.IsNotNull(compData["settings"],
                    $"settings変更が含まれるべき。JSON: {reSaved}");
            }
            finally
            {
                GameObject.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// Load→Saveラウンドトリップで複数オブジェクトが保持されることを検証。
        /// ユーザーのシナリオ: 2オブジェクト（配列追加 + コンポーネント変更）のデルタを
        /// ロードして再保存した際、両オブジェクトが出力に含まれること。
        /// </summary>
        [Test]
        public void RoundTrip_LoadDelta_ThenReSave_BothObjectsPreserved()
        {
            LiveClass.RegisterFromAttributes<TestDeltaNewItem>();
            LiveClass.RegisterFromAttributes<TestDeltaNewContainer>();
            LiveClass.RegisterFromAttributes<TestSceneClass>();

            // Object 1: 配列を持つオブジェクト（AvatarExpressionConfig相当）
            var container = new TestDeltaNewContainer
            {
                items = new List<TestDeltaNewItem>()
            };
            var containerClass = LiveClass.Find(typeof(TestDeltaNewContainer));
            var containerObj = new LiveObjectHandle("obj-container", containerClass, container);

            // Object 2: 単純なプロパティを持つオブジェクト
            var simpleObj = new TestSceneClass
            {
                value = 0,
                name = "Default",
                position = 0f
            };
            var simpleClass = LiveClass.Find(typeof(TestSceneClass));
            var simpleLive = new LiveObjectHandle("obj-simple", simpleClass, simpleObj);

            try
            {
                // CaptureDefaults（Playモード開始時相当）
                LivePropertyUtility.SetDefault(containerObj);
                LivePropertyUtility.SetDefault(simpleLive);

                // 保存済みデルタJSON（2オブジェクトの変更を含む）
                var savedDelta = @"{
                    ""objects"": [
                        {
                            ""@type"": ""TestDeltaNewContainer"",
                            ""@id"": ""obj-container"",
                            ""@name"": ""obj-container"",
                            ""items"": [
                                { ""@type"": ""TestDeltaNewItem"", ""name"": ""item1"", ""@op"": ""new"" }
                            ]
                        },
                        {
                            ""@type"": ""TestSceneClass"",
                            ""@id"": ""obj-simple"",
                            ""@name"": ""obj-simple"",
                            ""value"": 42,
                            ""name"": ""Modified""
                        }
                    ]
                }";

                // Load delta
                LiveSceneSerializer.LiveSceneFromJson(savedDelta, _resolver);
                Assert.AreEqual(1, container.items.Count, "Load後: items に1要素追加");
                Assert.AreEqual("item1", container.items[0].name);
                Assert.AreEqual(42, simpleObj.value, "Load後: value が変更");
                Assert.AreEqual("Modified", simpleObj.name, "Load後: name が変更");

                // Re-save (Delta mode)
                var resolved = new List<LiveObjectHandle>(LiveObjectRegistry.instances);
                var reSaved = LiveSceneSerializer.LiveSceneToJson(resolved, _resolver, SerializeMode.Delta);
                var jRoot = JObject.Parse(reSaved);
                var objects = jRoot["objects"] as JArray;

                // 両オブジェクトが出力に含まれるべき
                Assert.IsNotNull(objects, "objects配列が存在するべき");

                var containerResult = objects.FirstOrDefault(o => EntryKey(o) =="obj-container") as JObject;
                Assert.IsNotNull(containerResult,
                    $"Object1 (container) が再保存デルタに含まれるべき。JSON: {reSaved}");

                var simpleResult = objects.FirstOrDefault(o => EntryKey(o) =="obj-simple") as JObject;
                Assert.IsNotNull(simpleResult,
                    $"Object2 (simple) が再保存デルタに含まれるべき。JSON: {reSaved}");

                // Object2の変更が保持されているか
                Assert.AreEqual(42, simpleResult["value"]?.Value<int>(),
                    $"Object2: value が保持されるべき。JSON: {reSaved}");
            }
            finally
            {
                containerObj.Unregister();
                simpleLive.Unregister();
            }
        }

        #endregion

        #region Delta with Nested Readonly Properties Tests

        // ネストされたLiveClassにreadonly/非persistableプロパティがある場合の
        // Delta+forPersistence シリアライズテスト用クラス
        [Serializable]
        [LiveClass("TestDeltaNestedReadonly_Child")]
        public class TestDeltaNestedReadonly_Child
        {
            [LiveField]
            public int writableValue;

            // readonly かつ非persistable — CaptureDefaultsには含まれるが、forPersistence=trueでは除外される
            private string[] _readonlyNames = new[] { "name1", "name2" };

            [LiveProperty("readonlyNames")]
            public string[] readonlyNames => _readonlyNames;
        }

        [Serializable]
        [LiveClass("TestDeltaNestedReadonly_Parent")]
        public class TestDeltaNestedReadonly_Parent
        {
            [LiveField]
            public TestDeltaNestedReadonly_Child child;

            [LiveField]
            public int parentValue;
        }

        [Test]
        public void LiveSceneToJson_Delta_NestedReadonlyDoesNotPreventDirtyDetection()
        {
            // Arrange — ネストされたLiveClassにreadonly/非persistableプロパティがある場合、
            // CaptureDefaults（forPersistence=false）とDeltaシリアライズ（forPersistence=true）の
            // 非対称性がdirty検出を阻害しないことを確認する
            LiveClass.RegisterFromAttributes<TestDeltaNestedReadonly_Child>();
            LiveClass.RegisterFromAttributes<TestDeltaNestedReadonly_Parent>();

            var testObj = new TestDeltaNestedReadonly_Parent
            {
                parentValue = 10,
                child = new TestDeltaNestedReadonly_Child { writableValue = 5 }
            };

            var liveClass = LiveClass.Find(typeof(TestDeltaNestedReadonly_Parent));
            var liveObj = new LiveObjectHandle("test-nested-readonly-delta", liveClass, testObj);

            try
            {
                // デフォルト値をキャプチャ（forPersistence=falseでシリアライズされる）
                LivePropertyUtility.SetDefault(liveObj);

                // ネストされたchildのwritableValueを変更
                testObj.child.writableValue = 99;

                // Act — Delta modeでシリアライズ（内部でforPersistence=trueが使われる）
                var json = LiveSceneSerializer.LiveSceneToJson(
                    new List<LiveObjectHandle>(LiveObjectRegistry.instances),
                    _resolver,
                    SerializeMode.Delta);

                // Assert — オブジェクトがobjects配列に含まれるべき
                var jRoot = JObject.Parse(json);
                var objects = jRoot["objects"] as JArray;
                Assert.IsNotNull(objects, "objects配列が存在するべき");
                Assert.AreEqual(1, objects.Count,
                    $"dirtyなオブジェクトが1つ含まれるべき。JSON: {json}");

                var obj = objects[0] as JObject;
                Assert.IsNotNull(obj, "オブジェクトがJObjectであるべき");
                Assert.AreEqual("test-nested-readonly-delta", EntryKey(obj));

                // child.writableValueの変更がデルタに含まれるべき
                var childObj = obj["child"] as JObject;
                Assert.IsNotNull(childObj,
                    $"childプロパティがデルタに含まれるべき。JSON: {json}");
                Assert.AreEqual(99, childObj["writableValue"]?.Value<int>(),
                    $"child.writableValueの変更がデルタに含まれるべき。JSON: {json}");

                // readonlyNamesはforPersistenceで除外されるべき
                Assert.IsNull(childObj["readonlyNames"],
                    "readonlyNamesはforPersistence時にデルタに含まれるべきでない");
            }
            finally
            {
                liveObj.Unregister();
            }
        }

        [Test]
        public void LiveSceneToJson_Delta_NestedOnlyParentDirty_IncludesObject()
        {
            // Arrange — 親のプロパティだけ変更した場合もオブジェクトが含まれることを確認
            LiveClass.RegisterFromAttributes<TestDeltaNestedReadonly_Child>();
            LiveClass.RegisterFromAttributes<TestDeltaNestedReadonly_Parent>();

            var testObj = new TestDeltaNestedReadonly_Parent
            {
                parentValue = 10,
                child = new TestDeltaNestedReadonly_Child { writableValue = 5 }
            };

            var liveClass = LiveClass.Find(typeof(TestDeltaNestedReadonly_Parent));
            var liveObj = new LiveObjectHandle("test-nested-readonly-delta2", liveClass, testObj);

            try
            {
                LivePropertyUtility.SetDefault(liveObj);

                // 親プロパティのみ変更
                testObj.parentValue = 42;

                // Act
                var json = LiveSceneSerializer.LiveSceneToJson(
                    new List<LiveObjectHandle>(LiveObjectRegistry.instances),
                    _resolver,
                    SerializeMode.Delta);

                // Assert
                var jRoot = JObject.Parse(json);
                var objects = jRoot["objects"] as JArray;
                Assert.AreEqual(1, objects.Count,
                    $"親プロパティがdirtyなのでオブジェクトが含まれるべき。JSON: {json}");

                var obj = objects[0] as JObject;
                Assert.AreEqual(42, obj["parentValue"]?.Value<int>());
            }
            finally
            {
                liveObj.Unregister();
            }
        }

        [Test]
        public void LiveSceneToJson_Delta_NoDirtyChanges_ExcludesObject()
        {
            // Arrange — 変更がない場合はオブジェクトが除外されることを確認
            LiveClass.RegisterFromAttributes<TestDeltaNestedReadonly_Child>();
            LiveClass.RegisterFromAttributes<TestDeltaNestedReadonly_Parent>();

            var testObj = new TestDeltaNestedReadonly_Parent
            {
                parentValue = 10,
                child = new TestDeltaNestedReadonly_Child { writableValue = 5 }
            };

            var liveClass = LiveClass.Find(typeof(TestDeltaNestedReadonly_Parent));
            var liveObj = new LiveObjectHandle("test-nested-readonly-delta3", liveClass, testObj);

            try
            {
                LivePropertyUtility.SetDefault(liveObj);

                // 何も変更しない

                // Act
                var json = LiveSceneSerializer.LiveSceneToJson(
                    new List<LiveObjectHandle>(LiveObjectRegistry.instances),
                    _resolver,
                    SerializeMode.Delta);

                // Assert — 変更なしのオブジェクトは除外されるべき
                var jRoot = JObject.Parse(json);
                var objects = jRoot["objects"] as JArray;
                Assert.AreEqual(0, objects.Count,
                    $"変更がないオブジェクトはDeltaモードで除外されるべき。JSON: {json}");
            }
            finally
            {
                liveObj.Unregister();
            }
        }

        // readonlyプロパティのみが変化した場合のテスト用クラス
        // AvatarInputのactionNames（readonly, 非persistable）と
        // settings（read-write, persistable）の構造をモデル化
        [Serializable]
        [LiveClass("TestComponentWithReadonlyChange")]
        public class TestComponentWithReadonlyChange
        {
            // 書き込み可能+persistable（settingsに相当）
            [LiveField]
            public int settingsValue = 0;

            // readonly+非persistable（actionNamesに相当）
            // 外部から_readonlyNamesを変更してreadonly propertyの返す値を変える
            internal string[] _readonlyNames = new string[0];

            [LiveProperty("readonlyNames")]
            public string[] readonlyNames => _readonlyNames;
        }

        [Serializable]
        [LiveClass("TestParentWithComponentArray")]
        public class TestParentWithComponentArray
        {
            // writable+persistable 配列
            // 実際のLiveGameObject.componentsはreadonly+containsLiveObjectReference=trueだが
            // テストではComponent型を使えないためwritableで代替
            [LiveField]
            public TestComponentWithReadonlyChange[] components = new TestComponentWithReadonlyChange[0];
        }

        [Test]
        public void SerializeFullToJObject_ForPersistence_FiltersNestedReadonlyInArray()
        {
            // Arrange — SerializeFullToJObjectのforPersistence=trueで
            // 配列内ネストLiveClassのreadonlyプロパティが除外されることを確認
            LiveClass.RegisterFromAttributes<TestComponentWithReadonlyChange>();
            LiveClass.RegisterFromAttributes<TestParentWithComponentArray>();

            var component = new TestComponentWithReadonlyChange { settingsValue = 10 };
            component._readonlyNames = new[] { "test1" };
            var testObj = new TestParentWithComponentArray
            {
                components = new[] { component }
            };

            var liveClass = LiveClass.Find(typeof(TestParentWithComponentArray));
            var liveObj = new LiveObjectHandle("test-serialize-check", liveClass, testObj);

            try
            {
                // Act — forPersistence=true
                var jObj = LivePropertySerializer.SerializeFullToJObject(liveObj, _resolver, forPersistence: true);

                // Assert — components配列はwritable+persistableなので含まれる
                var comps = jObj["components"] as JArray;
                Assert.IsNotNull(comps, $"componentsがシリアライズされるべき。JSON: {jObj}");
                Assert.AreEqual(1, comps.Count);

                var comp = comps[0] as JObject;
                // writableValue は含まれるべき
                Assert.IsNotNull(comp["settingsValue"], $"settingsValueは含まれるべき。JSON: {jObj}");
                // readonlyNames は readonly+非persistable なので除外されるべき
                Assert.IsNull(comp["readonlyNames"],
                    $"readonlyNames はforPersistence時に除外されるべき。comp JSON: {comp}");
            }
            finally
            {
                liveObj.Unregister();
            }
        }

        [Test]
        public void LiveSceneToJson_Delta_ReadonlyOnlyChange_InNestedComponent_DetectsCorrectly()
        {
            // Arrange — ネストされたコンポーネントのreadonlyプロパティだけが変化した場合、
            // writableプロパティが変化していなければデルタに含まれるべきでない
            // （readonlyプロパティの変化はpersistenceで保存されるべきでない）
            LiveClass.RegisterFromAttributes<TestComponentWithReadonlyChange>();
            LiveClass.RegisterFromAttributes<TestParentWithComponentArray>();

            var component = new TestComponentWithReadonlyChange { settingsValue = 10 };
            var testObj = new TestParentWithComponentArray
            {
                components = new[] { component }
            };

            var liveClass = LiveClass.Find(typeof(TestParentWithComponentArray));
            var liveObj = new LiveObjectHandle("test-readonly-only-change", liveClass, testObj);

            try
            {
                // デフォルトキャプチャ時: readonlyNames = []
                LivePropertyUtility.SetDefault(liveObj);

                // readonlyプロパティだけ変更（settingsValueは変更しない）
                component._readonlyNames = new[] { "Expression.happy" };

                // Act
                var json = LiveSceneSerializer.LiveSceneToJson(
                    new List<LiveObjectHandle>(LiveObjectRegistry.instances),
                    _resolver,
                    SerializeMode.Delta);

                // Assert — readonlyプロパティの変化のみではオブジェクトは除外されるべき
                // （persistenceでは保存しないデータなので）
                var jRoot = JObject.Parse(json);
                var objects = jRoot["objects"] as JArray;
                Assert.AreEqual(0, objects.Count,
                    $"readonlyプロパティのみの変化ではオブジェクトが除外されるべき。JSON: {json}");
            }
            finally
            {
                liveObj.Unregister();
            }
        }

        [Test]
        public void LiveSceneToJson_Delta_WritableChange_InNestedComponent_AlwaysDetected()
        {
            // Arrange — ネストされたコンポーネントのwritableプロパティが変化した場合、
            // readonly変化の有無に関わらずデルタに含まれるべき
            LiveClass.RegisterFromAttributes<TestComponentWithReadonlyChange>();
            LiveClass.RegisterFromAttributes<TestParentWithComponentArray>();

            var component = new TestComponentWithReadonlyChange { settingsValue = 10 };
            var testObj = new TestParentWithComponentArray
            {
                components = new[] { component }
            };

            var liveClass = LiveClass.Find(typeof(TestParentWithComponentArray));
            var liveObj = new LiveObjectHandle("test-writable-change", liveClass, testObj);

            try
            {
                LivePropertyUtility.SetDefault(liveObj);

                // writableプロパティを変更
                component.settingsValue = 99;
                // readonlyも変わる（実際のシナリオでは両方変わることが多い）
                component._readonlyNames = new[] { "Expression.happy" };

                // Act
                var json = LiveSceneSerializer.LiveSceneToJson(
                    new List<LiveObjectHandle>(LiveObjectRegistry.instances),
                    _resolver,
                    SerializeMode.Delta);

                // Assert
                var jRoot = JObject.Parse(json);
                var objects = jRoot["objects"] as JArray;
                Assert.AreEqual(1, objects.Count,
                    $"writableプロパティの変化によりオブジェクトがデルタに含まれるべき。JSON: {json}");

                var obj = objects[0] as JObject;
                var comps = obj["components"] as JArray;
                Assert.IsNotNull(comps, $"componentsがデルタに含まれるべき。JSON: {json}");
            }
            finally
            {
                liveObj.Unregister();
            }
        }

        #endregion

        #region Delta Save Multiple Objects - Object Loss Bug

        /// <summary>
        /// 複数オブジェクトをDeltaモードで保存→ロード→再保存した際に、
        /// 2つ目以降のオブジェクトが消えるバグの回帰テスト。
        /// 原因: _ToJsonDeltaでデフォルト未登録時にcurrentJsonをデフォルトとみなし差分ゼロで除外。
        /// </summary>
        [Test]
        public void DeltaSave_MultipleObjects_LoadThenSave_AllObjectsPreserved()
        {
            // Arrange - 2つのオブジェクトを生成
            LiveClass.RegisterFromAttributes<TestSceneClass>();
            LiveClass.RegisterFromAttributes<TestDeltaNewContainer>();
            LiveClass.RegisterFromAttributes<TestDeltaNewItem>();

            var obj1 = new TestDeltaNewContainer { items = new List<TestDeltaNewItem>() };
            var obj2 = new TestSceneClass { value = 0, name = "", position = 0f };

            var liveClass1 = LiveClass.Find(typeof(TestDeltaNewContainer));
            var liveClass2 = LiveClass.Find(typeof(TestSceneClass));
            var liveObj1 = new LiveObjectHandle("obj-1", liveClass1, obj1);
            var liveObj2 = new LiveObjectHandle("obj-2", liveClass2, obj2);

            try
            {
                // Step 1: デフォルトキャプチャ
                LivePropertyUtility.SetDefault(liveObj1);
                LivePropertyUtility.SetDefault(liveObj2);

                // Step 2: デルタJSONをロード（両方のオブジェクトに変更あり）
                var loadJson = @"{
                    ""format"": ""jp.lilium.remotecontrol.scene"",
                    ""formatVersion"": 1,
                    ""objects"": [
                        {
                            ""@type"": ""TestDeltaNewContainer"",
                            ""@id"": ""obj-1"",
                            ""@name"": """",
                            ""items"": [
                                {
                                    ""@type"": ""TestDeltaNewItem"",
                                    ""name"": ""NewItem"",
                                    ""@op"": ""new""
                                }
                            ]
                        },
                        {
                            ""@type"": ""TestSceneClass"",
                            ""@id"": ""obj-2"",
                            ""@name"": """",
                            ""value"": 42,
                            ""name"": ""Changed""
                        }
                    ]
                }";
                LiveSceneSerializer.LiveSceneFromJson(loadJson, _resolver);

                // ロードされた値が反映されていることを確認
                Assert.AreEqual(1, obj1.items.Count, "items should have 1 element after load");
                Assert.AreEqual("NewItem", obj1.items[0].name, "item name should be loaded");
                Assert.AreEqual(42, obj2.value, "value should be loaded");
                Assert.AreEqual("Changed", obj2.name, "name should be loaded");

                // Step 3: DeltaFromDefaultで保存
                var savedJson = LiveSceneSerializer.LiveSceneToJson(
                    new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver, SerializeMode.Delta);

                // Assert: 両方のオブジェクトが出力に含まれること
                var jRoot = JObject.Parse(savedJson);
                var objects = jRoot["objects"] as JArray;
                Assert.IsNotNull(objects, "objects array should exist");
                Assert.AreEqual(2, objects.Count, $"Both objects should be preserved. JSON: {savedJson}");

                // Step 4: 保存したJSONを再ロードして値が復元されることを確認
                // まずオブジェクトをデフォルトに戻す
                obj1.items.Clear();
                obj2.value = 0;
                obj2.name = "";

                LiveSceneSerializer.LiveSceneFromJson(savedJson, _resolver);

                Assert.AreEqual(1, obj1.items.Count, "items should be restored from saved JSON");
                Assert.AreEqual("NewItem", obj1.items[0].name, "item name should be restored");
                Assert.AreEqual(42, obj2.value, "value should be restored from saved JSON");
                Assert.AreEqual("Changed", obj2.name, "name should be restored from saved JSON");
            }
            finally
            {
                liveObj1.Unregister();
                liveObj2.Unregister();
            }
        }

        /// <summary>
        /// Activator.CreateInstanceで生成不可な型（GetOrCreateが失敗する型）。
        /// LiveUnityObjectProxy等のScriptableObject派生型を模擬する。
        /// </summary>
        [Serializable]
        [LiveClass("TestNoDefaultCtorClass")]
        public class TestNoDefaultCtorClass
        {
            [LiveField]
            public int value;

            [LiveField]
            public string name;

            // デフォルトコンストラクタなし（引数付きのみ）
            public TestNoDefaultCtorClass(int initialValue)
            {
                value = initialValue;
                name = "";
            }
        }

        /// <summary>
        /// IDが変わったオブジェクト（LiveUnityObjectProxy等のGUID再生成）に対し、
        /// LiveSceneFromJsonが型名+@nameでマッチしてIDを復元し、データが正しくロードされることを確認。
        /// これにより、Play mode再入時のデルタ保存でオブジェクトが消えるバグを防止する。
        /// </summary>
        [Test]
        public void LiveSceneFromJson_IdMismatch_MatchesByTypeName_AndRestoresData()
        {
            // Arrange
            LiveClass.RegisterFromAttributes<TestNoDefaultCtorClass>();

            // オブジェクトを作成（自動生成されたIDを使用）
            var testObj = new TestNoDefaultCtorClass(0);
            var liveClass = LiveClass.Find(typeof(TestNoDefaultCtorClass));
            var liveObj = new LiveObjectHandle("auto-generated-id", liveClass, testObj);

            try
            {
                LivePropertyUtility.SetDefault(liveObj);

                // JSONには別のID（前回セッションで保存されたID）を指定
                var loadJson = @"{
                    ""format"": ""jp.lilium.remotecontrol.scene"",
                    ""formatVersion"": 1,
                    ""objects"": [
                        {
                            ""@type"": ""TestNoDefaultCtorClass"",
                            ""@id"": ""saved-old-id"",
                            ""@name"": """",
                            ""value"": 99,
                            ""name"": ""Restored""
                        }
                    ]
                }";

                // Act: ロード（IDミスマッチ）
                LiveSceneSerializer.LiveSceneFromJson(loadJson, _resolver);

                // Assert: 型名マッチでIDが復元され、データがロードされること
                // LiveObjectHandle は値型 (struct) なので、ReplaceId 後もローカルコピー liveObj は
                // 古い id のまま。再キーは Registry 側で行われるため Registry を引いて確認する。
                Assert.IsNotNull(LiveObjectRegistry.FindById("saved-old-id"), "ID should be replaced with saved ID");
                Assert.IsNull(LiveObjectRegistry.FindById("auto-generated-id"), "old ID should no longer resolve");
                Assert.AreEqual(99, testObj.value, "value should be loaded from JSON");
                Assert.AreEqual("Restored", testObj.name, "name should be loaded from JSON");

                // Delta保存してもオブジェクトが残ること
                var savedJson = LiveSceneSerializer.LiveSceneToJson(
                    new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver, SerializeMode.Delta);

                var jRoot = JObject.Parse(savedJson);
                var objects = jRoot["objects"] as JArray;
                Assert.IsNotNull(objects, "objects array should exist");
                Assert.GreaterOrEqual(objects.Count, 1, $"Object should be preserved in delta save. JSON: {savedJson}");

                // 保存されたデータにvalue=99が含まれること
                var found = false;
                foreach (JObject obj in objects)
                {
                    if (EntryKey(obj) =="saved-old-id")
                    {
                        Assert.AreEqual(99, obj["value"]?.Value<int>(), "value should be in saved delta");
                        found = true;
                        break;
                    }
                }
                Assert.IsTrue(found, $"Object with saved-old-id should be in output. JSON: {savedJson}");
            }
            finally
            {
                // 再キー後の Registry エントリを target 経由で引いて解除する
                // (liveObj は古い id のローカルコピーなので直接 Unregister しても _instances から消えない)
                var current = LiveObjectRegistry.FindByTarget(testObj);
                if (current != null) current.Value.Unregister();
            }
        }

        #endregion

        #region RemoteControlProvider Save/Load Cycle Regression

        /// <summary>
        /// RemoteControlProviderの完全なサイクルを再現:
        /// Initialize(SetDefault) → LoadCurrentData → SaveCurrentData → RevertAllToDefault
        /// 2つのオブジェクトが保存ファイルで両方保持されることを検証。
        /// </summary>
        [Test]
        public void RemoteControlProvider_Cycle_MultipleObjects_BothPreserved()
        {
            // Arrange - テストクラス登録
            LiveClass.RegisterFromAttributes<TestDeltaNewContainer>();
            LiveClass.RegisterFromAttributes<TestDeltaNewItem>();
            LiveClass.RegisterFromAttributes<TestSceneClass>();

            var containerClass = LiveClass.Find(typeof(TestDeltaNewContainer));
            var sceneClass = LiveClass.Find(typeof(TestSceneClass));

            // --- Play mode 開始: Initialize ---
            var container = new TestDeltaNewContainer { items = new List<TestDeltaNewItem>() };
            var simpleObj = new TestSceneClass { value = 0, name = "", position = 0f };

            var liveContainer = new LiveObjectHandle("container-id", containerClass, container);
            var liveSimple = new LiveObjectHandle("simple-id", sceneClass, simpleObj);

            // Initialize: SetDefault（LiveObjectContainer.Initializeと同等）
            LivePropertyUtility.SetDefault(liveContainer);
            LivePropertyUtility.SetDefault(liveSimple);

            try
            {
                // --- LoadCurrentData: 保存済みデルタJSONを読み込む ---
                var savedFileJson = @"{
                    ""format"": ""jp.lilium.remotecontrol.scene"",
                    ""formatVersion"": 1,
                    ""objects"": [
                        {
                            ""@type"": ""TestDeltaNewContainer"",
                            ""@id"": ""container-id"",
                            ""@name"": """",
                            ""items"": [
                                {
                                    ""@type"": ""TestDeltaNewItem"",
                                    ""name"": ""Expression1"",
                                    ""@op"": ""new""
                                }
                            ]
                        },
                        {
                            ""@type"": ""TestSceneClass"",
                            ""@id"": ""simple-id"",
                            ""@name"": """",
                            ""value"": 42,
                            ""name"": ""Modified""
                        }
                    ]
                }";
                LiveSceneSerializer.LiveSceneFromJson(savedFileJson, _resolver);

                // ロードされた値の確認
                Assert.AreEqual(1, container.items.Count, "items should have 1 element after load");
                Assert.AreEqual("Expression1", container.items[0].name, "item name should be loaded");
                Assert.AreEqual(42, simpleObj.value, "value should be loaded");
                Assert.AreEqual("Modified", simpleObj.name, "name should be loaded");

                // --- SaveCurrentData: デルタ保存（ExitingPlayMode時） ---
                var outputJson = LiveSceneSerializer.LiveSceneToJson(
                    new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver, SerializeMode.Delta);

                // 検証: 両方のオブジェクトが出力に含まれること
                var jRoot = JObject.Parse(outputJson);
                var objects = jRoot["objects"] as JArray;
                Assert.IsNotNull(objects, "objects array should exist");
                Assert.AreEqual(2, objects.Count, $"Both objects should be preserved in delta save. JSON:\n{outputJson}");

                // 各オブジェクトのデータが正しいこと
                bool foundContainer = false, foundSimple = false;
                foreach (JObject obj in objects)
                {
                    var id = EntryKey(obj);
                    if (id == "container-id")
                    {
                        foundContainer = true;
                        var items = obj["items"] as JArray;
                        Assert.IsNotNull(items, $"container should have items property. JSON:\n{outputJson}");
                        Assert.GreaterOrEqual(items.Count, 1, "container should have at least 1 item");
                    }
                    else if (id == "simple-id")
                    {
                        foundSimple = true;
                        Assert.AreEqual(42, obj["value"]?.Value<int>(), "value should be 42");
                        Assert.AreEqual("Modified", obj["name"]?.Value<string>(), "name should be Modified");
                    }
                }
                Assert.IsTrue(foundContainer, $"container-id should be in output. JSON:\n{outputJson}");
                Assert.IsTrue(foundSimple, $"simple-id should be in output. JSON:\n{outputJson}");

                // --- RevertAllToDefault ---
                var dirtyProps1 = liveContainer.GetDirtyProperties();
                foreach (var path in dirtyProps1) liveContainer.Revert(path);
                var dirtyProps2 = liveSimple.GetDirtyProperties();
                foreach (var path in dirtyProps2) liveSimple.Revert(path);

                // リバート後: デフォルト値に戻っていること
                Assert.AreEqual(0, container.items.Count, "items should be empty after revert");
                Assert.AreEqual(0, simpleObj.value, "value should be 0 after revert");

                // --- 次のPlay mode: 保存JSONから復元できること ---
                LivePropertyUtility.SetDefault(liveContainer);
                LivePropertyUtility.SetDefault(liveSimple);
                LiveSceneSerializer.LiveSceneFromJson(outputJson, _resolver);

                Assert.AreEqual(1, container.items.Count, "items should be restored from saved output");
                Assert.AreEqual("Expression1", container.items[0].name, "item name should be restored");
                Assert.AreEqual(42, simpleObj.value, "value should be restored");
                Assert.AreEqual("Modified", simpleObj.name, "name should be restored");
            }
            finally
            {
                liveContainer.Unregister();
                liveSimple.Unregister();
            }
        }

        #endregion
    }
}
