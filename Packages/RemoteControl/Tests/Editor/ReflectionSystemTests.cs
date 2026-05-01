// Copyright (c) You-Ri, 2026

using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Lilium.RemoteControl.Reflection;
using UnityEngine;

namespace Lilium.RemoteControl.Tests
{
    [TestFixture]
    public class ReflectionSystemTests
    {
        #region Test Classes

        [AttributeUsage(AttributeTargets.Class)]
        private class TestClassAttribute : Attribute
        {
            public string Name { get; }
            public TestClassAttribute(string name = null) { Name = name; }
        }

        [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Method)]
        private class TestMemberAttribute : Attribute
        {
            public string Description { get; }
            public TestMemberAttribute(string description = null) { Description = description; }
        }

        [TestClass("TestType")]
        private class SampleTestClass
        {
            [TestMember("Sample property")]
            public int PublicProperty { get; set; }

            [TestMember]
            private string _privateField = "private";

            public string ReadOnlyProperty => "readonly";

            public static int StaticProperty { get; set; } = 42;

            public static string StaticField = "static_field";

            public List<int> Numbers { get; set; } = new List<int> { 1, 2, 3 };

            public NestedClass Nested { get; set; } = new NestedClass();

            [TestMember("Test method")]
            public int Add(int a, int b) => a + b;

            public static int StaticAdd(int a, int b) => a + b;

            public void VoidMethod() { }
        }

        private class NestedClass
        {
            public string Value { get; set; } = "nested_value";
            public int Number { get; set; } = 100;
        }

        #endregion

        #region TypeReflectionSystem Tests

        [Test]
        public void TypeReflectionSystem_Collect_ReturnsValidData()
        {
            // Act
            var data = TypeReflectionSystem.Collect(typeof(SampleTestClass));

            // Assert
            Assert.IsNotNull(data);
            Assert.AreEqual(typeof(SampleTestClass), data.type);
            Assert.AreEqual("SampleTestClass", data.typeName);
            Assert.IsFalse(data.isStatic);
            Assert.IsTrue(data.members.Length > 0);
            Assert.IsTrue(data.methods.Length > 0);
        }

        [Test]
        public void TypeReflectionSystem_GetCustomAttribute_Type()
        {
            // Act
            var attr = TypeReflectionSystem.GetCustomAttribute<TestClassAttribute>(typeof(SampleTestClass));

            // Assert
            Assert.IsNotNull(attr);
            Assert.AreEqual("TestType", attr.Name);
        }

        [Test]
        public void TypeReflectionSystem_GetCustomAttribute_Member()
        {
            // Arrange
            var propInfo = typeof(SampleTestClass).GetProperty("PublicProperty");

            // Act
            var attr = TypeReflectionSystem.GetCustomAttribute<TestMemberAttribute>(propInfo);

            // Assert
            Assert.IsNotNull(attr);
            Assert.AreEqual("Sample property", attr.Description);
        }

        [Test]
        public void TypeReflectionSystem_GetProperty()
        {
            // Act
            var prop = TypeReflectionSystem.GetProperty(typeof(SampleTestClass), "PublicProperty");

            // Assert
            Assert.IsNotNull(prop);
            Assert.AreEqual("PublicProperty", prop.Name);
            Assert.AreEqual(typeof(int), prop.PropertyType);
        }

        [Test]
        public void TypeReflectionSystem_GetField()
        {
            // Act
            var field = TypeReflectionSystem.GetField(typeof(SampleTestClass), "StaticField");

            // Assert
            Assert.IsNotNull(field);
            Assert.AreEqual("StaticField", field.Name);
            Assert.IsTrue(field.IsStatic);
        }

        [Test]
        public void TypeReflectionSystem_GetMethod()
        {
            // Act
            var method = TypeReflectionSystem.GetMethod(typeof(SampleTestClass), "Add");

            // Assert
            Assert.IsNotNull(method);
            Assert.AreEqual("Add", method.Name);
            Assert.AreEqual(typeof(int), method.ReturnType);
        }

        #endregion

        #region MemberAccessData Tests

