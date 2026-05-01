using NUnit.Framework;
using UnityEngine;
using Lilium.RemoteControl;

namespace Lilium.RemoteControl.Tests
{
    [TestFixture]
    public class ExposedAttributeTests
    {
        #region Test Classes

        [ExposedClass]
        public class TestAttributeClass
        {
            [ExposedProperty("intProperty")]
            public int intValue { get; set; }

            [ExposedProperty("stringProperty")]
            public string stringValue { get; set; }

            [ExposedField("floatField")]
            private float _floatField;

            [ExposedField("boolField")]
            public bool boolField;

            public float GetFloatField() => _floatField;
            public void SetFloatField(float value) => _floatField = value;
        }

        [ExposedClass("TestMixedClass")]
        public class TestMixedAttributeClass
        {
            [ExposedProperty("publicProperty")]
            public int publicProperty { get; set; }

            [ExposedField("privateField")]
            private string _privateField = "default";

            public string GetPrivateField() => _privateField;
            public void SetPrivateField(string value) => _privateField = value;
        }

        public class TestNonAttributeClass
        {
            public int value { get; set; }
        }

        [ExposedClass]
        public class TestSimpleAttributeClass
        {
            [ExposedProperty]
            public int simpleProperty { get; set; }

            [ExposedProperty]
            public string anotherProperty { get; set; }
        }

        #endregion

        [SetUp]
        public void SetUp()
        {
            ExposedClass.Clear();
        }

        #region Attribute Registration Tests

        [Test]
        public void RegisterFromAttributes_ValidClass_RegistersCorrectly()
        {
            ExposedClass.RegisterFromAttributes<TestAttributeClass>();

            var exposedClass = ExposedClass.Get<TestAttributeClass>();
            Assert.IsNotNull(exposedClass);
            Assert.AreEqual("TestAttributeClass", exposedClass.typeName);
            Assert.AreEqual(4, exposedClass.propertyTypes.Length);

            // プロパティの名前を確認
            var propertyNames = new string[exposedClass.propertyTypes.Length];
            for (int i = 0; i < exposedClass.propertyTypes.Length; i++)
            {
                propertyNames[i] = exposedClass.propertyTypes[i].name;
            }

            Assert.Contains("intProperty", propertyNames);
            Assert.Contains("stringProperty", propertyNames);
            Assert.Contains("floatField", propertyNames);
            Assert.Contains("boolField", propertyNames);
        }

        [Test]
        public void RegisterFromAttributes_NonAttributeClass_DoesNotRegister()
        {
            ExposedClass.RegisterFromAttributes<TestNonAttributeClass>();

            var exposedClass = ExposedClass.Find(typeof(TestNonAttributeClass));
            Assert.IsNull(exposedClass);
        }

        [Test]
        public void RegisterFromAttributes_MixedPropertiesAndFields_RegistersCorrectly()
        {
            ExposedClass.RegisterFromAttributes<TestMixedAttributeClass>();

            var exposedClass = ExposedClass.Get<TestMixedAttributeClass>();
            Assert.IsNotNull(exposedClass);
            Assert.AreEqual("TestMixedClass", exposedClass.typeName);
            Assert.AreEqual(2, exposedClass.propertyTypes.Length);

            var propertyNames = new string[exposedClass.propertyTypes.Length];
            for (int i = 0; i < exposedClass.propertyTypes.Length; i++)
            {
                propertyNames[i] = exposedClass.propertyTypes[i].name;
            }

            Assert.Contains("publicProperty", propertyNames);
            Assert.Contains("privateField", propertyNames);
        }

        [Test]
        public void RegisterFromAttributes_SimpleClass_UsesClassAndPropertyNames()
        {
            ExposedClass.RegisterFromAttributes<TestSimpleAttributeClass>();

            var exposedClass = ExposedClass.Get<TestSimpleAttributeClass>();
            Assert.IsNotNull(exposedClass);
            Assert.AreEqual("TestSimpleAttributeClass", exposedClass.typeName);
            Assert.AreEqual(2, exposedClass.propertyTypes.Length);

            var propertyNames = new string[exposedClass.propertyTypes.Length];
            for (int i = 0; i < exposedClass.propertyTypes.Length; i++)
            {
                propertyNames[i] = exposedClass.propertyTypes[i].name;
            }

            Assert.Contains("simpleProperty", propertyNames);
            Assert.Contains("anotherProperty", propertyNames);
        }

        #endregion

        #region Functionality Tests

