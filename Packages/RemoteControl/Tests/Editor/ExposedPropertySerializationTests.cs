using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Lilium.RemoteControl;
using Newtonsoft.Json.Linq;

namespace Lilium.RemoteControl.Tests
{
    [TestFixture]
    public class ExposedPropertySerializationTests
    {
        #region Test Classes

        // JsonUtilityでシリアライズ可能なシンプルなクラス
        [Serializable]
        public class SimpleClass
        {
            public int intValue;
            public string stringValue;
            public float floatValue;
            public bool boolValue;
        }

        // ネストしたクラス
        [Serializable]
        public class NestedClass
        {
            public int id;
            public string name;
        }

        [Serializable]
        public class ComplexClass
        {
            public SimpleClass simpleObject;
            public NestedClass nestedObject;

            public NestedClass[] arrayOfNested;

            public int value;
        }


        [Serializable]
        public class ArrayClass
        {
            public NestedClass[] arrayOfNested;
        }


        // ExposedClassとして登録して使用するクラス（属性使用）
        [Serializable]
        [ExposedClass("TestExposedParent")]
        public class ExposedClassWithNonExposedMembers
        {
            [ExposedField]
            public ArrayClass arrayData;


            [ExposedField]
            public SimpleClass simpleData;

            [ExposedField]
            public int registeredInt;

            [ExposedField]
            public string registeredText;

            public int unregisteredInt; // ExposedProperty属性がないため登録されない
        }

        // Enum型テスト用
        public enum TestPriority
        {
            Low = 0,
            Medium = 1,
            High = 2,
            Critical = 3
        }

        [Serializable]
        public class ClassWithEnum
        {
            public TestPriority priority;
            public string name;
        }

        #endregion

        [SetUp]
        public void SetUp()
        {
            ExposedClass.Clear();
        }

        #region Serialization Tests

        [Test]
        public void SerializeNonExposedClass_WithJsonUtility_SerializesCorrectly()
        {
            // Arrange
            var obj = new SimpleClass
            {
                intValue = 42,
                stringValue = "test",
                floatValue = 3.14f,
                boolValue = true
            };

            // Act
            var json = ExposedPropertySerializer.ToJson(obj);

            // Assert
            Assert.IsNotNull(json);
            Assert.IsFalse(string.IsNullOrEmpty(json));

            // JSONをパースして検証
            var jObject = JObject.Parse(json);
            var valueToken = jObject["value"] as JObject;
            Assert.IsNotNull(valueToken);

            Assert.AreEqual("SimpleClass", valueToken["@type"]?.Value<string>());
            Assert.AreEqual(42, valueToken["intValue"]?.Value<int>());
            Assert.AreEqual("test", valueToken["stringValue"]?.Value<string>());
            Assert.AreEqual(3.14f, valueToken["floatValue"]?.Value<float>(), 0.001f);
            Assert.AreEqual(true, valueToken["boolValue"]?.Value<bool>());
        }

        [Test]
        public void SerializeNonExposedClass_WithNestedObject_SerializesCorrectly()
        {
            // Arrange
            var obj = new ComplexClass
            {
                value = 100,
                simpleObject = new SimpleClass
                {
                    intValue = 42,
                    stringValue = "nested"
                },
                nestedObject = new NestedClass
                {
                    id = 1,
                    name = "test"
                }
            };

            // Act
            var json = ExposedPropertySerializer.ToJson(obj);

            // Assert
            Assert.IsNotNull(json);
            Assert.IsFalse(string.IsNullOrEmpty(json));

            // JSONをパースして検証
            var jObject = JObject.Parse(json);
            var valueToken = jObject["value"] as JObject;
            Assert.IsNotNull(valueToken);

            Assert.AreEqual("ComplexClass", valueToken["@type"]?.Value<string>());
            Assert.AreEqual(100, valueToken["value"]?.Value<int>());

            // simpleObjectの確認
            var simpleObj = valueToken["simpleObject"] as JObject;
            Assert.IsNotNull(simpleObj);
            Assert.AreEqual(42, simpleObj["intValue"]?.Value<int>());
            Assert.AreEqual("nested", simpleObj["stringValue"]?.Value<string>());

            // nestedObjectの確認
            var nestedObj = valueToken["nestedObject"] as JObject;
            Assert.IsNotNull(nestedObj);
            Assert.AreEqual(1, nestedObj["id"]?.Value<int>());
            Assert.AreEqual("test", nestedObj["name"]?.Value<string>());
        }

        #endregion

        #region Deserialization Tests

        [Test]
        public void DeserializeNonExposedClass_WithJsonUtility_DeserializesCorrectly()
        {
            // Arrange
            var json = "{\"value\":{\"intValue\":42,\"stringValue\":\"test\",\"floatValue\":3.14,\"boolValue\":true}}";

            // Act
            var result = ExposedPropertySerializer.FromJson<SimpleClass>(json);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsInstanceOf<SimpleClass>(result);

            Assert.AreEqual(42, result.intValue);
            Assert.AreEqual("test", result.stringValue);
            Assert.AreEqual(3.14f, result.floatValue, 0.001f);
            Assert.AreEqual(true, result.boolValue);
        }

        [Test]
        public void DeserializeNonExposedClass_WithExistingInstance_UpdatesValues()
        {
            // Arrange
            var json = "{\"value\":{\"intValue\":42,\"stringValue\":\"new\",\"floatValue\":3.14,\"boolValue\":true}}";

            // Act
            var result = ExposedPropertySerializer.FromJson(json, typeof(SimpleClass));

            // Assert
            Assert.IsNotNull(result);
            Assert.IsInstanceOf<SimpleClass>(result);

            var obj = (SimpleClass)result;
            Assert.AreEqual(42, obj.intValue);
            Assert.AreEqual("new", obj.stringValue);
            Assert.AreEqual(3.14f, obj.floatValue, 0.001f);
            Assert.AreEqual(true, obj.boolValue);
        }

        [Test]
        public void DeserializeNonExposedClass_WithNestedObject_DeserializesCorrectly()
        {
            // Arrange
            var json = "{\"value\":{\"value\":100,\"simpleObject\":{\"intValue\":42,\"stringValue\":\"nested\"},\"nestedObject\":{\"id\":1,\"name\":\"test\"}}}";

            // Act
            var result = ExposedPropertySerializer.FromJson<ComplexClass>(json);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsInstanceOf<ComplexClass>(result);

            Assert.AreEqual(100, result.value);

            Assert.IsNotNull(result.simpleObject);
            Assert.AreEqual(42, result.simpleObject.intValue);
            Assert.AreEqual("nested", result.simpleObject.stringValue);

            Assert.IsNotNull(result.nestedObject);
            Assert.AreEqual(1, result.nestedObject.id);
            Assert.AreEqual("test", result.nestedObject.name);
        }