        [Test]
        public void MemberAccessData_FromProperty()
        {
            // Arrange
            var propInfo = typeof(SampleTestClass).GetProperty("PublicProperty");

            // Act
            var data = MemberAccessData.FromProperty(propInfo);

            // Assert
            Assert.IsTrue(data.isValid);
            Assert.IsTrue(data.isProperty);
            Assert.IsFalse(data.isStatic);
            Assert.IsFalse(data.isReadOnly);
            Assert.AreEqual(typeof(int), data.valueType);
        }

        [Test]
        public void MemberAccessData_FromReadOnlyProperty()
        {
            // Arrange
            var propInfo = typeof(SampleTestClass).GetProperty("ReadOnlyProperty");

            // Act
            var data = MemberAccessData.FromProperty(propInfo);

            // Assert
            Assert.IsTrue(data.isValid);
            Assert.IsTrue(data.isReadOnly);
        }

        [Test]
        public void MemberAccessData_FromStaticProperty()
        {
            // Arrange
            var propInfo = typeof(SampleTestClass).GetProperty("StaticProperty");

            // Act
            var data = MemberAccessData.FromProperty(propInfo);

            // Assert
            Assert.IsTrue(data.isValid);
            Assert.IsTrue(data.isStatic);
            Assert.IsFalse(data.isReadOnly);
        }

        #endregion

        #region MemberAccessSystem Tests

        [Test]
        public void MemberAccessSystem_GetValue_SimpleProperty()
        {
            // Arrange
            var obj = new SampleTestClass { PublicProperty = 123 };

            // Act
            var result = MemberAccessSystem.GetValue(obj, "PublicProperty");

            // Assert
            Assert.AreEqual(123, result);
        }

        [Test]
        public void MemberAccessSystem_SetValue_SimpleProperty()
        {
            // Arrange
            var obj = new SampleTestClass();

            // Act
            var success = MemberAccessSystem.SetValue(obj, "PublicProperty", 456);
            var result = obj.PublicProperty;

            // Assert
            Assert.IsTrue(success);
            Assert.AreEqual(456, result);
        }

        [Test]
        public void MemberAccessSystem_GetValue_NestedProperty()
        {
            // Arrange
            var obj = new SampleTestClass();
            obj.Nested.Value = "test_value";

            // Act
            var result = MemberAccessSystem.GetValue(obj, "Nested.Value");

            // Assert
            Assert.AreEqual("test_value", result);
        }

        [Test]
        public void MemberAccessSystem_GetValue_ArrayIndex()
        {
            // Arrange
            var obj = new SampleTestClass();
            obj.Numbers = new List<int> { 10, 20, 30 };

            // Act
            var result = MemberAccessSystem.GetValue(obj, "Numbers[1]");

            // Assert
            Assert.AreEqual(20, result);
        }

        [Test]
        public void MemberAccessSystem_SetValue_ArrayIndex()
        {
            // Arrange
            var obj = new SampleTestClass();
            obj.Numbers = new List<int> { 10, 20, 30 };

            // Act
            var success = MemberAccessSystem.SetValue(obj, "Numbers[1]", 99);

            // Assert
            Assert.IsTrue(success);
            Assert.AreEqual(99, obj.Numbers[1]);
        }

        [Test]
        public void MemberAccessSystem_GetValue_Static()
        {
            // Arrange
            SampleTestClass.StaticProperty = 999;
            var propInfo = typeof(SampleTestClass).GetProperty("StaticProperty");
            var accessData = MemberAccessData.FromProperty(propInfo);

            // Act
            var result = MemberAccessSystem.GetValue(null, accessData);

            // Assert
            Assert.AreEqual(999, result);
        }

        [Test]
        public void MemberAccessSystem_SetValue_Static()
        {
            // Arrange
            var propInfo = typeof(SampleTestClass).GetProperty("StaticProperty");
            var accessData = MemberAccessData.FromProperty(propInfo);

            // Act
            var success = MemberAccessSystem.SetValue(null, accessData, 777);

            // Assert
            Assert.IsTrue(success);
            Assert.AreEqual(777, SampleTestClass.StaticProperty);
        }

        #endregion

        #region MethodInvokeData Tests

