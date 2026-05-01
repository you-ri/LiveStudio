using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Lilium.RemoteControl;

namespace Lilium.RemoteControl.Tests
{
    [TestFixture]
    public class ExposedPropertyTests
    {
        #region Test Classes

        // 基本的なテストクラス（プロパティとフィールド両方を含む）
        public class TestClass
        {
            public int intValue { get; set; }
            public string stringValue { get; set; }
            public float floatValue { get; set; }
            public bool boolValue { get; set; }
            public Vector3 vectorValue { get; set; }
            public Color colorValue { get; set; }

            public int intField;
            public string stringField;
        }

        // 配列テストクラス
        public class TestArrayClass
        {
            public int[] intArray { get; set; }
            public List<int> intList { get; set; }
            public List<string> stringList { get; set; }
            public List<Vector3> vectorList { get; set; }
        }

        // IEnumerable テストクラス
        public class TestEnumerableClass
        {
            private string[] _names;

            public TestEnumerableClass(string[] names)
            {
                _names = names;
            }

            public IEnumerable<string> stringEnumerable => _names.Select(n => n);
            public IEnumerable<int> intEnumerable => _names.Select(n => n.Length);
        }

        public class NestedClass2
        {
            public int value { get; set; }
        }

        public class NestedClass
        {
            public int value { get; set; }

            public NestedClass2[] array { get; set; }
        }

        // struct テストクラス
        public struct TestStruct
        {
            public int intValue;
            public float floatValue;
            public bool boolValue;
        }

        public class TestStructClass
        {
            public TestStruct structProperty { get; set; }
            public TestStruct structField;
        }

        #endregion

        private ExposedObject _testObject;
        private TestClass _testInstance;
        private ExposedObject _arrayObject;
        private TestArrayClass _arrayInstance;
        private ExposedObject _structObject;
        private TestStructClass _structInstance;
        private ExposedObject _enumerableObject;
        private TestEnumerableClass _enumerableInstance;