        [Test]
        public void SerializeDeserialize_RoundTrip_PreservesData()
        {
            // Arrange
            var original = new SimpleClass
            {
                intValue = 42,
                stringValue = "test",
                floatValue = 3.14f,
                boolValue = true
            };

            // Act - Serialize
            var json = ExposedPropertySerializer.ToJson(original);

            // Act - Deserialize
            var result = ExposedPropertySerializer.FromJson<SimpleClass>(json);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsInstanceOf<SimpleClass>(result);

            Assert.AreEqual(original.intValue, result.intValue);
            Assert.AreEqual(original.stringValue, result.stringValue);
            Assert.AreEqual(original.floatValue, result.floatValue, 0.001f);
            Assert.AreEqual(original.boolValue, result.boolValue);
        }

        [Test]
        public void SerializeNonExposedClass_WithArray_SerializesCorrectly()
        {
            // Arrange
            var obj = new ComplexClass
            {
                value = 100,
                simpleObject = new SimpleClass
                {
                    intValue = 42,
                    stringValue = "simple"
                },
                nestedObject = new NestedClass
                {
                    id = 99,
                    name = "single"
                },
                arrayOfNested = new NestedClass[]
                {
                    new NestedClass { id = 1, name = "first" },
                    new NestedClass { id = 2, name = "second" },
                    new NestedClass { id = 3, name = "third" }
                }
            };

            // Act
            var json = ExposedPropertySerializer.ToJson(obj);

            // Assert
            Assert.IsNotNull(json);
            Assert.IsFalse(string.IsNullOrEmpty(json));

            // JSONをパースして検証
            var jObject = JObject.Parse(json);
            var valueToken = jObject["value"] as JObject;
            Assert.IsNotNull(valueToken);

            Assert.AreEqual("ComplexClass", valueToken["@type"]?.Value<string>());
            Assert.AreEqual(100, valueToken["value"]?.Value<int>());

            // simpleObjectの確認
            var simpleObj = valueToken["simpleObject"] as JObject;
            Assert.IsNotNull(simpleObj);
            Assert.AreEqual(42, simpleObj["intValue"]?.Value<int>());
            Assert.AreEqual("simple", simpleObj["stringValue"]?.Value<string>());

            // nestedObjectの確認
            var nestedObj = valueToken["nestedObject"] as JObject;
            Assert.IsNotNull(nestedObj);
            Assert.AreEqual(99, nestedObj["id"]?.Value<int>());
            Assert.AreEqual("single", nestedObj["name"]?.Value<string>());

            // arrayOfNestedの確認
            var arrayToken = valueToken["arrayOfNested"] as JArray;
            Assert.IsNotNull(arrayToken);
            Assert.AreEqual(3, arrayToken.Count);

            Assert.AreEqual(1, arrayToken[0]["id"]?.Value<int>());
            Assert.AreEqual("first", arrayToken[0]["name"]?.Value<string>());

            Assert.AreEqual(2, arrayToken[1]["id"]?.Value<int>());
            Assert.AreEqual("second", arrayToken[1]["name"]?.Value<string>());

            Assert.AreEqual(3, arrayToken[2]["id"]?.Value<int>());
            Assert.AreEqual("third", arrayToken[2]["name"]?.Value<string>());
        }

        [Test]
        public void DeserializeNonExposedClass_WithArray_DeserializesCorrectly()
        {
            // Arrange
            var json = "{\"value\":{\"value\":100,\"simpleObject\":{\"intValue\":42,\"stringValue\":\"simple\"},\"nestedObject\":{\"id\":99,\"name\":\"single\"},\"arrayOfNested\":[{\"id\":1,\"name\":\"first\"},{\"id\":2,\"name\":\"second\"},{\"id\":3,\"name\":\"third\"}]}}";

            // Act
            var result = ExposedPropertySerializer.FromJson<ComplexClass>(json);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsInstanceOf<ComplexClass>(result);

            Assert.AreEqual(100, result.value);

            // simpleObjectの確認
            Assert.IsNotNull(result.simpleObject);
            Assert.AreEqual(42, result.simpleObject.intValue);
            Assert.AreEqual("simple", result.simpleObject.stringValue);

            // nestedObjectの確認
            Assert.IsNotNull(result.nestedObject);
            Assert.AreEqual(99, result.nestedObject.id);
            Assert.AreEqual("single", result.nestedObject.name);

            // arrayOfNestedの確認
            Assert.IsNotNull(result.arrayOfNested);
            Assert.AreEqual(3, result.arrayOfNested.Length);

            Assert.AreEqual(1, result.arrayOfNested[0].id);
            Assert.AreEqual("first", result.arrayOfNested[0].name);

            Assert.AreEqual(2, result.arrayOfNested[1].id);
            Assert.AreEqual("second", result.arrayOfNested[1].name);

            Assert.AreEqual(3, result.arrayOfNested[2].id);
            Assert.AreEqual("third", result.arrayOfNested[2].name);
        }

        [Test]
        public void SerializeDeserialize_WithArray_RoundTrip_PreservesData()
        {
            // Arrange
            var original = new ComplexClass
            {
                value = 100,
                simpleObject = new SimpleClass
                {
                    intValue = 42,
                    stringValue = "test",
                    floatValue = 3.14f,
                    boolValue = true
                },
                nestedObject = new NestedClass
                {
                    id = 99,
                    name = "single"
                },
                arrayOfNested = new NestedClass[]
                {
                    new NestedClass { id = 1, name = "first" },
                    new NestedClass { id = 2, name = "second" },
                    new NestedClass { id = 3, name = "third" }
                }
            };

            // Act - Serialize
            var json = ExposedPropertySerializer.ToJson(original);

            // Act - Deserialize
            var result = ExposedPropertySerializer.FromJson<ComplexClass>(json);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsInstanceOf<ComplexClass>(result);

            Assert.AreEqual(original.value, result.value);

            // simpleObjectの確認
            Assert.IsNotNull(result.simpleObject);
            Assert.AreEqual(original.simpleObject.intValue, result.simpleObject.intValue);
            Assert.AreEqual(original.simpleObject.stringValue, result.simpleObject.stringValue);
            Assert.AreEqual(original.simpleObject.floatValue, result.simpleObject.floatValue, 0.001f);
            Assert.AreEqual(original.simpleObject.boolValue, result.simpleObject.boolValue);

            // nestedObjectの確認
            Assert.IsNotNull(result.nestedObject);
            Assert.AreEqual(original.nestedObject.id, result.nestedObject.id);
            Assert.AreEqual(original.nestedObject.name, result.nestedObject.name);

            // arrayOfNestedの確認
            Assert.IsNotNull(result.arrayOfNested);
            Assert.AreEqual(original.arrayOfNested.Length, result.arrayOfNested.Length);

            for (int i = 0; i < original.arrayOfNested.Length; i++)
            {
                Assert.AreEqual(original.arrayOfNested[i].id, result.arrayOfNested[i].id);
                Assert.AreEqual(original.arrayOfNested[i].name, result.arrayOfNested[i].name);
            }
        }

        #endregion

        #region ExposedClass Tests

