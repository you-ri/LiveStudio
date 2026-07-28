using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Lilium.RemoteControl;

namespace Lilium.RemoteControl.Tests
{
    [TestFixture]
    public class LiveClassTests
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
            LiveClass.Clear();
        }

        [Test]
        public void RegisterAndGetLiveClass()
        {
            var defines = new LivePropertyDefine[]
            {
                new LivePropertyDefine { name = "intValue", path = "intValue" },
                new LivePropertyDefine { name = "stringValue", path = "stringValue" }
            };

            LiveClass.Register<TestClass>("TestClass", defines);

            var liveClass = LiveClass.Get<TestClass>();
            Assert.IsNotNull(liveClass);
            Assert.AreEqual("TestClass", liveClass.typeName);
            Assert.AreEqual(typeof(TestClass), liveClass.type);
            Assert.AreEqual(2, liveClass.propertyTypes.Length);
        }

        [Test]
        public void UnregisterLiveClass()
        {
            var defines = new LivePropertyDefine[]
            {
                new LivePropertyDefine { name = "intValue", path = "intValue" }
            };

            LiveClass.Register<TestClass>("TestClass", defines);
            var liveClass = LiveClass.Get<TestClass>();
            Assert.IsNotNull(liveClass);

            LiveClass.Unregister(liveClass);
            var result = LiveClass.Find(typeof(TestClass));
            Assert.IsNull(result);
        }

        [Test]
        public void GetNonRegisteredClassReturnsNull()
        {
            var liveClass = LiveClass.Find(typeof(TestClass));
            Assert.IsNull(liveClass);
        }

        [Test]
        public void RegisterCreatesLiveClassWithProperties()
        {
            var defines = new LivePropertyDefine[]
            {
                new LivePropertyDefine { name = "intValue", path = "intValue" },
                new LivePropertyDefine { name = "stringValue", path = "stringValue" },
                new LivePropertyDefine { name = "floatValue", path = "floatValue" },
                new LivePropertyDefine { name = "boolValue", path = "boolValue" }
            };

            LiveClass.Register<TestClass>("TestClass", defines);
            var liveClass = LiveClass.Get<TestClass>();

            Assert.IsNotNull(liveClass);
            Assert.AreEqual("TestClass", liveClass.typeName);
            Assert.AreEqual(4, liveClass.propertyTypes.Length);

            Assert.AreEqual("intValue", liveClass.propertyTypes[0].name);
            Assert.AreEqual(typeof(int), liveClass.propertyTypes[0].valueType);

            Assert.AreEqual("stringValue", liveClass.propertyTypes[1].name);
            Assert.AreEqual(typeof(string), liveClass.propertyTypes[1].valueType);

            Assert.AreEqual("floatValue", liveClass.propertyTypes[2].name);
            Assert.AreEqual(typeof(float), liveClass.propertyTypes[2].valueType);

            Assert.AreEqual("boolValue", liveClass.propertyTypes[3].name);
            Assert.AreEqual(typeof(bool), liveClass.propertyTypes[3].valueType);
        }

        [Test]
        public void RegisterMultipleClasses()
        {
            var testClassDefines = new LivePropertyDefine[]
            {
                new LivePropertyDefine { name = "intValue", path = "intValue" }
            };

            var complexClassDefines = new LivePropertyDefine[]
            {
                new LivePropertyDefine { name = "position", path = "position" },
                new LivePropertyDefine { name = "color", path = "color" }
            };

            LiveClass.Register<TestClass>("TestClass", testClassDefines);
            LiveClass.Register<ComplexTestClass>("ComplexTestClass", complexClassDefines);

            var testClass = LiveClass.Get<TestClass>();
            var complexClass = LiveClass.Get<ComplexTestClass>();

            Assert.IsNotNull(testClass);
            Assert.IsNotNull(complexClass);
            Assert.AreEqual("TestClass", testClass.typeName);
            Assert.AreEqual("ComplexTestClass", complexClass.typeName);
        }

        [Test]
        public void AllDictionaryContainsRegisteredClasses()
        {
            var testClassDefines = new LivePropertyDefine[]
            {
                new LivePropertyDefine { name = "intValue", path = "intValue" }
            };

            var complexClassDefines = new LivePropertyDefine[]
            {
                new LivePropertyDefine { name = "position", path = "position" }
            };

            LiveClass.Register<TestClass>("TestClass", testClassDefines);
            LiveClass.Register<ComplexTestClass>("ComplexTestClass", complexClassDefines);

            Assert.AreEqual(2, LiveClass.all.Count);
            Assert.IsTrue(LiveClass.all.ContainsKey(typeof(TestClass)));
            Assert.IsTrue(LiveClass.all.ContainsKey(typeof(ComplexTestClass)));
            Assert.AreEqual("TestClass", LiveClass.all[typeof(TestClass)].typeName);
            Assert.AreEqual("ComplexTestClass", LiveClass.all[typeof(ComplexTestClass)].typeName);
        }

        [Test]
        public void RegisterLiveClassWithArrayProperties()
        {
            var defines = new LivePropertyDefine[]
            {
                new LivePropertyDefine { name = "intList", path = "intList" },
                new LivePropertyDefine { name = "stringArray", path = "stringArray" }
            };

            LiveClass.Register<ComplexTestClass>("ComplexTestClass", defines);
            var liveClass = LiveClass.Get<ComplexTestClass>();

            Assert.IsNotNull(liveClass);
            Assert.AreEqual(2, liveClass.propertyTypes.Length);

            var intListProperty = liveClass.propertyTypes[0];
            Assert.AreEqual("intList", intListProperty.name);
            Assert.AreEqual(typeof(List<int>), intListProperty.valueType);

            var stringArrayProperty = liveClass.propertyTypes[1];
            Assert.AreEqual("stringArray", stringArrayProperty.name);
            Assert.AreEqual(typeof(string[]), stringArrayProperty.valueType);
        }
    }
}