        [SetUp]
        public void Setup()
        {
            ExposedClass.Clear();

            // TestClass の登録
            var testDefines = new ExposedPropertyDefine[]
            {
                new ExposedPropertyDefine { name = "intValue", path = "intValue" },
                new ExposedPropertyDefine { name = "stringValue", path = "stringValue" },
                new ExposedPropertyDefine { name = "floatValue", path = "floatValue" },
                new ExposedPropertyDefine { name = "boolValue", path = "boolValue" },
                new ExposedPropertyDefine { name = "vectorValue", path = "vectorValue" },
                new ExposedPropertyDefine { name = "colorValue", path = "colorValue" },
                new ExposedPropertyDefine { name = "intField", path = "intField" },
                new ExposedPropertyDefine { name = "stringField", path = "stringField" }
            };
            ExposedClass.Register<TestClass>("TestClass", testDefines);

            // TestArrayClass の登録
            var arrayDefines = new ExposedPropertyDefine[]
            {
                new ExposedPropertyDefine { name = "intArray", path = "intArray" },
                new ExposedPropertyDefine { name = "intList", path = "intList" },
                new ExposedPropertyDefine { name = "stringList", path = "stringList" },
                new ExposedPropertyDefine { name = "vectorList", path = "vectorList" }
            };
            ExposedClass.Register<TestArrayClass>("TestArrayClass", arrayDefines);

            // TestEnumerableClass の登録
            var enumerableDefines = new ExposedPropertyDefine[]
            {
                new ExposedPropertyDefine { name = "stringEnumerable", path = "stringEnumerable" },
                new ExposedPropertyDefine { name = "intEnumerable", path = "intEnumerable" }
            };
            ExposedClass.Register<TestEnumerableClass>("TestEnumerableClass", enumerableDefines);

            // NestedClass の登録
            var nested2Defines = new ExposedPropertyDefine[]
            {
                new ExposedPropertyDefine { name = "value", path = "value" }
            };
            ExposedClass.Register<NestedClass2>("NestedClass2", nested2Defines);

            var nestedDefines = new ExposedPropertyDefine[]
            {
                new ExposedPropertyDefine { name = "value", path = "value" },
                new ExposedPropertyDefine { name = "array", path = "array" }
            };
            ExposedClass.Register<NestedClass>("NestedClass", nestedDefines);

            // テストインスタンスの作成
            _testInstance = new TestClass
            {
                intValue = 42,
                stringValue = "test",
                floatValue = 3.14f,
                boolValue = true,
                vectorValue = new Vector3(1, 2, 3),
                colorValue = Color.red,
                intField = 100,
                stringField = "field_test"
            };
            _testObject = ExposedObjectRegistry.Create(_testInstance, "test_object");

            _arrayInstance = new TestArrayClass
            {
                intArray = new int[] { 1, 2, 3 },
                intList = new List<int> { 10, 20, 30 },
                stringList = new List<string> { "x", "y", "z" },
                vectorList = new List<Vector3> { Vector3.zero, Vector3.one, Vector3.up }
            };
            _arrayObject = ExposedObjectRegistry.Create(_arrayInstance, "array_object");

            _enumerableInstance = new TestEnumerableClass(new[] { "alpha", "beta", "gamma" });
            _enumerableObject = ExposedObjectRegistry.Create(_enumerableInstance, "enumerable_object");

            // TestStruct の登録
            var structDefines = new ExposedPropertyDefine[]
            {
                new ExposedPropertyDefine { name = "intValue", path = "intValue" },
                new ExposedPropertyDefine { name = "floatValue", path = "floatValue" },
                new ExposedPropertyDefine { name = "boolValue", path = "boolValue" }
            };
            ExposedClass.Register<TestStruct>("TestStruct", structDefines);

            // TestStructClass の登録
            var structClassDefines = new ExposedPropertyDefine[]
            {
                new ExposedPropertyDefine { name = "structProperty", path = "structProperty" },
                new ExposedPropertyDefine { name = "structField", path = "structField" }
            };
            ExposedClass.Register<TestStructClass>("TestStructClass", structClassDefines);

            _structInstance = new TestStructClass
            {
                structProperty = new TestStruct { intValue = 10, floatValue = 1.5f, boolValue = true },
                structField = new TestStruct { intValue = 20, floatValue = 2.5f, boolValue = false }
            };
            _structObject = ExposedObjectRegistry.Create(_structInstance, "struct_object");
        }

        #region Basic Tests

        [Test]
        public void GetValue_ReturnsCorrectValues()
        {
            // プロパティ
            var intProperty = _testObject.GetProperty("intValue");
            Assert.IsNotNull(intProperty);
            Assert.AreEqual(42, intProperty.Value.GetValue());

            var stringProperty = _testObject.GetProperty("stringValue");
            Assert.IsNotNull(stringProperty);
            Assert.AreEqual("test", stringProperty.Value.GetValue());

            // フィールド
            var intField = _testObject.GetProperty("intField");
            Assert.IsNotNull(intField);
            Assert.AreEqual(100, intField.Value.GetValue());

            var stringField = _testObject.GetProperty("stringField");
            Assert.IsNotNull(stringField);
            Assert.AreEqual("field_test", stringField.Value.GetValue());
        }

        [Test]
        public void SetValue_UpdatesValues()
        {
            // プロパティ
            var intProperty = _testObject.GetProperty("intValue");
            intProperty.Value.SetValue(999);
            Assert.AreEqual(999, _testInstance.intValue);

            var vectorProperty = _testObject.GetProperty("vectorValue");
            vectorProperty.Value.SetValue(Vector3.forward);
            Assert.AreEqual(Vector3.forward, _testInstance.vectorValue);

            // フィールド
            var intField = _testObject.GetProperty("intField");
            intField.Value.SetValue(500);
            Assert.AreEqual(500, _testInstance.intField);

            var stringField = _testObject.GetProperty("stringField");
            stringField.Value.SetValue("updated");
            Assert.AreEqual("updated", _testInstance.stringField);
        }

