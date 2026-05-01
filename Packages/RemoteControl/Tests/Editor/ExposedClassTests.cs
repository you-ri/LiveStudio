using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Lilium.RemoteControl;

namespace Lilium.RemoteControl.Tests
{
    [TestFixture]
    public class ExposedClassTests
    {
        public class TestClass
        {
            public int intValue { get; set; }
            public string stringValue { get; set; }
            public float floatValue { get; set; }
            public bool boolValue { get; set; }
        }

        public class ComplexTestClass
        {
            public Vector3 position { get; set; }
            public Color color { get; set; }
            public List<int> intList { get; set; }
            public string[] stringArray { get; set; }
        }

        [SetUp]
        public void Setup()
        {
            ExposedClass.Clear();
        }

        [Test]
        public void RegisterAndGetExposedClass()
        {
            var defines = new ExposedPropertyDefine[]
            {
                new ExposedPropertyDefine { name = "intValue", path = "intValue" },
                new ExposedPropertyDefine { name = "stringValue", path = "stringValue" }
            };

            ExposedClass.Register<TestClass>("TestClass", defines);

            var exposedClass = ExposedClass.Get<TestClass>();
            Assert.IsNotNull(exposedClass);
            Assert.AreEqual("TestClass", exposedClass.typeName);
            Assert.AreEqual(typeof(TestClass), exposedClass.type);
            Assert.AreEqual(2, exposedClass.propertyTypes.Length);
        }

        [Test]
        public void UnregisterExposedClass()
        {
            var defines = new ExposedPropertyDefine[]
            {
                new ExposedPropertyDefine { name = "intValue", path = "intValue" }
            };

            ExposedClass.Register<TestClass>("TestClass", defines);
            var exposedClass = ExposedClass.Get<TestClass>();
            Assert.IsNotNull(exposedClass);

            ExposedClass.Unregister(exposedClass);
            var result = ExposedClass.Find(typeof(TestClass));
            Assert.IsNull(result);
        }

        [Test]
        public void GetNonRegisteredClassReturnsNull()
        {
            var exposedClass = ExposedClass.Find(typeof(TestClass));
            Assert.IsNull(exposedClass);
        }

        [Test]
        public void RegisterCreatesExposedClassWithProperties()
        {
            var defines = new ExposedPropertyDefine[]
            {
                new ExposedPropertyDefine { name = "intValue", path = "intValue" },
                new ExposedPropertyDefine { name = "stringValue", path = "stringValue" },
                new ExposedPropertyDefine { name = "floatValue", path = "floatValue" },
                new ExposedPropertyDefine { name = "boolValue", path = "boolValue" }
            };

            ExposedClass.Register<TestClass>("TestClass", defines);
            var exposedClass = ExposedClass.Get<TestClass>();

            Assert.IsNotNull(exposedClass);
            Assert.AreEqual("TestClass", exposedClass.typeName);
            Assert.AreEqual(4, exposedClass.propertyTypes.Length);

            Assert.AreEqual("intValue", exposedClass.propertyTypes[0].name);
            Assert.AreEqual(typeof(int), exposedClass.propertyTypes[0].valueType);

            Assert.AreEqual("stringValue", exposedClass.propertyTypes[1].name);
            Assert.AreEqual(typeof(string), exposedClass.propertyTypes[1].valueType);

            Assert.AreEqual("floatValue", exposedClass.propertyTypes[2].name);
            Assert.AreEqual(typeof(float), exposedClass.propertyTypes[2].valueType);

            Assert.AreEqual("boolValue", exposedClass.propertyTypes[3].name);
            Assert.AreEqual(typeof(bool), exposedClass.propertyTypes[3].valueType);
        }

        [Test]
        public void RegisterMultipleClasses()
        {
            var testClassDefines = new ExposedPropertyDefine[]
            {
                new ExposedPropertyDefine { name = "intValue", path = "intValue" }
            };

            var complexClassDefines = new ExposedPropertyDefine[]
            {
                new ExposedPropertyDefine { name = "position", path = "position" },
                new ExposedPropertyDefine { name = "color", path = "color" }
            };

            ExposedClass.Register<TestClass>("TestClass", testClassDefines);
            ExposedClass.Register<ComplexTestClass>("ComplexTestClass", complexClassDefines);

            var testClass = ExposedClass.Get<TestClass>();
            var complexClass = ExposedClass.Get<ComplexTestClass>();

            Assert.IsNotNull(testClass);
            Assert.IsNotNull(complexClass);
            Assert.AreEqual("TestClass", testClass.typeName);
            Assert.AreEqual("ComplexTestClass", complexClass.typeName);
        }

        [Test]
        public void AllDictionaryContainsRegisteredClasses()
        {
            var testClassDefines = new ExposedPropertyDefine[]
            {
                new ExposedPropertyDefine { name = "intValue", path = "intValue" }
            };

            var complexClassDefines = new ExposedPropertyDefine[]
            {
                new ExposedPropertyDefine { name = "position", path = "position" }
            };

            ExposedClass.Register<TestClass>("TestClass", testClassDefines);
            ExposedClass.Register<ComplexTestClass>("ComplexTestClass", complexClassDefines);

            Assert.AreEqual(2, ExposedClass.all.Count);
            Assert.IsTrue(ExposedClass.all.ContainsKey(typeof(TestClass)));
            Assert.IsTrue(ExposedClass.all.ContainsKey(typeof(ComplexTestClass)));
            Assert.AreEqual("TestClass", ExposedClass.all[typeof(TestClass)].typeName);
            Assert.AreEqual("ComplexTestClass", ExposedClass.all[typeof(ComplexTestClass)].typeName);
        }

        [Test]
        public void RegisterExposedClassWithArrayProperties()
        {
            var defines = new ExposedPropertyDefine[]
            {
                new ExposedPropertyDefine { name = "intList", path = "intList" },
                new ExposedPropertyDefine { name = "stringArray", path = "stringArray" }
            };

            ExposedClass.Register<ComplexTestClass>("ComplexTestClass", defines);
            var exposedClass = ExposedClass.Get<ComplexTestClass>();

            Assert.IsNotNull(exposedClass);
            Assert.AreEqual(2, exposedClass.propertyTypes.Length);

            var intListProperty = exposedClass.propertyTypes[0];
            Assert.AreEqual("intList", intListProperty.name);
            Assert.AreEqual(typeof(List<int>), intListProperty.valueType);

            var stringArrayProperty = exposedClass.propertyTypes[1];
            Assert.AreEqual("stringArray", stringArrayProperty.name);
            Assert.AreEqual(typeof(string[]), stringArrayProperty.valueType);
        }
    }
}