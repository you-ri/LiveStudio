using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Lilium.RemoteControl;
using Lilium.RemoteControl.Scene;
using Newtonsoft.Json.Linq;

namespace Lilium.RemoteControl.Tests
{
    [TestFixture]
    public class ExposedObjectDirtyTrackingTests
    {
        // テスト用ヘルパー: override エントリは @source、新規は @id が識別子
        private static string EntryKey(JToken t) => t is JObject o ? (o["@source"] ?? o["@id"])?.Value<string>() : null;

        #region Test Classes

        [Serializable]
        [ExposedClass("TestDirtyNestedStruct")]
        public struct TestDirtyNestedStruct
        {
            [ExposedField]
            public int id;

            [ExposedField]
            public string name;
        }

        // デフォルト値を持つ構造体（配列要素追加時のデフォルト値テスト用）
        [Serializable]
        [ExposedClass("TestDirtyStructWithDefault")]
        public struct TestDirtyStructWithDefault
        {
            [ExposedField]
            [DefaultValue(1)]
            public int value;

            [ExposedField]
            public string name;
        }

        [Serializable]
        [ExposedClass("TestDirtyClassWithDefaultArray")]
        public class TestDirtyClassWithDefaultArray
        {
            [ExposedField]
            public TestDirtyStructWithDefault[] items;
        }

        [Serializable]
        [ExposedClass("TestDirtyClass")]
        public class TestDirtyClass
        {
            [ExposedField]
            public int value;

            [ExposedField]
            public string name;

            [ExposedField]
            public float position;

            [ExposedField]
            public TestDirtyNestedStruct nested;
        }

        [Serializable]
        [ExposedClass("TestDirtyClassWithArray")]
        public class TestDirtyClassWithArray
        {
            [ExposedField]
            public TestDirtyNestedStruct[] items;
        }

        [Serializable]
        [ExposedClass("TestDirtyClassWithList")]
        public class TestDirtyClassWithList
        {
            [ExposedField]
            public List<int> intList;

            [ExposedField]
            public List<string> stringList;

            [ExposedField]
            public List<TestDirtyNestedStruct> structList;
        }

        // ObjectContainerの_objectsと同様のパターン:
        // List<参照型>の要素がそれぞれExposedObjectを持つケース
        [Serializable]
        [ExposedClass("TestDirtyRefItem")]
        public class TestDirtyRefItem : IExposedObject
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
        [ExposedClass("TestDirtyContainerWithRefList")]
        public class TestDirtyContainerWithRefList
        {
            [ExposedField]
            public List<TestDirtyRefItem> items;
        }

        #endregion

        private TestExposedObjectResolver _resolver;