        [Test]
        public void SerializeExposedClass_WithNonExposedMember_SerializesOnlyRegisteredProperties()
        {
            // Arrange
            // 属性から自動登録
            ExposedClass.RegisterFromAttributes<ExposedClassWithNonExposedMembers>();

            var obj = new ExposedClassWithNonExposedMembers
            {
                simpleData = new SimpleClass
                {
                    intValue = 42,
                    stringValue = "nested",
                    floatValue = 1.5f,
                    boolValue = false
                },
                registeredInt = 100,
                registeredText = "test",
                unregisteredInt = 999 // これはシリアライズされないはず
            };

            // Act
            var json = ExposedPropertySerializer.ToJson(obj);

            // Assert
            Assert.IsNotNull(json);
            Assert.IsFalse(string.IsNullOrEmpty(json));

            // JSONをパースして検証
            var jObject = JObject.Parse(json);
            var valueToken = jObject["value"] as JObject;
            Assert.IsNotNull(valueToken);

            // @typeの確認
            Assert.AreEqual("TestExposedParent", valueToken["@type"]?.Value<string>());

            // simpleDataの確認（非ExposedClassメンバ）
            var simpleDataToken = valueToken["simpleData"] as JObject;
            Assert.IsNotNull(simpleDataToken);
            Assert.AreEqual("SimpleClass", simpleDataToken["@type"]?.Value<string>());
            Assert.AreEqual(42, simpleDataToken["intValue"]?.Value<int>());
            Assert.AreEqual("nested", simpleDataToken["stringValue"]?.Value<string>());
            Assert.AreEqual(1.5f, simpleDataToken["floatValue"]?.Value<float>(), 0.001f);
            Assert.AreEqual(false, simpleDataToken["boolValue"]?.Value<bool>());

            // 登録されたプリミティブ型の確認
            Assert.AreEqual(100, valueToken["registeredInt"]?.Value<int>());
            Assert.AreEqual("test", valueToken["registeredText"]?.Value<string>());

            // 未登録のメンバは存在しないはず
            Assert.IsNull(valueToken["unregisteredInt"]);
        }

        [Test]
        public void DeserializeExposedClass_WithNonExposedMember_DeserializesCorrectly()
        {
            // Arrange
            // 属性から自動登録
            ExposedClass.RegisterFromAttributes<ExposedClassWithNonExposedMembers>();

            var json = "{\"value\":{\"@type\":\"ExposedParent\",\"simpleData\":{\"@type\":\"SimpleClass\",\"intValue\":42,\"stringValue\":\"nested\",\"floatValue\":1.5,\"boolValue\":false},\"registeredInt\":100,\"registeredText\":\"test\"}}";

            // Act
            var result = ExposedPropertySerializer.FromJson<ExposedClassWithNonExposedMembers>(json);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsInstanceOf<ExposedClassWithNonExposedMembers>(result);

            // simpleDataの確認（非ExposedClassメンバ）
            Assert.IsNotNull(result.simpleData);
            Assert.AreEqual(42, result.simpleData.intValue);
            Assert.AreEqual("nested", result.simpleData.stringValue);
            Assert.AreEqual(1.5f, result.simpleData.floatValue, 0.001f);
            Assert.AreEqual(false, result.simpleData.boolValue);

            // 登録されたプリミティブ型の確認
            Assert.AreEqual(100, result.registeredInt);
            Assert.AreEqual("test", result.registeredText);

            // 未登録のメンバはデフォルト値のまま
            Assert.AreEqual(0, result.unregisteredInt);
        }

        [Test]
        public void SerializeDeserialize_ExposedClass_RoundTrip_PreservesData()
        {
            // Arrange
            // 属性から自動登録
            ExposedClass.RegisterFromAttributes<ExposedClassWithNonExposedMembers>();

            var original = new ExposedClassWithNonExposedMembers
            {
                simpleData = new SimpleClass
                {
                    intValue = 42,
                    stringValue = "test",
                    floatValue = 3.14f,
                    boolValue = true
                },
                registeredInt = 100,
                registeredText = "sample",
                unregisteredInt = 999
            };

            // Act - Serialize
            var json = ExposedPropertySerializer.ToJson(original);

            // Act - Deserialize
            var result = ExposedPropertySerializer.FromJson<ExposedClassWithNonExposedMembers>(json);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsInstanceOf<ExposedClassWithNonExposedMembers>(result);

            // simpleDataの確認
            Assert.IsNotNull(result.simpleData);
            Assert.AreEqual(original.simpleData.intValue, result.simpleData.intValue);
            Assert.AreEqual(original.simpleData.stringValue, result.simpleData.stringValue);
            Assert.AreEqual(original.simpleData.floatValue, result.simpleData.floatValue, 0.001f);
            Assert.AreEqual(original.simpleData.boolValue, result.simpleData.boolValue);

            // 登録されたプリミティブ型の確認
            Assert.AreEqual(original.registeredInt, result.registeredInt);
            Assert.AreEqual(original.registeredText, result.registeredText);

            // 未登録のメンバはシリアライズされないため、デフォルト値
            Assert.AreNotEqual(original.unregisteredInt, result.unregisteredInt);
            Assert.AreEqual(0, result.unregisteredInt);
        }

        [Test]
        public void SerializeExposedClass_WithArrayData_SerializesCorrectly()
        {
            // Arrange
            // 属性から自動登録
            ExposedClass.RegisterFromAttributes<ExposedClassWithNonExposedMembers>();

            var obj = new ExposedClassWithNonExposedMembers
            {
                arrayData = new ArrayClass
                {
                    arrayOfNested = new NestedClass[]
                    {
                        new NestedClass { id = 1, name = "first" },
                        new NestedClass { id = 2, name = "second" },
                        new NestedClass { id = 3, name = "third" }
                    }
                },
                simpleData = new SimpleClass
                {
                    intValue = 42,
                    stringValue = "test"
                },
                registeredInt = 100,
                registeredText = "sample"
            };

            // Act
            var json = ExposedPropertySerializer.ToJson(obj);

            // Assert
            Assert.IsNotNull(json);
            Assert.IsFalse(string.IsNullOrEmpty(json));

            // JSONをパースして検証
            var jObject = JObject.Parse(json);
            var valueToken = jObject["value"] as JObject;
            Assert.IsNotNull(valueToken);

            // @typeの確認
            Assert.AreEqual("TestExposedParent", valueToken["@type"]?.Value<string>());

            // arrayDataの確認（非ExposedClassで配列を持つメンバ）
            var arrayDataToken = valueToken["arrayData"] as JObject;
            Assert.IsNotNull(arrayDataToken);
            Assert.AreEqual("ArrayClass", arrayDataToken["@type"]?.Value<string>());

            // arrayOfNestedの確認
            var arrayOfNestedToken = arrayDataToken["arrayOfNested"] as JArray;
            Assert.IsNotNull(arrayOfNestedToken);
            Assert.AreEqual(3, arrayOfNestedToken.Count);

            Assert.AreEqual(1, arrayOfNestedToken[0]["id"]?.Value<int>());
            Assert.AreEqual("first", arrayOfNestedToken[0]["name"]?.Value<string>());

            Assert.AreEqual(2, arrayOfNestedToken[1]["id"]?.Value<int>());
            Assert.AreEqual("second", arrayOfNestedToken[1]["name"]?.Value<string>());

            Assert.AreEqual(3, arrayOfNestedToken[2]["id"]?.Value<int>());
            Assert.AreEqual("third", arrayOfNestedToken[2]["name"]?.Value<string>());

            // simpleDataの確認
            var simpleDataToken = valueToken["simpleData"] as JObject;
            Assert.IsNotNull(simpleDataToken);
            Assert.AreEqual(42, simpleDataToken["intValue"]?.Value<int>());
            Assert.AreEqual("test", simpleDataToken["stringValue"]?.Value<string>());

            // 登録されたプリミティブ型の確認
            Assert.AreEqual(100, valueToken["registeredInt"]?.Value<int>());
            Assert.AreEqual("sample", valueToken["registeredText"]?.Value<string>());
        }