        [Test]
        public void MethodInvokeData_FromMethod()
        {
            // Arrange
            var methodInfo = typeof(SampleTestClass).GetMethod("Add");

            // Act
            var data = MethodInvokeData.FromMethod(methodInfo);

            // Assert
            Assert.IsTrue(data.isValid);
            Assert.IsFalse(data.isStatic);
            Assert.IsFalse(data.isVoid);
            Assert.AreEqual(typeof(int), data.returnType);
            Assert.AreEqual(2, data.parameterCount);
        }

        [Test]
        public void MethodInvokeData_FromStaticMethod()
        {
            // Arrange
            var methodInfo = typeof(SampleTestClass).GetMethod("StaticAdd");

            // Act
            var data = MethodInvokeData.FromMethod(methodInfo);

            // Assert
            Assert.IsTrue(data.isValid);
            Assert.IsTrue(data.isStatic);
        }

        [Test]
        public void MethodInvokeData_VoidMethod()
        {
            // Arrange
            var methodInfo = typeof(SampleTestClass).GetMethod("VoidMethod");

            // Act
            var data = MethodInvokeData.FromMethod(methodInfo);

            // Assert
            Assert.IsTrue(data.isValid);
            Assert.IsTrue(data.isVoid);
            Assert.IsTrue(data.hasNoParameters);
        }

        #endregion

        #region MethodInvokeSystem Tests

        [Test]
        public void MethodInvokeSystem_Invoke_InstanceMethod()
        {
            // Arrange
            var obj = new SampleTestClass();
            var methodInfo = typeof(SampleTestClass).GetMethod("Add");
            var invokeData = MethodInvokeData.FromMethod(methodInfo);

            // Act
            var result = MethodInvokeSystem.Invoke(obj, invokeData, new object[] { 10, 20 });

            // Assert
            Assert.AreEqual(30, result);
        }

        [Test]
        public void MethodInvokeSystem_Invoke_StaticMethod()
        {
            // Arrange
            var methodInfo = typeof(SampleTestClass).GetMethod("StaticAdd");
            var invokeData = MethodInvokeData.FromMethod(methodInfo);

            // Act
            var result = MethodInvokeSystem.Invoke(null, invokeData, new object[] { 15, 25 });

            // Assert
            Assert.AreEqual(40, result);
        }

        [Test]
        public void MethodInvokeSystem_Invoke_ByName()
        {
            // Arrange
            var obj = new SampleTestClass();

            // Act
            var result = MethodInvokeSystem.Invoke(obj, "Add", new object[] { 5, 7 });

            // Assert
            Assert.AreEqual(12, result);
        }

        [Test]
        public void MethodInvokeSystem_InvokeStatic()
        {
            // Act
            var result = MethodInvokeSystem.InvokeStatic(typeof(SampleTestClass), "StaticAdd", new object[] { 3, 4 });

            // Assert
            Assert.AreEqual(7, result);
        }

        #endregion

        #region Integration Tests

        [Test]
        public void Integration_PathParsing()
        {
            // Arrange
            var path = "Nested.Value";
            var segmentNames = new List<string>();

            // Act
            foreach (var segment in PropertyPathParser.Parse(path))
            {
                segmentNames.Add(segment.name.ToString());
            }

            // Assert
            Assert.AreEqual(2, segmentNames.Count);
            Assert.AreEqual("Nested", segmentNames[0]);
            Assert.AreEqual("Value", segmentNames[1]);
        }

        [Test]
        public void Integration_ComplexPath()
        {
            // Arrange
            var path = "Numbers[0]";
            var segmentCount = 0;
            string firstSegmentName = null;
            bool secondSegmentIsIndexed = false;
            int secondSegmentIndex = -1;

            // Act
            foreach (var segment in PropertyPathParser.Parse(path))
            {
                if (segmentCount == 0)
                {
                    firstSegmentName = segment.name.ToString();
                    Assert.IsFalse(segment.isIndexed);
                }
                else if (segmentCount == 1)
                {
                    secondSegmentIsIndexed = segment.isIndexed;
                    secondSegmentIndex = segment.index;
                }
                segmentCount++;
            }

            // Assert
            Assert.AreEqual(2, segmentCount);
            Assert.AreEqual("Numbers", firstSegmentName);
            Assert.IsTrue(secondSegmentIsIndexed);
            Assert.AreEqual(0, secondSegmentIndex);
        }

        #endregion
    }
}