        [Test]
        public void AttributeRegisteredClass_PropertyAccess_WorksCorrectly()
        {
            ExposedClass.RegisterFromAttributes<TestAttributeClass>();

            var testInstance = new TestAttributeClass
            {
                intValue = 42,
                stringValue = "test",
                boolField = true
            };
            testInstance.SetFloatField(3.14f);

            var exposedObject = ExposedObjectRegistry.Create(testInstance, "test_object");
            Assert.IsNotNull(exposedObject);

            // プロパティの値取得テスト
            var intProperty = exposedObject.GetProperty("intProperty");
            Assert.IsNotNull(intProperty);
            Assert.AreEqual(42, intProperty.Value.GetValue());

            var stringProperty = exposedObject.GetProperty("stringProperty");
            Assert.IsNotNull(stringProperty);
            Assert.AreEqual("test", stringProperty.Value.GetValue());

            var floatProperty = exposedObject.GetProperty("floatField");
            Assert.IsNotNull(floatProperty);
            Assert.AreEqual(3.14f, floatProperty.Value.GetValue());

            var boolProperty = exposedObject.GetProperty("boolField");
            Assert.IsNotNull(boolProperty);
            Assert.AreEqual(true, boolProperty.Value.GetValue());
        }

        [Test]
        public void AttributeRegisteredClass_PropertyUpdate_WorksCorrectly()
        {
            ExposedClass.RegisterFromAttributes<TestAttributeClass>();

            var testInstance = new TestAttributeClass();
            var exposedObject = ExposedObjectRegistry.Create(testInstance, "test_object");

            // プロパティの値設定テスト
            var intProperty = exposedObject.GetProperty("intProperty");
            intProperty.Value.SetValue(100);
            Assert.AreEqual(100, testInstance.intValue);

            var stringProperty = exposedObject.GetProperty("stringProperty");
            stringProperty.Value.SetValue("updated");
            Assert.AreEqual("updated", testInstance.stringValue);

            var boolProperty = exposedObject.GetProperty("boolField");
            boolProperty.Value.SetValue(false);
            Assert.AreEqual(false, testInstance.boolField);
        }

        [Test]
        public void AttributeRegisteredClass_PrivateField_WorksCorrectly()
        {
            ExposedClass.RegisterFromAttributes<TestMixedAttributeClass>();

            var testInstance = new TestMixedAttributeClass();
            testInstance.SetPrivateField("private_value");

            var exposedObject = ExposedObjectRegistry.Create(testInstance, "test_object");

            // プライベートフィールドのアクセステスト
            var privateProperty = exposedObject.GetProperty("privateField");
            Assert.IsNotNull(privateProperty);
            Assert.AreEqual("private_value", privateProperty.Value.GetValue());

            // プライベートフィールドの値設定テスト
            privateProperty.Value.SetValue("updated_private");
            Assert.AreEqual("updated_private", testInstance.GetPrivateField());
        }

        #endregion

        #region isPersistable Tests

        [Test]
        public void ExposedField_IsPersistable_ReturnsTrue()
        {
            ExposedClass.RegisterFromAttributes<TestAttributeClass>();

            var exposedClass = ExposedClass.Get<TestAttributeClass>();

            // ExposedField で登録されたフィールドは isPersistable = true
            var floatProp = exposedClass.FindProperty("floatField");
            Assert.IsNotNull(floatProp);
            Assert.IsTrue(floatProp.isPersistable);

            var boolProp = exposedClass.FindProperty("boolField");
            Assert.IsNotNull(boolProp);
            Assert.IsTrue(boolProp.isPersistable);
        }

        [Test]
        public void ExposedProperty_IsPersistable_ReturnsFalse()
        {
            ExposedClass.RegisterFromAttributes<TestAttributeClass>();

            var exposedClass = ExposedClass.Get<TestAttributeClass>();

            // ExposedProperty で登録されたプロパティは isPersistable = false
            var intProp = exposedClass.FindProperty("intProperty");
            Assert.IsNotNull(intProp);
            Assert.IsFalse(intProp.isPersistable);

            var stringProp = exposedClass.FindProperty("stringProperty");
            Assert.IsNotNull(stringProp);
            Assert.IsFalse(stringProp.isPersistable);
        }

        [Test]
        public void MixedClass_IsPersistable_CorrectlyDistinguished()
        {
            ExposedClass.RegisterFromAttributes<TestMixedAttributeClass>();

            var exposedClass = ExposedClass.Get<TestMixedAttributeClass>();

            // ExposedProperty（プロパティ）は isPersistable = false
            var publicProp = exposedClass.FindProperty("publicProperty");
            Assert.IsNotNull(publicProp);
            Assert.IsFalse(publicProp.isPersistable);

            // ExposedField（フィールド）は isPersistable = true
            var privateProp = exposedClass.FindProperty("privateField");
            Assert.IsNotNull(privateProp);
            Assert.IsTrue(privateProp.isPersistable);
        }