        [Test]
        public void DeserializeExposedClass_WithArrayData_DeserializesCorrectly()
        {
            // Arrange
            // 属性から自動登録
            ExposedClass.RegisterFromAttributes<ExposedClassWithNonExposedMembers>();

            var json = "{\"value\":{\"@type\":\"ExposedParent\",\"arrayData\":{\"@type\":\"ArrayClass\",\"arrayOfNested\":[{\"id\":1,\"name\":\"first\"},{\"id\":2,\"name\":\"second\"},{\"id\":3,\"name\":\"third\"}]},\"simpleData\":{\"@type\":\"SimpleClass\",\"intValue\":42,\"stringValue\":\"test\"},\"registeredInt\":100,\"registeredText\":\"sample\"}}";

            // Act
            var result = ExposedPropertySerializer.FromJson<ExposedClassWithNonExposedMembers>(json);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsInstanceOf<ExposedClassWithNonExposedMembers>(result);

            // arrayDataの確認
            Assert.IsNotNull(result.arrayData);
            Assert.IsNotNull(result.arrayData.arrayOfNested);
            Assert.AreEqual(3, result.arrayData.arrayOfNested.Length);

            Assert.AreEqual(1, result.arrayData.arrayOfNested[0].id);
            Assert.AreEqual("first", result.arrayData.arrayOfNested[0].name);

            Assert.AreEqual(2, result.arrayData.arrayOfNested[1].id);
            Assert.AreEqual("second", result.arrayData.arrayOfNested[1].name);

            Assert.AreEqual(3, result.arrayData.arrayOfNested[2].id);
            Assert.AreEqual("third", result.arrayData.arrayOfNested[2].name);

            // simpleDataの確認
            Assert.IsNotNull(result.simpleData);
            Assert.AreEqual(42, result.simpleData.intValue);
            Assert.AreEqual("test", result.simpleData.stringValue);

            // 登録されたプリミティブ型の確認
            Assert.AreEqual(100, result.registeredInt);
            Assert.AreEqual("sample", result.registeredText);
        }

        [Test]
        public void SerializeDeserialize_ExposedClass_WithArrayData_RoundTrip_PreservesData()
        {
            // Arrange
            // 属性から自動登録
            ExposedClass.RegisterFromAttributes<ExposedClassWithNonExposedMembers>();

            var original = new ExposedClassWithNonExposedMembers
            {
                arrayData = new ArrayClass
                {
                    arrayOfNested = new NestedClass[]
                    {
                        new NestedClass { id = 1, name = "first" },
                        new NestedClass { id = 2, name = "second" },
                        new NestedClass { id = 3, name = "third" }
                    }
                },
                simpleData = new SimpleClass
                {
                    intValue = 42,
                    stringValue = "test",
                    floatValue = 3.14f,
                    boolValue = true
                },
                registeredInt = 100,
                registeredText = "sample"
            };

            // Act - Serialize
            var json = ExposedPropertySerializer.ToJson(original);

            // Act - Deserialize
            var result = ExposedPropertySerializer.FromJson<ExposedClassWithNonExposedMembers>(json);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsInstanceOf<ExposedClassWithNonExposedMembers>(result);

            // arrayDataの確認
            Assert.IsNotNull(result.arrayData);
            Assert.IsNotNull(result.arrayData.arrayOfNested);
            Assert.AreEqual(original.arrayData.arrayOfNested.Length, result.arrayData.arrayOfNested.Length);

            for (int i = 0; i < original.arrayData.arrayOfNested.Length; i++)
            {
                Assert.AreEqual(original.arrayData.arrayOfNested[i].id, result.arrayData.arrayOfNested[i].id);
                Assert.AreEqual(original.arrayData.arrayOfNested[i].name, result.arrayData.arrayOfNested[i].name);
            }

            // simpleDataの確認
            Assert.IsNotNull(result.simpleData);
            Assert.AreEqual(original.simpleData.intValue, result.simpleData.intValue);
            Assert.AreEqual(original.simpleData.stringValue, result.simpleData.stringValue);
            Assert.AreEqual(original.simpleData.floatValue, result.simpleData.floatValue, 0.001f);
            Assert.AreEqual(original.simpleData.boolValue, result.simpleData.boolValue);

            // 登録されたプリミティブ型の確認
            Assert.AreEqual(original.registeredInt, result.registeredInt);
            Assert.AreEqual(original.registeredText, result.registeredText);
        }

        #endregion

        #region Enum Serialization Tests

        [Test]
        public void SerializeEnum_OutputsIntegerFormat()
        {
            // Arrange
            // ExposedClassに登録していないクラスの場合、JsonUtilityを使うため整数形式
            var obj = new ClassWithEnum
            {
                priority = TestPriority.High,
                name = "test"
            };

            // Act
            var json = ExposedPropertySerializer.ToJson(obj);

            // Assert
            Assert.IsNotNull(json);
            var jObject = JObject.Parse(json);
            var valueToken = jObject["value"] as JObject;
            Assert.IsNotNull(valueToken);

            // Enum値が整数形式で出力されていることを確認（JsonUtilityの動作）
            Assert.AreEqual(2, valueToken["priority"]?.Value<int>()); // High = 2
            Assert.AreEqual("test", valueToken["name"]?.Value<string>());
        }

        [Test]
        public void DeserializeEnum_FromStringFormat_DeserializesCorrectly()
        {
            // Arrange - 数値形式のEnum
            // ExposedClassに登録していない場合はEnumは数字で出力   
            var json = "{\"value\":{\"priority\":2,\"name\":\"test\"}}";

            // Act
            var result = ExposedPropertySerializer.FromJson<ClassWithEnum>(json);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(TestPriority.High, result.priority);
            Assert.AreEqual("test", result.name);
        }