        [SetUp]
        public void SetUp()
        {
            ExposedClass.Clear();

            // テストクラスを登録
            ExposedClass.RegisterFromAttributes<TestDirtyNestedStruct>();
            ExposedClass.RegisterFromAttributes<TestDirtyClass>();
            ExposedClass.RegisterFromAttributes<TestDirtyClassWithArray>();
            ExposedClass.RegisterFromAttributes<TestDirtyStructWithDefault>();
            ExposedClass.RegisterFromAttributes<TestDirtyClassWithDefaultArray>();
            ExposedClass.RegisterFromAttributes<TestDirtyClassWithList>();
            ExposedClass.RegisterFromAttributes<TestDirtyRefItem>();
            ExposedClass.RegisterFromAttributes<TestDirtyContainerWithRefList>();
            ExposedClass.RegisterFromAttributes<TestClassWithPlainStructField>();
            ExposedClass.RegisterFromAttributes<TestClassWithPlainSerializableProperty>();
            ExposedClass.RegisterFromAttributes<TestClassWithReadOnlyPlainProperty>();

            // ExposedObjectRegistry.instances をクリア
            var toRemove = ExposedObjectRegistry.instances.ToList();
            foreach (var obj in toRemove)
            {
                obj.Unregister();
            }

            _resolver = new TestExposedObjectResolver();
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

        #region Basic Dirty Detection Tests (比較ベース)

        [Test]
        public void ValueChanged_SingleProperty_DetectedAsDirty()
        {
            // Arrange
            ExposedClass.RegisterFromAttributes<TestDirtyClass>();
            var testObj = new TestDirtyClass { value = 42, name = "Test", position = 1.0f };
            var exposedClass = ExposedClass.Find(typeof(TestDirtyClass));
            var exposedObj = new ExposedObject("test-dirty-1", exposedClass, testObj);

            // Act - デフォルト値をキャプチャしてから値を変更
            exposedObj.SetDefault("value", testObj.value);
            testObj.value = 100;

            // Assert
            Assert.IsTrue(exposedObj.IsPropertyDirty("value"));
            Assert.IsTrue(exposedObj.isDirty);
        }

        [Test]
        public void ValueChanged_MultipleProperties_DetectsAllDirty()
        {
            // Arrange
            ExposedClass.RegisterFromAttributes<TestDirtyClass>();
            var testObj = new TestDirtyClass { value = 42, name = "Test", position = 1.0f };
            var exposedClass = ExposedClass.Find(typeof(TestDirtyClass));
            var exposedObj = new ExposedObject("test-dirty-2", exposedClass, testObj);

            // Act - デフォルト値をキャプチャしてから値を変更
            exposedObj.SetDefault("value", testObj.value);
            exposedObj.SetDefault("name", testObj.name);
            exposedObj.SetDefault("position", testObj.position);
            testObj.value = 100;
            testObj.name = "Changed";

            // Assert
            Assert.IsTrue(exposedObj.IsPropertyDirty("value"));
            Assert.IsTrue(exposedObj.IsPropertyDirty("name"));
            Assert.IsFalse(exposedObj.IsPropertyDirty("position"));
        }

        [Test]
        public void EnsureDefaultCaptured_NonExistentProperty_DoesNotThrow()
        {
            // Arrange
            ExposedClass.RegisterFromAttributes<TestDirtyClass>();
            var testObj = new TestDirtyClass { value = 42, name = "Test", position = 1.0f };
            var exposedClass = ExposedClass.Find(typeof(TestDirtyClass));
            var exposedObj = new ExposedObject("test-dirty-3", exposedClass, testObj);

            // Act & Assert - EnsureDefaultCapturedは例外を投げない
            Assert.DoesNotThrow(() => exposedObj.EnsureDefaultCaptured("nonExistent"));
            // 存在しないプロパティはFindPropertyがnullを返すためdirtyにならない
            Assert.IsFalse(exposedObj.IsPropertyDirty("nonExistent"));
        }

        #endregion

        #region Child to Parent Propagation Tests (HasDirtyChildPropertyベース)

        [Test]
        public void ValueChanged_ChildProperty_DetectedByHasDirtyChild()
        {
            // Arrange
            ExposedClass.RegisterFromAttributes<TestDirtyClass>();
            var testObj = new TestDirtyClass
            {
                value = 42,
                name = "Test",
                position = 1.0f,
                nested = new TestDirtyNestedStruct { id = 1, name = "Nested" }
            };
            var exposedClass = ExposedClass.Find(typeof(TestDirtyClass));
            var exposedObj = new ExposedObject("test-dirty-4", exposedClass, testObj);

            // Act - デフォルト値をキャプチャしてから値を変更
            exposedObj.SetDefault("nested.name", testObj.nested.name);
            testObj.nested = new TestDirtyNestedStruct { id = 1, name = "Changed" };

            // Assert
            Assert.IsTrue(exposedObj.IsPropertyDirty("nested.name"), "Child property should be dirty");
            Assert.IsTrue(exposedObj.HasDirtyChildProperty("nested"), "Parent should detect dirty child");
        }

        [Test]
        public void ValueChanged_ArrayElement_DetectedByHasDirtyChild()
        {
            // Arrange
            ExposedClass.RegisterFromAttributes<TestDirtyClassWithArray>();
            var testObj = new TestDirtyClassWithArray
            {
                items = new TestDirtyNestedStruct[]
                {
                    new TestDirtyNestedStruct { id = 1, name = "First" },
                    new TestDirtyNestedStruct { id = 2, name = "Second" }
                }
            };
            var exposedClass = ExposedClass.Find(typeof(TestDirtyClassWithArray));
            var exposedObj = new ExposedObject("test-dirty-6", exposedClass, testObj);

            // Act - SetValue経由で値を変更（EnsureDefaultCapturedが自動で呼ばれる）
            var prop = exposedObj.FindProperty("items[0].name");
            Assert.IsNotNull(prop);
            prop.Value.SetValue("Changed");

            // Assert
            Assert.IsTrue(exposedObj.IsPropertyDirty("items[0].name"), "Element property should be dirty");
            Assert.IsTrue(exposedObj.HasDirtyChildProperty("items[0]"), "Array element should have dirty child");
            Assert.IsTrue(exposedObj.HasDirtyChildProperty("items"), "Array itself should have dirty child");
        }

        #endregion

        #region Sibling Property Independence Tests

        [Test]
        public void ValueChanged_SiblingProperty_DoesNotAffectSibling()
        {
            // Arrange
            ExposedClass.RegisterFromAttributes<TestDirtyClass>();
            var testObj = new TestDirtyClass { value = 42, name = "Test", position = 1.0f };
            var exposedClass = ExposedClass.Find(typeof(TestDirtyClass));
            var exposedObj = new ExposedObject("test-dirty-7", exposedClass, testObj);

            // Act - nameのみ変更
            exposedObj.SetDefault("name", testObj.name);
            exposedObj.SetDefault("value", testObj.value);
            exposedObj.SetDefault("position", testObj.position);
            testObj.name = "Changed";

            // Assert
            Assert.IsTrue(exposedObj.IsPropertyDirty("name"));
            Assert.IsFalse(exposedObj.IsPropertyDirty("value"), "Sibling should not be dirty");
            Assert.IsFalse(exposedObj.IsPropertyDirty("position"), "Sibling should not be dirty");
        }

        [Test]
        public void ValueChanged_NestedSiblingProperty_DoesNotAffectSibling()
        {
            // Arrange
            ExposedClass.RegisterFromAttributes<TestDirtyClass>();
            var testObj = new TestDirtyClass
            {
                nested = new TestDirtyNestedStruct { id = 1, name = "Nested" }
            };
            var exposedClass = ExposedClass.Find(typeof(TestDirtyClass));
            var exposedObj = new ExposedObject("test-dirty-8", exposedClass, testObj);

            // Act - nested.nameのみ変更
            exposedObj.SetDefault("nested.name", testObj.nested.name);
            exposedObj.SetDefault("nested.id", testObj.nested.id);
            testObj.nested = new TestDirtyNestedStruct { id = 1, name = "Changed" };

            // Assert
            Assert.IsTrue(exposedObj.IsPropertyDirty("nested.name"));
            Assert.IsTrue(exposedObj.HasDirtyChildProperty("nested"), "Parent should detect dirty child");
            Assert.IsFalse(exposedObj.IsPropertyDirty("nested.id"), "Sibling property should not be dirty");
        }

        #endregion

        #region HasDirtyChildProperty Tests

        [Test]
        public void HasDirtyChildProperty_WithDirtyChild_ReturnsTrue()
        {
            // Arrange
            ExposedClass.RegisterFromAttributes<TestDirtyClass>();
            var testObj = new TestDirtyClass
            {
                nested = new TestDirtyNestedStruct { id = 1, name = "Nested" }
            };
            var exposedClass = ExposedClass.Find(typeof(TestDirtyClass));
            var exposedObj = new ExposedObject("test-dirty-9", exposedClass, testObj);

            // Act - SetValue経由で値を変更
            var prop = exposedObj.FindProperty("nested.name");
            Assert.IsNotNull(prop);
            prop.Value.SetValue("Changed");

            // Assert
            Assert.IsTrue(exposedObj.HasDirtyChildProperty("nested"));
        }

        [Test]
        public void HasDirtyChildProperty_WithoutDirtyChild_ReturnsFalse()
        {
            // Arrange
            ExposedClass.RegisterFromAttributes<TestDirtyClass>();
            var testObj = new TestDirtyClass
            {
                nested = new TestDirtyNestedStruct { id = 1, name = "Nested" }
            };
            var exposedClass = ExposedClass.Find(typeof(TestDirtyClass));
            var exposedObj = new ExposedObject("test-dirty-10", exposedClass, testObj);

            // Act - ベースライン設定後、別のプロパティを変更
            ExposedPropertyUtility.SetDefault(exposedObj);
            testObj.value = 100;

            // Assert
            Assert.IsFalse(exposedObj.HasDirtyChildProperty("nested"));
        }

        [Test]
        public void HasDirtyChildProperty_EmptyPath_ReturnsIsDirty()
        {
            // Arrange
            ExposedClass.RegisterFromAttributes<TestDirtyClass>();
            var testObj = new TestDirtyClass { value = 42 };
            var exposedClass = ExposedClass.Find(typeof(TestDirtyClass));
            var exposedObj = new ExposedObject("test-dirty-11", exposedClass, testObj);

            // Act
            exposedObj.SetDefault("value", testObj.value);
            testObj.value = 100;

            // Assert
            Assert.IsTrue(exposedObj.HasDirtyChildProperty(""));
            Assert.IsTrue(exposedObj.HasDirtyChildProperty(null));
        }

        #endregion

        #region ClearDirty / ClearPropertyDirty Tests

        [Test]
        public void ClearDirty_AllProperties_ClearsAll()
        {
            // Arrange
            ExposedClass.RegisterFromAttributes<TestDirtyClass>();
            var testObj = new TestDirtyClass { value = 42, name = "Test", position = 1.0f };
            var exposedClass = ExposedClass.Find(typeof(TestDirtyClass));
            var exposedObj = new ExposedObject("test-dirty-12", exposedClass, testObj);

            // デフォルト値をキャプチャしてから値を変更
            exposedObj.SetDefault("value", testObj.value);
            exposedObj.SetDefault("name", testObj.name);
            exposedObj.SetDefault("position", testObj.position);
            testObj.value = 100;
            testObj.name = "Changed";
            testObj.position = 2.0f;

            Assert.IsTrue(exposedObj.isDirty, "Should be dirty before ClearDirty");

            // Act
            exposedObj.ClearDirty();

            // Assert
            Assert.IsFalse(exposedObj.isDirty);
            Assert.IsFalse(exposedObj.IsPropertyDirty("value"));
            Assert.IsFalse(exposedObj.IsPropertyDirty("name"));
            Assert.IsFalse(exposedObj.IsPropertyDirty("position"));
        }

        [Test]
        public void ClearPropertyDirty_SingleProperty_ClearsOnlyTarget()
        {
            // Arrange
            ExposedClass.RegisterFromAttributes<TestDirtyClass>();
            var testObj = new TestDirtyClass { value = 42, name = "Test", position = 1.0f };
            var exposedClass = ExposedClass.Find(typeof(TestDirtyClass));
            var exposedObj = new ExposedObject("test-dirty-13", exposedClass, testObj);

            // デフォルト値をキャプチャしてから値を変更
            exposedObj.SetDefault("value", testObj.value);
            exposedObj.SetDefault("name", testObj.name);
            testObj.value = 100;
            testObj.name = "Changed";

            // Act
            exposedObj.ClearPropertyDirty("value");

            // Assert
            Assert.IsFalse(exposedObj.IsPropertyDirty("value"), "Cleared property should not be dirty");
            Assert.IsTrue(exposedObj.IsPropertyDirty("name"), "Other property should still be dirty");
            Assert.IsTrue(exposedObj.isDirty, "Object should still be dirty");
        }

        #endregion

        #region Reset Tests

        [Test]
        public void Reset_DirtyProperty_RestoresDefaultValue()
        {
            // Arrange
            ExposedClass.RegisterFromAttributes<TestDirtyClass>();
            var testObj = new TestDirtyClass { value = 42, name = "Test", position = 1.0f };
            var exposedClass = ExposedClass.Find(typeof(TestDirtyClass));
            var exposedObj = new ExposedObject("test-dirty-14", exposedClass, testObj);

            // デフォルト値をキャプチャ
            exposedObj.SetDefault("value", testObj.value);

            // 値を変更
            testObj.value = 100;

            // Act
            bool result = exposedObj.Revert("value");

            // Assert
            Assert.IsTrue(result);
            Assert.AreEqual(42, testObj.value, "Value should be restored to default");
            Assert.IsFalse(exposedObj.IsPropertyDirty("value"), "Property should no longer be dirty");
        }

        [Test]
        public void Reset_NonDirtyProperty_ReturnsTrue_BecauseDefaultIsCaptured()
        {
            // Arrange
            ExposedClass.RegisterFromAttributes<TestDirtyClass>();
            var testObj = new TestDirtyClass { value = 42, name = "Test", position = 1.0f };
            var exposedClass = ExposedClass.Find(typeof(TestDirtyClass));
            var exposedObj = new ExposedObject("test-dirty-15", exposedClass, testObj);

            // Act: コンストラクタでSetDefaultが自動実行されるため、デフォルト値が存在しRevertは成功する
            bool result = exposedObj.Revert("value");

            // Assert: デフォルト値が自動キャプチャされるのでRevertはtrueを返す（値自体は変わらない）
            Assert.IsTrue(result);
        }

        #endregion

        #region SceneToJson Integration Tests

        [Test]
        public void SceneToJson_DeltaFromDefault_OnlyOutputsDirtyProperties()
        {
            // Arrange
            ExposedClass.RegisterFromAttributes<TestDirtyClass>();
            var testObj = new TestDirtyClass
            {
                value = 42,
                name = "Test",
                position = 1.0f,
                nested = new TestDirtyNestedStruct { id = 1, name = "Nested" }
            };
            var exposedClass = ExposedClass.Find(typeof(TestDirtyClass));
            var exposedObj = new ExposedObject("test-dirty-16", exposedClass, testObj);

            // ベースライン設定
            ExposedPropertyUtility.SetDefault(exposedObj);

            // 構造体内の1つのプロパティのみを変更（SetValue経由でEnsureDefaultCapturedが呼ばれる）
            var prop = exposedObj.FindProperty("nested.name");
            Assert.IsNotNull(prop);
            prop.Value.SetValue("Changed");

            // Act
            var json = ExposedSceneSerializer.SceneToJson(new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver, SerializeMode.Delta);

            // Assert
            var jRoot = JObject.Parse(json);
            var objects = jRoot["objects"] as JArray;
            Assert.IsNotNull(objects);
            Assert.AreEqual(1, objects.Count);

            var obj = objects[0] as JObject;

            // nested.nameがdirtyなので、nestedオブジェクトは出力される
            Assert.IsNotNull(obj["nested"], "nested should be present because it has dirty child");

            var nestedObj = obj["nested"] as JObject;
            Assert.IsNotNull(nestedObj["name"], "nested.name should be present");

            // nested.idはdirtyでないので出力されない
            Assert.IsNull(nestedObj["id"], "nested.id should not be present");

            // valueはdirtyでないので出力されない
            Assert.IsNull(obj["value"], "value should not be present");
        }

        [Test]
        public void SceneToJson_DeltaFromDefault_ArrayElementPartialDirty()
        {
            // Arrange
            ExposedClass.RegisterFromAttributes<TestDirtyClassWithArray>();
            var testObj = new TestDirtyClassWithArray
            {
                items = new TestDirtyNestedStruct[]
                {
                    new TestDirtyNestedStruct { id = 1, name = "First" },
                    new TestDirtyNestedStruct { id = 2, name = "Second" }
                }
            };
            var exposedClass = ExposedClass.Find(typeof(TestDirtyClassWithArray));
            var exposedObj = new ExposedObject("test-dirty-17", exposedClass, testObj);

            // ベースライン設定
            ExposedPropertyUtility.SetDefault(exposedObj);

            // 配列の最初の要素のnameのみを変更
            var prop = exposedObj.FindProperty("items[0].name");
            Assert.IsNotNull(prop);
            prop.Value.SetValue("Changed");

            // Act
            var json = ExposedSceneSerializer.SceneToJson(new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver, SerializeMode.Delta);

            // Assert
            var jRoot = JObject.Parse(json);
            var objects = jRoot["objects"] as JArray;
            var obj = objects[0] as JObject;

            // items配列が存在することを確認
            Assert.IsNotNull(obj["items"], "items should be present");
            var itemsArray = obj["items"] as JArray;
            Assert.IsNotNull(itemsArray);

            // 最初の要素は出力される（dirtyな子がある）
            Assert.IsTrue(itemsArray.Count >= 1, "At least first element should be present");
            var firstItem = itemsArray[0] as JObject;
            Assert.IsNotNull(firstItem);
            Assert.IsNotNull(firstItem["name"], "name should be present in first item");

            // idはdirtyでないので出力されない
            Assert.IsNull(firstItem["id"], "id should not be present in first item");
        }

        #endregion

        #region FromJson Integration Tests

        [Test]
        public void FromJson_PartialUpdate_OnlyMarksDirtyOnChangedProperties()
        {
            // Arrange
            ExposedClass.RegisterFromAttributes<TestDirtyClass>();
            var testObj = new TestDirtyClass
            {
                value = 42,
                name = "Original",
                position = 1.0f,
                nested = new TestDirtyNestedStruct { id = 1, name = "Nested" }
            };
            var exposedClass = ExposedClass.Find(typeof(TestDirtyClass));
            var exposedObj = new ExposedObject("test-dirty-18", exposedClass, testObj);

            // nameのみを更新するJSON
            var json = @"{
                ""objects"": [
                    {
                        ""@type"": ""TestDirtyClass"",
                        ""@id"": ""test-dirty-18"",
                        ""name"": ""Updated""
                    }
                ]
            }";

            // Act
            ExposedSceneSerializer.SceneFromJson(json, _resolver);

            // Assert
            Assert.AreEqual("Updated", testObj.name, "name should be updated");
            Assert.IsTrue(exposedObj.IsPropertyDirty("name"), "name should be dirty");
            Assert.IsFalse(exposedObj.IsPropertyDirty("value"), "value should not be dirty");
            Assert.IsFalse(exposedObj.IsPropertyDirty("position"), "position should not be dirty");
        }

        [Test]
        public void FromJson_NestedPartialUpdate_OnlyMarksDirtyOnChangedNestedProperties()
        {
            // Arrange
            ExposedClass.RegisterFromAttributes<TestDirtyClass>();
            var testObj = new TestDirtyClass
            {
                value = 42,
                name = "Original",
                position = 1.0f,
                nested = new TestDirtyNestedStruct { id = 1, name = "OriginalNested" }
            };
            var exposedClass = ExposedClass.Find(typeof(TestDirtyClass));
            var exposedObj = new ExposedObject("test-dirty-19", exposedClass, testObj);

            // nested.nameのみを更新するJSON
            var json = @"{
                ""objects"": [
                    {
                        ""@type"": ""TestDirtyClass"",
                        ""@id"": ""test-dirty-19"",
                        ""nested"": {
                            ""name"": ""UpdatedNested""
                        }
                    }
                ]
            }";

            // Act
            ExposedSceneSerializer.SceneFromJson(json, _resolver);

            // Assert
            Assert.AreEqual("UpdatedNested", testObj.nested.name, "nested.name should be updated");
            Assert.IsTrue(exposedObj.IsPropertyDirty("nested.name"), "nested.name should be dirty");
            Assert.IsTrue(exposedObj.HasDirtyChildProperty("nested"), "nested should have dirty child");
            Assert.IsFalse(exposedObj.IsPropertyDirty("nested.id"), "nested.id should not be dirty");
            Assert.IsFalse(exposedObj.IsPropertyDirty("value"), "value should not be dirty");
        }

        #endregion