        [Test]
        public void IsValid_ReturnsCorrectValue()
        {
            var validProperty = _testObject.GetProperty("intValue");
            Assert.IsTrue(validProperty.Value.isValid);

            var invalidProperty = new ExposedProperty(null, null, null);
            Assert.IsFalse(invalidProperty.isValid);
        }

        #endregion

        #region Array Tests

        [Test]
        public void IsArray_ReturnsCorrectValue()
        {
            var intProperty = _testObject.GetProperty("intValue");
            Assert.IsFalse(intProperty.Value.isArray);

            var intArrayProperty = _arrayObject.GetProperty("intArray");
            Assert.IsTrue(intArrayProperty.Value.isArray);

            var intListProperty = _arrayObject.GetProperty("intList");
            Assert.IsTrue(intListProperty.Value.isArray);
        }

        [Test]
        public void ArrayLength_ReturnsCorrectLength()
        {
            var intArrayProperty = _arrayObject.GetProperty("intArray");
            Assert.AreEqual(3, intArrayProperty.Value.arrayLength);

            var intListProperty = _arrayObject.GetProperty("intList");
            Assert.AreEqual(3, intListProperty.Value.arrayLength);
        }

        [Test]
        public void GetPropertyIndex_ReturnsArrayElement()
        {
            var intArrayProperty = _arrayObject.GetProperty("intArray");

            var element0 = intArrayProperty.Value.GetPropertyIndex(0);
            Assert.IsNotNull(element0);
            Assert.AreEqual(1, element0.Value.GetValue());

            var element1 = intArrayProperty.Value.GetPropertyIndex(1);
            Assert.IsNotNull(element1);
            Assert.AreEqual(2, element1.Value.GetValue());
        }

        [Test]
        public void GetPropertyIndex_InvalidIndex_ReturnsNull()
        {
            var intArrayProperty = _arrayObject.GetProperty("intArray");

            var negativeIndex = intArrayProperty.Value.GetPropertyIndex(-1);
            Assert.IsNull(negativeIndex);

            var outOfRangeIndex = intArrayProperty.Value.GetPropertyIndex(10);
            Assert.IsNull(outOfRangeIndex);
        }

        [Test]
        public void Add_ToList_WorksCorrectly()
        {
            var intListProperty = _arrayObject.GetProperty("intList");
            Assert.AreEqual(3, intListProperty.Value.arrayLength);

            var result = intListProperty.Value.Add(40);
            Assert.IsTrue(result);
            Assert.AreEqual(4, intListProperty.Value.arrayLength);
            Assert.AreEqual(40, _arrayInstance.intList[3]);
        }

        [Test]
        public void Add_ToArray_CreatesNewArray()
        {
            var intArrayProperty = _arrayObject.GetProperty("intArray");
            Assert.AreEqual(3, intArrayProperty.Value.arrayLength);

            var result = intArrayProperty.Value.Add(4);
            Assert.IsTrue(result);

            intArrayProperty = _arrayObject.GetProperty("intArray");
            Assert.AreEqual(4, intArrayProperty.Value.arrayLength);
            Assert.AreEqual(4, _arrayInstance.intArray[3]);
        }

        [Test]
        public void Add_WrongType_ReturnsFalse()
        {
            var intListProperty = _arrayObject.GetProperty("intList");
            var initialLength = intListProperty.Value.arrayLength;

            var result = intListProperty.Value.Add("wrong type");

            Assert.IsFalse(result);
            Assert.AreEqual(initialLength, intListProperty.Value.arrayLength);
        }

