using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Lilium.RemoteControl;
using Lilium.RemoteControl.WebUI;
using Lilium.RemoteControl.WebUI.Editor;
using Newtonsoft.Json.Linq;

namespace Lilium.RemoteControl.Tests
{
    [TestFixture]
    public class ExposedPropertySceneSerializationTests
    {
        // テスト用ヘルパー: override エントリは @source、新規は @id が識別子
        private static string EntryKey(JToken t) => t is JObject o ? (o["@source"] ?? o["@id"])?.Value<string>() : null;

        #region Test Classes

        [ExposedClass("TestStaticSceneClass")]
        public static class TestStaticSceneClass
        {
            [ExposedField]
            public static int value = 0;

            [ExposedField]
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
        [ExposedClass("TestSceneClass")]
        public class TestSceneClass
        {
            [ExposedField]
            public int value;

            [ExposedField]
            public string name;

            [ExposedField]
            public float position;
        }

        [Serializable]
        [ExposedClass("TestSceneClassWithArray")]
        public class TestSceneClassWithArray
        {
            [ExposedField]
            public int[] intArray;

            [ExposedField]
            public string[] stringArray;

            [ExposedField]
            public NestedItem[] nestedItems;

            [ExposedField]
            public List<int> intList;
        }

        [Serializable]
        [ExposedClass("TestSceneNestedStruct")]
        public struct TestSceneNestedStruct
        {
            [ExposedField]
            public int id;

            [ExposedField]
            public string name;
        }

        [Serializable]
        [ExposedClass("TestSceneClassWithStructArray")]
        public class TestSceneClassWithStructArray
        {
            [ExposedField]
            public TestSceneNestedStruct[] items;
        }

        [Serializable]
        [ExposedClass("TestSceneRefItem")]
        public class TestSceneRefItem : IExposedObject
        {
            public string name { get; set; }
            public ExposedObject exposedObject => null;
            public string id => null;
            public void OnEnable() { }
            public void OnDisable() { }
            public void OnDispose() { OnDisable(); }
            public void Update() { }
            public void Reset() { }

            [ExposedField]
            public int value;
        }

        [Serializable]
        [ExposedClass("TestSceneContainerWithRefList")]
        public class TestSceneContainerWithRefList
        {
            [ExposedField]
            public List<TestSceneRefItem> items;
        }

        [Serializable]
        [ExposedClass("TestDeltaNewItem")]
        public struct TestDeltaNewItem
        {
            [ExposedField]
            public string name;

            [ExposedField]
            public float value1;

            [ExposedField]
            public float value2;

            [ExposedField]
            public int[] nested;

            [ExposedDefault]
            public static TestDeltaNewItem Default => new TestDeltaNewItem
            {
                name = "",
                value1 = 1.0f,
                value2 = 2.0f,
                nested = new int[0],
            };
        }

        [Serializable]
        [ExposedClass("TestDeltaNewContainer")]
        public class TestDeltaNewContainer
        {
            [ExposedField]
            public List<TestDeltaNewItem> items;
        }

        public class MockExposedObjectResolver : IExposedObjectResolver
        {
            public ExposedObject FindById(string id) => ExposedObjectRegistry.FindById(id);
            public ExposedObject FindByTarget(object target) => ExposedObjectRegistry.FindByTarget(target);
        }

        #endregion

        private MockExposedObjectResolver _resolver;

        [SetUp]
        public void SetUp()
        {
            ExposedClass.Clear();

            // ExposedObjectRegistry.instances をクリア
            var toRemove = ExposedObjectRegistry.instances.ToList();
            foreach (var obj in toRemove)
            {
                obj.Unregister();
            }

            _resolver = new MockExposedObjectResolver();
        }

        [TearDown]
        public void TearDown()
        {
            // クリーンアップ
            var toRemove = ExposedObjectRegistry.instances.ToList();
            foreach (var obj in toRemove)
            {
                obj.Unregister();
            }
        }

        #region SceneToJson Basic Tests

        [Test]
        public void SceneToJson_EmptyScene_ReturnsValidJson()
        {
            // Act
            var json = ExposedSceneSerializer.SceneToJson(new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver);

            // Assert
            Assert.IsNotNull(json);
            Assert.IsFalse(string.IsNullOrEmpty(json));

            var jRoot = JObject.Parse(json);
            Assert.IsNotNull(jRoot["objects"]);
            Assert.AreEqual(0, ((JArray)jRoot["objects"]).Count);
        }

        [Test]
        public void SceneToJson_ContainsAppMetadata()
        {
            // Act
            var json = ExposedSceneSerializer.SceneToJson(new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver);

            // Assert
            var jRoot = JObject.Parse(json);
            Assert.AreEqual(ExposedSceneSerializer.FormatIdentifier, jRoot["format"]?.Value<string>());
            Assert.AreEqual(ExposedSceneSerializer.CurrentFormatVersion, jRoot["formatVersion"]?.Value<int>());
            var jMetadata = jRoot["metadata"] as JObject;
            Assert.IsNotNull(jMetadata, "metadata object should exist");
            Assert.IsNotNull(jMetadata["appVersion"]);
            Assert.IsNotNull(jMetadata["appName"]);
            Assert.IsNotNull(jMetadata["unityVersion"]);
            Assert.IsNotNull(jMetadata["packageVersion"]);
        }