        [Test]
        public void DeserializeEnum_FromIntegerFormat_DeserializesCorrectly()
        {
            // Arrange - 整数形式のEnum（後方互換性）
            var json = "{\"value\":{\"priority\":2,\"name\":\"test\"}}";

            // Act
            var result = ExposedPropertySerializer.FromJson<ClassWithEnum>(json);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(TestPriority.High, result.priority); // 2 = High
            Assert.AreEqual("test", result.name);
        }

        [Test]
        public void SerializeDeserialize_Enum_RoundTrip_PreservesData()
        {
            // Arrange
            var original = new ClassWithEnum
            {
                priority = TestPriority.Critical,
                name = "important"
            };

            // Act - Serialize
            var json = ExposedPropertySerializer.ToJson(original);

            // Act - Deserialize
            var result = ExposedPropertySerializer.FromJson<ClassWithEnum>(json);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(original.priority, result.priority);
            Assert.AreEqual(original.name, result.name);
        }

        [Test]
        public void DeserializeEnum_InvalidString_ReturnsDefaultValue()
        {
            // Arrange - 無効な文字列
            var json = "{\"value\":{\"priority\":\"InvalidValue\",\"name\":\"test\"}}";

            // Act
            var result = ExposedPropertySerializer.FromJson<ClassWithEnum>(json);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(TestPriority.Low, result.priority); // デフォルト値（0）
            Assert.AreEqual("test", result.name);
        }

        [Test]
        public void SerializeEnum_AllValues_OutputsCorrectIntegers()
        {
            // 各Enum値が正しく整数化されることを確認（JsonUtilityの動作）
            foreach (TestPriority priority in System.Enum.GetValues(typeof(TestPriority)))
            {
                // Arrange
                var obj = new ClassWithEnum { priority = priority, name = "test" };

                // Act
                var json = ExposedPropertySerializer.ToJson(obj);

                // Assert
                var jObject = JObject.Parse(json);
                var valueToken = jObject["value"] as JObject;
                Assert.AreEqual((int)priority, valueToken["priority"]?.Value<int>(),
                    $"Enum value {priority} should be serialized as integer {(int)priority}");
            }
        }

        #endregion

        #region Readonly and Reference Persistence Tests

        // ExposedObject参照テスト用のScriptableObject
        [ExposedClass("TestRefSO")]
        public class TestRefScriptableObject : ScriptableObject
        {
            [ExposedField]
            public int soValue;
        }

        // readonlyプロパティ（string[], ScriptableObject, ScriptableObject[]）を持つクラス
        [Serializable]
        [ExposedClass("TestReadonlyRefClass")]
        public class TestReadonlyRefClass
        {
            // readonly string[] — 永続化時にスキップされるべき
            private string[] _meshNames = new[] { "mesh1", "mesh2" };

            [ExposedProperty("meshNames"), Persistable]
            public string[] meshNames => _meshNames;

            // readonly ScriptableObject — ExposedObject参照なので永続化されるべき
            private TestRefScriptableObject _refObj;

            [ExposedProperty("refObj"), Persistable]
            public TestRefScriptableObject refObj => _refObj;

            public void SetRefObj(TestRefScriptableObject obj) => _refObj = obj;

            // readonly ScriptableObject[] — 要素がExposedObject参照の配列なので永続化されるべき
            private TestRefScriptableObject[] _refArray;

            [ExposedProperty("refArray"), Persistable]
            public TestRefScriptableObject[] refArray => _refArray;

            public void SetRefArray(TestRefScriptableObject[] arr) => _refArray = arr;

            // readonly ScriptableObject[]（ベース型で宣言） — Component[]と同じパターン
            // 要素型自体(ScriptableObject)はExposedClassに未登録だが、
            // 派生型(TestRefScriptableObject)が登録されている
            private ScriptableObject[] _baseTypeRefArray;

            [ExposedProperty("baseTypeRefArray"), Persistable]
            public ScriptableObject[] baseTypeRefArray => _baseTypeRefArray;

            public void SetBaseTypeRefArray(ScriptableObject[] arr) => _baseTypeRefArray = arr;

            // 書き込み可能なプロパティ — 常に永続化される
            [ExposedField]
            public int writableValue;

            // readonly int — プリミティブ型は永続化時にスキップされるべき
            private int _readonlyInt = 99;

            [ExposedProperty("readonlyInt"), Persistable]
            public int readonlyInt => _readonlyInt;
        }

        private class TestResolver : IExposedObjectResolver
        {
            public ExposedObject FindById(string id) => ExposedObjectRegistry.FindById(id);
            public ExposedObject FindByTarget(object target) => ExposedObjectRegistry.FindByTarget(target);
        }

        [Test]
        public void ContainsExposedObjectReference_StringArray_ReturnsFalse()
        {
            // Arrange
            ExposedClass.RegisterFromAttributes<TestRefScriptableObject>();
            ExposedClass.RegisterFromAttributes<TestReadonlyRefClass>();
            var exposedClass = ExposedClass.Find(typeof(TestReadonlyRefClass));
            var meshNamesProp = exposedClass.propertyTypes.First(p => p.name == "meshNames");

            // Assert
            Assert.IsTrue(meshNamesProp.isReadOnly);
            Assert.IsFalse(meshNamesProp.containsExposedObjectReference,
                "string[] should not be treated as ExposedObject reference");
        }

        [Test]
        public void ContainsExposedObjectReference_SingleScriptableObject_ReturnsTrue()
        {
            // Arrange
            ExposedClass.RegisterFromAttributes<TestRefScriptableObject>();
            ExposedClass.RegisterFromAttributes<TestReadonlyRefClass>();
            var exposedClass = ExposedClass.Find(typeof(TestReadonlyRefClass));
            var refObjProp = exposedClass.propertyTypes.First(p => p.name == "refObj");

            // Assert
            Assert.IsTrue(refObjProp.isReadOnly);
            Assert.IsTrue(refObjProp.containsExposedObjectReference,
                "ScriptableObject reference should be treated as ExposedObject reference");
        }

        [Test]
        public void ContainsExposedObjectReference_ScriptableObjectArray_ReturnsTrue()
        {
            // Arrange
            ExposedClass.RegisterFromAttributes<TestRefScriptableObject>();
            ExposedClass.RegisterFromAttributes<TestReadonlyRefClass>();
            var exposedClass = ExposedClass.Find(typeof(TestReadonlyRefClass));
            var refArrayProp = exposedClass.propertyTypes.First(p => p.name == "refArray");

            // Assert
            Assert.IsTrue(refArrayProp.isReadOnly);
            Assert.IsTrue(refArrayProp.containsExposedObjectReference,
                "ScriptableObject[] should be treated as containing ExposedObject references");
        }