        [Test]
        public void RemoveAt_FromList_WorksCorrectly()
        {
            var intListProperty = _arrayObject.GetProperty("intList");
            Assert.AreEqual(3, intListProperty.Value.arrayLength);

            var result = intListProperty.Value.RemoveAt(1);
            Assert.IsTrue(result);
            Assert.AreEqual(2, intListProperty.Value.arrayLength);
            Assert.AreEqual(10, _arrayInstance.intList[0]);
            Assert.AreEqual(30, _arrayInstance.intList[1]);
        }

        [Test]
        public void RemoveAt_FromArray_CreatesNewArray()
        {
            var intArrayProperty = _arrayObject.GetProperty("intArray");
            Assert.AreEqual(3, intArrayProperty.Value.arrayLength);

            var result = intArrayProperty.Value.RemoveAt(1);
            Assert.IsTrue(result);

            intArrayProperty = _arrayObject.GetProperty("intArray");
            Assert.AreEqual(2, intArrayProperty.Value.arrayLength);
            Assert.AreEqual(1, _arrayInstance.intArray[0]);
            Assert.AreEqual(3, _arrayInstance.intArray[1]);
        }

        [Test]
        public void RemoveAt_InvalidIndex_ReturnsFalse()
        {
            var intListProperty = _arrayObject.GetProperty("intList");
            var initialLength = intListProperty.Value.arrayLength;

            var result = intListProperty.Value.RemoveAt(-1);
            Assert.IsFalse(result);
            Assert.AreEqual(initialLength, intListProperty.Value.arrayLength);

            result = intListProperty.Value.RemoveAt(10);
            Assert.IsFalse(result);
            Assert.AreEqual(initialLength, intListProperty.Value.arrayLength);
        }

        [Test]
        public void ComplexArrayOperations_WorksCorrectly()
        {
            var vectorListProperty = _arrayObject.GetProperty("vectorList");

            // 初期状態
            Assert.AreEqual(3, vectorListProperty.Value.arrayLength);

            // 追加
            var result = vectorListProperty.Value.Add(Vector3.down);
            Assert.IsTrue(result);
            Assert.AreEqual(4, vectorListProperty.Value.arrayLength);

            // 取得
            var element = vectorListProperty.Value.GetPropertyIndex(3);
            Assert.IsNotNull(element);
            Assert.AreEqual(Vector3.down, element.Value.GetValue());

            // 削除
            result = vectorListProperty.Value.RemoveAt(1);
            Assert.IsTrue(result);
            Assert.AreEqual(3, vectorListProperty.Value.arrayLength);

            // 削除後の確認
            Assert.AreEqual(Vector3.zero, _arrayInstance.vectorList[0]);
            Assert.AreEqual(Vector3.up, _arrayInstance.vectorList[1]);
            Assert.AreEqual(Vector3.down, _arrayInstance.vectorList[2]);
        }

        #endregion

        #region IEnumerable Tests

        [Test]
        public void IsArray_IEnumerable_ReturnsTrue()
        {
            var stringEnumerable = _enumerableObject.GetProperty("stringEnumerable");
            Assert.IsNotNull(stringEnumerable);
            Assert.IsTrue(stringEnumerable.Value.isArray);

            var intEnumerable = _enumerableObject.GetProperty("intEnumerable");
            Assert.IsNotNull(intEnumerable);
            Assert.IsTrue(intEnumerable.Value.isArray);
        }

        [Test]
        public void ArrayLength_IEnumerable_ReturnsCorrectLength()
        {
            var stringEnumerable = _enumerableObject.GetProperty("stringEnumerable");
            Assert.AreEqual(3, stringEnumerable.Value.arrayLength);
        }

        [Test]
        public void GetPropertyIndex_IEnumerable_ReturnsElementValues()
        {
            var stringEnumerable = _enumerableObject.GetProperty("stringEnumerable");

            var element0 = stringEnumerable.Value.GetPropertyIndex(0);
            Assert.IsNotNull(element0);
            Assert.AreEqual("alpha", element0.Value.GetValue());

            var element1 = stringEnumerable.Value.GetPropertyIndex(1);
            Assert.IsNotNull(element1);
            Assert.AreEqual("beta", element1.Value.GetValue());

            var element2 = stringEnumerable.Value.GetPropertyIndex(2);
            Assert.IsNotNull(element2);
            Assert.AreEqual("gamma", element2.Value.GetValue());
        }

