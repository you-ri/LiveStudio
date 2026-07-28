using NUnit.Framework;
using UnityEngine;
using Lilium.RemoteControl;

namespace Lilium.RemoteControl.Tests
{
    [TestFixture]
    public class LiveAttributeTests
    {
        #region Test Classes

        [LiveClass]
        public class TestAttributeClass
        {
            [LiveProperty("intProperty")]
            public int intValue { get; set; }

            [LiveProperty("stringProperty")]
            public string stringValue { get; set; }

            [LiveField("floatField")]
            private float _floatField;

            [LiveField("boolField")]
            public bool boolField;

            public float GetFloatField() => _floatField;
            public void SetFloatField(float value) => _floatField = value;
        }

        [LiveClass("TestMixedClass")]
        public class TestMixedAttributeClass
        {
            [LiveProperty("publicProperty")]
            public int publicProperty { get; set; }

            [LiveField("privateField")]
            private string _privateField = "default";

            public string GetPrivateField() => _privateField;
            public void SetPrivateField(string value) => _privateField = value;
        }

        public class TestNonAttributeClass
        {
            public int value { get; set; }
        }

        [LiveClass]
        public class TestSimpleAttributeClass
        {
            [LiveProperty]
            public int simpleProperty { get; set; }

            [LiveProperty]
            public string anotherProperty { get; set; }
        }

        #endregion

        [SetUp]
        public void SetUp()
        {
            LiveClass.Clear();
        }

        #region Attribute Registration Tests

        [Test]
        public void RegisterFromAttributes_ValidClass_RegistersCorrectly()
        {
            LiveClass.RegisterFromAttributes<TestAttributeClass>();

            var liveClass = LiveClass.Get<TestAttributeClass>();
            Assert.IsNotNull(liveClass);
            Assert.AreEqual("TestAttributeClass", liveClass.typeName);
            Assert.AreEqual(4, liveClass.propertyTypes.Length);

            // プロパティの名前を確認
            var propertyNames = new string[liveClass.propertyTypes.Length];
            for (int i = 0; i < liveClass.propertyTypes.Length; i++)
            {
                propertyNames[i] = liveClass.propertyTypes[i].name;
            }

            Assert.Contains("intProperty", propertyNames);
            Assert.Contains("stringProperty", propertyNames);
            Assert.Contains("floatField", propertyNames);
            Assert.Contains("boolField", propertyNames);
        }

        [Test]
        public void RegisterFromAttributes_NonAttributeClass_DoesNotRegister()
        {
            LiveClass.RegisterFromAttributes<TestNonAttributeClass>();

            var liveClass = LiveClass.Find(typeof(TestNonAttributeClass));
            Assert.IsNull(liveClass);
        }

        [Test]
        public void RegisterFromAttributes_MixedPropertiesAndFields_RegistersCorrectly()
        {
            LiveClass.RegisterFromAttributes<TestMixedAttributeClass>();

            var liveClass = LiveClass.Get<TestMixedAttributeClass>();
            Assert.IsNotNull(liveClass);
            Assert.AreEqual("TestMixedClass", liveClass.typeName);
            Assert.AreEqual(2, liveClass.propertyTypes.Length);

            var propertyNames = new string[liveClass.propertyTypes.Length];
            for (int i = 0; i < liveClass.propertyTypes.Length; i++)
            {
                propertyNames[i] = liveClass.propertyTypes[i].name;
            }

            Assert.Contains("publicProperty", propertyNames);
            Assert.Contains("privateField", propertyNames);
        }

        [Test]
        public void RegisterFromAttributes_SimpleClass_UsesClassAndPropertyNames()
        {
            LiveClass.RegisterFromAttributes<TestSimpleAttributeClass>();

            var liveClass = LiveClass.Get<TestSimpleAttributeClass>();
            Assert.IsNotNull(liveClass);
            Assert.AreEqual("TestSimpleAttributeClass", liveClass.typeName);
            Assert.AreEqual(2, liveClass.propertyTypes.Length);

            var propertyNames = new string[liveClass.propertyTypes.Length];
            for (int i = 0; i < liveClass.propertyTypes.Length; i++)
            {
                propertyNames[i] = liveClass.propertyTypes[i].name;
            }

            Assert.Contains("simpleProperty", propertyNames);
            Assert.Contains("anotherProperty", propertyNames);
        }