        #region GetDirtyProperties Tests

        [Test]
        public void GetDirtyProperties_ReturnsOnlyDirtyPaths()
        {
            // Arrange
            ExposedClass.RegisterFromAttributes<TestDirtyClass>();
            var testObj = new TestDirtyClass { value = 42, name = "Test", position = 1.0f };
            var exposedClass = ExposedClass.Find(typeof(TestDirtyClass));
            var exposedObj = new ExposedObject("test-dirty-20", exposedClass, testObj);

            // デフォルト値をキャプチャしてから値を変更
            exposedObj.SetDefault("value", testObj.value);
            exposedObj.SetDefault("name", testObj.name);
            exposedObj.SetDefault("position", testObj.position);
            testObj.value = 100;
            testObj.name = "Changed";

            // Act
            var dirtyProps = exposedObj.GetDirtyProperties();

            // Assert
            Assert.AreEqual(2, dirtyProps.Count);
            Assert.IsTrue(dirtyProps.Contains("value"));
            Assert.IsTrue(dirtyProps.Contains("name"));
            Assert.IsFalse(dirtyProps.Contains("position"));
        }

        #endregion

        #region AddArrayElement Dirty Marking Tests

        [Test]
        public void AddArrayElement_MarksOnlyJsonPropertiesDirty()
        {
            // Arrange
            ExposedClass.RegisterFromAttributes<TestDirtyClassWithArray>();
            var testObj = new TestDirtyClassWithArray
            {
                items = new TestDirtyNestedStruct[0]
            };
            var exposedClass = ExposedClass.Find(typeof(TestDirtyClassWithArray));
            var exposedObj = new ExposedObject("test-add-dirty-1", exposedClass, testObj);

            var property = exposedObj.FindProperty("items");
            Assert.IsNotNull(property);

            // nameのみを設定するJSON（idは設定しない）
            var json = @"{ ""value"": { ""name"": ""NewItem"" } }";

            // Act
            var result = ExposedPropertySerializer.AddArrayElement(json, property.Value);

            // Assert
            Assert.IsTrue(result, "AddArrayElement should succeed");
            Assert.AreEqual(1, testObj.items.Length, "Array should have 1 element");
            Assert.AreEqual("NewItem", testObj.items[0].name, "name should be set");

            // JSONで設定したnameはdirtyになる
            Assert.IsTrue(exposedObj.IsPropertyDirty("items[0].name"), "items[0].name should be dirty");
            // JSONで設定していないidはdirtyにならない
            Assert.IsFalse(exposedObj.IsPropertyDirty("items[0].id"), "items[0].id should NOT be dirty");
            // 配列と要素自体はdirty child
            Assert.IsTrue(exposedObj.HasDirtyChildProperty("items[0]"), "items[0] should have dirty child");
            Assert.IsTrue(exposedObj.HasDirtyChildProperty("items"), "items should have dirty child");
        }

        #endregion

        #region FromJson Array Element Default Value Tests

        [Test]
        public void FromJson_NewArrayElement_SetsDefaultValueFromCreateDefaultElement()
        {
            // Arrange
            // TestDirtyStructWithDefault.value のデフォルト値は 1
            var testObj = new TestDirtyClassWithDefaultArray
            {
                items = new TestDirtyStructWithDefault[]
                {
                    new TestDirtyStructWithDefault { value = 5, name = "First" }
                }
            };
            var exposedClass = ExposedClass.Find(typeof(TestDirtyClassWithDefaultArray));
            var exposedObj = new ExposedObject("test-default-1", exposedClass, testObj);

            // デフォルト値を設定
            ExposedPropertyUtility.SetDefault(exposedObj);

            // items配列に新しい要素を追加するJSON（2要素に拡張）
            // 1番目の要素は@typeのみ（変更なし）、2番目の要素は新規追加でvalueを10に設定
            var json = @"{
                ""objects"": [
                    {
                        ""@type"": ""TestDirtyClassWithDefaultArray"",
                        ""@id"": ""test-default-1"",
                        ""items"": [
                            { ""@type"": ""TestDirtyStructWithDefault"" },
                            { ""@type"": ""TestDirtyStructWithDefault"", ""value"": 10 }
                        ]
                    }
                ]
            }";

            // Act
            ExposedSceneSerializer.SceneFromJson(json, _resolver);

            // Assert
            Assert.AreEqual(2, testObj.items.Length, "Array should have 2 elements");

            // 既存要素 (index 0): 値は変わらず 5
            Assert.AreEqual(5, testObj.items[0].value, "First element value should remain 5");

            // 新規要素 (index 1): 値は 10 に設定される
            Assert.AreEqual(10, testObj.items[1].value, "Second element value should be 10");

            // 既存要素をリセットして初期値（5）に戻ることを確認
            var property0 = exposedObj.FindProperty("items[0].value");
            Assert.IsNotNull(property0);
            property0.Value.RevertValue();
            Assert.AreEqual(5, testObj.items[0].value, "Default value for existing element should be 5 (initial value)");