        #endregion

        #region Order Propagation Tests

        [ExposedClass]
        public class OrderTestClass
        {
            [ExposedFunction(order = 10)]
            public void LateFunction() { }

            [ExposedProperty]
            public int defaultProp { get; set; }

            [ExposedFunction(order = -10)]
            public void EarlyFunction() { }

            [ExposedFunction]
            public void DefaultFunction() { }

            [ExposedProperty(order = -20)]
            public int earlyProp { get; set; }
        }

        [Test]
        public void ExposedFunction_ExplicitOrder_SortsRelativeToOthers()
        {
            ExposedClass.RegisterFromAttributes<OrderTestClass>();

            var exposedClass = ExposedClass.Get<OrderTestClass>();
            Assert.IsNotNull(exposedClass);

            // 関数だけを order 昇順に並べる
            var funcs = exposedClass.functionTypes;
            Assert.AreEqual(3, funcs.Length);

            var ordered = new System.Collections.Generic.List<ExposedFunctionType>(funcs);
            ordered.Sort((a, b) => a.order.CompareTo(b.order));

            // 明示 order: EarlyFunction(-10) < DefaultFunction(0) < LateFunction(10) の順で表示される
            Assert.AreEqual("EarlyFunction", ordered[0].name);
            Assert.AreEqual("DefaultFunction", ordered[1].name);
            Assert.AreEqual("LateFunction", ordered[2].name);
        }

        [Test]
        public void ExposedFunction_OrderInterleavesWithProperties()
        {
            ExposedClass.RegisterFromAttributes<OrderTestClass>();

            var exposedClass = ExposedClass.Get<OrderTestClass>();

            // プロパティと関数を一つの列にして order でソート
            var members = new System.Collections.Generic.List<(string name, int order, string kind)>();
            foreach (var p in exposedClass.propertyTypes) members.Add((p.name, p.order, "prop"));
            foreach (var f in exposedClass.functionTypes) members.Add((f.name, f.order, "func"));
            members.Sort((a, b) => a.order.CompareTo(b.order));

            // 明示 order の昇順で関数とプロパティが混在して並ぶ:
            // earlyProp(-20) < EarlyFunction(-10) < (defaultProp=0, DefaultFunction=0 は宣言順) < LateFunction(10)
            Assert.AreEqual("earlyProp", members[0].name);
            Assert.AreEqual("EarlyFunction", members[1].name);
            Assert.AreEqual("LateFunction", members[members.Count - 1].name);
        }

        [Test]
        public void ExposedMembers_DefaultOrder_FollowsCSharpDeclarationOrder()
        {
            // order 未設定 (order=0 同値) のメンバーが property/function を跨いで宣言順に並ぶことを検証する。
            // OrderTestClass の宣言順では defaultProp (line 288) が DefaultFunction (line 294) より先。
            // Source Generator (Lilium.RemoteControl.SourceGenerator) が宣言順テーブルを runtime に
            // 提供しているため、tiebreaker で MetadataToken (= kind 別採番でブレる) ではなく宣言順が使われる。
            ExposedClass.RegisterFromAttributes<OrderTestClass>();

            var exposedClass = ExposedClass.Get<OrderTestClass>();
            Assert.IsNotNull(exposedClass);

            var members = new System.Collections.Generic.List<(string name, int order)>();
            foreach (var p in exposedClass.propertyTypes) members.Add((p.name, p.order));
            foreach (var f in exposedClass.functionTypes) members.Add((f.name, f.order));
            members.Sort((a, b) => a.order.CompareTo(b.order));

            // 期待される並び (C# 宣言順):
            // earlyProp (-20) → EarlyFunction (-10) → defaultProp (0, 宣言が先) → DefaultFunction (0) → LateFunction (10)
            Assert.AreEqual(5, members.Count);
            Assert.AreEqual("earlyProp", members[0].name);
            Assert.AreEqual("EarlyFunction", members[1].name);
            Assert.AreEqual("defaultProp", members[2].name,
                "Source Generator 経路では order=0 同値の場合 C# 宣言順 (defaultProp が先) で並ぶ");
            Assert.AreEqual("DefaultFunction", members[3].name);
            Assert.AreEqual("LateFunction", members[4].name);
        }

        #endregion
    }
}