        #endregion

        #region Functionality Tests

        [Test]
        public void AttributeRegisteredClass_PropertyAccess_WorksCorrectly()
        {
            LiveClass.RegisterFromAttributes<TestAttributeClass>();

            var testInstance = new TestAttributeClass
            {
                intValue = 42,
                stringValue = "test",
                boolField = true
            };
            testInstance.SetFloatField(3.14f);

            var liveObject = LiveObjectRegistry.Create(testInstance, "test_object").Value;
            Assert.IsNotNull(liveObject);

            // プロパティの値取得テスト
            var intProperty = liveObject.GetProperty("intProperty");
            Assert.IsNotNull(intProperty);
            Assert.AreEqual(42, intProperty.Value.GetValue());

            var stringProperty = liveObject.GetProperty("stringProperty");
            Assert.IsNotNull(stringProperty);
            Assert.AreEqual("test", stringProperty.Value.GetValue());

            var floatProperty = liveObject.GetProperty("floatField");
            Assert.IsNotNull(floatProperty);
            Assert.AreEqual(3.14f, floatProperty.Value.GetValue());

            var boolProperty = liveObject.GetProperty("boolField");
            Assert.IsNotNull(boolProperty);
            Assert.AreEqual(true, boolProperty.Value.GetValue());
        }

        [Test]
        public void AttributeRegisteredClass_PropertyUpdate_WorksCorrectly()
        {
            LiveClass.RegisterFromAttributes<TestAttributeClass>();

            var testInstance = new TestAttributeClass();
            var liveObject = LiveObjectRegistry.Create(testInstance, "test_object").Value;

            // プロパティの値設定テスト
            var intProperty = liveObject.GetProperty("intProperty");
            intProperty.Value.SetValue(100);
            Assert.AreEqual(100, testInstance.intValue);

            var stringProperty = liveObject.GetProperty("stringProperty");
            stringProperty.Value.SetValue("updated");
            Assert.AreEqual("updated", testInstance.stringValue);

            var boolProperty = liveObject.GetProperty("boolField");
            boolProperty.Value.SetValue(false);
            Assert.AreEqual(false, testInstance.boolField);
        }

        [Test]
        public void AttributeRegisteredClass_PrivateField_WorksCorrectly()
        {
            LiveClass.RegisterFromAttributes<TestMixedAttributeClass>();

            var testInstance = new TestMixedAttributeClass();
            testInstance.SetPrivateField("private_value");

            var liveObject = LiveObjectRegistry.Create(testInstance, "test_object").Value;

            // プライベートフィールドのアクセステスト
            var privateProperty = liveObject.GetProperty("privateField");
            Assert.IsNotNull(privateProperty);
            Assert.AreEqual("private_value", privateProperty.Value.GetValue());

            // プライベートフィールドの値設定テスト
            privateProperty.Value.SetValue("updated_private");
            Assert.AreEqual("updated_private", testInstance.GetPrivateField());
        }

        #endregion

        #region isPersistable Tests

        [Test]
        public void LiveField_IsPersistable_ReturnsTrue()
        {
            LiveClass.RegisterFromAttributes<TestAttributeClass>();

            var liveClass = LiveClass.Get<TestAttributeClass>();

            // LiveField で登録されたフィールドは isPersistable = true
            var floatProp = liveClass.FindProperty("floatField");
            Assert.IsNotNull(floatProp);
            Assert.IsTrue(floatProp.isPersistable);

            var boolProp = liveClass.FindProperty("boolField");
            Assert.IsNotNull(boolProp);
            Assert.IsTrue(boolProp.isPersistable);
        }

        [Test]
        public void LiveProperty_IsPersistable_ReturnsFalse()
        {
            LiveClass.RegisterFromAttributes<TestAttributeClass>();

            var liveClass = LiveClass.Get<TestAttributeClass>();

            // LiveProperty で登録されたプロパティは isPersistable = false
            var intProp = liveClass.FindProperty("intProperty");
            Assert.IsNotNull(intProp);
            Assert.IsFalse(intProp.isPersistable);

            var stringProp = liveClass.FindProperty("stringProperty");
            Assert.IsNotNull(stringProp);
            Assert.IsFalse(stringProp.isPersistable);
        }