        [Test]
        public void GetPropertyIndex_IEnumerable_OutOfRange_ReturnsNull()
        {
            var stringEnumerable = _enumerableObject.GetProperty("stringEnumerable");
            var outOfRange = stringEnumerable.Value.GetPropertyIndex(3);
            Assert.IsNull(outOfRange);
        }

        [Test]
        public void GetCollectionElementType_IEnumerable_ReturnsElementType()
        {
            var elementType = ExposedPropertyUtility.GetCollectionElementType(typeof(IEnumerable<string>));
            Assert.AreEqual(typeof(string), elementType);

            var intElementType = ExposedPropertyUtility.GetCollectionElementType(typeof(IEnumerable<int>));
            Assert.AreEqual(typeof(int), intElementType);
        }

        [Test]
        public void IsArrayType_VariousTypes()
        {
            Assert.IsTrue(ExposedPropertyUtility.IsArrayType(typeof(int[])));
            Assert.IsTrue(ExposedPropertyUtility.IsArrayType(typeof(List<string>)));
            Assert.IsTrue(ExposedPropertyUtility.IsArrayType(typeof(IEnumerable<string>)));
            Assert.IsFalse(ExposedPropertyUtility.IsArrayType(typeof(string)));
            Assert.IsFalse(ExposedPropertyUtility.IsArrayType(typeof(int)));
            Assert.IsFalse(ExposedPropertyUtility.IsArrayType(null));
        }

        #endregion

        #region Path Tests

        [Test]
        public void FindProperty_WithArrayPathBracketNotation_ReturnsCorrectElement()
        {
            // DotBracket形式（内部形式）でテスト
            var element0 = _arrayObject.FindProperty("intArray[0]");
            Assert.IsNotNull(element0);
            Assert.AreEqual(1, element0.Value.GetValue());

            var stringElement = _arrayObject.FindProperty("stringList[1]");
            Assert.IsNotNull(stringElement);
            Assert.AreEqual("y", stringElement.Value.GetValue());
        }

        [Test]
        public void FindProperty_WithArrayPathDotNotation_ReturnsCorrectElement()
        {
            var element0 = _arrayObject.FindProperty("intArray[0]");
            Assert.IsNotNull(element0);
            Assert.AreEqual(1, element0.Value.GetValue());

            var stringElement = _arrayObject.FindProperty("stringList[1]");
            Assert.IsNotNull(stringElement);
            Assert.AreEqual("y", stringElement.Value.GetValue());
        }

        [Test]
        public void FindProperty_WithArrayPathAndSetValue_UpdatesValue()
        {
            // DotBracket形式（内部形式）
            var intListElement = _arrayObject.FindProperty("intList[1]");
            Assert.IsNotNull(intListElement);
            Assert.AreEqual(20, intListElement.Value.GetValue());

            intListElement.Value.SetValue(999);
            Assert.AreEqual(999, _arrayInstance.intList[1]);

            // DotBracket形式（別の配列）
            var vectorListElement = _arrayObject.FindProperty("vectorList[0]");
            Assert.IsNotNull(vectorListElement);
            Assert.AreEqual(Vector3.zero, vectorListElement.Value.GetValue());

            vectorListElement.Value.SetValue(Vector3.forward);
            Assert.AreEqual(Vector3.forward, _arrayInstance.vectorList[0]);
        }

        [Test]
        public void FindProperty_WithInvalidArrayPath_ReturnsNull()
        {
            var outOfRange = _arrayObject.FindProperty("intArray[999]");
            Assert.IsNull(outOfRange);

            // 負のインデックスは null を返す
            var negativeIndex = _arrayObject.FindProperty("intArray[-1]");
            Assert.IsNull(negativeIndex);

            var nonExistent = _arrayObject.FindProperty("nonExistentArray[0]");
            Assert.IsNull(nonExistent);
        }