        [Test]
        public void ContainsExposedObjectReference_BaseTypeUnityObjectArray_ReturnsTrue()
        {
            // Arrange — Component[]と同じパターン：ベース型(ScriptableObject)自体はExposedClass未登録
            ExposedClass.RegisterFromAttributes<TestRefScriptableObject>();
            ExposedClass.RegisterFromAttributes<TestReadonlyRefClass>();
            var exposedClass = ExposedClass.Find(typeof(TestReadonlyRefClass));
            var baseRefArrayProp = exposedClass.propertyTypes.First(p => p.name == "baseTypeRefArray");

            // Assert
            Assert.IsTrue(baseRefArrayProp.isReadOnly);
            Assert.IsFalse(ExposedClass.Has(typeof(ScriptableObject)),
                "ScriptableObject itself should not be registered as ExposedClass");
            Assert.IsTrue(baseRefArrayProp.containsExposedObjectReference,
                "ScriptableObject[] (base type array) should be treated as containing ExposedObject references");
        }

        [Test]
        public void ContainsExposedObjectReference_ReadonlyPrimitive_ReturnsFalse()
        {
            // Arrange
            ExposedClass.RegisterFromAttributes<TestRefScriptableObject>();
            ExposedClass.RegisterFromAttributes<TestReadonlyRefClass>();
            var exposedClass = ExposedClass.Find(typeof(TestReadonlyRefClass));
            var readonlyIntProp = exposedClass.propertyTypes.First(p => p.name == "readonlyInt");

            // Assert
            Assert.IsTrue(readonlyIntProp.isReadOnly);
            Assert.IsFalse(readonlyIntProp.containsExposedObjectReference,
                "readonly int should not be treated as ExposedObject reference");
        }

        [Test]
        public void ToJson_ForPersistence_SkipsReadonlyStringArray()
        {
            // Arrange
            ExposedClass.RegisterFromAttributes<TestRefScriptableObject>();
            ExposedClass.RegisterFromAttributes<TestReadonlyRefClass>();
            var exposedClass = ExposedClass.Find(typeof(TestReadonlyRefClass));
            var testObj = new TestReadonlyRefClass { writableValue = 42 };
            var exposedObj = new ExposedObject("test-readonly-1", exposedClass, testObj);
            var resolver = new TestResolver();

            try
            {
                // Act
                var json = ExposedPropertySerializer.ToJson(exposedObj, resolver, forPersistence: true);
                var jObject = JObject.Parse(json);

                // Assert — readonly string[] はスキップされる
                Assert.IsNull(jObject["meshNames"],
                    "readonly string[] (meshNames) should be excluded from persistence output");

                // Assert — readonly int もスキップされる
                Assert.IsNull(jObject["readonlyInt"],
                    "readonly int should be excluded from persistence output");

                // Assert — writableValue は含まれる
                Assert.AreEqual(42, jObject["writableValue"]?.Value<int>(),
                    "writable property should be included in persistence output");
            }
            finally
            {
                exposedObj.Unregister();
            }
        }

        [Test]
        public void ToJson_ForPersistence_IncludesReadonlyExposedObjectReference()
        {
            // Arrange
            ExposedClass.RegisterFromAttributes<TestRefScriptableObject>();
            ExposedClass.RegisterFromAttributes<TestReadonlyRefClass>();

            var soInstance = ScriptableObject.CreateInstance<TestRefScriptableObject>();
            soInstance.soValue = 10;
            var soExposedClass = ExposedClass.Find(typeof(TestRefScriptableObject));
            var soExposedObj = new ExposedObject("so-ref-1", soExposedClass, soInstance);

            var exposedClass = ExposedClass.Find(typeof(TestReadonlyRefClass));
            var testObj = new TestReadonlyRefClass { writableValue = 1 };
            testObj.SetRefObj(soInstance);
            var exposedObj = new ExposedObject("test-readonly-2", exposedClass, testObj);
            var resolver = new TestResolver();

            try
            {
                // Act
                var json = ExposedPropertySerializer.ToJson(exposedObj, resolver, forPersistence: true);
                var jObject = JObject.Parse(json);

                // Assert — readonly ScriptableObject参照は含まれる（@ref情報が必要）
                var refObjToken = jObject["refObj"];
                Assert.IsNotNull(refObjToken,
                    "readonly ExposedObject reference (refObj) should be included in persistence output");
                Assert.AreEqual("so-ref-1", refObjToken["@ref"]?.Value<string>(),
                    "refObj should contain @ref pointing to the ExposedObject id");
            }
            finally
            {
                exposedObj.Unregister();
                soExposedObj.Unregister();
                ScriptableObject.DestroyImmediate(soInstance);
            }
        }

        [Test]
        public void ToJson_ForPersistence_IncludesReadonlyExposedObjectArray()
        {
            // Arrange
            ExposedClass.RegisterFromAttributes<TestRefScriptableObject>();
            ExposedClass.RegisterFromAttributes<TestReadonlyRefClass>();

            var so1 = ScriptableObject.CreateInstance<TestRefScriptableObject>();
            var so2 = ScriptableObject.CreateInstance<TestRefScriptableObject>();
            var soExposedClass = ExposedClass.Find(typeof(TestRefScriptableObject));
            var soExposed1 = new ExposedObject("so-arr-1", soExposedClass, so1);
            var soExposed2 = new ExposedObject("so-arr-2", soExposedClass, so2);

            var exposedClass = ExposedClass.Find(typeof(TestReadonlyRefClass));
            var testObj = new TestReadonlyRefClass { writableValue = 2 };
            testObj.SetRefArray(new[] { so1, so2 });
            var exposedObj = new ExposedObject("test-readonly-3", exposedClass, testObj);
            var resolver = new TestResolver();

            try
            {
                // Act
                var json = ExposedPropertySerializer.ToJson(exposedObj, resolver, forPersistence: true);
                var jObject = JObject.Parse(json);

                // Assert — readonly ScriptableObject[] は含まれる
                var refArrayToken = jObject["refArray"] as JArray;
                Assert.IsNotNull(refArrayToken,
                    "readonly ExposedObject[] (refArray) should be included in persistence output");
                Assert.AreEqual(2, refArrayToken.Count,
                    "refArray should contain 2 elements");
            }
            finally
            {
                exposedObj.Unregister();
                soExposed1.Unregister();
                soExposed2.Unregister();
                ScriptableObject.DestroyImmediate(so1);
                ScriptableObject.DestroyImmediate(so2);
            }
        }