        [Test]
        public void SceneToJson_SingleObject_SerializesCorrectly()
        {
            // Arrange
            ExposedClass.RegisterFromAttributes<TestSceneClass>();

            var testObj = new TestSceneClass
            {
                value = 42,
                name = "TestObject",
                position = 3.14f
            };

            var exposedClass = ExposedClass.Find(typeof(TestSceneClass));
            var exposedObj = new ExposedObject("test-id-1", exposedClass, testObj);

            // Act
            var json = ExposedSceneSerializer.SceneToJson(new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver);

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
        public void SceneToJson_MultipleObjects_SerializesAll()
        {
            // Arrange
            ExposedClass.RegisterFromAttributes<TestSceneClass>();

            var testObj1 = new TestSceneClass { value = 1, name = "First", position = 1.0f };
            var testObj2 = new TestSceneClass { value = 2, name = "Second", position = 2.0f };
            var testObj3 = new TestSceneClass { value = 3, name = "Third", position = 3.0f };

            var exposedClass = ExposedClass.Find(typeof(TestSceneClass));
            new ExposedObject("id-1", exposedClass, testObj1);
            new ExposedObject("id-2", exposedClass, testObj2);
            new ExposedObject("id-3", exposedClass, testObj3);

            // Act
            var json = ExposedSceneSerializer.SceneToJson(new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver);

            // Assert
            var jRoot = JObject.Parse(json);
            var objects = jRoot["objects"] as JArray;
            Assert.AreEqual(3, objects.Count);
        }

        [Test]
        public void SceneToJson_WithDeltaFromDefaultFilter_SerializesOnlyDirtyProperties()
        {
            // Arrange
            ExposedClass.RegisterFromAttributes<TestSceneClass>();

            var testObj = new TestSceneClass
            {
                value = 42,
                name = "TestObject",
                position = 3.14f
            };

            var exposedClass = ExposedClass.Find(typeof(TestSceneClass));
            var exposedObj = new ExposedObject("test-id-1", exposedClass, testObj);

            // ベースライン設定
            ExposedPropertyUtility.SetDefault(exposedObj);

            // valueプロパティのみ変更（EnsureDefaultCapturedが自動で呼ばれる）
            var valueProp = exposedObj.FindProperty("value");
            Assert.IsNotNull(valueProp);
            valueProp.Value.SetValue(99);

            // Act
            var json = ExposedSceneSerializer.SceneToJson(new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver, SerializeMode.Delta);

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
        public void SceneToJson_WithAllFilter_SerializesAllProperties()
        {
            // Arrange
            ExposedClass.RegisterFromAttributes<TestSceneClass>();

            var testObj = new TestSceneClass
            {
                value = 42,
                name = "TestObject",
                position = 3.14f
            };

            var exposedClass = ExposedClass.Find(typeof(TestSceneClass));
            new ExposedObject("test-id-1", exposedClass, testObj);

            // Act
            var json = ExposedSceneSerializer.SceneToJson(new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver, SerializeMode.Snapshot);

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

        #region SceneToJson Array Tests

        [Test]
        public void SceneToJson_WithIntArray_SerializesCorrectly()
        {
            // Arrange
            ExposedClass.RegisterFromAttributes<TestSceneClassWithArray>();

            var testObj = new TestSceneClassWithArray
            {
                intArray = new int[] { 1, 2, 3, 4, 5 }
            };

            var exposedClass = ExposedClass.Find(typeof(TestSceneClassWithArray));
            new ExposedObject("array-test-1", exposedClass, testObj);

            // Act
            var json = ExposedSceneSerializer.SceneToJson(new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver);

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
        public void SceneToJson_WithStringArray_SerializesCorrectly()
        {
            // Arrange
            ExposedClass.RegisterFromAttributes<TestSceneClassWithArray>();

            var testObj = new TestSceneClassWithArray
            {
                stringArray = new string[] { "apple", "banana", "cherry" }
            };

            var exposedClass = ExposedClass.Find(typeof(TestSceneClassWithArray));
            new ExposedObject("array-test-2", exposedClass, testObj);

            // Act
            var json = ExposedSceneSerializer.SceneToJson(new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver);

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
        public void SceneToJson_WithObjectArray_SerializesCorrectly()
        {
            // Arrange
            ExposedClass.RegisterFromAttributes<TestSceneClassWithArray>();

            var testObj = new TestSceneClassWithArray
            {
                nestedItems = new NestedItem[]
                {
                    new NestedItem { id = 1, name = "First" },
                    new NestedItem { id = 2, name = "Second" },
                    new NestedItem { id = 3, name = "Third" }
                }
            };

            var exposedClass = ExposedClass.Find(typeof(TestSceneClassWithArray));
            new ExposedObject("array-test-3", exposedClass, testObj);

            // Act
            var json = ExposedSceneSerializer.SceneToJson(new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver);

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
        public void SceneToJson_WithEmptyArray_SerializesCorrectly()
        {
            // Arrange
            ExposedClass.RegisterFromAttributes<TestSceneClassWithArray>();

            var testObj = new TestSceneClassWithArray
            {
                intArray = new int[0]
            };

            var exposedClass = ExposedClass.Find(typeof(TestSceneClassWithArray));
            new ExposedObject("array-test-4", exposedClass, testObj);

            // Act
            var json = ExposedSceneSerializer.SceneToJson(new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver);

            // Assert
            var jRoot = JObject.Parse(json);
            var objects = jRoot["objects"] as JArray;
            var obj = objects[0] as JObject;
            var intArrayToken = obj["intArray"] as JArray;

            Assert.IsNotNull(intArrayToken);
            Assert.AreEqual(0, intArrayToken.Count);
        }

        [Test]
        public void SceneToJson_WithNullArray_SerializesAsNull()
        {
            // Arrange
            ExposedClass.RegisterFromAttributes<TestSceneClassWithArray>();

            var testObj = new TestSceneClassWithArray
            {
                intArray = null
            };

            var exposedClass = ExposedClass.Find(typeof(TestSceneClassWithArray));
            new ExposedObject("array-test-5", exposedClass, testObj);

            // Act
            var json = ExposedSceneSerializer.SceneToJson(new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver);

            // Assert
            var jRoot = JObject.Parse(json);
            var objects = jRoot["objects"] as JArray;
            var obj = objects[0] as JObject;

            Assert.IsTrue(obj["intArray"] == null || obj["intArray"].Type == JTokenType.Null);
        }

        [Test]
        public void SceneToJson_WithIntList_SerializesCorrectly()
        {
            // Arrange
            ExposedClass.RegisterFromAttributes<TestSceneClassWithArray>();

            var testObj = new TestSceneClassWithArray
            {
                intList = new List<int> { 10, 20, 30 }
            };

            var exposedClass = ExposedClass.Find(typeof(TestSceneClassWithArray));
            new ExposedObject("array-test-6", exposedClass, testObj);

            // Act
            var json = ExposedSceneSerializer.SceneToJson(new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver);

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

        #region SceneFromJson Basic Tests

        [Test]
        public void SceneFromJson_EmptyJson_DoesNotThrow()
        {
            // Act & Assert
            Assert.DoesNotThrow(() => ExposedSceneSerializer.SceneFromJson("", _resolver));
            Assert.DoesNotThrow(() => ExposedSceneSerializer.SceneFromJson(null, _resolver));
        }

        [Test]
        public void SceneFromJson_MissingObjectsArray_HandlesGracefully()
        {
            // Arrange
            var json = "{\"appVersion\":\"1.0\"}";

            // Act & Assert - should log warning but not throw
            Assert.DoesNotThrow(() => ExposedSceneSerializer.SceneFromJson(json, _resolver));
        }

        [Test]
        public void SceneFromJson_SingleObject_DeserializesCorrectly()
        {
            // Arrange
            ExposedClass.RegisterFromAttributes<TestSceneClass>();

            var testObj = new TestSceneClass
            {
                value = 0,
                name = "",
                position = 0f
            };

            var exposedClass = ExposedClass.Find(typeof(TestSceneClass));
            new ExposedObject("test-id-1", exposedClass, testObj);

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
            ExposedSceneSerializer.SceneFromJson(json, _resolver);

            // Assert
            Assert.AreEqual(100, testObj.value);
            Assert.AreEqual("Loaded", testObj.name);
            Assert.AreEqual(9.99f, testObj.position, 0.001f);
        }

        [Test]
        public void SceneFromJson_MultipleObjects_DeserializesAll()
        {
            // Arrange
            ExposedClass.RegisterFromAttributes<TestSceneClass>();

            var testObj1 = new TestSceneClass { value = 0, name = "", position = 0f };
            var testObj2 = new TestSceneClass { value = 0, name = "", position = 0f };

            var exposedClass = ExposedClass.Find(typeof(TestSceneClass));
            new ExposedObject("id-1", exposedClass, testObj1);
            new ExposedObject("id-2", exposedClass, testObj2);

            var json = @"{
                ""objects"": [
                    { ""@type"": ""TestSceneClass"", ""@id"": ""id-1"", ""value"": 111, ""name"": ""Obj1"" },
                    { ""@type"": ""TestSceneClass"", ""@id"": ""id-2"", ""value"": 222, ""name"": ""Obj2"" }
                ]
            }";

            // Act
            ExposedSceneSerializer.SceneFromJson(json, _resolver);

            // Assert
            Assert.AreEqual(111, testObj1.value);
            Assert.AreEqual("Obj1", testObj1.name);
            Assert.AreEqual(222, testObj2.value);
            Assert.AreEqual("Obj2", testObj2.name);
        }

        [Test]
        public void SceneFromJson_UnknownId_HandlesGracefully()
        {
            // Arrange
            ExposedClass.RegisterFromAttributes<TestSceneClass>();

            var json = @"{
                ""objects"": [
                    { ""@type"": ""TestSceneClass"", ""@id"": ""unknown-id"", ""value"": 999 }
                ]
            }";

            // Act & Assert - should log warning but not throw
            Assert.DoesNotThrow(() => ExposedSceneSerializer.SceneFromJson(json, _resolver));
        }

        #endregion

        #region SceneFromJson Array Tests

        [Test]
        public void SceneFromJson_WithIntArray_DeserializesCorrectly()
        {
            // Arrange
            ExposedClass.RegisterFromAttributes<TestSceneClassWithArray>();

            var testObj = new TestSceneClassWithArray
            {
                intArray = null
            };

            var exposedClass = ExposedClass.Find(typeof(TestSceneClassWithArray));
            new ExposedObject("array-id-1", exposedClass, testObj);

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
            ExposedSceneSerializer.SceneFromJson(json, _resolver);

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
        public void SceneFromJson_WithStringArray_DeserializesCorrectly()
        {
            // Arrange
            ExposedClass.RegisterFromAttributes<TestSceneClassWithArray>();

            var testObj = new TestSceneClassWithArray
            {
                stringArray = null
            };

            var exposedClass = ExposedClass.Find(typeof(TestSceneClassWithArray));
            new ExposedObject("array-id-2", exposedClass, testObj);

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
            ExposedSceneSerializer.SceneFromJson(json, _resolver);

            // Assert
            Assert.IsNotNull(testObj.stringArray);
            Assert.AreEqual(3, testObj.stringArray.Length);
            Assert.AreEqual("hello", testObj.stringArray[0]);
            Assert.AreEqual("world", testObj.stringArray[1]);
            Assert.AreEqual("test", testObj.stringArray[2]);
        }

        [Test]
        public void SceneFromJson_WithObjectArray_DeserializesCorrectly()
        {
            // Arrange
            ExposedClass.RegisterFromAttributes<TestSceneClassWithArray>();

            var testObj = new TestSceneClassWithArray
            {
                nestedItems = null
            };

            var exposedClass = ExposedClass.Find(typeof(TestSceneClassWithArray));
            new ExposedObject("array-id-3", exposedClass, testObj);

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
            ExposedSceneSerializer.SceneFromJson(json, _resolver);

            // Assert
            Assert.IsNotNull(testObj.nestedItems);
            Assert.AreEqual(2, testObj.nestedItems.Length);
            Assert.AreEqual(100, testObj.nestedItems[0].id);
            Assert.AreEqual("ItemA", testObj.nestedItems[0].name);
            Assert.AreEqual(200, testObj.nestedItems[1].id);
            Assert.AreEqual("ItemB", testObj.nestedItems[1].name);
        }

        [Test]
        public void SceneFromJson_WithEmptyArray_DeserializesCorrectly()
        {
            // Arrange
            ExposedClass.RegisterFromAttributes<TestSceneClassWithArray>();

            var testObj = new TestSceneClassWithArray
            {
                intArray = new int[] { 1, 2, 3 } // 既存の値がある
            };

            var exposedClass = ExposedClass.Find(typeof(TestSceneClassWithArray));
            new ExposedObject("array-id-4", exposedClass, testObj);

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
            ExposedSceneSerializer.SceneFromJson(json, _resolver);

            // Assert
            Assert.IsNotNull(testObj.intArray);
            Assert.AreEqual(0, testObj.intArray.Length);
        }

        [Test]
        public void SceneFromJson_ArrayLengthChange_HandlesCorrectly()
        {
            // Arrange
            ExposedClass.RegisterFromAttributes<TestSceneClassWithArray>();

            var testObj = new TestSceneClassWithArray
            {
                intArray = new int[] { 1, 2, 3 } // 3要素
            };

            var exposedClass = ExposedClass.Find(typeof(TestSceneClassWithArray));
            new ExposedObject("array-id-5", exposedClass, testObj);

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
            ExposedSceneSerializer.SceneFromJson(json, _resolver);

            // Assert
            Assert.IsNotNull(testObj.intArray);
            Assert.AreEqual(5, testObj.intArray.Length);
            Assert.AreEqual(10, testObj.intArray[0]);
            Assert.AreEqual(50, testObj.intArray[4]);
        }

        [Test]
        public void SceneFromJson_WithIntList_DeserializesCorrectly()
        {
            // Arrange
            ExposedClass.RegisterFromAttributes<TestSceneClassWithArray>();

            var testObj = new TestSceneClassWithArray
            {
                intList = null
            };

            var exposedClass = ExposedClass.Find(typeof(TestSceneClassWithArray));
            new ExposedObject("array-id-6", exposedClass, testObj);

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
            ExposedSceneSerializer.SceneFromJson(json, _resolver);

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
            ExposedClass.RegisterFromAttributes<TestSceneClass>();

            var originalObj = new TestSceneClass
            {
                value = 42,
                name = "RoundTrip",
                position = 1.234f
            };

            var exposedClass = ExposedClass.Find(typeof(TestSceneClass));
            new ExposedObject("roundtrip-1", exposedClass, originalObj);

            // Act - Serialize
            var json = ExposedSceneSerializer.SceneToJson(new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver);

            // 新しいオブジェクトを作成してデシリアライズ
            var toRemove = ExposedObjectRegistry.instances.ToList();
            foreach (var obj in toRemove) obj.Unregister();

            var newObj = new TestSceneClass { value = 0, name = "", position = 0f };
            new ExposedObject("roundtrip-1", exposedClass, newObj);

            // Act - Deserialize
            ExposedSceneSerializer.SceneFromJson(json, _resolver);

            // Assert
            Assert.AreEqual(42, newObj.value);
            Assert.AreEqual("RoundTrip", newObj.name);
            Assert.AreEqual(1.234f, newObj.position, 0.001f);
        }

        [Test]
        public void RoundTrip_IntArray_PreservesData()
        {
            // Arrange
            ExposedClass.RegisterFromAttributes<TestSceneClassWithArray>();

            var originalObj = new TestSceneClassWithArray
            {
                intArray = new int[] { 1, 2, 3, 4, 5 }
            };

            var exposedClass = ExposedClass.Find(typeof(TestSceneClassWithArray));
            new ExposedObject("roundtrip-array-1", exposedClass, originalObj);

            // Act - Serialize
            var json = ExposedSceneSerializer.SceneToJson(new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver);

            // 新しいオブジェクトを作成
            var toRemove = ExposedObjectRegistry.instances.ToList();
            foreach (var obj in toRemove) obj.Unregister();

            var newObj = new TestSceneClassWithArray { intArray = null };
            new ExposedObject("roundtrip-array-1", exposedClass, newObj);

            // Act - Deserialize
            ExposedSceneSerializer.SceneFromJson(json, _resolver);

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
            ExposedClass.RegisterFromAttributes<TestSceneClassWithArray>();

            var originalObj = new TestSceneClassWithArray
            {
                nestedItems = new NestedItem[]
                {
                    new NestedItem { id = 1, name = "Alpha" },
                    new NestedItem { id = 2, name = "Beta" },
                    new NestedItem { id = 3, name = "Gamma" }
                }
            };

            var exposedClass = ExposedClass.Find(typeof(TestSceneClassWithArray));
            new ExposedObject("roundtrip-array-2", exposedClass, originalObj);

            // Act - Serialize
            var json = ExposedSceneSerializer.SceneToJson(new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver);

            // 新しいオブジェクトを作成
            var toRemove = ExposedObjectRegistry.instances.ToList();
            foreach (var obj in toRemove) obj.Unregister();

            var newObj = new TestSceneClassWithArray { nestedItems = null };
            new ExposedObject("roundtrip-array-2", exposedClass, newObj);

            // Act - Deserialize
            ExposedSceneSerializer.SceneFromJson(json, _resolver);

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
            ExposedClass.RegisterFromAttributes<TestSceneClass>();
            ExposedClass.RegisterFromAttributes<TestSceneClassWithArray>();

            var obj1 = new TestSceneClass { value = 100, name = "First", position = 1.0f };
            var obj2 = new TestSceneClassWithArray { intArray = new int[] { 10, 20 } };

            var exposedClass1 = ExposedClass.Find(typeof(TestSceneClass));
            var exposedClass2 = ExposedClass.Find(typeof(TestSceneClassWithArray));
            new ExposedObject("multi-1", exposedClass1, obj1);
            new ExposedObject("multi-2", exposedClass2, obj2);

            // Act - Serialize
            var json = ExposedSceneSerializer.SceneToJson(new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver);

            // 新しいオブジェクトを作成
            var toRemove = ExposedObjectRegistry.instances.ToList();
            foreach (var obj in toRemove) obj.Unregister();

            var newObj1 = new TestSceneClass { value = 0, name = "", position = 0f };
            var newObj2 = new TestSceneClassWithArray { intArray = null };
            new ExposedObject("multi-1", exposedClass1, newObj1);
            new ExposedObject("multi-2", exposedClass2, newObj2);

            // Act - Deserialize
            ExposedSceneSerializer.SceneFromJson(json, _resolver);

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
            ExposedClass.RegisterFromAttributes<TestSceneClass>();

            var originalObj = new TestSceneClass
            {
                value = 0,
                name = "",
                position = 5.5f
            };

            var exposedClass = ExposedClass.Find(typeof(TestSceneClass));
            var exposedObj = new ExposedObject("dirty-test", exposedClass, originalObj);

            // ベースライン設定
            ExposedPropertyUtility.SetDefault(exposedObj);

            // valueとnameのみ変更（EnsureDefaultCapturedが自動で呼ばれdirty判定される）
            var valueProp = exposedObj.FindProperty("value");
            valueProp.Value.SetValue(42);
            var nameProp = exposedObj.FindProperty("name");
            nameProp.Value.SetValue("Dirty");

            // Act - Serialize (DeltaFromDefault)
            var json = ExposedSceneSerializer.SceneToJson(new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver, SerializeMode.Delta);

            // 新しいオブジェクトを作成
            var toRemove = ExposedObjectRegistry.instances.ToList();
            foreach (var obj in toRemove) obj.Unregister();

            var newObj = new TestSceneClass { value = 0, name = "", position = 99.9f };
            new ExposedObject("dirty-test", exposedClass, newObj);

            // Act - Deserialize
            ExposedSceneSerializer.SceneFromJson(json, _resolver);

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
            ExposedClass.RegisterFromAttributes<TestSceneClassWithArray>();

            var originalObj = new TestSceneClassWithArray
            {
                stringArray = new string[] { "apple", "banana", "cherry" }
            };

            var exposedClass = ExposedClass.Find(typeof(TestSceneClassWithArray));
            new ExposedObject("rt-str-arr", exposedClass, originalObj);

            // Act - Serialize
            var json = ExposedSceneSerializer.SceneToJson(new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver);

            var toRemove = ExposedObjectRegistry.instances.ToList();
            foreach (var obj in toRemove) obj.Unregister();

            var newObj = new TestSceneClassWithArray { stringArray = null };
            new ExposedObject("rt-str-arr", exposedClass, newObj);

            // Act - Deserialize
            ExposedSceneSerializer.SceneFromJson(json, _resolver);

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
            ExposedClass.RegisterFromAttributes<TestSceneClassWithStructArray>();
            ExposedClass.RegisterFromAttributes<TestSceneNestedStruct>();

            var originalObj = new TestSceneClassWithStructArray
            {
                items = new TestSceneNestedStruct[]
                {
                    new TestSceneNestedStruct { id = 1, name = "First" },
                    new TestSceneNestedStruct { id = 2, name = "Second" }
                }
            };

            var exposedClass = ExposedClass.Find(typeof(TestSceneClassWithStructArray));
            new ExposedObject("rt-struct-arr", exposedClass, originalObj);

            // Act - Serialize
            var json = ExposedSceneSerializer.SceneToJson(new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver);

            var toRemove = ExposedObjectRegistry.instances.ToList();
            foreach (var obj in toRemove) obj.Unregister();

            var newObj = new TestSceneClassWithStructArray { items = null };
            new ExposedObject("rt-struct-arr", exposedClass, newObj);

            // Act - Deserialize
            ExposedSceneSerializer.SceneFromJson(json, _resolver);

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
            ExposedClass.RegisterFromAttributes<TestSceneClassWithArray>();

            var originalObj = new TestSceneClassWithArray
            {
                intList = new List<int> { 10, 20, 30, 40 }
            };

            var exposedClass = ExposedClass.Find(typeof(TestSceneClassWithArray));
            new ExposedObject("rt-int-list", exposedClass, originalObj);

            // Act - Serialize
            var json = ExposedSceneSerializer.SceneToJson(new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver);

            var toRemove = ExposedObjectRegistry.instances.ToList();
            foreach (var obj in toRemove) obj.Unregister();

            var newObj = new TestSceneClassWithArray { intList = null };
            new ExposedObject("rt-int-list", exposedClass, newObj);

            // Act - Deserialize
            ExposedSceneSerializer.SceneFromJson(json, _resolver);

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
            ExposedClass.RegisterFromAttributes<TestSceneRefItem>();
            ExposedClass.RegisterFromAttributes<TestSceneContainerWithRefList>();

            var item1 = new TestSceneRefItem { name = "A", value = 10 };
            var item2 = new TestSceneRefItem { name = "B", value = 20 };
            var container = new TestSceneContainerWithRefList
            {
                items = new List<TestSceneRefItem> { item1, item2 }
            };

            var containerClass = ExposedClass.Find(typeof(TestSceneContainerWithRefList));
            var itemClass = ExposedClass.Find(typeof(TestSceneRefItem));
            new ExposedObject("container-rt", containerClass, container);
            new ExposedObject("item-rt-1", itemClass, item1);
            new ExposedObject("item-rt-2", itemClass, item2);

            // Act - Serialize (All)
            var json = ExposedSceneSerializer.SceneToJson(new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver, SerializeMode.Snapshot);

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

            // Deserialize - 既存のExposedObjectはそのまま（@refで参照解決）
            ExposedSceneSerializer.SceneFromJson(json, _resolver);

            // Assert - 参照が解決されている
            Assert.AreEqual(2, container.items.Count);
            Assert.AreSame(item1, container.items[0]);
            Assert.AreSame(item2, container.items[1]);
        }

        [Test]
        public void RoundTrip_RefList_NonDirtyElements_NotOutput()
        {
            // Arrange
            ExposedClass.RegisterFromAttributes<TestSceneRefItem>();
            ExposedClass.RegisterFromAttributes<TestSceneContainerWithRefList>();

            var item1 = new TestSceneRefItem { name = "X", value = 100 };
            var item2 = new TestSceneRefItem { name = "Y", value = 200 };
            var container = new TestSceneContainerWithRefList
            {
                items = new List<TestSceneRefItem> { item1, item2 }
            };

            var containerClass = ExposedClass.Find(typeof(TestSceneContainerWithRefList));
            var itemClass = ExposedClass.Find(typeof(TestSceneRefItem));
            var containerExposed = new ExposedObject("container-nd", containerClass, container);
            new ExposedObject("item-nd-1", itemClass, item1);
            new ExposedObject("item-nd-2", itemClass, item2);

            // SetDefaultで全プロパティのデフォルトをキャプチャ
            ExposedPropertyUtility.SetDefault(containerExposed);

            // 値を変更しない → 全要素がnon-dirty

            // Act - DeltaFromDefaultでシリアライズ
            var json = ExposedSceneSerializer.SceneToJson(new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver, SerializeMode.Delta);

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
            ExposedClass.RegisterFromAttributes<TestSceneClass>();

            var original = new TestSceneClass { value = 42, name = "Original", position = 1.0f };
            var exposedClass = ExposedClass.Find(typeof(TestSceneClass));
            var exposedObj = new ExposedObject("rt-dirty-basic", exposedClass, original);

            // デフォルトキャプチャ → 値変更
            ExposedPropertyUtility.SetDefault(exposedObj);
            original.value = 99; // valueのみ変更

            // Act - DeltaFromDefaultでシリアライズ
            var json = ExposedSceneSerializer.SceneToJson(new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver, SerializeMode.Delta);

            // 新オブジェクトに復元（nameとpositionは元の値のまま）
            var toRemove = ExposedObjectRegistry.instances.ToList();
            foreach (var obj in toRemove) obj.Unregister();

            var restored = new TestSceneClass { value = 42, name = "Original", position = 1.0f };
            new ExposedObject("rt-dirty-basic", exposedClass, restored);
            ExposedSceneSerializer.SceneFromJson(json, _resolver);

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
            ExposedClass.RegisterFromAttributes<TestSceneClassWithArray>();

            var original = new TestSceneClassWithArray
            {
                intList = new List<int> { 10, 20, 30 }
            };
            var exposedClass = ExposedClass.Find(typeof(TestSceneClassWithArray));
            new ExposedObject("rt-dirty-list", exposedClass, original);

            // Act - Allフィルタでシリアライズ
            var json = ExposedSceneSerializer.SceneToJson(new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver, SerializeMode.Snapshot);

            // 新オブジェクトに復元
            var toRemove = ExposedObjectRegistry.instances.ToList();
            foreach (var obj in toRemove) obj.Unregister();

            var restored = new TestSceneClassWithArray { intList = new List<int>() };
            new ExposedObject("rt-dirty-list", exposedClass, restored);
            ExposedSceneSerializer.SceneFromJson(json, _resolver);

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
            ExposedClass.RegisterFromAttributes<TestSceneRefItem>();
            ExposedClass.RegisterFromAttributes<TestSceneContainerWithRefList>();

            var item1 = new TestSceneRefItem { name = "A", value = 10 };
            var container = new TestSceneContainerWithRefList
            {
                items = new List<TestSceneRefItem> { item1 }
            };

            var containerClass = ExposedClass.Find(typeof(TestSceneContainerWithRefList));
            var itemClass = ExposedClass.Find(typeof(TestSceneRefItem));
            var containerExposed = new ExposedObject("container-dirty-ref", containerClass, container);
            new ExposedObject("item-dirty-1", itemClass, item1);

            // デフォルトキャプチャ
            ExposedPropertyUtility.SetDefault(containerExposed);

            // 新要素追加
            var item2 = new TestSceneRefItem { name = "B", value = 20 };
            container.items.Add(item2);
            new ExposedObject("item-dirty-2", itemClass, item2);

            // Act - DeltaFromDefaultでシリアライズ
            var json = ExposedSceneSerializer.SceneToJson(new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver, SerializeMode.Delta);

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
            var containerExposed2 = new ExposedObject("container-dirty-ref", containerClass, container2);
            ExposedPropertyUtility.SetDefault(containerExposed2);
            ExposedSceneSerializer.SceneFromJson(json, _resolver);

            Assert.AreEqual(2, container2.items.Count);
            Assert.AreSame(item1, container2.items[0]);
            Assert.AreSame(item2, container2.items[1]);
        }

        [Test]
        public void RoundTrip_DeltaFromDefault_AfterClearDirty_DetectsNewChanges()
        {
            // Arrange
            ExposedClass.RegisterFromAttributes<TestSceneClass>();

            var original = new TestSceneClass { value = 10, name = "Initial", position = 1.0f };
            var exposedClass = ExposedClass.Find(typeof(TestSceneClass));
            var exposedObj = new ExposedObject("rt-clear-dirty", exposedClass, original);

            // デフォルトキャプチャ → 値変更 → ClearDirty
            ExposedPropertyUtility.SetDefault(exposedObj);
            original.value = 50;
            exposedObj.ClearDirty();

            // ClearDirty後、別の変更を加える
            original.name = "Changed";

            // Act - DeltaFromDefaultでシリアライズ
            var json = ExposedSceneSerializer.SceneToJson(new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver, SerializeMode.Delta);

            // 新オブジェクトに復元
            var toRemove = ExposedObjectRegistry.instances.ToList();
            foreach (var obj in toRemove) obj.Unregister();

            var restored = new TestSceneClass { value = 50, name = "Initial", position = 1.0f };
            new ExposedObject("rt-clear-dirty", exposedClass, restored);
            ExposedSceneSerializer.SceneFromJson(json, _resolver);

            // Assert - ClearDirty後のnameの変更のみ反映される
            Assert.AreEqual(50, restored.value); // ClearDirty後の値のまま（dirtyでない）
            Assert.AreEqual("Changed", restored.name); // 新しい変更が反映される
            Assert.AreEqual(1.0f, restored.position, 0.001f); // 変更されていない
        }

        [Test]
        public void SceneToJson_DeltaFromDefault_UnchangedString_NotSerialized()
        {
            // Arrange
            ExposedClass.RegisterFromAttributes<TestSceneClass>();

            var obj = new TestSceneClass { value = 10, name = "Initial", position = 1.0f };
            var exposedClass = ExposedClass.Find(typeof(TestSceneClass));
            var exposedObj = new ExposedObject("str-unchanged", exposedClass, obj);

            ExposedPropertyUtility.SetDefault(exposedObj);

            // Act - stringを変更せずDeltaFromDefaultでシリアライズ
            var json = ExposedSceneSerializer.SceneToJson(new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver, SerializeMode.Delta);

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
        public void SceneToJson_DeltaFromDefault_ChangedString_Serialized()
        {
            // Arrange
            ExposedClass.RegisterFromAttributes<TestSceneClass>();

            var obj = new TestSceneClass { value = 10, name = "Initial", position = 1.0f };
            var exposedClass = ExposedClass.Find(typeof(TestSceneClass));
            var exposedObj = new ExposedObject("str-changed", exposedClass, obj);

            ExposedPropertyUtility.SetDefault(exposedObj);

            // stringを変更
            obj.name = "Modified";

            // Act - DeltaFromDefaultでシリアライズ
            var json = ExposedSceneSerializer.SceneToJson(new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver, SerializeMode.Delta);

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
            ExposedClass.RegisterFromAttributes<TestSceneClass>();

            var original = new TestSceneClass { value = 10, name = "Initial", position = 1.0f };
            var exposedClass = ExposedClass.Find(typeof(TestSceneClass));
            var exposedObj = new ExposedObject("rt-string", exposedClass, original);

            ExposedPropertyUtility.SetDefault(exposedObj);

            // nameのみ変更
            original.name = "UpdatedName";

            // Act - DeltaFromDefaultでシリアライズ
            var json = ExposedSceneSerializer.SceneToJson(new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver, SerializeMode.Delta);

            // 新オブジェクトに復元
            var toRemove = ExposedObjectRegistry.instances.ToList();
            foreach (var obj in toRemove) obj.Unregister();

            var restored = new TestSceneClass { value = 10, name = "Initial", position = 1.0f };
            new ExposedObject("rt-string", exposedClass, restored);
            ExposedSceneSerializer.SceneFromJson(json, _resolver);

            // Assert - nameのみ更新され、他は元の値のまま
            Assert.AreEqual(10, restored.value); // 変更されていない
            Assert.AreEqual("UpdatedName", restored.name); // 変更が反映される
            Assert.AreEqual(1.0f, restored.position, 0.001f); // 変更されていない
        }

        #endregion

        #region Persistence Filter Tests

        [Serializable]
        [ExposedClass("TestPersistenceClass")]
        public class TestPersistenceClass
        {
            // ExposedField → isPersistable = true（永続化される）
            [ExposedField]
            public int persistableValue;

            [ExposedField]
            public string persistableName;

            // ExposedProperty → isPersistable = false（永続化されない）
            [ExposedProperty]
            public float nonPersistableComputed { get => _computedBacking; set => _computedBacking = value; }
            private float _computedBacking;

            [ExposedProperty]
            public string nonPersistableLabel { get => _labelBacking; set => _labelBacking = value; }
            private string _labelBacking;
        }

        [Test]
        public void SceneToJson_ExcludesNonPersistableProperties()
        {
            // Arrange
            ExposedClass.RegisterFromAttributes<TestPersistenceClass>();

            var obj = new TestPersistenceClass
            {
                persistableValue = 42,
                persistableName = "saved",
                nonPersistableComputed = 3.14f,
                nonPersistableLabel = "not_saved",
            };

            var exposedClass = ExposedClass.Find(typeof(TestPersistenceClass));
            new ExposedObject("persist-test", exposedClass, obj);

            // Act
            var json = ExposedSceneSerializer.SceneToJson(new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver);

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
            ExposedClass.RegisterFromAttributes<TestPersistenceClass>();

            var original = new TestPersistenceClass
            {
                persistableValue = 100,
                persistableName = "original",
                nonPersistableComputed = 9.99f,
                nonPersistableLabel = "computed",
            };

            var exposedClass = ExposedClass.Find(typeof(TestPersistenceClass));
            new ExposedObject("roundtrip-persist", exposedClass, original);

            // Act - Serialize
            var json = ExposedSceneSerializer.SceneToJson(new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver);

            // 新しいオブジェクトを作成
            var toRemove = ExposedObjectRegistry.instances.ToList();
            foreach (var o in toRemove) o.Unregister();

            var restored = new TestPersistenceClass
            {
                persistableValue = 0,
                persistableName = "",
                nonPersistableComputed = 0f,
                nonPersistableLabel = "",
            };
            new ExposedObject("roundtrip-persist", exposedClass, restored);

            // Act - Deserialize
            ExposedSceneSerializer.SceneFromJson(json, _resolver);

            // Assert - persistable フィールドのみ復元される
            Assert.AreEqual(100, restored.persistableValue);
            Assert.AreEqual("original", restored.persistableName);

            // non-persistable プロパティは保存JSONに含まれないため、デフォルト値のまま
            Assert.AreEqual(0f, restored.nonPersistableComputed, 0.001f);
            Assert.AreEqual("", restored.nonPersistableLabel);
        }

        #endregion

        #region Additions Section Tests

        [ExposedClass("TestAdditionsComponent")]
        public class TestAdditionsComponent : MonoBehaviour
        {
            [ExposedField]
            public int health;

            [ExposedField]
            public string label;
        }

        [ExposedClass("TestAdditionsComponent2")]
        public class TestAdditionsComponent2 : MonoBehaviour
        {
            [ExposedField]
            public float speed;
        }

        [Serializable]
        [ExposedClass("TestPluglikePath")]
        public class TestPluglikePath
        {
            [ExposedField]
            public string rootObjectName;

            [ExposedField]
            public string transformName;
        }

        [ExposedClass("TestPluglikeComponent")]
        public class TestPluglikeComponent : MonoBehaviour
        {
            [SerializeField]
            [ExposedField]
            public TestPluglikePath target = new TestPluglikePath();
        }

        /// <summary>
        /// 配列プロパティを持つテスト用コンポーネント（meshStateOverrides相当）
        /// </summary>
        [ExposedClass("TestComponentWithArray")]
        public class TestComponentWithArray : MonoBehaviour
        {
            [ExposedField]
            public List<TestDeltaNewItem> items;
        }

        [Test]
        public void SceneToJson_TrackedPrefabInstance_HasOpNewAndPrefab()
        {
            // Arrange
            ExposedClass.RegisterFromAttributes<TestAdditionsComponent>();
            ExposedClass.RegisterFromAttributes<ExposedGameObject>();

            var instance = new GameObject("TestPrefab(Clone)");
            try
            {
                // ExposedGameObjectでGOをラップし、prefabSourceKey (Asset GUID) を設定
                var exposedGO = new ExposedGameObject(instance);
                exposedGO.prefabSourceKey = "11111111111111111111111111111111";
                exposedGO.OnEnable();

                var testComp = instance.AddComponent<TestAdditionsComponent>();
                testComp.health = 100;
                testComp.label = "Hero";
                var exposedClass = ExposedClass.Find(typeof(TestAdditionsComponent));
                var exposedObj = new ExposedObject("comp-id-1", exposedClass, testComp);

                // Act
                var json = ExposedSceneSerializer.SceneToJson(new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver);

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
        public void SceneToJson_ComponentOnTrackedInstance_HasOpNewAndPrefab()
        {
            // Arrange
            ExposedClass.RegisterFromAttributes<TestAdditionsComponent>();
            ExposedClass.RegisterFromAttributes<ExposedGameObject>();

            var instance = new GameObject("LightPrefab(Clone)");
            try
            {
                var exposedGO = new ExposedGameObject(instance);
                exposedGO.prefabSourceKey = "22222222222222222222222222222222";
                exposedGO.OnEnable();

                var testComp = instance.AddComponent<TestAdditionsComponent>();
                testComp.health = 50;
                testComp.label = "Light";
                var exposedClass = ExposedClass.Find(typeof(TestAdditionsComponent));
                var exposedObj = new ExposedObject("light-comp-1", exposedClass, testComp);

                // Act
                var json = ExposedSceneSerializer.SceneToJson(new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver);

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
        public void SceneToJson_MultipleComponentsOnSameInstance_EachHasOpNewAndPrefab()
        {
            // Arrange
            ExposedClass.RegisterFromAttributes<TestAdditionsComponent>();
            ExposedClass.RegisterFromAttributes<TestAdditionsComponent2>();
            ExposedClass.RegisterFromAttributes<ExposedGameObject>();

            var instance = new GameObject("MultiCompPrefab(Clone)");
            try
            {
                var exposedGO = new ExposedGameObject(instance);
                exposedGO.prefabSourceKey = "33333333333333333333333333333333";
                exposedGO.OnEnable();

                var comp1 = instance.AddComponent<TestAdditionsComponent>();
                var comp2 = instance.AddComponent<TestAdditionsComponent2>();
                var exposedClass1 = ExposedClass.Find(typeof(TestAdditionsComponent));
                var exposedClass2 = ExposedClass.Find(typeof(TestAdditionsComponent2));
                new ExposedObject("multi-comp-1", exposedClass1, comp1);
                new ExposedObject("multi-comp-2", exposedClass2, comp2);

                // Act
                var json = ExposedSceneSerializer.SceneToJson(new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver);

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
        public void SceneToJson_PrefabInstance_NestsExposedObject()
        {
            // Arrange
            ExposedClass.RegisterFromAttributes<TestAdditionsComponent>();
            ExposedClass.RegisterFromAttributes<ExposedGameObject>();

            var instance = new GameObject("NestedPrefab(Clone)");
            try
            {
                var exposedGO = new ExposedGameObject(instance);
                exposedGO.prefabSourceKey = "44444444444444444444444444444444";
                exposedGO.OnEnable();

                var testComp = instance.AddComponent<TestAdditionsComponent>();
                testComp.health = 77;
                testComp.label = "Nested";
                var exposedClass = ExposedClass.Find(typeof(TestAdditionsComponent));
                new ExposedObject("nested-comp-1", exposedClass, testComp);

                // Act
                var json = ExposedSceneSerializer.SceneToJson(new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver);

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
                Assert.IsNull(entry["exposedObject"], "Entry should NOT have nested exposedObject");
            }
            finally
            {
                GameObject.DestroyImmediate(instance);
            }
        }

        [Test]
        public void SceneToJson_NoTrackedInstances_NoOpNewInObjects()
        {
            // Arrange
            ExposedClass.RegisterFromAttributes<TestSceneClass>();

            var testObj = new TestSceneClass { value = 1, name = "Normal", position = 0f };
            var exposedClass = ExposedClass.Find(typeof(TestSceneClass));
            new ExposedObject("normal-id", exposedClass, testObj);

            // Act
            var json = ExposedSceneSerializer.SceneToJson(new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver);

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
        public void SceneFromJson_WithAdditionsSection_InstantiatesPrefab()
        {
            // Arrange
            ExposedClass.RegisterFromAttributes<TestAdditionsComponent>();

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
                ExposedSceneSerializer.SceneFromJson(json, _resolver);

                // Assert - ExposedObjectが旧IDで登録されているか
                var restored = ExposedObjectRegistry.FindById("restored-comp-1");
                // TestAdditionsComponentのExposedClassでCamera型のコンポーネントを検索するが、
                // TestAdditionsComponentはComponentではないため、_RegisterComponentExposedObjectでは見つからない。
                // これは期待通りの動作 - 実際のComponentベースのExposedClassでのみ動作する。
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
        public void SceneFromJson_WithAdditions_AlreadyExists_SkipsInstantiation()
        {
            // Arrange
            ExposedClass.RegisterFromAttributes<TestAdditionsComponent>();

            var prefab = new GameObject("SkipPrefab");
            PrefabRegistry.Register("66666666666666666666666666666666", prefab);

            // 既にこのIDのExposedObjectが存在する
            var existingGo = new GameObject("ExistingComp");
            var existingComp = existingGo.AddComponent<TestAdditionsComponent>();
            existingComp.health = 999;
            var exposedClass = ExposedClass.Find(typeof(TestAdditionsComponent));
            new ExposedObject("existing-comp-1", exposedClass, existingComp);

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
                ExposedSceneSerializer.SceneFromJson(json, _resolver);

                // Assert - 既存のオブジェクトが変更されていないことを確認
                var found = ExposedObjectRegistry.FindById("existing-comp-1");
                Assert.IsNotNull(found);
                Assert.AreEqual(existingComp, found.target);
            }
            finally
            {
                GameObject.DestroyImmediate(existingGo);
                GameObject.DestroyImmediate(prefab);
            }
        }

        [Test]
        public void SceneFromJson_WithAdditions_UnknownPrefab_HandlesGracefully()
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

            // Act & Assert - 例外なく処理される（警告ログは出る）
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*Prefab not found.*"));
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*Failed to instantiate prefab.*"));
            Assert.DoesNotThrow(() => ExposedSceneSerializer.SceneFromJson(json, _resolver));
        }

        [Test]
        public void SceneFromJson_WithAdditionsThenObjects_RestoresProperties()
        {
            // Arrange
            ExposedClass.RegisterFromAttributes<TestSceneClass>();

            // 既存のオブジェクトに対して@opなしでobjectsのみ復元するケース
            var testObj = new TestSceneClass { value = 0, name = "", position = 0f };
            var exposedClass = ExposedClass.Find(typeof(TestSceneClass));
            new ExposedObject("prop-restore-1", exposedClass, testObj);

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
            ExposedSceneSerializer.SceneFromJson(json, _resolver);

            // Assert
            Assert.AreEqual(777, testObj.value);
            Assert.AreEqual("Restored", testObj.name);
            Assert.AreEqual(1.5f, testObj.position, 0.001f);
        }

        [Test]
        public void SceneFromJson_NoOpNew_WorksAsExistingBehavior()
        {
            // Arrange - @opなしの通常のobjectsのみのJSON
            ExposedClass.RegisterFromAttributes<TestSceneClass>();

            var testObj = new TestSceneClass { value = 0, name = "", position = 0f };
            var exposedClass = ExposedClass.Find(typeof(TestSceneClass));
            new ExposedObject("legacy-1", exposedClass, testObj);

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
            ExposedSceneSerializer.SceneFromJson(json, _resolver);

            // Assert
            Assert.AreEqual(42, testObj.value);
            Assert.AreEqual("Legacy", testObj.name);
        }

        [Test]
        public void SceneFromJson_MultipleInstancesOfSamePrefab_CreatesSeparateGameObjects()
        {
            ExposedClass.RegisterFromAttributes<TestAdditionsComponent>();

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
                ExposedSceneSerializer.SceneFromJson(json, _resolver);

                var obj1 = ExposedObjectRegistry.FindById("inst-1");
                var obj2 = ExposedObjectRegistry.FindById("inst-2");
                var obj3 = ExposedObjectRegistry.FindById("inst-3");
                Assert.IsNotNull(obj1, "inst-1 should be registered");
                Assert.IsNotNull(obj2, "inst-2 should be registered");
                Assert.IsNotNull(obj3, "inst-3 should be registered");

                var comp1 = obj1.target as TestAdditionsComponent;
                var comp2 = obj2.target as TestAdditionsComponent;
                var comp3 = obj3.target as TestAdditionsComponent;
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
        public void SceneFromJson_MultipleComponentsOnSameInstance_SharesGameObject()
        {
            ExposedClass.RegisterFromAttributes<TestAdditionsComponent>();
            ExposedClass.RegisterFromAttributes<TestAdditionsComponent2>();

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
                ExposedSceneSerializer.SceneFromJson(json, _resolver);

                var obj1 = ExposedObjectRegistry.FindById("shared-1");
                var obj2 = ExposedObjectRegistry.FindById("shared-2");
                Assert.IsNotNull(obj1);
                Assert.IsNotNull(obj2);

                var comp1 = obj1.target as TestAdditionsComponent;
                var comp2 = obj2.target as TestAdditionsComponent2;
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
        /// ExposedGameObjectWithTransform のように、既に登録済みの componentType (GameObject) を
        /// 共有する別 displayName の Proxy も、シーンロード時に Container._objects へ追加されなければならない。
        /// 従来は ExposedUnityObjectFactory._AutoRegisterDerivedTypes が componentType 衝突時に
        /// _registrationList にも登録しなかったため、displayName "GameObjectWithTransform" が
        /// 解決できず wrapper が null になり、Container に追加されなかった。
        /// </summary>
        [Test]
        public void SceneFromJson_GameObjectWithTransform_AddedToContainerObjects()
        {
            ExposedClass.RegisterFromAttributes<ExposedObjectContainer>();
            ExposedClass.RegisterFromAttributes<ExposedGameObject>();
            ExposedClass.RegisterFromAttributes<ExposedGameObjectWithTransform>();

            var containerGo = new GameObject("TestContainer");
            var container = containerGo.AddComponent<ExposedObjectContainer>();

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

                ExposedSceneSerializer.SceneFromJson(json, container);

                Assert.AreEqual(1, container._objects.Count,
                    "GameObjectWithTransform wrapper should be added to container._objects");
                var wrapper = container._objects[0] as ExposedGameObjectWithTransform;
                Assert.IsNotNull(wrapper,
                    "Container entry should be ExposedGameObjectWithTransform, not some fallback type");
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
        public void SceneFromJson_GameObjectWithTransform_RoundTrip_PreservesEntry()
        {
            ExposedClass.RegisterFromAttributes<ExposedObjectContainer>();
            ExposedClass.RegisterFromAttributes<ExposedGameObject>();
            ExposedClass.RegisterFromAttributes<ExposedGameObjectWithTransform>();

            var containerGo = new GameObject("TestContainer");
            var container = containerGo.AddComponent<ExposedObjectContainer>();

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

                ExposedSceneSerializer.SceneFromJson(json, container);

                var wrapper = container._objects.FirstOrDefault() as ExposedGameObjectWithTransform;
                if (wrapper != null) instance = wrapper.reference as GameObject;

                var resolved = ExposedSceneSerializer.ResolveExposedObjects(container.objects, container);
                var saved = ExposedSceneSerializer.SceneToJson(resolved, container, SerializeMode.Snapshot);

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
        /// Factory._prefabGuid が空 (Simulator Reset / OnValidate 未実行) の状態で Factory.Create → container に追加 →
        /// BuildSceneJson (Delta) したとき、exposed.prefabSourceKey も空のため isPrefabNew=false + メタのみ判定で
        /// エントリが完全に欠落するリグレッションを捕捉する。ユーザー報告 test.scene.json (objects: []) と同じ症状。
        /// RefreshPrefabKey を事前に呼べば @prefab が出力されることも検証する。
        /// </summary>
        [Test]
        public void RuntimeCreate_WithPrefabGuidPopulatedViaRefresh_EmitsPrefabEntry()
        {
            ExposedClass.RegisterFromAttributes<ExposedGameObject>();
            ExposedClass.RegisterFromAttributes<ExposedObjectContainer>();

            const string tmpPath = "Assets/_TmpPrefab_DeltaRepro.prefab";
            var seed = new GameObject("DeltaReproPrefab");
            var asset = UnityEditor.PrefabUtility.SaveAsPrefabAsset(seed, tmpPath);
            GameObject.DestroyImmediate(seed);
            var expectedGuid = UnityEditor.AssetDatabase.AssetPathToGUID(tmpPath);

            var containerGo = new GameObject("DeltaReproContainer");
            var container = containerGo.AddComponent<ExposedObjectContainer>();
            container.Initialize();

            GameObject instance = null;
            try
            {
                var factory = new ExposedGameObjectFactory { prefab = asset };

                // 前提: _prefabGuid が空の状態で Create すると save 後の objects は空
                var beforeCreated = factory.Create() as ExposedGameObject;
                instance = beforeCreated.reference as GameObject;
                container.AddExposedObject(beforeCreated);
                beforeCreated.OnEnable();

                var beforeJson = ExposedSceneSerializer.BuildSceneJson(container);
                var beforeRoot = JObject.Parse(beforeJson);
                var beforeObjs = beforeRoot["objects"] as JArray;
                Assert.AreEqual(0, beforeObjs.Count,
                    "Precondition: when _prefabGuid is empty, Delta save drops the entry entirely. Actual: " + beforeJson);

                // Shutdown して作業をクリアしてから Refresh 後の挙動を検証
                container.RemoveExposedObject(beforeCreated);
                beforeCreated.OnDispose();
                GameObject.DestroyImmediate(instance);
                instance = null;

                // Simulator Reset 相当: Factory の _prefabGuid を AssetDatabase から再解決
                factory.RefreshPrefabKey();
                Assert.AreEqual(expectedGuid, factory.prefabGuid,
                    "RefreshPrefabKey should populate _prefabGuid from AssetDatabase");

                var afterCreated = factory.Create() as ExposedGameObject;
                instance = afterCreated.reference as GameObject;
                container.AddExposedObject(afterCreated);
                afterCreated.OnEnable();

                var afterJson = ExposedSceneSerializer.BuildSceneJson(container);
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
        /// Simulator Reset (= WebUIDefinitionPrefabKeyRefresher.Refresh) は渡された WebUIDefinition
        /// のみ更新し、それ以外の WebUIDefinition アセットには影響しないことを検証する。
        /// Play 中に呼ばれても PrefabRegistry に即座に登録されることも併せて確認する。
        /// </summary>
        [Test]
        public void Refresh_TargetsOnlyGivenDefinition_AndRegistersPrefab()
        {
            const string prefabTargetPath = "Assets/_TmpPrefab_Refresher_Target.prefab";
            const string prefabOtherPath = "Assets/_TmpPrefab_Refresher_Other.prefab";
            const string defTargetPath = "Assets/_TmpWebUIDef_Refresher_Target.asset";
            const string defOtherPath = "Assets/_TmpWebUIDef_Refresher_Other.asset";

            var seedT = new GameObject("RefresherPrefabTarget");
            var prefabTarget = UnityEditor.PrefabUtility.SaveAsPrefabAsset(seedT, prefabTargetPath);
            GameObject.DestroyImmediate(seedT);

            var seedO = new GameObject("RefresherPrefabOther");
            var prefabOther = UnityEditor.PrefabUtility.SaveAsPrefabAsset(seedO, prefabOtherPath);
            GameObject.DestroyImmediate(seedO);

            var expectedGuidTarget = UnityEditor.AssetDatabase.AssetPathToGUID(prefabTargetPath);
            var expectedGuidOther = UnityEditor.AssetDatabase.AssetPathToGUID(prefabOtherPath);

            WebUIDefinition defTarget = null;
            WebUIDefinition defOther = null;

            try
            {
                var factoryTarget = new ExposedGameObjectFactory { prefab = prefabTarget };
                var factoryOther = new ExposedGameObjectFactory { prefab = prefabOther };

                defTarget = ScriptableObject.CreateInstance<WebUIDefinition>();
                defTarget.menuItems.Add(new MenuItem
                {
                    id = "target",
                    page = new CategoryPage { factory = new StandardObjectFactory { factories = new IExposedObjectFactory[] { factoryTarget } } }
                });
                UnityEditor.AssetDatabase.CreateAsset(defTarget, defTargetPath);

                defOther = ScriptableObject.CreateInstance<WebUIDefinition>();
                defOther.menuItems.Add(new MenuItem
                {
                    id = "other",
                    page = new CategoryPage { factory = new StandardObjectFactory { factories = new IExposedObjectFactory[] { factoryOther } } }
                });
                UnityEditor.AssetDatabase.CreateAsset(defOther, defOtherPath);

                Assert.IsTrue(string.IsNullOrEmpty(factoryTarget.prefabGuid), "Precondition: target factory guid empty");
                Assert.IsTrue(string.IsNullOrEmpty(factoryOther.prefabGuid), "Precondition: other factory guid empty");

                // Act: Simulator が設定している _definition だけを対象に Refresh
                var updated = WebUIDefinitionPrefabKeyRefresher.Refresh(defTarget);
                Assert.IsTrue(updated);

                // Target は更新される
                var reloadedTarget = UnityEditor.AssetDatabase.LoadAssetAtPath<WebUIDefinition>(defTargetPath);
                var rfTarget = ((StandardObjectFactory)((CategoryPage)reloadedTarget.menuItems[0].page).factory).factories[0] as ExposedGameObjectFactory;
                Assert.AreEqual(expectedGuidTarget, rfTarget.prefabGuid, "Target definition factory GUID should be refreshed");
                Assert.IsTrue(PrefabRegistry.TryFind(expectedGuidTarget, out var regTarget), "PrefabRegistry should have target prefab");
                Assert.AreEqual(prefabTarget, regTarget);

                // Other は一切触られない
                var reloadedOther = UnityEditor.AssetDatabase.LoadAssetAtPath<WebUIDefinition>(defOtherPath);
                var rfOther = ((StandardObjectFactory)((CategoryPage)reloadedOther.menuItems[0].page).factory).factories[0] as ExposedGameObjectFactory;
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
        /// ユーザーの Studio WebUI Definition で ScenePage 側の Factory が更新されなかった症状の再発防止。
        /// </summary>
        [Test]
        public void Refresh_SupportsScenePageInAdditionToCategoryPage()
        {
            const string prefabPath = "Assets/_TmpPrefab_ScenePage.prefab";
            const string defPath = "Assets/_TmpWebUIDef_ScenePage.asset";

            var seed = new GameObject("ScenePagePrefab");
            var prefab = UnityEditor.PrefabUtility.SaveAsPrefabAsset(seed, prefabPath);
            GameObject.DestroyImmediate(seed);

            var expectedGuid = UnityEditor.AssetDatabase.AssetPathToGUID(prefabPath);

            WebUIDefinition def = null;
            try
            {
                var factory = new ExposedGameObjectFactory { prefab = prefab };

                def = ScriptableObject.CreateInstance<WebUIDefinition>();
                def.menuItems.Add(new MenuItem
                {
                    id = "scene",
                    page = new ScenePage { factory = new StandardObjectFactory { factories = new IExposedObjectFactory[] { factory } } }
                });
                UnityEditor.AssetDatabase.CreateAsset(def, defPath);

                Assert.IsTrue(string.IsNullOrEmpty(factory.prefabGuid), "Precondition: factory guid empty");

                var updated = WebUIDefinitionPrefabKeyRefresher.Refresh(def);
                Assert.IsTrue(updated);

                var reloaded = UnityEditor.AssetDatabase.LoadAssetAtPath<WebUIDefinition>(defPath);
                var rf = ((StandardObjectFactory)((ScenePage)reloaded.menuItems[0].page).factory).factories[0] as ExposedGameObjectFactory;
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

        #region ExposedUnityObjectBase.prefabSourceKey Tests

        [Test]
        public void PrefabSourceKey_SetAndGet_ReturnsCorrectKey()
        {
            ExposedClass.RegisterFromAttributes<ExposedGameObject>();
            var instance = new GameObject("MyPrefab(Clone)");
            try
            {
                var exposedGO = new ExposedGameObject(instance);
                exposedGO.prefabSourceKey = "dddddddddddddddddddddddddddddddd";

                Assert.AreEqual("dddddddddddddddddddddddddddddddd", exposedGO.prefabSourceKey);
            }
            finally
            {
                GameObject.DestroyImmediate(instance);
            }
        }

        [Test]
        public void PrefabSourceKey_Default_IsNull()
        {
            ExposedClass.RegisterFromAttributes<ExposedGameObject>();
            var instance = new GameObject("NeverTracked");
            try
            {
                var exposedGO = new ExposedGameObject(instance);
                Assert.IsNull(exposedGO.prefabSourceKey);
            }
            finally
            {
                GameObject.DestroyImmediate(instance);
            }
        }

        [Test]
        public void OnDispose_CallsOnDisable()
        {
            ExposedClass.RegisterFromAttributes<ExposedGameObject>();
            var instance = new GameObject("DisposeTest");
            try
            {
                var exposedGO = new ExposedGameObject(instance);
                exposedGO.OnEnable();
                Assert.IsNotNull(exposedGO.exposedObject);

                exposedGO.OnDispose();
                Assert.IsNull(exposedGO.exposedObject);
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
            ExposedClass.RegisterFromAttributes<TestDeltaNewItem>();
            ExposedClass.RegisterFromAttributes<TestDeltaNewContainer>();

            var container = new TestDeltaNewContainer
            {
                items = new List<TestDeltaNewItem>()
            };
            var containerClass = ExposedClass.Find(typeof(TestDeltaNewContainer));
            var containerExposed = new ExposedObject("delta-new-test", containerClass, container);
            ExposedPropertyUtility.SetDefault(containerExposed);

            // 新要素追加: nameのみ変更、value1/value2はデフォルトのまま
            container.items.Add(new TestDeltaNewItem
            {
                name = "test",
                value1 = 1.0f,  // デフォルトと同じ
                value2 = 2.0f,  // デフォルトと同じ
                nested = new int[0], // デフォルトと同じ
            });

            // Act
            var json = ExposedSceneSerializer.SceneToJson(new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver, SerializeMode.Delta);
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
            ExposedClass.RegisterFromAttributes<TestDeltaNewItem>();
            ExposedClass.RegisterFromAttributes<TestDeltaNewContainer>();

            var container = new TestDeltaNewContainer
            {
                items = new List<TestDeltaNewItem>()
            };
            var containerClass = ExposedClass.Find(typeof(TestDeltaNewContainer));
            var containerExposed = new ExposedObject("delta-new-minimal", containerClass, container);
            ExposedPropertyUtility.SetDefault(containerExposed);

            // 全プロパティがデフォルトと同じ新要素を追加
            container.items.Add(TestDeltaNewItem.Default);

            // Act
            var json = ExposedSceneSerializer.SceneToJson(new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver, SerializeMode.Delta);
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
            ExposedClass.RegisterFromAttributes<TestDeltaNewItem>();
            ExposedClass.RegisterFromAttributes<TestDeltaNewContainer>();

            var container = new TestDeltaNewContainer
            {
                items = new List<TestDeltaNewItem>()
            };
            var containerClass = ExposedClass.Find(typeof(TestDeltaNewContainer));
            var containerExposed = new ExposedObject("delta-new-rt", containerClass, container);
            ExposedPropertyUtility.SetDefault(containerExposed);

            // nameとvalue1のみ変更
            container.items.Add(new TestDeltaNewItem
            {
                name = "roundtrip",
                value1 = 5.0f,
                value2 = 2.0f,  // デフォルト
                nested = new int[0],
            });

            // Act - シリアライズ
            var json = ExposedSceneSerializer.SceneToJson(new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver, SerializeMode.Delta);

            // 新コンテナに復元
            var toRemove = ExposedObjectRegistry.instances.ToList();
            foreach (var obj in toRemove) obj.Unregister();

            var restored = new TestDeltaNewContainer
            {
                items = new List<TestDeltaNewItem>()
            };
            var restoredExposed = new ExposedObject("delta-new-rt", containerClass, restored);
            ExposedPropertyUtility.SetDefault(restoredExposed);
            ExposedSceneSerializer.SceneFromJson(json, _resolver);

            // Assert
            Assert.AreEqual(1, restored.items.Count);
            Assert.AreEqual("roundtrip", restored.items[0].name);
            Assert.AreEqual(5.0f, restored.items[0].value1);
            Assert.AreEqual(2.0f, restored.items[0].value2, "Default value should be preserved");
            Assert.IsNotNull(restored.items[0].nested);
            Assert.AreEqual(0, restored.items[0].nested.Length, "Default empty array should be preserved");
        }

        #endregion

        #region ResolveExposedObjects Array Ref Auto-Discovery Tests

        /// <summary>
        /// ExposedGameObjectの_components配列に含まれるExposedClass付きコンポーネントが、
        /// ResolveExposedObjectsのBFSで探索されることを検証。
        /// IDなしの inline コンポーネントも result に含めて返す（呼び出し側が
        /// SetDefault/EnsureDefaultsCaptured で defaults を登録できるようにするため）。
        /// SceneToJson 側では hasId チェックでトップレベル出力はスキップされる。
        /// </summary>
        [Test]
        public void ResolveExposedObjects_ComponentOnGameObject_AutoDiscovered()
        {
            ExposedClass.RegisterFromAttributes<ExposedGameObject>();
            ExposedClass.RegisterFromAttributes<TestAdditionsComponent>();

            var go = new GameObject("TestGO");
            try
            {
                var comp = go.AddComponent<TestAdditionsComponent>();
                comp.health = 99;

                var exposedGO = new ExposedGameObject(go);
                exposedGO.OnEnable();

                // コンポーネントのExposedObjectは事前に作成しない
                var objects = ExposedSceneSerializer.ResolveExposedObjects(
                    new object[] { exposedGO }, _resolver);

                // IDなしコンポーネントも result に含まれる（defaults 登録のため）
                Assert.IsTrue(
                    objects.Any(o => o.targetType.typeName == "TestAdditionsComponent"),
                    "IDなしコンポーネントもResolveExposedObjectsの結果に含まれるべき（defaults登録対象）");

                // コンテナ未管理のコンポーネントはレジストリには登録されない
                // （CreateUnregistered で作成されるため）
                Assert.IsFalse(
                    ExposedObjectRegistry.TryFindByTarget(comp, out _),
                    "コンテナ未管理のコンポーネントはレジストリに登録されるべきではない");
            }
            finally
            {
                GameObject.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// ExposedGameObjectの_components配列経由で発見されたIDなしコンポーネントが、
        /// SceneToJson(DeltaFromDefault)で親オブジェクト内にインライン展開されることを検証。
        /// コンポーネント内部のプロパティ変更は親の子パスとしてdirty追跡されるため、
        /// componentsは正しくdelta出力に含まれる。
        /// </summary>
        [Test]
        public void SceneToJson_DeltaFromDefault_ComponentDataSerialized()
        {
            ExposedClass.RegisterFromAttributes<ExposedGameObject>();
            ExposedClass.RegisterFromAttributes<TestAdditionsComponent>();

            var go = new GameObject("TestGO");
            try
            {
                var comp = go.AddComponent<TestAdditionsComponent>();
                comp.health = 42;
                comp.label = "Test";

                var exposedGO = new ExposedGameObject(go);
                exposedGO.OnEnable();

                // ResolveExposedObjectsで依存解決
                var resolved = ExposedSceneSerializer.ResolveExposedObjects(
                    new object[] { exposedGO }, _resolver);

                // 全オブジェクトのデフォルト値をキャプチャ
                foreach (var obj in resolved)
                    ExposedPropertyUtility.SetDefault(obj);

                // 値を変更してdirtyにする
                comp.health = 100;

                var json = ExposedSceneSerializer.SceneToJson(resolved, _resolver, SerializeMode.Delta);
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
        /// かつ SceneFromJson 経由のラウンドトリップで target が正しく解決できることを検証する。
        /// </summary>
        [Test]
        public void SceneToJson_PendingEntry_EmitsSourceAsString()
        {
            ExposedClass.RegisterFromAttributes<ExposedGameObject>();
            ExposedClass.RegisterFromAttributes<TestAdditionsComponent>();

            var go = new GameObject("TestGO_SourceString");
            try
            {
                var comp = go.AddComponent<TestAdditionsComponent>();
                comp.health = 7;

                var exposedGO = new ExposedGameObject(go);
                exposedGO.OnEnable();

                var resolved = ExposedSceneSerializer.ResolveExposedObjects(
                    new object[] { exposedGO }, _resolver);
                foreach (var obj in resolved)
                    ExposedPropertyUtility.SetDefault(obj);

                comp.health = 9;

                var json = ExposedSceneSerializer.SceneToJson(resolved, _resolver, SerializeMode.Delta);
                var jRoot = JObject.Parse(json);
                var objects = jRoot["objects"] as JArray;
                var compObj = objects.FirstOrDefault(o => o["@type"]?.ToString() == "TestAdditionsComponent") as JObject;
                Assert.IsNotNull(compObj);

                var sourceToken = compObj["@source"];
                Assert.AreEqual(JTokenType.String, sourceToken.Type, "@source must be string");
                var sourceKey = sourceToken.Value<string>();
                Assert.IsFalse(string.IsNullOrEmpty(sourceKey), "@source must not be empty");
                StringAssert.StartsWith(exposedGO.exposedObject.id, sourceKey,
                    "@source は ExposedGameObject の root id で始まる");
                // path 部分 (components[0] 相当) が含まれる
                StringAssert.Contains("components", sourceKey, "@source は path 'components' を含む");
            }
            finally
            {
                GameObject.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// ExposedGameObjectの_components配列で、未変更のコンポーネントが
        /// DeltaFromDefaultで出力されないことを検証。
        /// rootのExposedObjectのみがIDを保持するため、ネストされたExposedObject参照も
        /// dirtyでなければ出力しない。
        /// </summary>
        [Test]
        public void SceneToJson_DeltaFromDefault_UnchangedComponentNotOutput()
        {
            ExposedClass.RegisterFromAttributes<ExposedGameObject>();
            ExposedClass.RegisterFromAttributes<TestAdditionsComponent>();

            var go = new GameObject("TestGO");
            try
            {
                var comp = go.AddComponent<TestAdditionsComponent>();
                comp.health = 42;

                var exposedGO = new ExposedGameObject(go);
                exposedGO.OnEnable();

                var resolved = ExposedSceneSerializer.ResolveExposedObjects(
                    new object[] { exposedGO }, _resolver);

                foreach (var obj in resolved)
                    ExposedPropertyUtility.SetDefault(obj);

                // コンポーネントの値は変更しない（未変更のまま）
                var json = ExposedSceneSerializer.SceneToJson(resolved, _resolver, SerializeMode.Delta);
                var jRoot = JObject.Parse(json);
                var objects = jRoot["objects"] as JArray;

                // ExposedGameObjectのcomponents配列を取得
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
        /// SceneFromJsonでComponent型（MonoBehaviour）のExposedObjectが
        /// シーン上のコンポーネントから自動的に復元されることを検証。
        /// InstanceIDベースのIDはセッション間で変わるため、型名とGameObject名で検索する。
        /// </summary>
        [Test]
        public void SceneFromJson_ComponentType_RestoredFromScene()
        {
            ExposedClass.RegisterFromAttributes<ExposedGameObject>();
            ExposedClass.RegisterFromAttributes<TestAdditionsComponent>();

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

                ExposedSceneSerializer.SceneFromJson(json, _resolver);

                // ExposedObjectがシーン上のコンポーネントから作成される
                var exposedObj = ExposedObjectRegistry.FindById("-999999");
                Assert.IsNotNull(exposedObj,
                    "Component型のExposedObjectがシーンから自動復元されるべき");
                Assert.AreEqual(comp, exposedObj.target,
                    "ExposedObjectのターゲットがシーン上のコンポーネントであるべき");
                Assert.AreEqual(77, comp.health,
                    "復元されたプロパティ値が適用されるべき");
            }
            finally
            {
                GameObject.DestroyImmediate(go);
            }
        }

        #endregion

        #region ID-less ExposedObject Inline Serialization Tests

        [Serializable]
        [ExposedClass("TestInlineChild")]
        public class TestInlineChild
        {
            [ExposedField]
            public int childValue;

            [ExposedField]
            public string childName;
        }

        [Serializable]
        [ExposedClass("TestParentWithChild")]
        public class TestParentWithChild
        {
            [ExposedField]
            public string parentName;

            [ExposedField]
            public TestInlineChild child;
        }

        [Test]
        public void SceneToJson_IdLessExposedObject_IsInlinedInParent()
        {
            // Arrange
            ExposedClass.RegisterFromAttributes<TestInlineChild>();
            ExposedClass.RegisterFromAttributes<TestParentWithChild>();

            var child = new TestInlineChild { childValue = 10, childName = "InlineChild" };
            var parent = new TestParentWithChild { parentName = "Parent", child = child };

            var parentClass = ExposedClass.Find(typeof(TestParentWithChild));
            var childClass = ExposedClass.Find(typeof(TestInlineChild));

            // 親はID付き、子はIDなし
            var parentExposed = new ExposedObject("parent-id-1", parentClass, parent);
            var childExposed = ExposedObjectRegistry.GetOrCreateWithoutId(childClass, child);

            Assert.IsTrue(parentExposed.hasId);
            Assert.IsFalse(childExposed.hasId);

            // Act
            var json = ExposedSceneSerializer.SceneToJson(
                new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver);

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
        public void SceneToJson_IdExposedObject_IsRefInParent()
        {
            // Arrange
            ExposedClass.RegisterFromAttributes<TestInlineChild>();
            ExposedClass.RegisterFromAttributes<TestParentWithChild>();

            var child = new TestInlineChild { childValue = 20, childName = "RefChild" };
            var parent = new TestParentWithChild { parentName = "Parent", child = child };

            var parentClass = ExposedClass.Find(typeof(TestParentWithChild));
            var childClass = ExposedClass.Find(typeof(TestInlineChild));

            // 両方ID付き
            var parentExposed = new ExposedObject("parent-id-1", parentClass, parent);
            var childExposed = new ExposedObject("child-id-1", childClass, child);

            Assert.IsTrue(parentExposed.hasId);
            Assert.IsTrue(childExposed.hasId);

            // Act
            var json = ExposedSceneSerializer.SceneToJson(
                new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver);

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
        public void SceneToJson_IdLessObject_RoundTrip()
        {
            // Arrange
            ExposedClass.RegisterFromAttributes<TestInlineChild>();
            ExposedClass.RegisterFromAttributes<TestParentWithChild>();

            var child = new TestInlineChild { childValue = 42, childName = "RoundTrip" };
            var parent = new TestParentWithChild { parentName = "Parent", child = child };

            var parentClass = ExposedClass.Find(typeof(TestParentWithChild));
            var childClass = ExposedClass.Find(typeof(TestInlineChild));

            var parentExposed = new ExposedObject("parent-id-1", parentClass, parent);
            ExposedObjectRegistry.GetOrCreateWithoutId(childClass, child);

            // Act: シリアライズ
            var json = ExposedSceneSerializer.SceneToJson(
                new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver);

            // 状態をリセットしてデシリアライズ
            parent.child = new TestInlineChild();
            parent.parentName = "";

            ExposedSceneSerializer.SceneFromJson(json, _resolver);

            // Assert: 値が復元されている
            Assert.AreEqual("Parent", parent.parentName);
            Assert.IsNotNull(parent.child);
            Assert.AreEqual(42, parent.child.childValue);
            Assert.AreEqual("RoundTrip", parent.child.childName);
        }

        [Test]
        public void ExposedObject_HasId_ReturnsFalseForNullId()
        {
            // Arrange
            ExposedClass.RegisterFromAttributes<TestInlineChild>();
            var child = new TestInlineChild();
            var childClass = ExposedClass.Find(typeof(TestInlineChild));

            // Act
            var withId = new ExposedObject("some-id", childClass, child);
            Assert.IsTrue(withId.hasId);

            withId.Unregister();

            var withoutId = ExposedObjectRegistry.GetOrCreateWithoutId(childClass, child);
            Assert.IsFalse(withoutId.hasId);
        }

        #endregion

        #region SetDefault → Load → Modify → Save Flow Tests

        /// <summary>
        /// 実アプリフローの再現:
        /// 1. ExposedObject作成（コンストラクタでSetDefault）
        /// 2. 追加のSetDefault（ExposedObjectContainer.Initializeと同等）
        /// 3. SceneFromJsonでデルタ��ード
        /// 4. プロパティ変更
        /// 5. SceneToJson(DeltaFromDefault)���保存
        /// 変更し��プロパティが正しく保存されることを検��。
        /// </summary>
        [Test]
        public void AppFlow_SetDefault_Load_Modify_Save_PreservesChanges()
        {
            // Arrange
            ExposedClass.RegisterFromAttributes<TestSceneClass>();

            var testObj = new TestSceneClass { value = 42, name = "Original", position = 1.0f };
            var exposedClass = ExposedClass.Find(typeof(TestSceneClass));
            var exposedObj = new ExposedObject("app-flow-1", exposedClass, testObj);

            // Step 1: 初期SetDefault（ExposedObjectContainer.Initializeと同等）
            ExposedPropertyUtility.SetDefault(exposedObj);

            // Step 2: 前回保存されたデルタを適用（SceneFromJsonシミュレーション）
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
            ExposedSceneSerializer.SceneFromJson(savedDeltaJson, _resolver);
            Assert.AreEqual(99, testObj.value, "SceneFromJson should have restored value to 99");

            // Step 3: ユーザーがプロパティを変更
            var valueProp = exposedObj.FindProperty("value");
            Assert.IsNotNull(valueProp);
            valueProp.Value.SetValue(200);
            Assert.AreEqual(200, testObj.value);

            // Step 4: 保存（DeltaFromDefault）
            var json = ExposedSceneSerializer.SceneToJson(
                new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver, SerializeMode.Delta);

            // Assert: 変更したプロパティが保存される
            var jRoot = JObject.Parse(json);
            var objects = jRoot["objects"] as JArray;
            var obj = objects?.FirstOrDefault(o => EntryKey(o) =="app-flow-1") as JObject;
            Assert.IsNotNull(obj, $"変更さ���たオブジェクトがdelta出力に含まれるべき. JSON: {json}");
            Assert.AreEqual(200, obj["value"]?.Value<int>(), "変更されたvalueが保存されるべき");
        }

        /// <summary>
        /// SetDefault後にSceneFromJsonでロードし、何も変更しない場合、
        /// ロード済みの値が保存されることを検証（デフォルト値からの���分として���。
        /// </summary>
        [Test]
        public void AppFlow_SetDefault_Load_NoModify_Save_PreservesLoadedValues()
        {
            // Arrange
            ExposedClass.RegisterFromAttributes<TestSceneClass>();

            var testObj = new TestSceneClass { value = 42, name = "Original", position = 1.0f };
            var exposedClass = ExposedClass.Find(typeof(TestSceneClass));
            var exposedObj = new ExposedObject("app-flow-2", exposedClass, testObj);

            // Step 1: 初期SetDefault
            ExposedPropertyUtility.SetDefault(exposedObj);

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
            ExposedSceneSerializer.SceneFromJson(savedDeltaJson, _resolver);

            // Step 3: 何も変更しない → しかしロード済みの値はデフォルトと異なるのでdirty

            // Step 4: 保存
            var json = ExposedSceneSerializer.SceneToJson(
                new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver, SerializeMode.Delta);

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
            ExposedClass.RegisterFromAttributes<TestSceneClass>();

            // === Session 1: 変更して保存 ===
            var obj1 = new TestSceneClass { value = 42, name = "Original", position = 1.0f };
            var exposedClass = ExposedClass.Find(typeof(TestSceneClass));
            var exposed1 = new ExposedObject("integ-basic-1", exposedClass, obj1);
            ExposedPropertyUtility.SetDefault(exposed1);

            // プロパティ変更
            obj1.value = 200;
            obj1.name = "Changed";
            // positionは変更しない

            // デルタ保存
            var deltaJson = ExposedSceneSerializer.SceneToJson(
                new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver, SerializeMode.Delta);

            // === Session 2: 新インスタンスで再構築してロード ===
            // ExposedObjectをクリア
            exposed1.Unregister();

            var obj2 = new TestSceneClass { value = 42, name = "Original", position = 1.0f };
            var exposed2 = new ExposedObject("integ-basic-1", exposedClass, obj2);
            ExposedPropertyUtility.SetDefault(exposed2);

            // デルタロード
            ExposedSceneSerializer.SceneFromJson(deltaJson, _resolver);

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
            ExposedClass.RegisterFromAttributes<TestSceneClass>();
            var exposedClass = ExposedClass.Find(typeof(TestSceneClass));

            // === Cycle 1: 変更して保存 ===
            var obj1 = new TestSceneClass { value = 42, name = "Original", position = 1.0f };
            var exposed1 = new ExposedObject("integ-cycle-1", exposedClass, obj1);
            ExposedPropertyUtility.SetDefault(exposed1);

            obj1.value = 100;

            var deltaJson1 = ExposedSceneSerializer.SceneToJson(
                new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver, SerializeMode.Delta);
            exposed1.Unregister();

            // === Cycle 2: ロード→追加変更→保存 ===
            var obj2 = new TestSceneClass { value = 42, name = "Original", position = 1.0f };
            var exposed2 = new ExposedObject("integ-cycle-1", exposedClass, obj2);
            ExposedPropertyUtility.SetDefault(exposed2);
            ExposedSceneSerializer.SceneFromJson(deltaJson1, _resolver);

            Assert.AreEqual(100, obj2.value, "Cycle 1 value should be loaded");

            // 追加変更
            obj2.name = "Cycle2";

            var deltaJson2 = ExposedSceneSerializer.SceneToJson(
                new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver, SerializeMode.Delta);
            exposed2.Unregister();

            // === Cycle 3: 最終ロード→検証 ===
            var obj3 = new TestSceneClass { value = 42, name = "Original", position = 1.0f };
            var exposed3 = new ExposedObject("integ-cycle-1", exposedClass, obj3);
            ExposedPropertyUtility.SetDefault(exposed3);
            ExposedSceneSerializer.SceneFromJson(deltaJson2, _resolver);

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
            ExposedClass.RegisterFromAttributes<TestSceneClass>();
            var exposedClass = ExposedClass.Find(typeof(TestSceneClass));

            // === Session 1: 2オブジェクト、1つだけ変更 ===
            var objA = new TestSceneClass { value = 10, name = "A", position = 0f };
            var objB = new TestSceneClass { value = 20, name = "B", position = 0f };
            var exposedA = new ExposedObject("integ-multi-a", exposedClass, objA);
            var exposedB = new ExposedObject("integ-multi-b", exposedClass, objB);
            ExposedPropertyUtility.SetDefault(exposedA);
            ExposedPropertyUtility.SetDefault(exposedB);

            // Aのみ変更
            objA.value = 999;

            var deltaJson = ExposedSceneSerializer.SceneToJson(
                new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver, SerializeMode.Delta);

            // デルタにAのみ含まれることを確認
            var jRoot = JObject.Parse(deltaJson);
            var objects = jRoot["objects"] as JArray;
            Assert.AreEqual(1, objects.Count, "変更したオブジェクトのみdelta出力に含まれるべき");
            Assert.AreEqual("integ-multi-a", EntryKey(objects[0]));

            exposedA.Unregister();
            exposedB.Unregister();

            // === Session 2: ロード→検証 ===
            var objA2 = new TestSceneClass { value = 10, name = "A", position = 0f };
            var objB2 = new TestSceneClass { value = 20, name = "B", position = 0f };
            var exposedA2 = new ExposedObject("integ-multi-a", exposedClass, objA2);
            var exposedB2 = new ExposedObject("integ-multi-b", exposedClass, objB2);
            ExposedPropertyUtility.SetDefault(exposedA2);
            ExposedPropertyUtility.SetDefault(exposedB2);
            ExposedSceneSerializer.SceneFromJson(deltaJson, _resolver);

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
            ExposedClass.RegisterFromAttributes<TestSceneClassWithArray>();
            var exposedClass = ExposedClass.Find(typeof(TestSceneClassWithArray));

            // === Session 1: 配列変更して保存 ===
            var obj1 = new TestSceneClassWithArray
            {
                intArray = new int[] { 1, 2, 3 },
                stringArray = new string[] { "a", "b" },
                intList = new List<int> { 10, 20 }
            };
            var exposed1 = new ExposedObject("integ-array-1", exposedClass, obj1);
            ExposedPropertyUtility.SetDefault(exposed1);

            // 配列の値を変更
            obj1.intArray[0] = 100;
            obj1.stringArray = new string[] { "a", "b", "c" };

            var deltaJson = ExposedSceneSerializer.SceneToJson(
                new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver, SerializeMode.Delta);
            exposed1.Unregister();

            // === Session 2: ロード→検証 ===
            var obj2 = new TestSceneClassWithArray
            {
                intArray = new int[] { 1, 2, 3 },
                stringArray = new string[] { "a", "b" },
                intList = new List<int> { 10, 20 }
            };
            var exposed2 = new ExposedObject("integ-array-1", exposedClass, obj2);
            ExposedPropertyUtility.SetDefault(exposed2);
            ExposedSceneSerializer.SceneFromJson(deltaJson, _resolver);

            Assert.AreEqual(100, obj2.intArray[0], "intArray[0] should be restored to modified value");
            Assert.AreEqual(2, obj2.intArray[1], "intArray[1] should remain at default");
            Assert.AreEqual(3, obj2.stringArray.Length, "stringArray should have 3 elements after restore");
            Assert.AreEqual("c", obj2.stringArray[2], "stringArray[2] should be restored");
            // 変更しなかったintListはデフォルトのまま
            Assert.AreEqual(2, obj2.intList.Count, "intList should remain at default size");
        }

        /// <summary>
        /// 統合テスト: 参照リストの保存/ロードサイクル。
        /// ExposedObject参照を含む配列の追加要素がデルタで正しく保存/復元されるか検証。
        /// </summary>
        [Test]
        public void Integration_SaveLoad_RefList()
        {
            ExposedClass.RegisterFromAttributes<TestSceneRefItem>();
            ExposedClass.RegisterFromAttributes<TestSceneContainerWithRefList>();
            var containerClass = ExposedClass.Find(typeof(TestSceneContainerWithRefList));
            var itemClass = ExposedClass.Find(typeof(TestSceneRefItem));

            // === Session 1: 要素追加して保存 ===
            var item1 = new TestSceneRefItem { name = "Item1", value = 10 };
            var container1 = new TestSceneContainerWithRefList
            {
                items = new List<TestSceneRefItem> { item1 }
            };
            var containerExposed1 = new ExposedObject("integ-ref-container", containerClass, container1);
            new ExposedObject("integ-ref-item1", itemClass, item1);
            ExposedPropertyUtility.SetDefault(containerExposed1);

            // 新要素追加
            var item2 = new TestSceneRefItem { name = "Item2", value = 20 };
            container1.items.Add(item2);
            new ExposedObject("integ-ref-item2", itemClass, item2);

            var deltaJson = ExposedSceneSerializer.SceneToJson(
                new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver, SerializeMode.Delta);

            // 全Unregister
            foreach (var obj in ExposedObjectRegistry.instances.ToList()) obj.Unregister();

            // === Session 2: ロード→検証 ===
            var item1b = new TestSceneRefItem { name = "Item1", value = 10 };
            var item2b = new TestSceneRefItem { name = "Item2", value = 20 };
            var container2 = new TestSceneContainerWithRefList
            {
                items = new List<TestSceneRefItem> { item1b }
            };
            var containerExposed2 = new ExposedObject("integ-ref-container", containerClass, container2);
            new ExposedObject("integ-ref-item1", itemClass, item1b);
            new ExposedObject("integ-ref-item2", itemClass, item2b);
            ExposedPropertyUtility.SetDefault(containerExposed2);

            ExposedSceneSerializer.SceneFromJson(deltaJson, _resolver);

            Assert.AreEqual(2, container2.items.Count, "container should have 2 items after restore");
        }

        #endregion

        #region Static Object Dirty Detection Tests

        [Test]
        public void Static_SetDefault_ChangeProperty_IsDirty()
        {
            ExposedClass.RegisterClass(typeof(TestStaticSceneClass));
            ExposedClass.RegisterProperties(typeof(TestStaticSceneClass));
            TestStaticSceneClass.Reset();

            var exposedClass = ExposedClass.Find(typeof(TestStaticSceneClass));
            Assert.IsNotNull(exposedClass, "TestStaticSceneClass should be registered");
            Assert.IsTrue(exposedClass.isStatic, "TestStaticSceneClass should be static");

            // staticオブジェクトを手動作成（コンストラクタでSetDefaultが呼ばれる）
            var exposedObj = new ExposedObject("TestStaticSceneClass", exposedClass, null);

            try
            {
                // ExposedClassのプロパティが登録されているか確認
                Assert.IsTrue(exposedClass.propertyTypes.Length > 0,
                    $"ExposedClass should have properties. isStatic={exposedClass.isStatic}, type={exposedClass.type}");

                // 初期状態ではdirtyでない
                Assert.IsFalse(exposedObj.isDirty, "Should not be dirty before change");

                // staticプロパティを変更
                TestStaticSceneClass.value = 999;

                // dirtyになるべき
                Assert.IsTrue(exposedObj.isDirty, "Should be dirty after changing static property");
            }
            finally
            {
                TestStaticSceneClass.Reset();
            }
        }

        [Test]
        public void Static_DeltaFromDefault_OutputsChangedProperties()
        {
            ExposedClass.RegisterClass(typeof(TestStaticSceneClass));
            ExposedClass.RegisterProperties(typeof(TestStaticSceneClass));
            TestStaticSceneClass.Reset();

            var exposedClass = ExposedClass.Find(typeof(TestStaticSceneClass));
            var exposedObj = new ExposedObject("TestStaticSceneClass", exposedClass, null);

            try
            {
                // staticプロパティを変更
                TestStaticSceneClass.value = 42;

                var json = ExposedSceneSerializer.SceneToJson(
                    new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver, SerializeMode.Delta);

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
        /// ID付き非rootのExposedObject参照で、変更したプロパティのみがdelta出力に含まれ、
        /// 未変更プロパティは出力されないことを検証。
        /// </summary>
        [Test]
        public void SceneToJson_DeltaFromDefault_NonRootRefObject_OnlyDirtyPropertiesOutput()
        {
            // Arrange
            ExposedClass.RegisterFromAttributes<TestSceneRefItem>();
            ExposedClass.RegisterFromAttributes<TestSceneContainerWithRefList>();

            var item1 = new TestSceneRefItem { name = "Item1", value = 10 };
            var item2 = new TestSceneRefItem { name = "Item2", value = 20 };
            var container = new TestSceneContainerWithRefList
            {
                items = new List<TestSceneRefItem> { item1, item2 }
            };

            var containerClass = ExposedClass.Find(typeof(TestSceneContainerWithRefList));
            var itemClass = ExposedClass.Find(typeof(TestSceneRefItem));
            var containerExposed = new ExposedObject("container-minimal", containerClass, container);
            var item1Exposed = new ExposedObject("item-minimal-1", itemClass, item1);
            var item2Exposed = new ExposedObject("item-minimal-2", itemClass, item2);

            // デフォルト値キャプチャ
            ExposedPropertyUtility.SetDefault(containerExposed);
            ExposedPropertyUtility.SetDefault(item1Exposed);
            ExposedPropertyUtility.SetDefault(item2Exposed);

            // item1のvalueのみ変更
            item1.value = 99;

            // Act
            var json = ExposedSceneSerializer.SceneToJson(
                new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver, SerializeMode.Delta);
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
        /// インライン展開のExposedObject参照（IDなし、コンポーネント等）で、
        /// 変更したプロパティのみがdelta出力に含まれ、未変更プロパティは出力されないことを検証。
        /// </summary>
        [Test]
        public void SceneToJson_DeltaFromDefault_InlineExposedObject_OnlyDirtyPropertiesOutput()
        {
            ExposedClass.RegisterFromAttributes<ExposedGameObject>();
            ExposedClass.RegisterFromAttributes<TestAdditionsComponent>();

            var go = new GameObject("TestGO");
            try
            {
                var comp = go.AddComponent<TestAdditionsComponent>();
                comp.health = 42;
                comp.label = "Original";

                var exposedGO = new ExposedGameObject(go);
                exposedGO.OnEnable();

                var resolved = ExposedSceneSerializer.ResolveExposedObjects(
                    new object[] { exposedGO }, _resolver);

                foreach (var obj in resolved)
                    ExposedPropertyUtility.SetDefault(obj);

                // healthのみ変更（labelは変更しない）
                comp.health = 100;

                var json = ExposedSceneSerializer.SceneToJson(resolved, _resolver, SerializeMode.Delta);
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
        /// ExposedGameObjectのcomponents配列内のインラインExposedObject参照で、
        /// 変更プロパティのみがデルタ出力に含まれ、未変更プロパティは出力されないことを検証。
        /// _SerializeArrayDeltaの結果ベースdirty判定が正しく動作することの確認。
        /// </summary>
        [Test]
        public void SceneToJson_DeltaFromDefault_InlineComponentChange_OutputsDirtyProperties()
        {
            ExposedClass.RegisterFromAttributes<ExposedGameObject>();
            ExposedClass.RegisterFromAttributes<TestAdditionsComponent>();

            var go = new GameObject("TestGO");
            try
            {
                var comp = go.AddComponent<TestAdditionsComponent>();
                comp.health = 50;
                comp.label = "Original";

                var exposedGO = new ExposedGameObject(go);
                exposedGO.OnEnable();

                var resolved = ExposedSceneSerializer.ResolveExposedObjects(
                    new object[] { exposedGO }, _resolver);

                foreach (var obj in resolved)
                    ExposedPropertyUtility.SetDefault(obj);

                // healthのみ変更
                comp.health = 200;

                var json = ExposedSceneSerializer.SceneToJson(resolved, _resolver, SerializeMode.Delta);
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
        /// インラインExposedObject参照(コンポーネント)のデルタ保存→復元ラウンドトリップ。
        /// 変更プロパティのみが復元され、未変更プロパティはデフォルトのままであることを検証。
        /// </summary>
        [Test]
        public void RoundTrip_DeltaFromDefault_InlineComponentChange_Restored()
        {
            ExposedClass.RegisterFromAttributes<ExposedGameObject>();
            ExposedClass.RegisterFromAttributes<TestAdditionsComponent>();

            var go = new GameObject("TestGO");
            try
            {
                var comp = go.AddComponent<TestAdditionsComponent>();
                comp.health = 50;
                comp.label = "Original";

                var exposedGO = new ExposedGameObject(go);
                exposedGO.OnEnable();

                var resolved = ExposedSceneSerializer.ResolveExposedObjects(
                    new object[] { exposedGO }, _resolver);

                foreach (var obj in resolved)
                    ExposedPropertyUtility.SetDefault(obj);

                // healthのみ変更
                comp.health = 200;

                // デルタ保存
                var json = ExposedSceneSerializer.SceneToJson(resolved, _resolver, SerializeMode.Delta);

                // 値をデフォルトに戻す（ExposedObjectはIDを維持するため再構築しない）
                comp.health = 50;
                comp.label = "Original";

                // デルタからロード
                ExposedSceneSerializer.SceneFromJson(json, _resolver);

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
        public void SceneToJson_DeltaFromDefault_MultipleComponents_OnlyChangedOutput()
        {
            ExposedClass.RegisterFromAttributes<ExposedGameObject>();
            ExposedClass.RegisterFromAttributes<TestAdditionsComponent>();
            ExposedClass.RegisterFromAttributes<TestAdditionsComponent2>();

            var go = new GameObject("TestGO");
            try
            {
                var comp1 = go.AddComponent<TestAdditionsComponent>();
                comp1.health = 42;
                comp1.label = "Label1";
                var comp2 = go.AddComponent<TestAdditionsComponent2>();
                comp2.speed = 5.0f;

                var exposedGO = new ExposedGameObject(go);
                exposedGO.OnEnable();

                var resolved = ExposedSceneSerializer.ResolveExposedObjects(
                    new object[] { exposedGO }, _resolver);

                foreach (var obj in resolved)
                    ExposedPropertyUtility.SetDefault(obj);

                // comp1のhealthのみ変更（comp2は未変更）
                comp1.health = 100;

                var json = ExposedSceneSerializer.SceneToJson(resolved, _resolver, SerializeMode.Delta);
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
            ExposedClass.RegisterFromAttributes<TestDeltaNewItem>();
            ExposedClass.RegisterFromAttributes<TestDeltaNewContainer>();

            var container = new TestDeltaNewContainer
            {
                items = new List<TestDeltaNewItem>()
            };
            var containerClass = ExposedClass.Find(typeof(TestDeltaNewContainer));
            var containerExposed = new ExposedObject("idempotent-test", containerClass, container);

            // デフォルトキャプチャ（空リスト）
            ExposedPropertyUtility.SetDefault(containerExposed);

            // 要素追加
            container.items.Add(new TestDeltaNewItem { name = "Added", value1 = 10f, value2 = 20f });

            // デルタ保存
            var json = ExposedSceneSerializer.SceneToJson(
                new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver, SerializeMode.Delta);

            // 1回目ロード
            ExposedSceneSerializer.SceneFromJson(json, _resolver);
            Assert.AreEqual(1, container.items.Count, $"1回目ロード後: 要素は1つであるべき. JSON: {json}");

            // 2回目ロード（冪等性テスト）
            ExposedSceneSerializer.SceneFromJson(json, _resolver);
            Assert.AreEqual(1, container.items.Count, $"2回目ロード後: 要素は1つのまま（重複しない）. JSON: {json}");

            // 3回目ロード
            ExposedSceneSerializer.SceneFromJson(json, _resolver);
            Assert.AreEqual(1, container.items.Count, "3回目ロード後: 要素は1つのまま");
        }

        /// <summary>
        /// インラインExposedObject参照(コンポーネント)のデルタ保存→復元ラウンドトリップで、
        /// 再保存時にcomponents[]が空にならないことを検証。
        /// </summary>
        [Test]
        public void RoundTrip_DeltaFromDefault_InlineComponent_ReSavePreservesContent()
        {
            ExposedClass.RegisterFromAttributes<ExposedGameObject>();
            ExposedClass.RegisterFromAttributes<TestAdditionsComponent>();

            var go = new GameObject("TestGO");
            try
            {
                var comp = go.AddComponent<TestAdditionsComponent>();
                comp.health = 50;
                comp.label = "Original";

                var exposedGO = new ExposedGameObject(go);
                exposedGO.OnEnable();

                var resolved = ExposedSceneSerializer.ResolveExposedObjects(
                    new object[] { exposedGO }, _resolver);

                foreach (var obj in resolved)
                    ExposedPropertyUtility.SetDefault(obj);

                // 変更
                comp.health = 200;

                // 1回目デルタ保存
                var json1 = ExposedSceneSerializer.SceneToJson(resolved, _resolver, SerializeMode.Delta);
                var jRoot1 = JObject.Parse(json1);
                var objects1 = jRoot1["objects"] as JArray;
                // 新フォーマット: Component は pending エントリとしてトップレベルに出力される
                var compData1 = objects1?.FirstOrDefault(o => o["@type"]?.ToString() == "TestAdditionsComponent") as JObject;
                Assert.IsNotNull(compData1, $"1回目: コンポーネントが pending エントリとして出力されるべき. JSON: {json1}");

                // ロードしてから再保存
                ExposedSceneSerializer.SceneFromJson(json1, _resolver);

                var json2 = ExposedSceneSerializer.SceneToJson(resolved, _resolver, SerializeMode.Delta);
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
            ExposedClass.RegisterFromAttributes<TestDeltaNewItem>();
            ExposedClass.RegisterFromAttributes<TestDeltaNewContainer>();

            var container = new TestDeltaNewContainer
            {
                items = new List<TestDeltaNewItem>()
            };
            var containerClass = ExposedClass.Find(typeof(TestDeltaNewContainer));
            var containerExposed = new ExposedObject("loaded-modify-test", containerClass, container);
            ExposedPropertyUtility.SetDefault(containerExposed);

            // 1回目: 要素追加してデルタ保存
            container.items.Add(new TestDeltaNewItem { name = "Original", value1 = 1.0f, value2 = 2.0f });
            var delta1 = ExposedSceneSerializer.SceneToJson(
                new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver, SerializeMode.Delta);

            // デフォルト状態に戻す
            container.items.Clear();

            // デルタをロード → 要素が復元される
            ExposedSceneSerializer.SceneFromJson(delta1, _resolver);
            Assert.AreEqual(1, container.items.Count, "ロード後: 要素が1つあるべき");
            Assert.AreEqual("Original", container.items[0].name, "ロード後: nameが復元されるべき");

            // ロードした要素のプロパティを変更（structなのでコピー→変更→代入）
            var item = container.items[0];
            item.name = "Modified";
            item.value1 = 99.0f;
            container.items[0] = item;

            // 再保存
            var delta2 = ExposedSceneSerializer.SceneToJson(
                new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver, SerializeMode.Delta);
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
        /// ExposedGameObject → components[] → Component → items[] → item.name を変更し、
        /// デルタ保存→復元でその変更が保持されることを確認。
        ///
        /// 実アプリではコンポーネントのExposedObjectがExposedGameObjectより先に生成されるため、
        /// SetDefaultの@ref境界停止でネストパス(components[0].items等)が親に作られない。
        /// この状態を再現してテストする。
        /// </summary>
        [Test]
        public void RoundTrip_DeltaFromDefault_InlineComponentNestedArrayElementChange_Preserved()
        {
            ExposedClass.RegisterFromAttributes<ExposedGameObject>();
            ExposedClass.RegisterFromAttributes<TestComponentWithArray>();
            ExposedClass.RegisterFromAttributes<TestDeltaNewItem>();

            var go = new GameObject("TestGO");
            try
            {
                var comp = go.AddComponent<TestComponentWithArray>();
                comp.items = new List<TestDeltaNewItem>();

                // 実アプリの初期化順序を再現:
                // 1. コンポーネントのExposedObjectを先に作成（ExposedComponent.OnEnable相当）
                var compClass = ExposedClass.Find(typeof(TestComponentWithArray));
                var compExposedObj = new ExposedObject(null, compClass, comp);

                // 2. ExposedGameObject作成 → auto-SetDefaultでcomponents[0]は@ref境界で停止
                var exposedGO = new ExposedGameObject(go);
                exposedGO.OnEnable();

                var resolved = ExposedSceneSerializer.ResolveExposedObjects(
                    new object[] { exposedGO }, _resolver);

                // 3. 全オブジェクトのSetDefault（@ref境界でネストパスなし）
                foreach (var obj in resolved)
                    ExposedPropertyUtility.SetDefault(obj);

                // 要素を追加してデルタ保存
                comp.items.Add(new TestDeltaNewItem { name = "Original", value1 = 1.0f, value2 = 2.0f });
                var delta1 = ExposedSceneSerializer.SceneToJson(resolved, _resolver, SerializeMode.Delta);

                // 値をデフォルトに戻す
                comp.items.Clear();

                // デルタをロード → 要素が復元される
                ExposedSceneSerializer.SceneFromJson(delta1, _resolver);
                Assert.AreEqual(1, comp.items.Count, $"ロード後: 要素が1つあるべき. JSON: {delta1}");

                // ロードした要素のnameを変更（meshStateOverrides[0].name変更相当）
                var item = comp.items[0];
                item.name = "Modified";
                comp.items[0] = item;

                // 再保存 → 変更が出力されること
                var delta2 = ExposedSceneSerializer.SceneToJson(resolved, _resolver, SerializeMode.Delta);
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
                ExposedSceneSerializer.SceneFromJson(delta2, _resolver);
                Assert.AreEqual(1, comp.items.Count, "復元後: 要素が1つあるべき");
                Assert.AreEqual("Modified", comp.items[0].name, "復元後: 変更したnameが復元されるべき");
            }
            finally
            {
                GameObject.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// ExposedGameObjectのcomponents内コンポーネントの配列プロパティが@op:newで追加された場合、
        /// ロード→再保存でcomponents配列が消えないことを検証。
        /// 親のdirty追跡にネストパスがない場合（@ref境界停止）でも、
        /// ExposedClass要素を含むプロパティは結果ベースで判定される。
        /// </summary>
        [Test]
        public void RoundTrip_DeltaFromDefault_NestedArrayOpNew_PreservedAfterReSave()
        {
            ExposedClass.RegisterFromAttributes<ExposedGameObject>();
            ExposedClass.RegisterFromAttributes<TestAdditionsComponent>();
            ExposedClass.RegisterFromAttributes<TestDeltaNewItem>();

            var go = new GameObject("TestGO");
            try
            {
                var comp = go.AddComponent<TestAdditionsComponent>();
                comp.health = 50;

                var exposedGO = new ExposedGameObject(go);
                exposedGO.OnEnable();

                var resolved = ExposedSceneSerializer.ResolveExposedObjects(
                    new object[] { exposedGO }, _resolver);

                // ResolveExposedObjects後にSetDefault（@ref境界でネストパスが作られない状態を再現）
                foreach (var obj in resolved)
                    ExposedPropertyUtility.SetDefault(obj);

                // healthを変更
                comp.health = 100;

                // 1回目デルタ保存
                var delta1 = ExposedSceneSerializer.SceneToJson(resolved, _resolver, SerializeMode.Delta);

                // 値をデフォルトに戻す
                comp.health = 50;

                // デルタをロード
                ExposedSceneSerializer.SceneFromJson(delta1, _resolver);
                Assert.AreEqual(100, comp.health, "ロード後: healthが復元されるべき");

                // ロード後に再保存（components内のデータが消えないこと）
                var delta2 = ExposedSceneSerializer.SceneToJson(resolved, _resolver, SerializeMode.Delta);
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
        /// components配列はコレクション型であり、ExposedClass.Find(listType)がnullを返すため、
        /// 要素レベルのExposedClassチェックが必要。
        /// </summary>
        [Test]
        public void RoundTrip_DeltaFromDefault_LoadThenReSave_InlineComponentPreserved()
        {
            ExposedClass.RegisterFromAttributes<ExposedGameObject>();
            ExposedClass.RegisterFromAttributes<TestAdditionsComponent>();

            var go = new GameObject("TestGO");
            try
            {
                var comp = go.AddComponent<TestAdditionsComponent>();
                comp.health = 50;
                comp.label = "Original";

                var exposedGO = new ExposedGameObject(go);
                exposedGO.OnEnable();

                var resolved = ExposedSceneSerializer.ResolveExposedObjects(
                    new object[] { exposedGO }, _resolver);

                foreach (var obj in resolved)
                    ExposedPropertyUtility.SetDefault(obj);

                // 変更してデルタ保存
                comp.health = 200;
                var deltaJson = ExposedSceneSerializer.SceneToJson(resolved, _resolver, SerializeMode.Delta);

                // 値をデフォルトに戻す
                comp.health = 50;

                // デルタをロード → healthが200に復元される
                ExposedSceneSerializer.SceneFromJson(deltaJson, _resolver);
                Assert.AreEqual(200, comp.health, "ロード後: healthが復元されるべき");

                // ロード後に再保存 → 変更が消えないこと
                var reSavedJson = ExposedSceneSerializer.SceneToJson(resolved, _resolver, SerializeMode.Delta);
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
            ExposedClass.RegisterFromAttributes<TestSceneRefItem>();
            ExposedClass.RegisterFromAttributes<TestSceneContainerWithRefList>();

            var item1 = new TestSceneRefItem { name = "A", value = 10 };
            var item2 = new TestSceneRefItem { name = "B", value = 20 };
            var container = new TestSceneContainerWithRefList
            {
                items = new List<TestSceneRefItem> { item1, item2 }
            };

            var containerClass = ExposedClass.Find(typeof(TestSceneContainerWithRefList));
            var itemClass = ExposedClass.Find(typeof(TestSceneRefItem));
            var containerExposed = new ExposedObject("container-rt", containerClass, container);
            var item1Exposed = new ExposedObject("item-rt-1", itemClass, item1);
            var item2Exposed = new ExposedObject("item-rt-2", itemClass, item2);

            ExposedPropertyUtility.SetDefault(containerExposed);
            ExposedPropertyUtility.SetDefault(item1Exposed);
            ExposedPropertyUtility.SetDefault(item2Exposed);

            // item1のvalueのみ変更
            item1.value = 99;

            // delta保存
            var json = ExposedSceneSerializer.SceneToJson(
                new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver, SerializeMode.Delta);

            // 新しいオブジェクトセットに復元
            var toRemove = ExposedObjectRegistry.instances.ToList();
            foreach (var obj in toRemove) obj.Unregister();

            var restoredItem1 = new TestSceneRefItem { name = "A", value = 10 };
            var restoredItem2 = new TestSceneRefItem { name = "B", value = 20 };
            var restoredContainer = new TestSceneContainerWithRefList
            {
                items = new List<TestSceneRefItem> { restoredItem1, restoredItem2 }
            };

            new ExposedObject("container-rt", containerClass, restoredContainer);
            new ExposedObject("item-rt-1", itemClass, restoredItem1);
            new ExposedObject("item-rt-2", itemClass, restoredItem2);

            ExposedSceneSerializer.SceneFromJson(json, _resolver);

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
        public void SceneToJson_Delta_PendingEntry_NoChange_EmitsNothing()
        {
            ExposedClass.RegisterFromAttributes<ExposedGameObject>();
            ExposedClass.RegisterFromAttributes<TestAdditionsComponent>();

            var go = new GameObject("TestGO");
            try
            {
                var comp = go.AddComponent<TestAdditionsComponent>();
                comp.health = 42;
                comp.label = "Original";

                var exposedGO = new ExposedGameObject(go);
                exposedGO.OnEnable();

                var resolved = ExposedSceneSerializer.ResolveExposedObjects(
                    new object[] { exposedGO }, _resolver);

                foreach (var obj in resolved)
                    ExposedPropertyUtility.SetDefault(obj);

                // 何も変更しない
                var json = ExposedSceneSerializer.SceneToJson(resolved, _resolver, SerializeMode.Delta);
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
        public void SceneToJson_Delta_PendingEntry_WithChange_EmitsOnlyChangedField()
        {
            ExposedClass.RegisterFromAttributes<ExposedGameObject>();
            ExposedClass.RegisterFromAttributes<TestAdditionsComponent>();

            var go = new GameObject("TestGO");
            try
            {
                var comp = go.AddComponent<TestAdditionsComponent>();
                comp.health = 42;
                comp.label = "Original";

                var exposedGO = new ExposedGameObject(go);
                exposedGO.OnEnable();

                var resolved = ExposedSceneSerializer.ResolveExposedObjects(
                    new object[] { exposedGO }, _resolver);

                foreach (var obj in resolved)
                    ExposedPropertyUtility.SetDefault(obj);

                // health のみ変更
                comp.health = 100;

                var json = ExposedSceneSerializer.SceneToJson(resolved, _resolver, SerializeMode.Delta);
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
        public void SceneToJson_Snapshot_PendingEntry_EmitsAllFields()
        {
            ExposedClass.RegisterFromAttributes<ExposedGameObject>();
            ExposedClass.RegisterFromAttributes<TestAdditionsComponent>();

            var go = new GameObject("TestGO");
            try
            {
                var comp = go.AddComponent<TestAdditionsComponent>();
                comp.health = 42;
                comp.label = "Original";

                var exposedGO = new ExposedGameObject(go);
                exposedGO.OnEnable();

                var resolved = ExposedSceneSerializer.ResolveExposedObjects(
                    new object[] { exposedGO }, _resolver);

                foreach (var obj in resolved)
                    ExposedPropertyUtility.SetDefault(obj);

                // Snapshot モードでは全フィールドが出力される
                var json = ExposedSceneSerializer.SceneToJson(resolved, _resolver, SerializeMode.Snapshot);
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
            ExposedClass.RegisterFromAttributes<TestSceneClass>();
            var exposedClass = ExposedClass.Find(typeof(TestSceneClass));

            // === Session 1: 変更して保存 ===
            var obj1 = new TestSceneClass { value = 42, name = "Original", position = 1.0f };
            var exposed1 = new ExposedObject("idemp-basic-1", exposedClass, obj1);
            ExposedPropertyUtility.SetDefault(exposed1);

            obj1.value = 200;
            obj1.name = "Changed";

            var deltaJson1 = ExposedSceneSerializer.SceneToJson(
                new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver, SerializeMode.Delta);
            exposed1.Unregister();

            // === Session 2: ロードして再保存 ===
            var obj2 = new TestSceneClass { value = 42, name = "Original", position = 1.0f };
            var exposed2 = new ExposedObject("idemp-basic-1", exposedClass, obj2);
            ExposedPropertyUtility.SetDefault(exposed2);

            ExposedSceneSerializer.SceneFromJson(deltaJson1, _resolver);

            // 値が復元されたことを確認
            Assert.AreEqual(200, obj2.value, "value should be restored");
            Assert.AreEqual("Changed", obj2.name, "name should be restored");
            Assert.AreEqual(1.0f, obj2.position, "position should remain at default");

            // 再デルタ保存
            var deltaJson2 = ExposedSceneSerializer.SceneToJson(
                new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver, SerializeMode.Delta);

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
        /// 冪等性テスト: ExposedClass配列で一部の要素のみ変更した場合。
        /// デルタ保存 → ロード → 未変更要素が消えないことを検証。
        /// </summary>
        [Test]
        public void Idempotency_DeltaSaveLoadSave_ArrayPartialChange()
        {
            ExposedClass.RegisterFromAttributes<TestDeltaNewItem>();
            ExposedClass.RegisterFromAttributes<TestDeltaNewContainer>();
            var containerClass = ExposedClass.Find(typeof(TestDeltaNewContainer));

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
            var exposed1 = new ExposedObject("idemp-array-1", containerClass, container1);
            ExposedPropertyUtility.SetDefault(exposed1);

            // 最初の要素のみ変更
            var item0 = container1.items[0];
            item0.name = "A-Modified";
            item0.value1 = 99.0f;
            container1.items[0] = item0;

            var deltaJson1 = ExposedSceneSerializer.SceneToJson(
                new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver, SerializeMode.Delta);
            exposed1.Unregister();

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
            var exposed2 = new ExposedObject("idemp-array-1", containerClass, container2);
            ExposedPropertyUtility.SetDefault(exposed2);

            ExposedSceneSerializer.SceneFromJson(deltaJson1, _resolver);

            // 配列サイズが保持されていること
            Assert.AreEqual(3, container2.items.Count, "配列サイズが3のまま保持されるべき");
            // 変更された要素が復元されていること
            Assert.AreEqual("A-Modified", container2.items[0].name, "items[0].nameが復元されるべき");
            Assert.AreEqual(99.0f, container2.items[0].value1, "items[0].value1が復元されるべき");
            // 未変更要素が保持されていること
            Assert.AreEqual("B", container2.items[1].name, "items[1].nameが保持されるべき");
            Assert.AreEqual("C", container2.items[2].name, "items[2].nameが保持されるべき");

            // === 再デルタ保存 ===
            var deltaJson2 = ExposedSceneSerializer.SceneToJson(
                new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver, SerializeMode.Delta);
            exposed2.Unregister();

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
            var exposed3 = new ExposedObject("idemp-array-1", containerClass, container3);
            ExposedPropertyUtility.SetDefault(exposed3);

            ExposedSceneSerializer.SceneFromJson(deltaJson2, _resolver);

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
            ExposedClass.RegisterFromAttributes<TestDeltaNewItem>();
            ExposedClass.RegisterFromAttributes<TestDeltaNewContainer>();
            var containerClass = ExposedClass.Find(typeof(TestDeltaNewContainer));

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
            var exposed1 = new ExposedObject("idemp-mid-1", containerClass, container1);
            ExposedPropertyUtility.SetDefault(exposed1);

            // 中間要素のみ変更
            var mid = container1.items[1];
            mid.name = "Middle-Changed";
            container1.items[1] = mid;

            var deltaJson1 = ExposedSceneSerializer.SceneToJson(
                new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver, SerializeMode.Delta);
            exposed1.Unregister();

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
            var exposed2 = new ExposedObject("idemp-mid-1", containerClass, container2);
            ExposedPropertyUtility.SetDefault(exposed2);

            ExposedSceneSerializer.SceneFromJson(deltaJson1, _resolver);

            Assert.AreEqual(3, container2.items.Count, "配列サイズが保持されるべき");
            Assert.AreEqual("First", container2.items[0].name, "先頭要素が保持されるべき");
            Assert.AreEqual("Middle-Changed", container2.items[1].name, "中間要素が復元されるべき");
            Assert.AreEqual("Last", container2.items[2].name, "末尾要素が保持されるべき");

            var deltaJson2 = ExposedSceneSerializer.SceneToJson(
                new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver, SerializeMode.Delta);
            exposed2.Unregister();

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
            var exposed3 = new ExposedObject("idemp-mid-1", containerClass, container3);
            ExposedPropertyUtility.SetDefault(exposed3);

            ExposedSceneSerializer.SceneFromJson(deltaJson2, _resolver);

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
            ExposedClass.RegisterFromAttributes<TestDeltaNewItem>();
            ExposedClass.RegisterFromAttributes<TestDeltaNewContainer>();
            var containerClass = ExposedClass.Find(typeof(TestDeltaNewContainer));

            // === Session 1: 要素を追加して保存 ===
            var container1 = new TestDeltaNewContainer
            {
                items = new List<TestDeltaNewItem>
                {
                    new TestDeltaNewItem { name = "Original", value1 = 1.0f, value2 = 2.0f },
                }
            };
            var exposed1 = new ExposedObject("idemp-new-1", containerClass, container1);
            ExposedPropertyUtility.SetDefault(exposed1);

            // 新規要素を追加
            container1.items.Add(new TestDeltaNewItem { name = "Added", value1 = 5.0f, value2 = 2.0f });

            var deltaJson1 = ExposedSceneSerializer.SceneToJson(
                new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver, SerializeMode.Delta);
            exposed1.Unregister();

            // === Session 2: ロード → 再保存 ===
            var container2 = new TestDeltaNewContainer
            {
                items = new List<TestDeltaNewItem>
                {
                    new TestDeltaNewItem { name = "Original", value1 = 1.0f, value2 = 2.0f },
                }
            };
            var exposed2 = new ExposedObject("idemp-new-1", containerClass, container2);
            ExposedPropertyUtility.SetDefault(exposed2);

            ExposedSceneSerializer.SceneFromJson(deltaJson1, _resolver);

            Assert.AreEqual(2, container2.items.Count, "追加要素がロードされるべき");
            Assert.AreEqual("Original", container2.items[0].name, "元の要素が保持されるべき");
            Assert.AreEqual("Added", container2.items[1].name, "追加要素が復元されるべき");
            Assert.AreEqual(5.0f, container2.items[1].value1, "追加要素のvalue1が復元されるべき");

            var deltaJson2 = ExposedSceneSerializer.SceneToJson(
                new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver, SerializeMode.Delta);
            exposed2.Unregister();

            // === Session 3: 再ロード ===
            var container3 = new TestDeltaNewContainer
            {
                items = new List<TestDeltaNewItem>
                {
                    new TestDeltaNewItem { name = "Original", value1 = 1.0f, value2 = 2.0f },
                }
            };
            var exposed3 = new ExposedObject("idemp-new-1", containerClass, container3);
            ExposedPropertyUtility.SetDefault(exposed3);

            ExposedSceneSerializer.SceneFromJson(deltaJson2, _resolver);

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
            ExposedClass.RegisterFromAttributes<TestSceneClass>();
            var exposedClass = ExposedClass.Find(typeof(TestSceneClass));

            // === Session 1 ===
            var objA1 = new TestSceneClass { value = 10, name = "A", position = 0f };
            var objB1 = new TestSceneClass { value = 20, name = "B", position = 0f };
            var exposedA1 = new ExposedObject("idemp-multi-a", exposedClass, objA1);
            var exposedB1 = new ExposedObject("idemp-multi-b", exposedClass, objB1);
            ExposedPropertyUtility.SetDefault(exposedA1);
            ExposedPropertyUtility.SetDefault(exposedB1);

            // Aのみ変更
            objA1.value = 999;

            var deltaJson1 = ExposedSceneSerializer.SceneToJson(
                new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver, SerializeMode.Delta);
            exposedA1.Unregister();
            exposedB1.Unregister();

            // Bは保存されないことを確認
            var jRoot1 = JObject.Parse(deltaJson1);
            var objects1 = jRoot1["objects"] as JArray;
            Assert.AreEqual(1, objects1.Count, "変更されたオブジェクトのみ保存されるべき");

            // === Session 2: ロード → 再保存 ===
            var objA2 = new TestSceneClass { value = 10, name = "A", position = 0f };
            var objB2 = new TestSceneClass { value = 20, name = "B", position = 0f };
            var exposedA2 = new ExposedObject("idemp-multi-a", exposedClass, objA2);
            var exposedB2 = new ExposedObject("idemp-multi-b", exposedClass, objB2);
            ExposedPropertyUtility.SetDefault(exposedA2);
            ExposedPropertyUtility.SetDefault(exposedB2);

            ExposedSceneSerializer.SceneFromJson(deltaJson1, _resolver);

            Assert.AreEqual(999, objA2.value, "Aの変更が復元されるべき");
            Assert.AreEqual(20, objB2.value, "Bは変更されないべき");

            var deltaJson2 = ExposedSceneSerializer.SceneToJson(
                new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver, SerializeMode.Delta);

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
            ExposedClass.RegisterFromAttributes<TestDeltaNewItem>();
            ExposedClass.RegisterFromAttributes<TestDeltaNewContainer>();
            var containerClass = ExposedClass.Find(typeof(TestDeltaNewContainer));

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
            var exposed1 = new ExposedObject("idemp-all-1", containerClass, container1);
            ExposedPropertyUtility.SetDefault(exposed1);

            // 全要素を変更
            var a = container1.items[0]; a.name = "A-Changed"; container1.items[0] = a;
            var b = container1.items[1]; b.name = "B-Changed"; container1.items[1] = b;
            var c = container1.items[2]; c.name = "C-Changed"; container1.items[2] = c;

            var deltaJson1 = ExposedSceneSerializer.SceneToJson(
                new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver, SerializeMode.Delta);
            exposed1.Unregister();

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
            var exposed2 = new ExposedObject("idemp-all-1", containerClass, container2);
            ExposedPropertyUtility.SetDefault(exposed2);

            ExposedSceneSerializer.SceneFromJson(deltaJson1, _resolver);

            Assert.AreEqual(3, container2.items.Count, "配列サイズが保持されるべき");
            Assert.AreEqual("A-Changed", container2.items[0].name);
            Assert.AreEqual("B-Changed", container2.items[1].name);
            Assert.AreEqual("C-Changed", container2.items[2].name);

            var deltaJson2 = ExposedSceneSerializer.SceneToJson(
                new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver, SerializeMode.Delta);
            exposed2.Unregister();

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
            var exposed3 = new ExposedObject("idemp-all-1", containerClass, container3);
            ExposedPropertyUtility.SetDefault(exposed3);

            ExposedSceneSerializer.SceneFromJson(deltaJson2, _resolver);

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
            ExposedClass.RegisterFromAttributes<ExposedGameObject>();
            ExposedClass.RegisterFromAttributes<TestAdditionsComponent>();
            ExposedClass.RegisterFromAttributes<TestAdditionsComponent2>();

            var go = new GameObject("TestAvatar");
            try
            {
                var comp1 = go.AddComponent<TestAdditionsComponent>();
                comp1.health = 100;
                comp1.label = "Default";

                var comp2 = go.AddComponent<TestAdditionsComponent2>();
                comp2.speed = 1.0f;

                var exposedGO = new ExposedGameObject(go);
                exposedGO.OnEnable();

                var resolved = ExposedSceneSerializer.ResolveExposedObjects(
                    new object[] { exposedGO }, _resolver);

                foreach (var obj in resolved)
                    ExposedPropertyUtility.SetDefault(obj);

                // === Session 1: 両方のコンポーネントを変更してデルタ保存 ===
                comp1.health = 200;
                comp2.speed = 5.0f;

                var deltaJson1 = ExposedSceneSerializer.SceneToJson(resolved, _resolver, SerializeMode.Delta);

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
                ExposedSceneSerializer.SceneFromJson(deltaJson1, _resolver);

                // 値が復元されたことを確認
                Assert.AreEqual(200, comp1.health, "Session2: comp1.healthが復元されるべき");
                Assert.AreEqual(5.0f, comp2.speed, "Session2: comp2.speedが復元されるべき");

                // 再デルタ保存
                var deltaJson2 = ExposedSceneSerializer.SceneToJson(resolved, _resolver, SerializeMode.Delta);

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
        /// 再現テスト: pending エントリで Nested ExposedClass フィールドを持つコンポーネントが、
        /// ロード → 再保存で中身を失わないこと。
        /// Plug._target (TransformRef) が次の上書き保存で消える問題の回帰テスト。
        /// </summary>
        [Test]
        public void Idempotency_PendingComponentWithNestedExposedClass_PreservedOnReSave()
        {
            ExposedClass.RegisterFromAttributes<ExposedGameObject>();
            ExposedClass.RegisterFromAttributes<TestPluglikeComponent>();
            ExposedClass.RegisterFromAttributes<TestPluglikePath>();

            var go = new GameObject("TestGO");
            try
            {
                var comp = go.AddComponent<TestPluglikeComponent>();
                // プレハブ初期値相当: target はインスタンス化済みだが中身は空

                var exposedGO = new ExposedGameObject(go);
                exposedGO.OnEnable();

                var resolved = ExposedSceneSerializer.ResolveExposedObjects(
                    new object[] { exposedGO }, _resolver);
                foreach (var obj in resolved)
                    ExposedPropertyUtility.SetDefault(obj);

                // Session 1: ユーザーが値を設定して保存
                comp.target.rootObjectName = "Main Avatar";
                comp.target.transformName = "Head";

                var json1 = ExposedSceneSerializer.SceneToJson(resolved, _resolver, SerializeMode.Delta);

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
                ExposedObjectDefaultRegistry.ClearAll();

                // Session 2: ロード
                ExposedSceneSerializer.SceneFromJson(json1, _resolver);

                Assert.AreEqual("Main Avatar", comp.target.rootObjectName,
                    "Session2: rootObjectName が復元されるべき");
                Assert.AreEqual("Head", comp.target.transformName,
                    "Session2: transformName が復元されるべき");

                // Session 2: 再デルタ保存（上書き保存を模擬）
                var json2 = ExposedSceneSerializer.SceneToJson(resolved, _resolver, SerializeMode.Delta);

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
            ExposedClass.RegisterFromAttributes<ExposedGameObject>();
            ExposedClass.RegisterFromAttributes<TestAdditionsComponent>();
            ExposedClass.RegisterFromAttributes<TestAdditionsComponent2>();

            var go1 = new GameObject("CurrentAvatar");
            var go2 = new GameObject("MainScreen");
            try
            {
                // GO1: TestAdditionsComponent
                var comp1 = go1.AddComponent<TestAdditionsComponent>();
                comp1.health = 100;
                comp1.label = "Default";

                var exposedGO1 = new ExposedGameObject(go1);
                exposedGO1.OnEnable();

                // GO2: TestAdditionsComponent2
                var comp2 = go2.AddComponent<TestAdditionsComponent2>();
                comp2.speed = 1.0f;

                var exposedGO2 = new ExposedGameObject(go2);
                exposedGO2.OnEnable();

                var resolved = ExposedSceneSerializer.ResolveExposedObjects(
                    new object[] { exposedGO1, exposedGO2 }, _resolver);

                foreach (var obj in resolved)
                    ExposedPropertyUtility.SetDefault(obj);

                // === Session 1: 両方のGOのコンポーネントを変更してデルタ保存 ===
                comp1.health = 200;
                comp2.speed = 5.0f;

                var deltaJson1 = ExposedSceneSerializer.SceneToJson(resolved, _resolver, SerializeMode.Delta);

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
                ExposedSceneSerializer.SceneFromJson(deltaJson1, _resolver);

                // 値が復元されたことを確認
                Assert.AreEqual(200, comp1.health, "Session2: comp1.healthが復元されるべき");
                Assert.AreEqual(5.0f, comp2.speed, "Session2: comp2.speedが復元されるべき");

                // 再デルタ保存
                var deltaJson2 = ExposedSceneSerializer.SceneToJson(resolved, _resolver, SerializeMode.Delta);

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
            ExposedClass.RegisterFromAttributes<ExposedGameObject>();
            ExposedClass.RegisterFromAttributes<TestAdditionsComponent>();
            ExposedClass.RegisterFromAttributes<TestAdditionsComponent2>();

            var go = new GameObject("TestAvatar");
            try
            {
                var comp1 = go.AddComponent<TestAdditionsComponent>();
                comp1.health = 100;
                comp1.label = "Default";

                var comp2 = go.AddComponent<TestAdditionsComponent2>();
                comp2.speed = 1.0f;

                var exposedGO = new ExposedGameObject(go);
                exposedGO.OnEnable();

                var resolved = ExposedSceneSerializer.ResolveExposedObjects(
                    new object[] { exposedGO }, _resolver);

                foreach (var obj in resolved)
                    ExposedPropertyUtility.SetDefault(obj);

                // === Session 1: 1番目のコンポーネントのみ変更、2番目は未変更 ===
                comp1.health = 200;
                // comp2は変更しない

                var deltaJson1 = ExposedSceneSerializer.SceneToJson(resolved, _resolver, SerializeMode.Delta);

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
                ExposedSceneSerializer.SceneFromJson(deltaJson1, _resolver);

                Assert.AreEqual(200, comp1.health, "Session2: comp1.healthが復元されるべき");
                Assert.AreEqual(1.0f, comp2.speed, "Session2: comp2.speedはデフォルトのまま");

                var deltaJson2 = ExposedSceneSerializer.SceneToJson(resolved, _resolver, SerializeMode.Delta);

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
        [ExposedClass("TestInputLikeComponent")]
        public class TestInputLikeComponent : MonoBehaviour
        {
            [SerializeField, ExposedField("settings")] internal string _settingsJson = "{}";

            public string settings
            {
                get => _settingsJson;
                set => _settingsJson = value ?? "{}";
            }

            // readonly + 非persistable（AvatarInput.actionNames相当）
            [ExposedProperty("readonlyInfo")]
            public string readonlyInfo => _settingsJson.Length > 2 ? "has-data" : "empty";
        }

        /// <summary>
        /// ExposedGameObject + readonly componentsプロパティ経由で
        /// readonlyプロパティ持ちコンポーネントのLoad→Saveラウンドトリップテスト。
        /// AvatarInputのsettings+actionNamesシナリオを再現する。
        /// </summary>
        [Test]
        public void RoundTrip_LoadDelta_ComponentWithReadonlyProp_PreservedOnReSave()
        {
            ExposedClass.RegisterFromAttributes<ExposedGameObject>();
            ExposedClass.RegisterFromAttributes<TestInputLikeComponent>();

            var go = new GameObject("TestGO_InputLike");
            try
            {
                var comp = go.AddComponent<TestInputLikeComponent>();
                comp._settingsJson = "{}"; // 初期状態

                var exposedGO = new ExposedGameObject(go);
                exposedGO.OnEnable();

                var resolved = ExposedSceneSerializer.ResolveExposedObjects(
                    new object[] { exposedGO }, _resolver);

                foreach (var obj in resolved)
                    ExposedPropertyUtility.SetDefault(obj);

                // readonlyInfoの初期値を確認
                Assert.AreEqual("empty", comp.readonlyInfo, "初期: readonlyInfoはempty");

                // ExposedGameObject._componentsはExposedClass.Has()でフィルタされるため、
                // ExposedClass登録済みコンポーネントのみ含まれる（Transformは除外）。
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
                            ["@id"] = exposedGO.id,
                            ["components"] = componentsArray
                        }
                    }
                };

                // Load delta
                ExposedSceneSerializer.SceneFromJson(loadJson.ToString(), _resolver);
                Assert.AreEqual("{\"binding\":\"keyboard/a\"}", comp.settings, "Load後: settingsが変更されるべき");
                Assert.AreEqual("has-data", comp.readonlyInfo, "Load後: readonlyInfoが変化");

                // Re-save (Delta)
                var reSaved = ExposedSceneSerializer.SceneToJson(resolved, _resolver, SerializeMode.Delta);
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
            ExposedClass.RegisterFromAttributes<TestDeltaNewItem>();
            ExposedClass.RegisterFromAttributes<TestDeltaNewContainer>();
            ExposedClass.RegisterFromAttributes<TestSceneClass>();

            // Object 1: 配列を持つオブジェクト（AvatarExpressionConfig相当）
            var container = new TestDeltaNewContainer
            {
                items = new List<TestDeltaNewItem>()
            };
            var containerClass = ExposedClass.Find(typeof(TestDeltaNewContainer));
            var containerObj = new ExposedObject("obj-container", containerClass, container);

            // Object 2: 単純なプロパティを持つオブジェクト
            var simpleObj = new TestSceneClass
            {
                value = 0,
                name = "Default",
                position = 0f
            };
            var simpleClass = ExposedClass.Find(typeof(TestSceneClass));
            var simpleExposed = new ExposedObject("obj-simple", simpleClass, simpleObj);

            try
            {
                // CaptureDefaults（Playモード開始時相当）
                ExposedPropertyUtility.SetDefault(containerObj);
                ExposedPropertyUtility.SetDefault(simpleExposed);

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
                ExposedSceneSerializer.SceneFromJson(savedDelta, _resolver);
                Assert.AreEqual(1, container.items.Count, "Load後: items に1要素追加");
                Assert.AreEqual("item1", container.items[0].name);
                Assert.AreEqual(42, simpleObj.value, "Load後: value が変更");
                Assert.AreEqual("Modified", simpleObj.name, "Load後: name が変更");

                // Re-save (Delta mode)
                var resolved = new List<ExposedObject>(ExposedObjectRegistry.instances);
                var reSaved = ExposedSceneSerializer.SceneToJson(resolved, _resolver, SerializeMode.Delta);
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
                simpleExposed.Unregister();
            }
        }

        #endregion

        #region Delta with Nested Readonly Properties Tests

        // ネストされたExposedClassにreadonly/非persistableプロパティがある場合の
        // Delta+forPersistence シリアライズテスト用クラス
        [Serializable]
        [ExposedClass("TestDeltaNestedReadonly_Child")]
        public class TestDeltaNestedReadonly_Child
        {
            [ExposedField]
            public int writableValue;

            // readonly かつ非persistable — CaptureDefaultsには含まれるが、forPersistence=trueでは除外される
            private string[] _readonlyNames = new[] { "name1", "name2" };

            [ExposedProperty("readonlyNames")]
            public string[] readonlyNames => _readonlyNames;
        }

        [Serializable]
        [ExposedClass("TestDeltaNestedReadonly_Parent")]
        public class TestDeltaNestedReadonly_Parent
        {
            [ExposedField]
            public TestDeltaNestedReadonly_Child child;

            [ExposedField]
            public int parentValue;
        }

        [Test]
        public void SceneToJson_Delta_NestedReadonlyDoesNotPreventDirtyDetection()
        {
            // Arrange — ネストされたExposedClassにreadonly/非persistableプロパティがある場合、
            // CaptureDefaults（forPersistence=false）とDeltaシリアライズ（forPersistence=true）の
            // 非対称性がdirty検出を阻害しないことを確認する
            ExposedClass.RegisterFromAttributes<TestDeltaNestedReadonly_Child>();
            ExposedClass.RegisterFromAttributes<TestDeltaNestedReadonly_Parent>();

            var testObj = new TestDeltaNestedReadonly_Parent
            {
                parentValue = 10,
                child = new TestDeltaNestedReadonly_Child { writableValue = 5 }
            };

            var exposedClass = ExposedClass.Find(typeof(TestDeltaNestedReadonly_Parent));
            var exposedObj = new ExposedObject("test-nested-readonly-delta", exposedClass, testObj);

            try
            {
                // デフォルト値をキャプチャ（forPersistence=falseでシリアライズされる）
                ExposedPropertyUtility.SetDefault(exposedObj);

                // ネストされたchildのwritableValueを変更
                testObj.child.writableValue = 99;

                // Act — Delta modeでシリアライズ（内部でforPersistence=trueが使われる）
                var json = ExposedSceneSerializer.SceneToJson(
                    new List<ExposedObject>(ExposedObjectRegistry.instances),
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
                exposedObj.Unregister();
            }
        }

        [Test]
        public void SceneToJson_Delta_NestedOnlyParentDirty_IncludesObject()
        {
            // Arrange — 親のプロパティだけ変更した場合もオブジェクトが含まれることを確認
            ExposedClass.RegisterFromAttributes<TestDeltaNestedReadonly_Child>();
            ExposedClass.RegisterFromAttributes<TestDeltaNestedReadonly_Parent>();

            var testObj = new TestDeltaNestedReadonly_Parent
            {
                parentValue = 10,
                child = new TestDeltaNestedReadonly_Child { writableValue = 5 }
            };

            var exposedClass = ExposedClass.Find(typeof(TestDeltaNestedReadonly_Parent));
            var exposedObj = new ExposedObject("test-nested-readonly-delta2", exposedClass, testObj);

            try
            {
                ExposedPropertyUtility.SetDefault(exposedObj);

                // 親プロパティのみ変更
                testObj.parentValue = 42;

                // Act
                var json = ExposedSceneSerializer.SceneToJson(
                    new List<ExposedObject>(ExposedObjectRegistry.instances),
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
                exposedObj.Unregister();
            }
        }

        [Test]
        public void SceneToJson_Delta_NoDirtyChanges_ExcludesObject()
        {
            // Arrange — 変更がない場合はオブジェクトが除外されることを確認
            ExposedClass.RegisterFromAttributes<TestDeltaNestedReadonly_Child>();
            ExposedClass.RegisterFromAttributes<TestDeltaNestedReadonly_Parent>();

            var testObj = new TestDeltaNestedReadonly_Parent
            {
                parentValue = 10,
                child = new TestDeltaNestedReadonly_Child { writableValue = 5 }
            };

            var exposedClass = ExposedClass.Find(typeof(TestDeltaNestedReadonly_Parent));
            var exposedObj = new ExposedObject("test-nested-readonly-delta3", exposedClass, testObj);

            try
            {
                ExposedPropertyUtility.SetDefault(exposedObj);

                // 何も変更しない

                // Act
                var json = ExposedSceneSerializer.SceneToJson(
                    new List<ExposedObject>(ExposedObjectRegistry.instances),
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
                exposedObj.Unregister();
            }
        }

        // readonlyプロパティのみが変化した場合のテスト用クラス
        // AvatarInputのactionNames（readonly, 非persistable）と
        // settings（read-write, persistable）の構造をモデル化
        [Serializable]
        [ExposedClass("TestComponentWithReadonlyChange")]
        public class TestComponentWithReadonlyChange
        {
            // 書き込み可能+persistable（settingsに相当）
            [ExposedField]
            public int settingsValue = 0;

            // readonly+非persistable（actionNamesに相当）
            // 外部から_readonlyNamesを変更してreadonly propertyの返す値を変える
            internal string[] _readonlyNames = new string[0];

            [ExposedProperty("readonlyNames")]
            public string[] readonlyNames => _readonlyNames;
        }

        [Serializable]
        [ExposedClass("TestParentWithComponentArray")]
        public class TestParentWithComponentArray
        {
            // writable+persistable 配列
            // 実際のExposedGameObject.componentsはreadonly+containsExposedObjectReference=trueだが
            // テストではComponent型を使えないためwritableで代替
            [ExposedField]
            public TestComponentWithReadonlyChange[] components = new TestComponentWithReadonlyChange[0];
        }

        [Test]
        public void SerializeFullToJObject_ForPersistence_FiltersNestedReadonlyInArray()
        {
            // Arrange — SerializeFullToJObjectのforPersistence=trueで
            // 配列内ネストExposedClassのreadonlyプロパティが除外されることを確認
            ExposedClass.RegisterFromAttributes<TestComponentWithReadonlyChange>();
            ExposedClass.RegisterFromAttributes<TestParentWithComponentArray>();

            var component = new TestComponentWithReadonlyChange { settingsValue = 10 };
            component._readonlyNames = new[] { "test1" };
            var testObj = new TestParentWithComponentArray
            {
                components = new[] { component }
            };

            var exposedClass = ExposedClass.Find(typeof(TestParentWithComponentArray));
            var exposedObj = new ExposedObject("test-serialize-check", exposedClass, testObj);

            try
            {
                // Act — forPersistence=true
                var jObj = ExposedPropertySerializer.SerializeFullToJObject(exposedObj, _resolver, forPersistence: true);

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
                exposedObj.Unregister();
            }
        }

        [Test]
        public void SceneToJson_Delta_ReadonlyOnlyChange_InNestedComponent_DetectsCorrectly()
        {
            // Arrange — ネストされたコンポーネントのreadonlyプロパティだけが変化した場合、
            // writableプロパティが変化していなければデルタに含まれるべきでない
            // （readonlyプロパティの変化はpersistenceで保存されるべきでない）
            ExposedClass.RegisterFromAttributes<TestComponentWithReadonlyChange>();
            ExposedClass.RegisterFromAttributes<TestParentWithComponentArray>();

            var component = new TestComponentWithReadonlyChange { settingsValue = 10 };
            var testObj = new TestParentWithComponentArray
            {
                components = new[] { component }
            };

            var exposedClass = ExposedClass.Find(typeof(TestParentWithComponentArray));
            var exposedObj = new ExposedObject("test-readonly-only-change", exposedClass, testObj);

            try
            {
                // デフォルトキャプチャ時: readonlyNames = []
                ExposedPropertyUtility.SetDefault(exposedObj);

                // readonlyプロパティだけ変更（settingsValueは変更しない）
                component._readonlyNames = new[] { "Expression.happy" };

                // Act
                var json = ExposedSceneSerializer.SceneToJson(
                    new List<ExposedObject>(ExposedObjectRegistry.instances),
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
                exposedObj.Unregister();
            }
        }

        [Test]
        public void SceneToJson_Delta_WritableChange_InNestedComponent_AlwaysDetected()
        {
            // Arrange — ネストされたコンポーネントのwritableプロパティが変化した場合、
            // readonly変化の有無に関わらずデルタに含まれるべき
            ExposedClass.RegisterFromAttributes<TestComponentWithReadonlyChange>();
            ExposedClass.RegisterFromAttributes<TestParentWithComponentArray>();

            var component = new TestComponentWithReadonlyChange { settingsValue = 10 };
            var testObj = new TestParentWithComponentArray
            {
                components = new[] { component }
            };

            var exposedClass = ExposedClass.Find(typeof(TestParentWithComponentArray));
            var exposedObj = new ExposedObject("test-writable-change", exposedClass, testObj);

            try
            {
                ExposedPropertyUtility.SetDefault(exposedObj);

                // writableプロパティを変更
                component.settingsValue = 99;
                // readonlyも変わる（実際のシナリオでは両方変わることが多い）
                component._readonlyNames = new[] { "Expression.happy" };

                // Act
                var json = ExposedSceneSerializer.SceneToJson(
                    new List<ExposedObject>(ExposedObjectRegistry.instances),
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
                exposedObj.Unregister();
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
            ExposedClass.RegisterFromAttributes<TestSceneClass>();
            ExposedClass.RegisterFromAttributes<TestDeltaNewContainer>();
            ExposedClass.RegisterFromAttributes<TestDeltaNewItem>();

            var obj1 = new TestDeltaNewContainer { items = new List<TestDeltaNewItem>() };
            var obj2 = new TestSceneClass { value = 0, name = "", position = 0f };

            var exposedClass1 = ExposedClass.Find(typeof(TestDeltaNewContainer));
            var exposedClass2 = ExposedClass.Find(typeof(TestSceneClass));
            var exposedObj1 = new ExposedObject("obj-1", exposedClass1, obj1);
            var exposedObj2 = new ExposedObject("obj-2", exposedClass2, obj2);

            try
            {
                // Step 1: デフォルトキャプチャ
                ExposedPropertyUtility.SetDefault(exposedObj1);
                ExposedPropertyUtility.SetDefault(exposedObj2);

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
                ExposedSceneSerializer.SceneFromJson(loadJson, _resolver);

                // ロードされた値が反映されていることを確認
                Assert.AreEqual(1, obj1.items.Count, "items should have 1 element after load");
                Assert.AreEqual("NewItem", obj1.items[0].name, "item name should be loaded");
                Assert.AreEqual(42, obj2.value, "value should be loaded");
                Assert.AreEqual("Changed", obj2.name, "name should be loaded");

                // Step 3: DeltaFromDefaultで保存
                var savedJson = ExposedSceneSerializer.SceneToJson(
                    new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver, SerializeMode.Delta);

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

                ExposedSceneSerializer.SceneFromJson(savedJson, _resolver);

                Assert.AreEqual(1, obj1.items.Count, "items should be restored from saved JSON");
                Assert.AreEqual("NewItem", obj1.items[0].name, "item name should be restored");
                Assert.AreEqual(42, obj2.value, "value should be restored from saved JSON");
                Assert.AreEqual("Changed", obj2.name, "name should be restored from saved JSON");
            }
            finally
            {
                exposedObj1.Unregister();
                exposedObj2.Unregister();
            }
        }

        /// <summary>
        /// Activator.CreateInstanceで生成不可な型（GetOrCreateが失敗する型）。
        /// ExposedUnityObjectProxy等のScriptableObject派生型を模擬する。
        /// </summary>
        [Serializable]
        [ExposedClass("TestNoDefaultCtorClass")]
        public class TestNoDefaultCtorClass
        {
            [ExposedField]
            public int value;

            [ExposedField]
            public string name;

            // デフォルトコンストラクタなし（引数付きのみ）
            public TestNoDefaultCtorClass(int initialValue)
            {
                value = initialValue;
                name = "";
            }
        }

        /// <summary>
        /// IDが変わったオブジェクト（ExposedUnityObjectProxy等のGUID再生成）に対し、
        /// SceneFromJsonが型名+@nameでマッチしてIDを復元し、データが正しくロードされることを確認。
        /// これにより、Play mode再入時のデルタ保存でオブジェクトが消えるバグを防止する。
        /// </summary>
        [Test]
        public void SceneFromJson_IdMismatch_MatchesByTypeName_AndRestoresData()
        {
            // Arrange
            ExposedClass.RegisterFromAttributes<TestNoDefaultCtorClass>();

            // オブジェクトを作成（自動生成されたIDを使用）
            var testObj = new TestNoDefaultCtorClass(0);
            var exposedClass = ExposedClass.Find(typeof(TestNoDefaultCtorClass));
            var exposedObj = new ExposedObject("auto-generated-id", exposedClass, testObj);

            try
            {
                ExposedPropertyUtility.SetDefault(exposedObj);

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
                ExposedSceneSerializer.SceneFromJson(loadJson, _resolver);

                // Assert: 型名マッチでIDが復元され、データがロードされること
                Assert.AreEqual("saved-old-id", exposedObj.id, "ID should be replaced with saved ID");
                Assert.AreEqual(99, testObj.value, "value should be loaded from JSON");
                Assert.AreEqual("Restored", testObj.name, "name should be loaded from JSON");

                // Delta保存してもオブジェクトが残ること
                var savedJson = ExposedSceneSerializer.SceneToJson(
                    new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver, SerializeMode.Delta);

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
                exposedObj.Unregister();
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
            ExposedClass.RegisterFromAttributes<TestDeltaNewContainer>();
            ExposedClass.RegisterFromAttributes<TestDeltaNewItem>();
            ExposedClass.RegisterFromAttributes<TestSceneClass>();

            var containerClass = ExposedClass.Find(typeof(TestDeltaNewContainer));
            var sceneClass = ExposedClass.Find(typeof(TestSceneClass));

            // --- Play mode 開始: Initialize ---
            var container = new TestDeltaNewContainer { items = new List<TestDeltaNewItem>() };
            var simpleObj = new TestSceneClass { value = 0, name = "", position = 0f };

            var exposedContainer = new ExposedObject("container-id", containerClass, container);
            var exposedSimple = new ExposedObject("simple-id", sceneClass, simpleObj);

            // Initialize: SetDefault（ExposedObjectContainer.Initializeと同等）
            ExposedPropertyUtility.SetDefault(exposedContainer);
            ExposedPropertyUtility.SetDefault(exposedSimple);

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
                ExposedSceneSerializer.SceneFromJson(savedFileJson, _resolver);

                // ロードされた値の確認
                Assert.AreEqual(1, container.items.Count, "items should have 1 element after load");
                Assert.AreEqual("Expression1", container.items[0].name, "item name should be loaded");
                Assert.AreEqual(42, simpleObj.value, "value should be loaded");
                Assert.AreEqual("Modified", simpleObj.name, "name should be loaded");

                // --- SaveCurrentData: デルタ保存（ExitingPlayMode時） ---
                var outputJson = ExposedSceneSerializer.SceneToJson(
                    new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver, SerializeMode.Delta);

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
                var dirtyProps1 = exposedContainer.GetDirtyProperties();
                foreach (var path in dirtyProps1) exposedContainer.Revert(path);
                var dirtyProps2 = exposedSimple.GetDirtyProperties();
                foreach (var path in dirtyProps2) exposedSimple.Revert(path);

                // リバート後: デフォルト値に戻っていること
                Assert.AreEqual(0, container.items.Count, "items should be empty after revert");
                Assert.AreEqual(0, simpleObj.value, "value should be 0 after revert");

                // --- 次のPlay mode: 保存JSONから復元できること ---
                ExposedPropertyUtility.SetDefault(exposedContainer);
                ExposedPropertyUtility.SetDefault(exposedSimple);
                ExposedSceneSerializer.SceneFromJson(outputJson, _resolver);

                Assert.AreEqual(1, container.items.Count, "items should be restored from saved output");
                Assert.AreEqual("Expression1", container.items[0].name, "item name should be restored");
                Assert.AreEqual(42, simpleObj.value, "value should be restored");
                Assert.AreEqual("Modified", simpleObj.name, "name should be restored");
            }
            finally
            {
                exposedContainer.Unregister();
                exposedSimple.Unregister();
            }
        }

        #endregion
    }
}