        [Test]
        public void FindProperty_NestedClassArray_WorksCorrectly()
        {
            var nestedInstance = new NestedClass
            {
                value = 123,
                array = new NestedClass2[]
                {
                    new NestedClass2 { value = 1 },
                    new NestedClass2 { value = 2 },
                    new NestedClass2 { value = 3 }
                }
            };

            var nestedObject = ExposedObjectRegistry.Create(nestedInstance, "nested_object");

            var valueProperty = nestedObject.FindProperty("value");
            Assert.IsNotNull(valueProperty);
            Assert.AreEqual(123, valueProperty.Value.GetValue());

            valueProperty = nestedObject.FindProperty("array");
            Assert.IsNotNull(valueProperty);

            valueProperty = nestedObject.FindProperty("array[0]");
            Assert.IsNotNull(valueProperty);
            Assert.AreEqual(1, (valueProperty.Value.GetValue() as NestedClass2).value);

            valueProperty = nestedObject.FindProperty("array[0].value");
            Assert.IsNotNull(valueProperty);
            Assert.AreEqual(1, valueProperty.Value.GetValue());

            valueProperty = nestedObject.FindProperty("array[1].value");
            Assert.IsNotNull(valueProperty);
            Assert.AreEqual(2, valueProperty.Value.GetValue());

            valueProperty = nestedObject.FindProperty("array[1].value");
            valueProperty.Value.SetValue(10);
            valueProperty = nestedObject.FindProperty("array[1].value");
            Assert.AreEqual(10, valueProperty.Value.GetValue());
        }


        #endregion

        #region Struct Tests

        [Test]
        public void GetValue_StructProperty_ReturnsCorrectValue()
        {
            var structProperty = _structObject.GetProperty("structProperty");
            Assert.IsNotNull(structProperty);

            var structValue = (TestStruct)structProperty.Value.GetValue();
            Assert.AreEqual(10, structValue.intValue);
            Assert.AreEqual(1.5f, structValue.floatValue);
            Assert.AreEqual(true, structValue.boolValue);
        }

        [Test]
        public void SetValue_StructProperty_UpdatesValue()
        {
            var structProperty = _structObject.GetProperty("structProperty");
            Assert.IsNotNull(structProperty);

            var newStruct = new TestStruct { intValue = 99, floatValue = 9.9f, boolValue = false };
            structProperty.Value.SetValue(newStruct);

            Assert.AreEqual(99, _structInstance.structProperty.intValue);
            Assert.AreEqual(9.9f, _structInstance.structProperty.floatValue);
            Assert.AreEqual(false, _structInstance.structProperty.boolValue);
        }

        [Test]
        public void GetValue_StructField_WorksCorrectly()
        {
            var structField = _structObject.GetProperty("structField");
            Assert.IsNotNull(structField);

            var structValue = (TestStruct)structField.Value.GetValue();
            Assert.AreEqual(20, structValue.intValue);
            Assert.AreEqual(2.5f, structValue.floatValue);
            Assert.AreEqual(false, structValue.boolValue);
        }

        [Test]
        public void SetValue_StructField_UpdatesValue()
        {
            var structField = _structObject.GetProperty("structField");
            Assert.IsNotNull(structField);

            var newStruct = new TestStruct { intValue = 88, floatValue = 8.8f, boolValue = true };
            structField.Value.SetValue(newStruct);

            Assert.AreEqual(88, _structInstance.structField.intValue);
            Assert.AreEqual(8.8f, _structInstance.structField.floatValue);
            Assert.AreEqual(true, _structInstance.structField.boolValue);
        }