        [Test]
        public void ToJson_ForPersistence_IncludesReadonlyBaseTypeUnityObjectArray()
        {
            // Arrange — Component[]と同じパターン：ベース型配列に派生型の要素を格納
            ExposedClass.RegisterFromAttributes<TestRefScriptableObject>();
            ExposedClass.RegisterFromAttributes<TestReadonlyRefClass>();

            var so1 = ScriptableObject.CreateInstance<TestRefScriptableObject>();
            var so2 = ScriptableObject.CreateInstance<TestRefScriptableObject>();
            var soExposedClass = ExposedClass.Find(typeof(TestRefScriptableObject));
            var soExposed1 = new ExposedObject("so-base-1", soExposedClass, so1);
            var soExposed2 = new ExposedObject("so-base-2", soExposedClass, so2);

            var exposedClass = ExposedClass.Find(typeof(TestReadonlyRefClass));
            var testObj = new TestReadonlyRefClass { writableValue = 3 };
            // ScriptableObject[]（ベース型）として格納
            testObj.SetBaseTypeRefArray(new ScriptableObject[] { so1, so2 });
            var exposedObj = new ExposedObject("test-readonly-5", exposedClass, testObj);
            var resolver = new TestResolver();

            try
            {
                // Act
                var json = ExposedPropertySerializer.ToJson(exposedObj, resolver, forPersistence: true);
                var jObject = JObject.Parse(json);

                // Assert — readonly ScriptableObject[]（ベース型宣言）は含まれる
                var baseRefArrayToken = jObject["baseTypeRefArray"] as JArray;
                Assert.IsNotNull(baseRefArrayToken,
                    "readonly base-type UnityObject[] (baseTypeRefArray) should be included in persistence output");
                Assert.AreEqual(2, baseRefArrayToken.Count,
                    "baseTypeRefArray should contain 2 elements");
            }
            finally
            {
                exposedObj.Unregister();
                soExposed1.Unregister();
                soExposed2.Unregister();
                ScriptableObject.DestroyImmediate(so1);
                ScriptableObject.DestroyImmediate(so2);
            }
        }

        [Test]
        public void ToJson_NotForPersistence_IncludesAllReadonlyProperties()
        {
            // Arrange
            ExposedClass.RegisterFromAttributes<TestRefScriptableObject>();
            ExposedClass.RegisterFromAttributes<TestReadonlyRefClass>();
            var exposedClass = ExposedClass.Find(typeof(TestReadonlyRefClass));
            var testObj = new TestReadonlyRefClass { writableValue = 5 };
            var exposedObj = new ExposedObject("test-readonly-4", exposedClass, testObj);
            var resolver = new TestResolver();

            try
            {
                // Act — forPersistence = false（通常のシリアライズ）
                var json = ExposedPropertySerializer.ToJson(exposedObj, resolver, forPersistence: false);
                var jObject = JObject.Parse(json);

                // Assert — 通常シリアライズではreadonly含めすべて出力される
                Assert.IsNotNull(jObject["meshNames"],
                    "meshNames should be included in non-persistence output");
                Assert.IsNotNull(jObject["readonlyInt"],
                    "readonlyInt should be included in non-persistence output");
                Assert.AreEqual(5, jObject["writableValue"]?.Value<int>());
            }
            finally
            {
                exposedObj.Unregister();
            }
        }

        #endregion

        #region Nested ExposedObject Readonly Persistence Tests

        // ネストされたExposedClassオブジェクト内のreadonly/非persistableプロパティのテスト用
        [Serializable]
        [ExposedClass("TestNestedChild")]
        public class TestNestedChild
        {
            [ExposedField]
            public int writableValue;

            // readonlyかつ非persistable — 永続化時にスキップされるべき
            private string[] _readonlyNames = new[] { "name1", "name2" };

            [ExposedProperty("readonlyNames")]
            public string[] readonlyNames => _readonlyNames;

            // readonlyかつpersistable — 永続化時にスキップされるべき（ExposedObject参照でないため）
            private int _readonlyInt = 42;

            [ExposedProperty("readonlyInt"), Persistable]
            public int readonlyInt => _readonlyInt;
        }

        [Serializable]
        [ExposedClass("TestNestedParent")]
        public class TestNestedParent
        {
            [ExposedField]
            public TestNestedChild child;

            [ExposedField]
            public int parentValue;
        }

        [Test]
        public void SerializeNestedExposedObject_ForPersistence_ExcludesReadonlyProperties()
        {
            // Arrange — ネストされたExposedClassオブジェクト内のreadonly/非persistableプロパティが
            // forPersistence=true 時にスキップされることを確認
            ExposedClass.RegisterFromAttributes<TestNestedChild>();
            ExposedClass.RegisterFromAttributes<TestNestedParent>();

            var exposedClass = ExposedClass.Find(typeof(TestNestedParent));
            var testObj = new TestNestedParent
            {
                parentValue = 10,
                child = new TestNestedChild { writableValue = 5 }
            };
            var exposedObj = new ExposedObject("test-nested-readonly", exposedClass, testObj);
            var resolver = new TestResolver();

            try
            {
                // Act — forPersistence=true
                var json = ExposedPropertySerializer.ToJson(exposedObj, resolver, forPersistence: true);
                var jObject = JObject.Parse(json);

                // Assert — 親プロパティは出力される
                Assert.AreEqual(10, jObject["parentValue"]?.Value<int>());

                // Assert — ネストされたchildオブジェクトは出力される
                var childObj = jObject["child"] as JObject;
                Assert.IsNotNull(childObj, "child object should be included");

                // Assert — childの書き込み可能プロパティは出力される
                Assert.AreEqual(5, childObj["writableValue"]?.Value<int>(),
                    "writable property in nested object should be included");

                // Assert — childのreadonly非persistableプロパティはスキップされる
                Assert.IsNull(childObj["readonlyNames"],
                    "readonly non-persistable property (readonlyNames) in nested object should be excluded from persistence");

                // Assert — childのreadonly persistableプロパティもスキップされる（ExposedObject参照でないため）
                Assert.IsNull(childObj["readonlyInt"],
                    "readonly persistable primitive (readonlyInt) in nested object should be excluded from persistence");
            }
            finally
            {
                exposedObj.Unregister();
            }
        }

        [Test]
        public void SerializeNestedExposedObject_NonPersistence_IncludesReadonlyProperties()
        {
            // Arrange — forPersistence=false の場合、ネストされたExposedClassオブジェクト内の
            // readonlyプロパティも含めてすべて出力されることを確認
            ExposedClass.RegisterFromAttributes<TestNestedChild>();
            ExposedClass.RegisterFromAttributes<TestNestedParent>();

            var exposedClass = ExposedClass.Find(typeof(TestNestedParent));
            var testObj = new TestNestedParent
            {
                parentValue = 10,
                child = new TestNestedChild { writableValue = 5 }
            };
            var exposedObj = new ExposedObject("test-nested-nonpersist", exposedClass, testObj);
            var resolver = new TestResolver();

            try
            {
                // Act — forPersistence=false（通常のシリアライズ）
                var json = ExposedPropertySerializer.ToJson(exposedObj, resolver, forPersistence: false);
                var jObject = JObject.Parse(json);

                var childObj = jObject["child"] as JObject;
                Assert.IsNotNull(childObj);

                // Assert — readonlyプロパティも含めすべて出力される
                Assert.IsNotNull(childObj["readonlyNames"],
                    "readonly property should be included in non-persistence output");
                Assert.IsNotNull(childObj["readonlyInt"],
                    "readonly int should be included in non-persistence output");
                Assert.AreEqual(5, childObj["writableValue"]?.Value<int>());
            }
            finally
            {
                exposedObj.Unregister();
            }
        }

        #endregion