            // 新規要素をリセットしてCreateDefaultElementの値（1）に戻ることを確認
            var property1 = exposedObj.FindProperty("items[1].value");
            Assert.IsNotNull(property1);
            property1.Value.RevertValue();
            Assert.AreEqual(1, testObj.items[1].value, "Default value for new element should be 1 (from CreateDefaultElement)");
        }

        [Test]
        public void FromJson_NewArrayElement_ResetRestoresToCreateDefaultElementValue()
        {
            // Arrange
            var testObj = new TestDirtyClassWithDefaultArray
            {
                items = new TestDirtyStructWithDefault[]
                {
                    new TestDirtyStructWithDefault { value = 5, name = "First" }
                }
            };
            var exposedClass = ExposedClass.Find(typeof(TestDirtyClassWithDefaultArray));
            var exposedObj = new ExposedObject("test-default-2", exposedClass, testObj);

            // デフォルト値を設定
            ExposedPropertyUtility.SetDefault(exposedObj);

            // 新しい要素を追加
            var json = @"{
                ""objects"": [
                    {
                        ""@type"": ""TestDirtyClassWithDefaultArray"",
                        ""@id"": ""test-default-2"",
                        ""items"": [
                            { ""@type"": ""TestDirtyStructWithDefault"" },
                            { ""@type"": ""TestDirtyStructWithDefault"", ""value"": 10 }
                        ]
                    }
                ]
            }";

            ExposedSceneSerializer.SceneFromJson(json, _resolver);

            // Act - 新規要素をリセット
            var property = exposedObj.FindProperty("items[1].value");
            Assert.IsNotNull(property);
            property.Value.RevertValue();

            // Assert
            // 新規要素のvalueはCreateDefaultElementのデフォルト値（1）にリセットされる
            Assert.AreEqual(1, testObj.items[1].value, "New element value should be reset to 1 (CreateDefaultElement default)");
        }

        [Test]
        public void FromJson_ExistingArrayElement_ResetRestoresToInitialValue()
        {
            // Arrange
            var testObj = new TestDirtyClassWithDefaultArray
            {
                items = new TestDirtyStructWithDefault[]
                {
                    new TestDirtyStructWithDefault { value = 5, name = "First" }
                }
            };
            var exposedClass = ExposedClass.Find(typeof(TestDirtyClassWithDefaultArray));
            var exposedObj = new ExposedObject("test-default-3", exposedClass, testObj);

            // デフォルト値を設定
            ExposedPropertyUtility.SetDefault(exposedObj);

            // 既存要素のvalueを更新
            var json = @"{
                ""objects"": [
                    {
                        ""@type"": ""TestDirtyClassWithDefaultArray"",
                        ""@id"": ""test-default-3"",
                        ""items"": [
                            { ""@type"": ""TestDirtyStructWithDefault"", ""value"": 100 }
                        ]
                    }
                ]
            }";

            ExposedSceneSerializer.SceneFromJson(json, _resolver);
            Assert.AreEqual(100, testObj.items[0].value, "Value should be updated to 100");

            // Act - 既存要素をリセット
            var property = exposedObj.FindProperty("items[0].value");
            Assert.IsNotNull(property);
            property.Value.RevertValue();

            // Assert
            // 既存要素のvalueは初期値（5）にリセットされる
            Assert.AreEqual(5, testObj.items[0].value, "Existing element value should be reset to 5 (initial value)");
        }

        #endregion

        #region SetDefaultValue Tests

        [Test]
        public void SetDefaultValue_StructWithNonDirtyChild_SkipsNonDirtyProperties()
        {
            // Arrange
            var testObj = new TestDirtyClass
            {
                value = 42,
                name = "Test",
                nested = new TestDirtyNestedStruct { id = 1, name = "Nested" }
            };
            var exposedClass = ExposedClass.Find(typeof(TestDirtyClass));
            var exposedObj = new ExposedObject("test-setdefault-1", exposedClass, testObj);

            // nested.nameのみをSetValue経由で変更（EnsureDefaultCapturedが呼ばれる）
            var nestedNameProp = exposedObj.FindProperty("nested.name");
            nestedNameProp.Value.SetValue("Changed");

            // nested.nameをリバート
            nestedNameProp = exposedObj.FindProperty("nested.name");
            nestedNameProp.Value.RevertValue();

            // 値を変更
            testObj.nested = new TestDirtyNestedStruct { id = 99, name = "Changed" };

            // Act - nestedに対してRevertValueを呼び出す
            var nestedProp = exposedObj.FindProperty("nested");
            nestedProp.Value.RevertValue();

            // Assert
            // コンストラクタでSetDefaultが自動実行されるため、idもデフォルト値(1)にリバートされる
            Assert.AreEqual(1, testObj.nested.id, "id should be reverted to default (auto-captured by constructor)");
        }

        [Test]
        public void SetDefaultValue_Property_ClearsDirtyByRevert()
        {
            // Arrange
            var testObj = new TestDirtyClass { value = 42, name = "Test" };
            var exposedClass = ExposedClass.Find(typeof(TestDirtyClass));
            var exposedObj = new ExposedObject("test-setdefault-2", exposedClass, testObj);

            // SetValue経由で値を変更（EnsureDefaultCapturedが呼ばれる）
            var valueProp = exposedObj.FindProperty("value") ?? throw new Exception("Property not found");
            valueProp.SetValue(100);
            Assert.IsTrue(exposedObj.IsPropertyDirty("value"), "Property should be dirty after value change");

            // Act - リバートで値を戻す
            valueProp = exposedObj.FindProperty("value") ?? throw new Exception("Property not found");
            valueProp.RevertValue();

            // Assert
            Assert.IsFalse(exposedObj.IsPropertyDirty("value"), "Property should not be dirty after revert");
        }

        [Test]
        public void SetDefaultValue_Struct_ClearsDirtyFlagForParent()
        {
            // Arrange
            var testObj = new TestDirtyClass
            {
                nested = new TestDirtyNestedStruct { id = 1, name = "Test" }
            };
            var exposedClass = ExposedClass.Find(typeof(TestDirtyClass));
            var exposedObj = new ExposedObject("test-setdefault-3", exposedClass, testObj);

            // nested.nameのみをSetValue経由で変更
            var nestedNameProp = exposedObj.FindProperty("nested.name") ?? throw new Exception("Property not found");
            nestedNameProp.SetValue("Changed");

            // リバートして元の値に戻す
            nestedNameProp = exposedObj.FindProperty("nested.name") ?? throw new Exception("Property not found");
            nestedNameProp.RevertValue();

            Assert.IsTrue(exposedObj.HasDirtyChildProperty("nested") == false || !exposedObj.IsPropertyDirty("nested.name"),
                "nested.name should not be dirty after revert");
            Assert.IsFalse(exposedObj.IsPropertyDirty("nested.id"), "nested.id should not be dirty");

            // 値を変更（dirtyでないidも変更）
            testObj.nested = new TestDirtyNestedStruct { id = 99, name = "Changed" };

            // Act - 親のRevertValueを呼び出す
            var nestedProp = exposedObj.FindProperty("nested") ?? throw new Exception("Property not found");
            nestedProp.RevertValue();

            // Assert
            // 子プロパティもすべてisDirty==falseになる
            Assert.IsFalse(exposedObj.IsPropertyDirty("nested.id"), "nested.id should not be dirty after RevertValue");
            Assert.IsFalse(exposedObj.IsPropertyDirty("nested.name"), "nested.name should not be dirty after RevertValue");
            // コンストラクタでSetDefaultが自動実行されるため、idもデフォルト値(1)にリバートされる
            Assert.AreEqual(1, testObj.nested.id, "nested.id should be reverted to default (auto-captured by constructor)");
        }

        #endregion

        #region Comparison-based Dirty Detection Tests

        [Test]
        public void ValueUnchanged_WithDefaultCaptured_NotDirty()
        {
            // Arrange - 値を変更せずにデフォルト値をキャプチャ
            var testObj = new TestDirtyClass { value = 42, name = "Test" };
            var exposedClass = ExposedClass.Find(typeof(TestDirtyClass));
            var exposedObj = new ExposedObject("test-compare-1", exposedClass, testObj);

            // Act - デフォルト値をキャプチャするが値は変更しない
            exposedObj.SetDefault("value", testObj.value);

            // Assert - 値が変わっていないのでdirtyではない
            Assert.IsFalse(exposedObj.IsPropertyDirty("value"), "Unchanged value should not be dirty");
        }

        [Test]
        public void ExternalValueChange_DetectedAsDirty()
        {
            // Arrange - API経由ではなく直接フィールドを変更
            var testObj = new TestDirtyClass { value = 42, name = "Test" };
            var exposedClass = ExposedClass.Find(typeof(TestDirtyClass));
            var exposedObj = new ExposedObject("test-compare-2", exposedClass, testObj);

            exposedObj.SetDefault("value", testObj.value);

            // Act - 外部から直接変更（アニメーション等を想定）
            testObj.value = 999;

            // Assert - 比較ベースなので外部変更も検出される
            Assert.IsTrue(exposedObj.IsPropertyDirty("value"), "Externally changed value should be detected as dirty");
        }

        #endregion

        #region List<T> Dirty Tracking Tests

        [Test]
        public void ListAdd_DetectedAsDirty()
        {
            // Arrange
            var testObj = new TestDirtyClassWithList
            {
                intList = new List<int> { 1, 2, 3 }
            };
            var exposedClass = ExposedClass.Find(typeof(TestDirtyClassWithList));
            var exposedObj = new ExposedObject("test-list-add-1", exposedClass, testObj);

            exposedObj.SetDefault("intList", testObj.intList);

            // Act - List に要素を追加（in-place変更）
            testObj.intList.Add(4);

            // Assert
            Assert.IsTrue(exposedObj.IsPropertyDirty("intList"), "List should be dirty after Add");
        }

        [Test]
        public void ListRemoveAt_DetectedAsDirty()
        {
            // Arrange
            var testObj = new TestDirtyClassWithList
            {
                intList = new List<int> { 10, 20, 30 }
            };
            var exposedClass = ExposedClass.Find(typeof(TestDirtyClassWithList));
            var exposedObj = new ExposedObject("test-list-remove-1", exposedClass, testObj);

            exposedObj.SetDefault("intList", testObj.intList);

            // Act - List から要素を削除（in-place変更）
            testObj.intList.RemoveAt(1);

            // Assert
            Assert.IsTrue(exposedObj.IsPropertyDirty("intList"), "List should be dirty after RemoveAt");
        }

        [Test]
        public void ListReorder_DetectedAsDirty()
        {
            // Arrange
            var testObj = new TestDirtyClassWithList
            {
                intList = new List<int> { 1, 2, 3 }
            };
            var exposedClass = ExposedClass.Find(typeof(TestDirtyClassWithList));
            var exposedObj = new ExposedObject("test-list-reorder-1", exposedClass, testObj);

            exposedObj.SetDefault("intList", testObj.intList);

            // Act - 要素を入れ替え
            testObj.intList[0] = 3;
            testObj.intList[2] = 1;

            // Assert
            Assert.IsTrue(exposedObj.IsPropertyDirty("intList"), "List should be dirty after reorder");
        }

        [Test]
        public void ListUnchanged_NotDirty()
        {
            // Arrange
            var testObj = new TestDirtyClassWithList
            {
                intList = new List<int> { 1, 2, 3 }
            };
            var exposedClass = ExposedClass.Find(typeof(TestDirtyClassWithList));
            var exposedObj = new ExposedObject("test-list-unchanged-1", exposedClass, testObj);

            exposedObj.SetDefault("intList", testObj.intList);

            // Act - 何も変更しない

            // Assert
            Assert.IsFalse(exposedObj.IsPropertyDirty("intList"), "Unchanged List should not be dirty");
        }

        [Test]
        public void ListClear_DetectedAsDirty()
        {
            // Arrange
            var testObj = new TestDirtyClassWithList
            {
                intList = new List<int> { 1, 2, 3 }
            };
            var exposedClass = ExposedClass.Find(typeof(TestDirtyClassWithList));
            var exposedObj = new ExposedObject("test-list-clear-1", exposedClass, testObj);

            exposedObj.SetDefault("intList", testObj.intList);

            // Act - Listをクリア
            testObj.intList.Clear();

            // Assert
            Assert.IsTrue(exposedObj.IsPropertyDirty("intList"), "List should be dirty after Clear");
        }

        [Test]
        public void ListStringAdd_DetectedAsDirty()
        {
            // Arrange
            var testObj = new TestDirtyClassWithList
            {
                stringList = new List<string> { "a", "b" }
            };
            var exposedClass = ExposedClass.Find(typeof(TestDirtyClassWithList));
            var exposedObj = new ExposedObject("test-list-string-1", exposedClass, testObj);

            exposedObj.SetDefault("stringList", testObj.stringList);

            // Act
            testObj.stringList.Add("c");

            // Assert
            Assert.IsTrue(exposedObj.IsPropertyDirty("stringList"), "String List should be dirty after Add");
        }

        [Test]
        public void ListStructAdd_DetectedAsDirty()
        {
            // Arrange
            var testObj = new TestDirtyClassWithList
            {
                structList = new List<TestDirtyNestedStruct>
                {
                    new TestDirtyNestedStruct { id = 1, name = "First" }
                }
            };
            var exposedClass = ExposedClass.Find(typeof(TestDirtyClassWithList));
            var exposedObj = new ExposedObject("test-list-struct-1", exposedClass, testObj);

            exposedObj.SetDefault("structList", testObj.structList);

            // Act
            testObj.structList.Add(new TestDirtyNestedStruct { id = 2, name = "Second" });

            // Assert
            Assert.IsTrue(exposedObj.IsPropertyDirty("structList"), "Struct List should be dirty after Add");
        }

        [Test]
        public void ListClearDirty_UpdatesDefaultToCurrentValue()
        {
            // Arrange
            var testObj = new TestDirtyClassWithList
            {
                intList = new List<int> { 1, 2, 3 }
            };
            var exposedClass = ExposedClass.Find(typeof(TestDirtyClassWithList));
            var exposedObj = new ExposedObject("test-list-cleardirty-1", exposedClass, testObj);

            exposedObj.SetDefault("intList", testObj.intList);
            testObj.intList.Add(4);
            Assert.IsTrue(exposedObj.IsPropertyDirty("intList"), "Should be dirty before ClearDirty");

            // Act - ClearDirtyでデフォルト値を現在値に更新
            exposedObj.ClearDirty();

            // Assert - dirtyが解消される
            Assert.IsFalse(exposedObj.IsPropertyDirty("intList"), "List should not be dirty after ClearDirty");

            // さらに変更を加えると再びdirtyになる
            testObj.intList.Add(5);
            Assert.IsTrue(exposedObj.IsPropertyDirty("intList"), "List should be dirty again after further Add");
        }

        [Test]
        public void ListElementModified_DetectedAsDirty()
        {
            // Arrange
            var testObj = new TestDirtyClassWithList
            {
                intList = new List<int> { 10, 20, 30 }
            };
            var exposedClass = ExposedClass.Find(typeof(TestDirtyClassWithList));
            var exposedObj = new ExposedObject("test-list-modify-1", exposedClass, testObj);

            exposedObj.SetDefault("intList", testObj.intList);

            // Act - 要素の値を変更（サイズは同じ）
            testObj.intList[1] = 99;

            // Assert
            Assert.IsTrue(exposedObj.IsPropertyDirty("intList"), "List should be dirty after element modification");
        }

        #endregion

        #region ObjectContainer-like List<RefType> Dirty & Serialization Tests

        [Test]
        public void ContainerRefListAdd_DetectedAsDirty()
        {
            // Arrange - ObjectContainerと同様のパターン: List<参照型>を持つコンテナ
            var item1 = new TestDirtyRefItem { name = "Item1", value = 10 };
            var testObj = new TestDirtyContainerWithRefList
            {
                items = new List<TestDirtyRefItem> { item1 }
            };
            var exposedClass = ExposedClass.Find(typeof(TestDirtyContainerWithRefList));
            var containerExposedObj = new ExposedObject("test-container-1", exposedClass, testObj);

            // デフォルト値をキャプチャ（ObjectContainer.Initializeと同等）
            ExposedPropertyUtility.SetDefault(containerExposedObj);

            // Act - 新しい要素を追加（ObjectContainer.AddExposedObjectと同等）
            var item2 = new TestDirtyRefItem { name = "Item2", value = 20 };
            testObj.items.Add(item2);

            // Assert
            Assert.IsTrue(containerExposedObj.IsPropertyDirty("items"), "items list should be dirty after Add");
            Assert.IsTrue(containerExposedObj.isDirty, "Container should be dirty");
        }

        [Test]
        public void ContainerRefListRemove_DetectedAsDirty()
        {
            // Arrange
            var item1 = new TestDirtyRefItem { name = "Item1", value = 10 };
            var item2 = new TestDirtyRefItem { name = "Item2", value = 20 };
            var testObj = new TestDirtyContainerWithRefList
            {
                items = new List<TestDirtyRefItem> { item1, item2 }
            };
            var exposedClass = ExposedClass.Find(typeof(TestDirtyContainerWithRefList));
            var containerExposedObj = new ExposedObject("test-container-2", exposedClass, testObj);

            ExposedPropertyUtility.SetDefault(containerExposedObj);

            // Act - 要素を削除
            testObj.items.Remove(item2);

            // Assert
            Assert.IsTrue(containerExposedObj.IsPropertyDirty("items"), "items list should be dirty after Remove");
        }

        [Test]
        public void ContainerRefListAdd_SceneToJson_DeltaFromDefault_OutputsItems()
        {
            // Arrange - ObjectContainerの保存フローを再現
            var item1 = new TestDirtyRefItem { name = "Item1", value = 10 };
            var testObj = new TestDirtyContainerWithRefList
            {
                items = new List<TestDirtyRefItem> { item1 }
            };
            var exposedClass = ExposedClass.Find(typeof(TestDirtyContainerWithRefList));
            var containerExposedObj = new ExposedObject("test-container-3", exposedClass, testObj);

            // 子要素のExposedObjectを登録
            var itemExposedClass = ExposedClass.Find(typeof(TestDirtyRefItem));
            var item1ExposedObj = new ExposedObject("item-1", itemExposedClass, item1);

            // デフォルト値をキャプチャ
            ExposedPropertyUtility.SetDefault(containerExposedObj);

            // Act - 新しい要素を追加
            var item2 = new TestDirtyRefItem { name = "Item2", value = 20 };
            var item2ExposedObj = new ExposedObject("item-2", itemExposedClass, item2);
            testObj.items.Add(item2);

            // DeltaFromDefaultでシリアライズ（SaveCurrentDataと同等）
            var json = ExposedSceneSerializer.SceneToJson(new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver, SerializeMode.Delta);

            // Assert - ObjectContainerのitemsが出力される
            var jRoot = JObject.Parse(json);
            var objects = jRoot["objects"] as JArray;
            Assert.IsNotNull(objects, "objects should exist");

            // コンテナオブジェクトを探す
            JObject containerJson = null;
            foreach (var obj in objects)
            {
                if (EntryKey(obj) =="test-container-3")
                {
                    containerJson = obj as JObject;
                    break;
                }
            }
            Assert.IsNotNull(containerJson, "Container should be in output");
            Assert.IsNotNull(containerJson["items"], "items should be present in container output");

            var itemsArray = containerJson["items"] as JArray;
            Assert.IsNotNull(itemsArray);
            // append-only ケースでは leading の未変更スタブは省略され、@op:"new" 要素のみが出力される
            // （JsonDiff_ObjectArray_AppendOnly_ForPersistence_OmitsUnchangedStubs と整合）。
            Assert.AreEqual(1, itemsArray.Count, "items array should contain only the new element (@op:new)");
            var newElem = itemsArray[0] as JObject;
            Assert.IsNotNull(newElem);
            Assert.AreEqual("new", newElem["@op"]?.Value<string>(), "Element should be marked as new");
        }

        [Test]
        public void ContainerRefListAdd_DeltaFromDefault_SaveLoad_AddedObjectPreservesDirtyFlag()
        {
            // Arrange - コンテナ（items: [item1]）を作成
            var item1 = new TestDirtyRefItem { name = "Item1", value = 10 };
            var testObj = new TestDirtyContainerWithRefList
            {
                items = new List<TestDirtyRefItem> { item1 }
            };
            var exposedClass = ExposedClass.Find(typeof(TestDirtyContainerWithRefList));
            var containerExposedObj = new ExposedObject("test-container-save-load", exposedClass, testObj);

            var itemExposedClass = ExposedClass.Find(typeof(TestDirtyRefItem));
            var item1ExposedObj = new ExposedObject("item-sl-1", itemExposedClass, item1);

            // ベースラインキャプチャ
            ExposedPropertyUtility.SetDefault(containerExposedObj);

            // Act - 新しい要素を追加
            var item2 = new TestDirtyRefItem { name = "Item2", value = 20 };
            var item2ExposedObj = new ExposedObject("item-sl-2", itemExposedClass, item2);
            testObj.items.Add(item2);

            // DeltaFromDefaultでシリアライズ
            var json = ExposedSceneSerializer.SceneToJson(new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver, SerializeMode.Delta);

            // SceneFromJsonでデシリアライズ（読み込み）
            ExposedSceneSerializer.SceneFromJson(json, _resolver);

            // Assert - item2のExposedObjectはデフォルト値未設定のため dirty ではない
            // hasBaseline廃止後: デフォルト値が設定されていないプロパティはdirtyと見なさない
            Assert.IsFalse(item2ExposedObj.isDirty,
                "Object without default values should not be dirty");
        }

        [Test]
        public void ContainerRefList_SceneToJson_DeltaFromDefault_NonDirtyElementsAreEmptyStubs()
        {
            // Arrange - 非dirty要素が空stubで出力されることを検証
            var item1 = new TestDirtyRefItem { name = "Item1", value = 10 };
            var item2 = new TestDirtyRefItem { name = "Item2", value = 20 };
            var testObj = new TestDirtyContainerWithRefList
            {
                items = new List<TestDirtyRefItem> { item1, item2 }
            };
            var exposedClass = ExposedClass.Find(typeof(TestDirtyContainerWithRefList));
            var containerExposedObj = new ExposedObject("test-container-4", exposedClass, testObj);

            // 子要素のExposedObjectを登録
            var itemExposedClass = ExposedClass.Find(typeof(TestDirtyRefItem));
            new ExposedObject("item-ref-1", itemExposedClass, item1);
            new ExposedObject("item-ref-2", itemExposedClass, item2);

            // デフォルト値をキャプチャ
            ExposedPropertyUtility.SetDefault(containerExposedObj);

            // Act - 新しい要素を追加してdirtyにする
            var item3 = new TestDirtyRefItem { name = "Item3", value = 30 };
            new ExposedObject("item-ref-3", itemExposedClass, item3);
            testObj.items.Add(item3);

            var json = ExposedSceneSerializer.SceneToJson(new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver, SerializeMode.Delta);

            // Assert
            var jRoot = JObject.Parse(json);
            var objects = jRoot["objects"] as JArray;
            JObject containerJson = null;
            foreach (var obj in objects)
            {
                if (EntryKey(obj) =="test-container-4")
                {
                    containerJson = obj as JObject;
                    break;
                }
            }
            Assert.IsNotNull(containerJson, "Container should be in output");
            var itemsArray = containerJson["items"] as JArray;
            Assert.IsNotNull(itemsArray);
            // 仕様変更: append-only ケースでは leading の未変更スタブは省略される
            // （JsonDiff_ObjectArray_AppendOnly_ForPersistence_OmitsUnchangedStubs 参照）。
            // 出力は @op:"new" 要素のみで、デシリアライズ時に既存配列に追記される。
            Assert.AreEqual(1, itemsArray.Count, "items array should contain only the new element (@op:new)");

            var newElem = itemsArray[0] as JObject;
            Assert.IsNotNull(newElem);
            Assert.AreEqual("new", newElem["@op"]?.Value<string>(), "Element should be marked as new");
        }

        #endregion

        #region SetDefault → Load → Save Roundtrip Tests

        [Test]
        public void SetDefault_ThenLoadFromJson_ThenSaveDeltaFromDefault_PreservesLoadedValues()
        {
            // Arrange - オブジェクト生成（初期値: value=0, name="", position=0）
            var testObj = new TestDirtyClass
            {
                value = 0,
                name = "",
                position = 0f,
                nested = new TestDirtyNestedStruct { id = 0, name = "" }
            };
            var exposedClass = ExposedClass.Find(typeof(TestDirtyClass));
            var exposedObj = new ExposedObject("test-roundtrip-1", exposedClass, testObj);

            // Step 1: SetDefault（初期値をベースラインとしてキャプチャ）
            ExposedPropertyUtility.SetDefault(exposedObj);

            // Step 2: SceneFromJson（保存済みJSON: value=42, name="Changed" をロード）
            var loadJson = @"{
                ""objects"": [
                    {
                        ""@type"": ""TestDirtyClass"",
                        ""@id"": ""test-roundtrip-1"",
                        ""value"": 42,
                        ""name"": ""Changed""
                    }
                ]
            }";
            ExposedSceneSerializer.SceneFromJson(loadJson, _resolver);

            // ロードされた値が反映されていることを確認
            Assert.AreEqual(42, testObj.value, "value should be loaded from JSON");
            Assert.AreEqual("Changed", testObj.name, "name should be loaded from JSON");

            // Step 3: SceneToJson(DeltaFromDefault) で保存
            var savedJson = ExposedSceneSerializer.SceneToJson(new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver, SerializeMode.Delta);

            // Assert: 出力JSONにvalue=42, name="Changed"が含まれること
            var jRoot = JObject.Parse(savedJson);
            var objects = jRoot["objects"] as JArray;
            Assert.IsNotNull(objects, "objects array should exist");
            Assert.AreEqual(1, objects.Count, "Should have 1 object");

            var obj = objects[0] as JObject;
            Assert.IsNotNull(obj["value"], "value should be in saved JSON (dirty)");
            Assert.AreEqual(42, obj["value"].Value<int>(), "value should be 42");
            Assert.IsNotNull(obj["name"], "name should be in saved JSON (dirty)");
            Assert.AreEqual("Changed", obj["name"].Value<string>(), "name should be 'Changed'");

            // positionは変更されていないので出力されないこと
            Assert.IsNull(obj["position"], "position should not be in saved JSON (not dirty)");
        }

        [Test]
        public void SetDefault_ThenLoadFromJson_ThenSaveDeltaFromDefault_NestedStruct_PreservesLoadedValues()
        {
            // Arrange - ネスト構造体を持つオブジェクト生成
            var testObj = new TestDirtyClass
            {
                value = 0,
                name = "",
                position = 0f,
                nested = new TestDirtyNestedStruct { id = 0, name = "" }
            };
            var exposedClass = ExposedClass.Find(typeof(TestDirtyClass));
            var exposedObj = new ExposedObject("test-roundtrip-2", exposedClass, testObj);

            // Step 1: SetDefault
            ExposedPropertyUtility.SetDefault(exposedObj);

            // Step 2: SceneFromJson（nested.name="Updated" をロード）
            var loadJson = @"{
                ""objects"": [
                    {
                        ""@type"": ""TestDirtyClass"",
                        ""@id"": ""test-roundtrip-2"",
                        ""nested"": {
                            ""@type"": ""TestDirtyNestedStruct"",
                            ""name"": ""Updated""
                        }
                    }
                ]
            }";
            ExposedSceneSerializer.SceneFromJson(loadJson, _resolver);

            // ロードされた値が反映されていることを確認
            Assert.AreEqual("Updated", testObj.nested.name, "nested.name should be loaded from JSON");

            // Step 3: SceneToJson(DeltaFromDefault)
            var savedJson = ExposedSceneSerializer.SceneToJson(new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver, SerializeMode.Delta);

            // Assert: nested.nameが出力に含まれること
            var jRoot = JObject.Parse(savedJson);
            var objects = jRoot["objects"] as JArray;
            Assert.IsNotNull(objects, "objects array should exist");
            Assert.AreEqual(1, objects.Count, "Should have 1 object");

            var obj = objects[0] as JObject;
            Assert.IsNotNull(obj["nested"], "nested should be in saved JSON");
            var nestedObj = obj["nested"] as JObject;
            Assert.IsNotNull(nestedObj["name"], "nested.name should be in saved JSON (dirty)");
            Assert.AreEqual("Updated", nestedObj["name"].Value<string>(), "nested.name should be 'Updated'");

            // nested.idは変更されていないので出力されないこと
            Assert.IsNull(nestedObj["id"], "nested.id should not be in saved JSON (not dirty)");

            // valueは変更されていないので出力されないこと
            Assert.IsNull(obj["value"], "value should not be in saved JSON (not dirty)");
        }

        [Test]
        public void SetDefault_ThenLoadFromJson_ThenSaveDeltaFromDefault_Array_PreservesLoadedValues()
        {
            // Arrange - 配列を持つオブジェクト生成
            var testObj = new TestDirtyClassWithArray
            {
                items = new TestDirtyNestedStruct[]
                {
                    new TestDirtyNestedStruct { id = 1, name = "Original" }
                }
            };
            var exposedClass = ExposedClass.Find(typeof(TestDirtyClassWithArray));
            var exposedObj = new ExposedObject("test-roundtrip-3", exposedClass, testObj);

            // Step 1: SetDefault
            ExposedPropertyUtility.SetDefault(exposedObj);

            // Step 2: SceneFromJson（配列に新しい要素を含むJSONをロード）
            var loadJson = @"{
                ""objects"": [
                    {
                        ""@type"": ""TestDirtyClassWithArray"",
                        ""@id"": ""test-roundtrip-3"",
                        ""items"": [
                            { ""@type"": ""TestDirtyNestedStruct"", ""id"": 1, ""name"": ""Original"" },
                            { ""@type"": ""TestDirtyNestedStruct"", ""id"": 2, ""name"": ""NewItem"" }
                        ]
                    }
                ]
            }";
            ExposedSceneSerializer.SceneFromJson(loadJson, _resolver);

            // ロードされた値が反映されていることを確認
            Assert.AreEqual(2, testObj.items.Length, "Array should have 2 elements after load");
            Assert.AreEqual("NewItem", testObj.items[1].name, "Second item name should be loaded");

            // Step 3: SceneToJson(DeltaFromDefault)
            var savedJson = ExposedSceneSerializer.SceneToJson(new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver, SerializeMode.Delta);

            // Assert: 配列の変更が出力に含まれること
            var jRoot = JObject.Parse(savedJson);
            var objects = jRoot["objects"] as JArray;
            Assert.IsNotNull(objects, "objects array should exist");
            Assert.AreEqual(1, objects.Count, "Should have 1 object");

            var obj = objects[0] as JObject;
            Assert.IsNotNull(obj["items"], "items should be in saved JSON (dirty)");
            var itemsArray = obj["items"] as JArray;
            Assert.IsNotNull(itemsArray, "items should be an array");
            // append-only ケースでは leading の未変更スタブは省略され、@op:"new" 要素のみが出力される
            // （JsonDiff_ObjectArray_AppendOnly_ForPersistence_OmitsUnchangedStubs と整合）。
            Assert.AreEqual(1, itemsArray.Count, "items array should contain only the new element (@op:new)");

            // 出力された @op:"new" 要素が NewItem であること
            var newItem = itemsArray[0] as JObject;
            Assert.IsNotNull(newItem, "New item should exist");
            Assert.AreEqual("new", newItem["@op"]?.Value<string>(), "Element should be marked as new");
            Assert.AreEqual(2, newItem["id"].Value<int>(), "New item id should be 2");
            Assert.AreEqual("NewItem", newItem["name"].Value<string>(), "New item name should be 'NewItem'");
        }

        #endregion

        #region Delta Array Format Tests

        [Test]
        public void DeltaFormat_ExistingElementPartialChange_OutputsChangedPropertiesOnly()
        {
            // Arrange: デフォルト [{ id:1, name:"First" }, { id:2, name:"Second" }]
            ExposedClass.RegisterFromAttributes<TestDirtyClassWithArray>();
            var testObj = new TestDirtyClassWithArray
            {
                items = new TestDirtyNestedStruct[]
                {
                    new TestDirtyNestedStruct { id = 1, name = "First" },
                    new TestDirtyNestedStruct { id = 2, name = "Second" }
                }
            };
            var exposedClass = ExposedClass.Find(typeof(TestDirtyClassWithArray));
            var exposedObj = new ExposedObject("test-delta-array-1", exposedClass, testObj);
            ExposedPropertyUtility.SetDefault(exposedObj);

            // items[1].name のみ変更
            var prop = exposedObj.FindProperty("items[1].name");
            Assert.IsNotNull(prop);
            prop.Value.SetValue("Changed");

            // Act
            var json = ExposedSceneSerializer.SceneToJson(new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver, SerializeMode.Delta);

            // Assert
            var jRoot = JObject.Parse(json);
            var obj = (jRoot["objects"] as JArray)[0] as JObject;
            var itemsArray = obj["items"] as JArray;
            Assert.IsNotNull(itemsArray);

            // items[0] は空オブジェクト（未変更）
            var first = itemsArray[0] as JObject;
            Assert.IsNotNull(first, "First element should be present as empty object");
            Assert.AreEqual(0, first.Count, "First element should be empty (no changes)");

            // items[1] は name のみ出力
            var second = itemsArray[1] as JObject;
            Assert.IsNotNull(second);
            Assert.IsNotNull(second["name"], "Changed name should be present");
            Assert.AreEqual("Changed", second["name"].Value<string>());
            Assert.IsNull(second["id"], "Unchanged id should not be present");
        }

        [Test]
        public void DeltaFormat_TrailingUnchangedElements_AreOmitted()
        {
            // Arrange: デフォルト [{ id:1, name:"A" }, { id:2, name:"B" }, { id:3, name:"C" }]
            ExposedClass.RegisterFromAttributes<TestDirtyClassWithArray>();
            var testObj = new TestDirtyClassWithArray
            {
                items = new TestDirtyNestedStruct[]
                {
                    new TestDirtyNestedStruct { id = 1, name = "A" },
                    new TestDirtyNestedStruct { id = 2, name = "B" },
                    new TestDirtyNestedStruct { id = 3, name = "C" }
                }
            };
            var exposedClass = ExposedClass.Find(typeof(TestDirtyClassWithArray));
            var exposedObj = new ExposedObject("test-delta-array-2", exposedClass, testObj);
            ExposedPropertyUtility.SetDefault(exposedObj);

            // items[0].name のみ変更（items[1], items[2] は未変更）
            var prop = exposedObj.FindProperty("items[0].name");
            Assert.IsNotNull(prop);
            prop.Value.SetValue("Modified");

            // Act
            var json = ExposedSceneSerializer.SceneToJson(new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver, SerializeMode.Delta);

            // Assert
            var jRoot = JObject.Parse(json);
            var obj = (jRoot["objects"] as JArray)[0] as JObject;
            var itemsArray = obj["items"] as JArray;
            Assert.IsNotNull(itemsArray);

            // 仕様: forPersistence=true でも、最後に変更された既存要素より後の未変更要素は省略される
            // （JsonDiff_ObjectArray_ModifyAndAppend_KeepsLeadingStubsUpToLastModified 参照）。
            // ここでは index=0 のみ変更のため、index=1,2 のスタブは省略される。
            // ただし配列がデルタ形式と認識されるよう末尾に空マーカーが1つ補われる。
            Assert.AreEqual(2, itemsArray.Count, "Only modified element + delta-format marker should remain");
            Assert.IsTrue(ExposedPropertySerializer.IsArrayDeltaFormat(itemsArray), "Result should be detectable as delta format");

            var first = itemsArray[0] as JObject;
            Assert.IsNotNull(first["name"]);
            Assert.AreEqual("Modified", first["name"].Value<string>());

            // 末尾に追加された空マーカー
            var marker = itemsArray[1] as JObject;
            Assert.IsNotNull(marker);
            Assert.IsFalse(ExposedPropertySerializer.HasNonMetaProperties(marker), "Trailing marker should be empty");
        }

        [Test]
        public void DeltaFormat_AddedElements_HaveNewFlag()
        {
            // Arrange: デフォルト [{ id:1, name:"First" }]
            ExposedClass.RegisterFromAttributes<TestDirtyClassWithArray>();
            ExposedClass.RegisterFromAttributes<TestDirtyNestedStruct>();
            var testObj = new TestDirtyClassWithArray
            {
                items = new TestDirtyNestedStruct[]
                {
                    new TestDirtyNestedStruct { id = 1, name = "First" }
                }
            };
            var exposedClass = ExposedClass.Find(typeof(TestDirtyClassWithArray));
            var exposedObj = new ExposedObject("test-delta-array-3", exposedClass, testObj);
            ExposedPropertyUtility.SetDefault(exposedObj);

            // 要素追加
            testObj.items = new TestDirtyNestedStruct[]
            {
                new TestDirtyNestedStruct { id = 1, name = "First" },
                new TestDirtyNestedStruct { id = 10, name = "New" }
            };
            // items配列自体がdirtyになるようにSetValueで更新
            var itemsProp = exposedObj.FindProperty("items");
            Assert.IsNotNull(itemsProp);
            itemsProp.Value.SetValue(testObj.items);

            // Act
            var json = ExposedSceneSerializer.SceneToJson(new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver, SerializeMode.Delta);

            // Assert
            var jRoot = JObject.Parse(json);
            var obj = (jRoot["objects"] as JArray)[0] as JObject;
            var itemsArray = obj["items"] as JArray;
            Assert.IsNotNull(itemsArray);

            // 追加要素に @op: "new" がある
            bool hasNewElement = false;
            foreach (var element in itemsArray)
            {
                if (element is JObject o && o["@op"]?.ToString() == "new")
                {
                    hasNewElement = true;
                    Assert.AreEqual(10, o["id"].Value<int>());
                    Assert.AreEqual("New", o["name"].Value<string>());
                }
            }
            Assert.IsTrue(hasNewElement, "Added element should have @op: new");
        }

        [Test]
        public void DeltaFormat_Deserialize_MergesPartialChanges()
        {
            // Arrange: デフォルト状態のオブジェクト
            ExposedClass.RegisterFromAttributes<TestDirtyClassWithArray>();
            var testObj = new TestDirtyClassWithArray
            {
                items = new TestDirtyNestedStruct[]
                {
                    new TestDirtyNestedStruct { id = 1, name = "First" },
                    new TestDirtyNestedStruct { id = 2, name = "Second" }
                }
            };
            var exposedClass = ExposedClass.Find(typeof(TestDirtyClassWithArray));
            var exposedObj = new ExposedObject("test-delta-array-4", exposedClass, testObj);
            ExposedPropertyUtility.SetDefault(exposedObj);

            // デルタ形式のJSON: items[1].nameのみ変更
            var deltaJson = @"{
                ""objects"": [
                    {
                        ""@type"": ""TestDirtyClassWithArray"",
                        ""@id"": ""test-delta-array-4"",
                        ""items"": [
                            { },
                            { ""name"": ""Changed"" }
                        ]
                    }
                ]
            }";

            // Act
            ExposedSceneSerializer.SceneFromJson(deltaJson, _resolver);

            // Assert: items[0] はデフォルト値のまま
            Assert.AreEqual(1, testObj.items[0].id);
            Assert.AreEqual("First", testObj.items[0].name);

            // items[1] は name のみ変更、id はデフォルト値のまま
            Assert.AreEqual(2, testObj.items[1].id);
            Assert.AreEqual("Changed", testObj.items[1].name);
        }

        [Test]
        public void DeltaFormat_Deserialize_AddsNewElements()
        {
            // Arrange
            ExposedClass.RegisterFromAttributes<TestDirtyClassWithArray>();
            ExposedClass.RegisterFromAttributes<TestDirtyNestedStruct>();
            var testObj = new TestDirtyClassWithArray
            {
                items = new TestDirtyNestedStruct[]
                {
                    new TestDirtyNestedStruct { id = 1, name = "First" }
                }
            };
            var exposedClass = ExposedClass.Find(typeof(TestDirtyClassWithArray));
            var exposedObj = new ExposedObject("test-delta-array-5", exposedClass, testObj);
            ExposedPropertyUtility.SetDefault(exposedObj);

            // デルタ形式JSON: 追加要素
            var deltaJson = @"{
                ""objects"": [
                    {
                        ""@type"": ""TestDirtyClassWithArray"",
                        ""@id"": ""test-delta-array-5"",
                        ""items"": [
                            { ""@op"": ""new"", ""id"": 10, ""name"": ""New"" }
                        ]
                    }
                ]
            }";

            // Act
            ExposedSceneSerializer.SceneFromJson(deltaJson, _resolver);

            // Assert: 元の要素 + 追加要素
            Assert.AreEqual(2, testObj.items.Length, "Array should have 2 elements after adding");
            Assert.AreEqual(1, testObj.items[0].id, "Original element should be preserved");
            Assert.AreEqual("First", testObj.items[0].name);
            Assert.AreEqual(10, testObj.items[1].id, "New element id should be 10");
            Assert.AreEqual("New", testObj.items[1].name, "New element name should be 'New'");
        }

        [Test]
        public void DeltaFormat_PrimitiveArray_NotDeltaFormat()
        {
            // Arrange: プリミティブ配列はデルタ対象外
            ExposedClass.RegisterFromAttributes<TestDirtyClassWithList>();
            var testObj = new TestDirtyClassWithList
            {
                intList = new List<int> { 1, 2, 3 },
                stringList = new List<string> { "a", "b" },
                structList = new List<TestDirtyNestedStruct>()
            };
            var exposedClass = ExposedClass.Find(typeof(TestDirtyClassWithList));
            var exposedObj = new ExposedObject("test-delta-array-6", exposedClass, testObj);
            ExposedPropertyUtility.SetDefault(exposedObj);

            // intList[1] を変更
            testObj.intList[1] = 99;
            var intListProp = exposedObj.FindProperty("intList");
            Assert.IsNotNull(intListProp);
            intListProp.Value.SetValue(testObj.intList);

            // Act
            var json = ExposedSceneSerializer.SceneToJson(new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver, SerializeMode.Delta);

            // Assert: プリミティブ配列はデルタ形式ではなく全要素出力
            var jRoot = JObject.Parse(json);
            var obj = (jRoot["objects"] as JArray)[0] as JObject;
            var intListArr = obj["intList"] as JArray;
            Assert.IsNotNull(intListArr);

            // 全要素がプリミティブ値として出力される（@op なし、空オブジェクトなし）
            foreach (var element in intListArr)
            {
                Assert.AreNotEqual(JTokenType.Object, element.Type, "Primitive array elements should not be JObject");
            }
        }

        [Test]
        public void DeltaFormat_RoundTrip_PreservesValues()
        {
            // Arrange
            ExposedClass.RegisterFromAttributes<TestDirtyClassWithArray>();
            ExposedClass.RegisterFromAttributes<TestDirtyNestedStruct>();
            var testObj = new TestDirtyClassWithArray
            {
                items = new TestDirtyNestedStruct[]
                {
                    new TestDirtyNestedStruct { id = 1, name = "First" },
                    new TestDirtyNestedStruct { id = 2, name = "Second" },
                    new TestDirtyNestedStruct { id = 3, name = "Third" }
                }
            };
            var exposedClass = ExposedClass.Find(typeof(TestDirtyClassWithArray));
            var exposedObj = new ExposedObject("test-delta-array-7", exposedClass, testObj);
            ExposedPropertyUtility.SetDefault(exposedObj);

            // items[1].name のみ変更
            var prop = exposedObj.FindProperty("items[1].name");
            Assert.IsNotNull(prop);
            prop.Value.SetValue("Modified");

            // Act: シリアライズ → デシリアライズ
            var json = ExposedSceneSerializer.SceneToJson(new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver, SerializeMode.Delta);

            // 新しいオブジェクトで復元
            var testObj2 = new TestDirtyClassWithArray
            {
                items = new TestDirtyNestedStruct[]
                {
                    new TestDirtyNestedStruct { id = 1, name = "First" },
                    new TestDirtyNestedStruct { id = 2, name = "Second" },
                    new TestDirtyNestedStruct { id = 3, name = "Third" }
                }
            };
            var exposedObj2 = new ExposedObject("test-delta-array-7", exposedClass, testObj2);
            ExposedPropertyUtility.SetDefault(exposedObj2);

            ExposedSceneSerializer.SceneFromJson(json, _resolver);

            // Assert: items[0] と items[2] はデフォルトのまま、items[1].name は変更後の値
            Assert.AreEqual(1, testObj2.items[0].id);
            Assert.AreEqual("First", testObj2.items[0].name);
            Assert.AreEqual(2, testObj2.items[1].id);
            Assert.AreEqual("Modified", testObj2.items[1].name);
            Assert.AreEqual(3, testObj2.items[2].id);
            Assert.AreEqual("Third", testObj2.items[2].name);
        }

        #endregion

        #region Non-ExposedClass Serializable Struct with Array Field Tests

        // ExposedClass属性を持たないSerializable struct（内部にfloat[]を含む）
        // FusionWorld.BaseBlendShapeと同様のパターン
        [Serializable]
        public struct TestPlainStructWithArray
        {
            [SerializeField]
            public float[] values;

            public static TestPlainStructWithArray Default()
            {
                var s = new TestPlainStructWithArray();
                s.values = new float[3];
                return s;
            }
        }

        [Serializable]
        [ExposedClass("TestClassWithPlainStructField")]
        public class TestClassWithPlainStructField
        {
            [ExposedField]
            public int id;

            [ExposedField]
            [SerializeField]
            public TestPlainStructWithArray structField;
        }

        [Test]
        public void PlainStructWithArray_InPlaceModify_DetectedAsDirty()
        {
            // Arrange - JSON比較ベースのdirty検出では、struct内の配列をin-place変更しても
            // 毎回シリアライズして比較するため正しく検出される
            ExposedClass.RegisterFromAttributes<TestClassWithPlainStructField>();
            var testObj = new TestClassWithPlainStructField
            {
                id = 1,
                structField = TestPlainStructWithArray.Default()
            };
            var exposedClass = ExposedClass.Find(typeof(TestClassWithPlainStructField));
            var exposedObj = new ExposedObject("test-plain-struct-1", exposedClass, testObj);

            ExposedPropertyUtility.SetDefault(exposedObj);

            // Act - 配列をin-place変更
            testObj.structField.values[0] = 0.5f;
            testObj.structField.values[1] = 0.3f;

            // Assert - JSON比較により正しく検出される
            Assert.IsTrue(exposedObj.IsPropertyDirty("structField"),
                "In-place modification of array inside struct is detected by JSON-based comparison");
        }

        [Test]
        public void PlainStructWithArray_StructReassignment_DetectedAsDirty()
        {
            // Arrange - structを新しいインスタンスに再代入すれば、配列参照が変わりdirty検出される
            ExposedClass.RegisterFromAttributes<TestClassWithPlainStructField>();
            var testObj = new TestClassWithPlainStructField
            {
                id = 1,
                structField = TestPlainStructWithArray.Default()
            };
            var exposedClass = ExposedClass.Find(typeof(TestClassWithPlainStructField));
            var exposedObj = new ExposedObject("test-plain-struct-2", exposedClass, testObj);

            ExposedPropertyUtility.SetDefault(exposedObj);

            // Act - 新しいstructを作成して代入（配列参照が変わる）
            var newStruct = new TestPlainStructWithArray();
            newStruct.values = new float[] { 0.5f, 0.3f, 0.1f };
            testObj.structField = newStruct;

            // Assert - dirty
            Assert.IsTrue(exposedObj.IsPropertyDirty("structField"),
                "Struct reassignment with new array should be detected as dirty");
        }

        [Test]
        public void PlainStructWithArray_UnchangedValues_NotDirty()
        {
            // Arrange - 値を変更しなければdirtyにならない
            ExposedClass.RegisterFromAttributes<TestClassWithPlainStructField>();
            var testObj = new TestClassWithPlainStructField
            {
                id = 1,
                structField = TestPlainStructWithArray.Default()
            };
            var exposedClass = ExposedClass.Find(typeof(TestClassWithPlainStructField));
            var exposedObj = new ExposedObject("test-plain-struct-3", exposedClass, testObj);

            ExposedPropertyUtility.SetDefault(exposedObj);

            // Assert - dirtyではない
            Assert.IsFalse(exposedObj.IsPropertyDirty("structField"),
                "Unchanged struct with array should not be dirty");
        }

        [Test]
        public void PlainStructWithArray_SameContentNewArray_NotDirty()
        {
            // Arrange - 新しい配列だが中身が同じ場合、_ValuesEqualのフィールド比較でnot dirty
            ExposedClass.RegisterFromAttributes<TestClassWithPlainStructField>();
            var testObj = new TestClassWithPlainStructField
            {
                id = 1,
                structField = TestPlainStructWithArray.Default()
            };
            var exposedClass = ExposedClass.Find(typeof(TestClassWithPlainStructField));
            var exposedObj = new ExposedObject("test-plain-struct-4", exposedClass, testObj);

            ExposedPropertyUtility.SetDefault(exposedObj);

            // Act - 新しい配列だがデフォルトと同じ内容（全0）
            testObj.structField.values = new float[3];

            // Assert - 中身が同じなのでdirtyではない
            Assert.IsFalse(exposedObj.IsPropertyDirty("structField"),
                "Struct with new array but same content should not be dirty");
        }

        [Test]
        public void PlainStructWithArray_SceneToJson_RoundTrip()
        {
            // Arrange - struct再代入でのシリアライズ→デシリアライズの往復テスト
            ExposedClass.RegisterFromAttributes<TestClassWithPlainStructField>();
            var testObj = new TestClassWithPlainStructField
            {
                id = 1,
                structField = TestPlainStructWithArray.Default()
            };
            var exposedClass = ExposedClass.Find(typeof(TestClassWithPlainStructField));
            var exposedObj = new ExposedObject("test-plain-struct-5", exposedClass, testObj);

            ExposedPropertyUtility.SetDefault(exposedObj);

            // struct再代入で変更
            var newStruct = new TestPlainStructWithArray();
            newStruct.values = new float[] { 0.5f, 0.3f, 0.1f };
            testObj.structField = newStruct;

            // Act - シリアライズ
            var json = ExposedSceneSerializer.SceneToJson(new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver, SerializeMode.Delta);

            // 新しいオブジェクトにデシリアライズ
            exposedObj.Unregister();
            var testObj2 = new TestClassWithPlainStructField
            {
                id = 1,
                structField = TestPlainStructWithArray.Default()
            };
            var exposedObj2 = new ExposedObject("test-plain-struct-5", exposedClass, testObj2);
            ExposedPropertyUtility.SetDefault(exposedObj2);

            ExposedSceneSerializer.SceneFromJson(json, _resolver);

            // Assert - 値が復元される
            Assert.AreEqual(0.5f, testObj2.structField.values[0], 0.001f);
            Assert.AreEqual(0.3f, testObj2.structField.values[1], 0.001f);
            Assert.AreEqual(0.1f, testObj2.structField.values[2], 0.001f);
        }

        #endregion

        #region Non-ExposedClass Persistable Property Tests

        // ExposedClassでないSerializableクラス（AvatarInputSettingsと同パターン）
        [System.Serializable]
        public class PlainSerializableSettings
        {
            public string deviceName;
            public int[] values;

            public PlainSerializableSettings()
            {
                deviceName = "";
                values = new int[0];
            }
        }

        // readOnlyな非ExposedClassプロパティ（デルタ出力に含まれないべき）
        [System.Serializable]
        [ExposedClass("TestClassWithReadOnlyPlainProperty")]
        public class TestClassWithReadOnlyPlainProperty
        {
            [ExposedField]
            public int id;

            [ExposedProperty]
            public PlainSerializableSettings readOnlySettings => _settings;
            private PlainSerializableSettings _settings;

            public TestClassWithReadOnlyPlainProperty() { _settings = new PlainSerializableSettings(); }
            public TestClassWithReadOnlyPlainProperty(PlainSerializableSettings s) { _settings = s; }
        }

        [System.Serializable]
        [ExposedClass("TestClassWithPlainSerializableProperty")]
        public class TestClassWithPlainSerializableProperty
        {
            [ExposedField]
            public int id;

            // ExposedClassでないSerializableクラスをExposedFieldとして持つ
            [ExposedField]
            public PlainSerializableSettings settings = new PlainSerializableSettings();
        }

        [Test]
        public void NonExposedClassPersistableProperty_IncludedInDeltaOutput()
        {
            // Arrange - ExposedClassでないSerializableクラスをPersistableプロパティとして持つ
            ExposedClass.RegisterFromAttributes<TestClassWithPlainSerializableProperty>();
            var testObj = new TestClassWithPlainSerializableProperty
            {
                id = 1,
                settings = new PlainSerializableSettings { deviceName = "TestDevice", values = new[] { 10, 20 } }
            };
            var exposedClass = ExposedClass.Find(typeof(TestClassWithPlainSerializableProperty));
            var exposedObj = new ExposedObject("test-plain-serializable-1", exposedClass, testObj);

            ExposedPropertyUtility.SetDefault(exposedObj);

            // Act - settingsを変更
            testObj.settings = new PlainSerializableSettings { deviceName = "Changed", values = new[] { 30 } };

            // Delta出力
            var json = ExposedPropertySerializer.ToJson(exposedObj, _resolver, isDirtyOnly: true, forPersistence: true);
            var jObj = JObject.Parse(json);

            // Assert - settingsが出力に含まれること
            Assert.IsNotNull(jObj["settings"], "Non-ExposedClass persistable property 'settings' should be included in delta output");
            var settingsObj = jObj["settings"];
            Assert.AreEqual("Changed", settingsObj["deviceName"]?.Value<string>());
        }

        [Test]
        public void NonExposedClassPersistableProperty_UnchangedExcludedFromDelta()
        {
            // Arrange - 変更なしなら dirty 追跡外の POCO プロパティもデルタ出力に含まれないこと
            // （未操作での再生終了で objects[] が空になるために必要）
            ExposedClass.RegisterFromAttributes<TestClassWithPlainSerializableProperty>();
            var testObj = new TestClassWithPlainSerializableProperty
            {
                id = 1,
                settings = new PlainSerializableSettings { deviceName = "Unchanged", values = new[] { 1, 2 } }
            };
            var exposedClass = ExposedClass.Find(typeof(TestClassWithPlainSerializableProperty));
            var exposedObj = new ExposedObject("test-plain-serializable-unchanged", exposedClass, testObj);

            ExposedPropertyUtility.SetDefault(exposedObj);

            // Act - settingsを変更しない
            var json = ExposedPropertySerializer.ToJson(exposedObj, _resolver, isDirtyOnly: true, forPersistence: true);
            var jObj = JObject.Parse(json);

            // Assert - 変更が無いので settings は出力に含まれず、メタデータのみになること
            Assert.IsNull(jObj["settings"], "Unchanged non-ExposedClass persistable property should NOT be included in delta");
            Assert.IsFalse(ExposedPropertySerializer.HasNonMetaProperties(jObj),
                "delta には非メタプロパティが無く、SceneToJson からは除外されるべき");
        }

        [Test]
        public void NonExposedClassReadOnlyProperty_ExcludedFromDelta()
        {
            // Arrange - readOnlyプロパティはデシリアライズ不要なのでデルタ出力に含まれない
            ExposedClass.RegisterFromAttributes<TestClassWithReadOnlyPlainProperty>();
            var testObj = new TestClassWithReadOnlyPlainProperty(
                new PlainSerializableSettings { deviceName = "ReadOnly", values = new[] { 5 } });
            var exposedClass = ExposedClass.Find(typeof(TestClassWithReadOnlyPlainProperty));
            var exposedObj = new ExposedObject("test-readonly-plain", exposedClass, testObj);

            ExposedPropertyUtility.SetDefault(exposedObj);

            // Act - forPersistence: trueで実際の保存シナリオを再現
            var json = ExposedPropertySerializer.ToJson(exposedObj, _resolver, isDirtyOnly: true, forPersistence: true);
            var jObj = JObject.Parse(json);

            // Assert - readOnlyプロパティはデルタ出力に含まれないこと
            Assert.IsNull(jObj["readOnlySettings"], "ReadOnly non-ExposedClass property should be excluded from delta output");
        }

        [Test]
        public void NonExposedClassPersistableProperty_IncludedInSceneDelta()
        {
            // Arrange
            ExposedClass.RegisterFromAttributes<TestClassWithPlainSerializableProperty>();
            var testObj = new TestClassWithPlainSerializableProperty
            {
                id = 1,
                settings = new PlainSerializableSettings { deviceName = "Original", values = new[] { 1, 2, 3 } }
            };
            var exposedClass = ExposedClass.Find(typeof(TestClassWithPlainSerializableProperty));
            var exposedObj = new ExposedObject("test-plain-serializable-2", exposedClass, testObj);

            ExposedPropertyUtility.SetDefault(exposedObj);

            // Act - settingsを変更してSceneToJsonのDeltaモードで出力
            testObj.settings = new PlainSerializableSettings { deviceName = "Modified", values = new[] { 4, 5 } };

            var sceneJson = ExposedSceneSerializer.SceneToJson(
                new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver, SerializeMode.Delta);
            var jRoot = JObject.Parse(sceneJson);
            var jArray = jRoot["objects"] as JArray;

            // Assert - オブジェクトが出力に含まれ、settingsも含まれること
            Assert.IsNotNull(jArray, "objects array should exist");
            Assert.IsTrue(jArray.Count > 0, "objects array should not be empty");

            var objEntry = jArray[0] as JObject;
            Assert.IsNotNull(objEntry["settings"], "Non-ExposedClass persistable property 'settings' should be included in scene delta output");
        }

        [Test]
        public void NonExposedClassPersistableProperty_RoundTrip()
        {
            // Arrange
            ExposedClass.RegisterFromAttributes<TestClassWithPlainSerializableProperty>();
            var testObj = new TestClassWithPlainSerializableProperty
            {
                id = 1,
                settings = new PlainSerializableSettings { deviceName = "RoundTrip", values = new[] { 100, 200 } }
            };
            var exposedClass = ExposedClass.Find(typeof(TestClassWithPlainSerializableProperty));
            var exposedObj = new ExposedObject("test-plain-serializable-3", exposedClass, testObj);

            ExposedPropertyUtility.SetDefault(exposedObj);

            // settingsを変更
            testObj.settings = new PlainSerializableSettings { deviceName = "Updated", values = new[] { 300 } };

            // Act - シリアライズ→デシリアライズ
            var sceneJson = ExposedSceneSerializer.SceneToJson(
                new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver, SerializeMode.Delta);

            // 新しいオブジェクトでデシリアライズ
            exposedObj.Unregister();
            var testObj2 = new TestClassWithPlainSerializableProperty
            {
                id = 1,
                settings = new PlainSerializableSettings { deviceName = "Default", values = new int[0] }
            };
            var exposedObj2 = new ExposedObject("test-plain-serializable-3", exposedClass, testObj2);
            ExposedPropertyUtility.SetDefault(exposedObj2);

            ExposedSceneSerializer.SceneFromJson(sceneJson, _resolver);

            // Assert - 値が復元される
            Assert.AreEqual("Updated", testObj2.settings.deviceName);
            Assert.AreEqual(1, testObj2.settings.values.Length);
            Assert.AreEqual(300, testObj2.settings.values[0]);
        }

        #endregion

        #region Array Revert on Play-Stop-Play cycle

        /// <summary>
        /// Play→Stop→Playサイクルで配列が肥大化する問題のテスト。
        ///
        /// 問題のシナリオ:
        /// 1. Play開始 → CaptureDefaults (配列=空)
        /// 2. Load delta → @op:new で要素追加 (配列=1要素)
        /// 3. Save (正しいデルタ出力)
        /// 4. Stop → RevertAllToDefault → 配列が空に戻るべき
        /// 5. 再Play → CaptureDefaults (配列=空のはず)
        /// 6. Load delta → @op:new で要素追加 (配列=1要素)
        /// 7. Save → デルタは最初と同じであるべき
        ///
        /// バグ: ステップ4でRevertが配列長を復元しない場合、
        /// ステップ5のデフォルトが配列=1要素になり、
        /// ステップ6で配列=2要素になる → デルタが肥大化
        /// </summary>
        [Test]
        public void RevertArray_AfterDeltaLoad_RestoresArrayLength()
        {
            // Arrange — 配列を持つオブジェクトを作成
            var testObj = new TestDirtyClassWithArray
            {
                items = new TestDirtyNestedStruct[0] // 初期状態: 空配列
            };

            var exposedClass = ExposedClass.Find(typeof(TestDirtyClassWithArray));
            var exposedObj = new ExposedObject("test-revert-array", exposedClass, testObj);

            try
            {
                // --- Session 1: Play開始 ---
                ExposedPropertyUtility.SetDefault(exposedObj);
                Assert.AreEqual(0, testObj.items.Length, "初期状態: 配列は空");

                // Save delta (配列が空 → デルタには何もない)
                var deltaJson1 = ExposedSceneSerializer.SceneToJson(
                    new List<ExposedObject>(ExposedObjectRegistry.instances),
                    _resolver, SerializeMode.Delta);

                // Load delta: @op:new で1要素追加
                var loadJson = @"{
                    ""objects"": [{
                        ""@type"": ""TestDirtyClassWithArray"",
                        ""@id"": ""test-revert-array"",
                        ""items"": [
                            { ""@type"": ""TestDirtyNestedStruct"", ""id"": 1, ""name"": ""added"", ""@op"": ""new"" }
                        ]
                    }]
                }";
                ExposedSceneSerializer.SceneFromJson(loadJson, _resolver);
                Assert.AreEqual(1, testObj.items.Length, "Load後: 配列に1要素追加");
                Assert.AreEqual("added", testObj.items[0].name);

                // Save delta → @op:new が含まれるべき
                var savedJson1 = ExposedSceneSerializer.SceneToJson(
                    new List<ExposedObject>(ExposedObjectRegistry.instances),
                    _resolver, SerializeMode.Delta);
                var jRoot1 = JObject.Parse(savedJson1);
                var objects1 = jRoot1["objects"] as JArray;
                Assert.AreEqual(1, objects1.Count, "Session1: dirtyなオブジェクトが含まれるべき");

                // --- Stop: Revert ---
                var dirtyProps = exposedObj.GetDirtyProperties();
                foreach (var path in dirtyProps)
                {
                    exposedObj.Revert(path);
                }

                // Revert後、配列は空に戻るべき
                Assert.AreEqual(0, testObj.items.Length,
                    $"Revert後: 配列は初期状態（空）に戻るべき。実際の長さ: {testObj.items.Length}");
            }
            finally
            {
                exposedObj.Unregister();
            }
        }

        [Test]
        public void PlayStopPlay_DeltaArrayDoesNotGrow()
        {
            // Arrange
            var testObj = new TestDirtyClassWithArray
            {
                items = new TestDirtyNestedStruct[0]
            };

            var exposedClass = ExposedClass.Find(typeof(TestDirtyClassWithArray));
            var exposedObj = new ExposedObject("test-play-stop-play", exposedClass, testObj);

            try
            {
                // --- Session 1 ---
                ExposedPropertyUtility.SetDefault(exposedObj);

                // Load delta with 1 new element
                var loadJson = @"{
                    ""objects"": [{
                        ""@type"": ""TestDirtyClassWithArray"",
                        ""@id"": ""test-play-stop-play"",
                        ""items"": [
                            { ""@type"": ""TestDirtyNestedStruct"", ""id"": 1, ""name"": ""item1"", ""@op"": ""new"" }
                        ]
                    }]
                }";
                ExposedSceneSerializer.SceneFromJson(loadJson, _resolver);

                // Save
                var saved1 = ExposedSceneSerializer.SceneToJson(
                    new List<ExposedObject>(ExposedObjectRegistry.instances),
                    _resolver, SerializeMode.Delta);
                var items1 = (JObject.Parse(saved1)["objects"] as JArray)?[0]?["items"] as JArray;
                Assert.IsNotNull(items1, "Session1: items配列がデルタに含まれるべき");
                int session1ItemCount = items1.Count;

                // --- Stop: Revert ---
                foreach (var path in exposedObj.GetDirtyProperties())
                    exposedObj.Revert(path);

                // --- Session 2 ---
                ExposedPropertyUtility.SetDefault(exposedObj);

                // Load same delta again
                ExposedSceneSerializer.SceneFromJson(loadJson, _resolver);

                // Save
                var saved2 = ExposedSceneSerializer.SceneToJson(
                    new List<ExposedObject>(ExposedObjectRegistry.instances),
                    _resolver, SerializeMode.Delta);
                var items2 = (JObject.Parse(saved2)["objects"] as JArray)?[0]?["items"] as JArray;
                Assert.IsNotNull(items2, $"Session2: items配列がデルタに含まれるべき。JSON: {saved2}");
                int session2ItemCount = items2.Count;

                // Session2のデルタはSession1と同じサイズであるべき（肥大化しない）
                Assert.AreEqual(session1ItemCount, session2ItemCount,
                    $"Session2のデルタ配列がSession1と同じサイズであるべき（肥大化しない）。" +
                    $"Session1: {session1ItemCount}, Session2: {session2ItemCount}。" +
                    $"\nSession1 JSON: {saved1}\nSession2 JSON: {saved2}");
            }
            finally
            {
                exposedObj.Unregister();
            }
        }

        #endregion
    }
}