        [Test]
        public void SetValue_StructNestedProperty_WorksCorrectly()
        {
            // structProperty内のintValueにアクセス
            var structProperty = _structObject.GetProperty("structProperty");
            Assert.IsNotNull(structProperty);

            var intProperty = structProperty.Value.GetProperty("intValue");
            Assert.IsNotNull(intProperty);
            Assert.AreEqual(10, intProperty.Value.GetValue());

            // structのネストされたプロパティを変更
            intProperty.Value.SetValue(555);

            // 変更が反映されているか確認
            var updatedStructProperty = _structObject.GetProperty("structProperty");
            var updatedStruct = (TestStruct)updatedStructProperty.Value.GetValue();
            Assert.AreEqual(555, updatedStruct.intValue);
        }

        #endregion

        #region Object Array Owner Switch Tests

        // セレクタ風のクラス（object[]を持つ）
        public class SelectorClass
        {
            public object[] objects { get; set; }
        }

        // セレクタ内のオブジェクト
        public class TargetClass
        {
            public int health { get; set; }
            public string label { get; set; }
            public List<int> scores { get; set; }
        }

        /// <summary>
        /// object[]配列要素のプロパティにアクセスしたとき、
        /// 要素が登録済みExposedObjectであればownerが正しく切り替わることを検証
        /// </summary>
        [Test]
        public void GetProperty_ObjectArrayElement_SwitchesOwnerToRegisteredExposedObject()
        {
            // TargetClass を登録
            var targetDefines = new ExposedPropertyDefine[]
            {
                new ExposedPropertyDefine { name = "health", path = "health" },
                new ExposedPropertyDefine { name = "label", path = "label" },
                new ExposedPropertyDefine { name = "scores", path = "scores" },
            };
            ExposedClass.Register<TargetClass>("TargetClass", targetDefines);

            // SelectorClass を登録（objectsプロパティ）
            var selectorDefines = new ExposedPropertyDefine[]
            {
                new ExposedPropertyDefine { name = "objects", path = "objects" },
            };
            ExposedClass.Register<SelectorClass>("SelectorClass", selectorDefines);

            // ターゲットインスタンスを作成・登録
            var targetInstance = new TargetClass
            {
                health = 100,
                label = "Player",
                scores = new List<int> { 10, 20, 30 },
            };
            var targetExposedObject = ExposedObjectRegistry.Create<TargetClass>(targetInstance, "target_001");

            // セレクタインスタンスを作成・登録
            var selectorInstance = new SelectorClass
            {
                objects = new object[] { targetInstance },
            };
            var selectorExposedObject = ExposedObjectRegistry.Create<SelectorClass>(selectorInstance, "selector_001");

            // セレクタパス経由でプロパティを取得: objects[0].health
            var healthProp = selectorExposedObject.FindProperty("objects[0].health");
            Assert.IsNotNull(healthProp);
            Assert.AreEqual(100, healthProp.Value.GetValue());

            // ownerがターゲットのExposedObjectに切り替わっていることを確認
            Assert.AreEqual(targetExposedObject, healthProp.Value.owner,
                "owner should be the target's ExposedObject, not the selector's");
            Assert.AreNotEqual(selectorExposedObject, healthProp.Value.owner);

            // pathがターゲットのルートからの相対パスであることを確認
            Assert.AreEqual("health", healthProp.Value.path.Value,
                "path should be relative to the target owner, not the selector");
        }