        #region DeltaFromDefault ExposedObject Reference Tests

        // DeltaFromDefaultテスト用: 書き込み可能なExposedObject参照を持つクラス
        [Serializable]
        [ExposedClass("TestDeltaFromDefaultRefClass")]
        public class TestDeltaFromDefaultRefClass
        {
            [ExposedField]
            public int normalValue;

            [ExposedField]
            public TestRefScriptableObject config;
        }

        [Test]
        public void ToJson_DeltaFromDefault_SkipsNonDirtyExposedObjectReference()
        {
            // Arrange — ExposedObject参照を持つがdirtyでないプロパティが
            // onlyDirty=true でスキップされることを確認
            ExposedClass.RegisterFromAttributes<TestRefScriptableObject>();
            ExposedClass.RegisterFromAttributes<TestDeltaFromDefaultRefClass>();

            var soInstance = ScriptableObject.CreateInstance<TestRefScriptableObject>();
            soInstance.soValue = 10;
            var soExposedClass = ExposedClass.Find(typeof(TestRefScriptableObject));
            var soExposedObj = new ExposedObject("dirty-so-1", soExposedClass, soInstance);

            var exposedClass = ExposedClass.Find(typeof(TestDeltaFromDefaultRefClass));
            var testObj = new TestDeltaFromDefaultRefClass { normalValue = 5, config = soInstance };
            var exposedObj = new ExposedObject("dirty-ref-test", exposedClass, testObj);
            var resolver = new TestResolver();

            // デフォルト値を設定（dirty判定のベースライン）
            ExposedPropertyUtility.SetDefault(exposedObj);

            try
            {
                // Act — isDirtyOnly=true, forPersistence=true（SaveCurrentDataと同条件）
                // normalValueもconfigもdirtyではない
                var json = ExposedPropertySerializer.ToJson(exposedObj, resolver, isDirtyOnly: true, forPersistence: true);
                var jObject = JObject.Parse(json);

                // Assert — dirtyでない通常プロパティはスキップされる
                Assert.IsNull(jObject["normalValue"],
                    "non-dirty normalValue should be skipped in DeltaFromDefault mode");

                // Assert — dirtyでないExposedObject参照もスキップされる
                Assert.IsNull(jObject["config"],
                    "non-dirty ExposedObject reference (config) should be skipped in DeltaFromDefault mode");
            }
            finally
            {
                exposedObj.Unregister();
                soExposedObj.Unregister();
                ScriptableObject.DestroyImmediate(soInstance);
            }
        }

        #endregion

        #region TransformValue Tests

        [Serializable]
        [ExposedClass("TestTransformHolder")]
        public class TransformValueHolder
        {
            [ExposedField]
            public TransformValue trs;
        }

        [Test]
        public void TransformValue_SerializeToJson_IncludesPositionRotationScale()
        {
            ExposedClass.RegisterFromAttributes<TransformValueHolder>();
            var obj = new TransformValueHolder
            {
                trs = new TransformValue(
                    new Vector3(1, 2, 3),
                    new Quaternion(0.1f, 0.2f, 0.3f, 0.927f),
                    new Vector3(4, 5, 6))
            };

            var json = ExposedPropertySerializer.ToJson(obj);
            var valueToken = JObject.Parse(json)["value"] as JObject;
            var trsToken = valueToken?["trs"] as JObject;

            Assert.IsNotNull(trsToken);
            var pos = trsToken["position"] as JObject;
            var rot = trsToken["rotation"] as JObject;
            var scl = trsToken["scale"] as JObject;

            Assert.AreEqual(1f, pos["x"].Value<float>(), 0.0001f);
            Assert.AreEqual(2f, pos["y"].Value<float>(), 0.0001f);
            Assert.AreEqual(3f, pos["z"].Value<float>(), 0.0001f);

            Assert.AreEqual(0.1f, rot["x"].Value<float>(), 0.0001f);
            Assert.AreEqual(0.2f, rot["y"].Value<float>(), 0.0001f);
            Assert.AreEqual(0.3f, rot["z"].Value<float>(), 0.0001f);
            Assert.AreEqual(0.927f, rot["w"].Value<float>(), 0.0001f);

            Assert.AreEqual(4f, scl["x"].Value<float>(), 0.0001f);
            Assert.AreEqual(5f, scl["y"].Value<float>(), 0.0001f);
            Assert.AreEqual(6f, scl["z"].Value<float>(), 0.0001f);
        }

        [Test]
        public void TransformValue_RoundTrip_PreservesValue()
        {
            ExposedClass.RegisterFromAttributes<TransformValueHolder>();
            var original = new TransformValueHolder
            {
                trs = new TransformValue(
                    new Vector3(10f, -20f, 30f),
                    Quaternion.Euler(45f, 90f, 135f),
                    new Vector3(0.5f, 1.5f, 2.5f))
            };

            var json = ExposedPropertySerializer.ToJson(original);
            var result = ExposedPropertySerializer.FromJson<TransformValueHolder>(json);

            Assert.IsNotNull(result);
            Assert.AreEqual(original.trs.position, result.trs.position);
            Assert.AreEqual(original.trs.rotation.x, result.trs.rotation.x, 0.0001f);
            Assert.AreEqual(original.trs.rotation.y, result.trs.rotation.y, 0.0001f);
            Assert.AreEqual(original.trs.rotation.z, result.trs.rotation.z, 0.0001f);
            Assert.AreEqual(original.trs.rotation.w, result.trs.rotation.w, 0.0001f);
            Assert.AreEqual(original.trs.scale, result.trs.scale);
        }

        [Test]
        public void TransformValue_PartialDeserialize_FallsBackToExisting()
        {
            ExposedClass.RegisterFromAttributes<TransformValueHolder>();
            // rotation のみ含む JSON でデシリアライズした場合、
            // position と scale は既存インスタンスの値が保持されること。
            var existing = new TransformValueHolder
            {
                trs = new TransformValue(
                    new Vector3(7, 8, 9),
                    Quaternion.identity,
                    new Vector3(2, 2, 2))
            };

            var partial = new JObject
            {
                ["value"] = new JObject
                {
                    ["@type"] = "TestTransformHolder",
                    ["trs"] = new JObject
                    {
                        ["rotation"] = new JObject { ["x"] = 0.1f, ["y"] = 0.2f, ["z"] = 0.3f, ["w"] = 0.927f },
                    }
                }
            }.ToString();

            var resolver = new TestResolver();
            var token = JObject.Parse(partial)["value"];
            var resultObj = ExposedPropertySerializer.DeserializeExposedObject(resolver, token, typeof(TransformValueHolder), existing);
            var result = (TransformValueHolder)resultObj;

            Assert.AreEqual(new Vector3(7, 8, 9), result.trs.position);
            Assert.AreEqual(new Vector3(2, 2, 2), result.trs.scale);
            Assert.AreEqual(0.1f, result.trs.rotation.x, 0.0001f);
        }

        #endregion
    }
}