        [Test]
        public void MixedClass_IsPersistable_CorrectlyDistinguished()
        {
            LiveClass.RegisterFromAttributes<TestMixedAttributeClass>();

            var liveClass = LiveClass.Get<TestMixedAttributeClass>();

            // LiveProperty（プロパティ）は isPersistable = false
            var publicProp = liveClass.FindProperty("publicProperty");
            Assert.IsNotNull(publicProp);
            Assert.IsFalse(publicProp.isPersistable);

            // LiveField（フィールド）は isPersistable = true
            var privateProp = liveClass.FindProperty("privateField");
            Assert.IsNotNull(privateProp);
            Assert.IsTrue(privateProp.isPersistable);
        }

        #endregion

        #region Order Propagation Tests

        [LiveClass]
        public class OrderTestClass
        {
            [LiveFunction(order = 10)]
            public void LateFunction() { }

            [LiveProperty]
            public int defaultProp { get; set; }

            [LiveFunction(order = -10)]
            public void EarlyFunction() { }

            [LiveFunction]
            public void DefaultFunction() { }

            [LiveProperty(order = -20)]
            public int earlyProp { get; set; }
        }

        [Test]
        public void LiveFunction_ExplicitOrder_SortsRelativeToOthers()
        {
            LiveClass.RegisterFromAttributes<OrderTestClass>();

            var liveClass = LiveClass.Get<OrderTestClass>();
            Assert.IsNotNull(liveClass);

            // 関数だけを order 昇順に並べる
            var funcs = liveClass.functionTypes;
            Assert.AreEqual(3, funcs.Length);

            var ordered = new System.Collections.Generic.List<LiveFunctionType>(funcs);
            ordered.Sort((a, b) => a.order.CompareTo(b.order));

            // 明示 order: EarlyFunction(-10) < DefaultFunction(0) < LateFunction(10) の順で表示される
            Assert.AreEqual("EarlyFunction", ordered[0].name);
            Assert.AreEqual("DefaultFunction", ordered[1].name);
            Assert.AreEqual("LateFunction", ordered[2].name);
        }

        [Test]
        public void LiveFunction_OrderInterleavesWithProperties()
        {
            LiveClass.RegisterFromAttributes<OrderTestClass>();

            var liveClass = LiveClass.Get<OrderTestClass>();

            // プロパティと関数を一つの列にして order でソート
            var members = new System.Collections.Generic.List<(string name, int order, string kind)>();
            foreach (var p in liveClass.propertyTypes) members.Add((p.name, p.order, "prop"));
            foreach (var f in liveClass.functionTypes) members.Add((f.name, f.order, "func"));
            members.Sort((a, b) => a.order.CompareTo(b.order));

            // 明示 order の昇順で関数とプロパティが混在して並ぶ:
            // earlyProp(-20) < EarlyFunction(-10) < (defaultProp=0, DefaultFunction=0 は宣言順) < LateFunction(10)
            Assert.AreEqual("earlyProp", members[0].name);
            Assert.AreEqual("EarlyFunction", members[1].name);
            Assert.AreEqual("LateFunction", members[members.Count - 1].name);
        }

        [Test]
        public void LiveMembers_DefaultOrder_FollowsCSharpDeclarationOrder()
        {
            // order 未設定 (order=0 同値) のメンバーが property/function を跨いで宣言順に並ぶことを検証する。
            // OrderTestClass の宣言順では defaultProp (line 288) が DefaultFunction (line 294) より先。
            // Source Generator (Lilium.RemoteControl.SourceGenerator) が宣言順テーブルを runtime に
            // 提供しているため、tiebreaker で MetadataToken (= kind 別採番でブレる) ではなく宣言順が使われる。
            LiveClass.RegisterFromAttributes<OrderTestClass>();

            var liveClass = LiveClass.Get<OrderTestClass>();
            Assert.IsNotNull(liveClass);

            var members = new System.Collections.Generic.List<(string name, int order)>();
            foreach (var p in liveClass.propertyTypes) members.Add((p.name, p.order));
            foreach (var f in liveClass.functionTypes) members.Add((f.name, f.order));
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