        /// <summary>
        /// object[]配列要素のプロパティを変更したとき、
        /// ターゲット型のOnPropertyChangingイベントが発火することを検証
        /// </summary>
        [Test]
        public void SetValue_ObjectArrayElement_FiresOnPropertyChangingOnTargetType()
        {
            // TargetClass を登録
            var targetDefines = new ExposedPropertyDefine[]
            {
                new ExposedPropertyDefine { name = "health", path = "health" },
                new ExposedPropertyDefine { name = "label", path = "label" },
                new ExposedPropertyDefine { name = "scores", path = "scores" },
            };
            ExposedClass.Register<TargetClass>("TargetClass", targetDefines);

            // SelectorClass を登録
            var selectorDefines = new ExposedPropertyDefine[]
            {
                new ExposedPropertyDefine { name = "objects", path = "objects" },
            };
            ExposedClass.Register<SelectorClass>("SelectorClass", selectorDefines);

            // インスタンスとExposedObjectを作成
            var targetInstance = new TargetClass { health = 100, label = "Player", scores = new List<int>() };
            var targetExposedObject = ExposedObjectRegistry.Create<TargetClass>(targetInstance, "target_002");

            var selectorInstance = new SelectorClass { objects = new object[] { targetInstance } };
            var selectorExposedObject = ExposedObjectRegistry.Create<SelectorClass>(selectorInstance, "selector_002");

            // イベント監視用の変数
            ExposedProperty? changingProperty = null;
            object changingNewValue = null;
            bool targetChangingFired = false;
            bool selectorChangingFired = false;

            // TargetClass の onPropertyChanging を監視
            ExposedClass.Get<TargetClass>().onPropertyChanging += (prop, newVal) =>
            {
                targetChangingFired = true;
                changingProperty = prop;
                changingNewValue = newVal;
            };

            // SelectorClass の onPropertyChanging を監視
            ExposedClass.Get<SelectorClass>().onPropertyChanging += (prop, newVal) =>
            {
                selectorChangingFired = true;
            };

            // セレクタパス経由でプロパティを変更
            var healthProp = selectorExposedObject.FindProperty("objects[0].health");
            Assert.IsNotNull(healthProp);
            healthProp.Value.SetValue(200);

            // ターゲット側のイベントが発火したことを確認
            Assert.IsTrue(targetChangingFired,
                "OnPropertyChanging should fire on the TargetClass, not the SelectorClass");
            Assert.IsFalse(selectorChangingFired,
                "OnPropertyChanging should NOT fire on the SelectorClass for target's properties");
            Assert.AreEqual(200, changingNewValue);
            Assert.AreEqual("health", changingProperty?.path.Value);

            // 実際の値が更新されていることを確認
            Assert.AreEqual(200, targetInstance.health);
        }

        /// <summary>
        /// object[]配列要素が未登録（FindByTargetでnull）の場合、
        /// ownerがセレクタのExposedObjectのままであることを検証
        /// </summary>
        [Test]
        public void GetProperty_ObjectArrayElement_KeepsOwnerWhenNotRegistered()
        {
            // TargetClass を登録（ExposedClassのみ、ExposedObjectは作成しない）
            var targetDefines = new ExposedPropertyDefine[]
            {
                new ExposedPropertyDefine { name = "health", path = "health" },
                new ExposedPropertyDefine { name = "label", path = "label" },
                new ExposedPropertyDefine { name = "scores", path = "scores" },
            };
            ExposedClass.Register<TargetClass>("TargetClass", targetDefines);

            var selectorDefines = new ExposedPropertyDefine[]
            {
                new ExposedPropertyDefine { name = "objects", path = "objects" },
            };
            ExposedClass.Register<SelectorClass>("SelectorClass", selectorDefines);

            // ターゲットインスタンスはExposedObjectとして登録しない
            var targetInstance = new TargetClass { health = 50, label = "NPC", scores = new List<int>() };

            var selectorInstance = new SelectorClass { objects = new object[] { targetInstance } };
            var selectorExposedObject = ExposedObjectRegistry.Create<SelectorClass>(selectorInstance, "selector_003");

            // セレクタパス経由でプロパティを取得
            var healthProp = selectorExposedObject.FindProperty("objects[0].health");
            Assert.IsNotNull(healthProp);
            Assert.AreEqual(50, healthProp.Value.GetValue());

            // ownerがセレクタのまま
            Assert.AreEqual(selectorExposedObject, healthProp.Value.owner,
                "owner should remain the selector when target is not a registered ExposedObject");
        }

        #endregion
    }
}
