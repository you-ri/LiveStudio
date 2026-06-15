// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Newtonsoft.Json.Linq;
using Lilium.RemoteControl;
using Lilium.RemoteControl.LiveScene;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// Verifies live scene save/load across a host container plus merged sources
    /// (objects carried in from other scenes via RemoteControlContainer), including the
    /// invariant that a host with no sources serializes byte-identically to before.
    /// </summary>
    [TestFixture]
    public class MultiContainerSaveLoadTests
    {
        [Serializable]
        [ExposedClass("MultiContainerTestObject", Icon = "test")]
        public class TestObject : IExposedObject
        {
            private string _id;
            private ExposedObjectHandle? _handle;

            public TestObject(string id) { _id = id; }

            [ExposedField]
            public int value;

            public string name { get => _id; set => _id = value; }
            public string id => _id;
            public ExposedObjectHandle? exposedObject => _handle;

            public void OnEnable() { _handle = ExposedObjectRegistry.Create<TestObject>(this, _id); }
            public void OnDisable() { _handle?.Unregister(); _handle = null; }
            public void OnDispose() { }
            public void Update() { }
            public void Reset() { value = 0; }
        }

        [SetUp]
        public void SetUp()
        {
            ExposedClass.Clear();
            ExposedClass.RegisterFromAttributes<ExposedObjectContainer>();
            ExposedClass.RegisterFromAttributes<TestObject>();
            _ClearRegistry();
        }

        [TearDown]
        public void TearDown()
        {
            _ClearRegistry();
        }

        private static void _ClearRegistry()
        {
            foreach (var obj in ExposedObjectRegistry.instances.ToList())
                obj.Unregister();
        }

        [Test]
        public void BuildLiveSceneJson_MainOnly_UnaffectedByEmptySource()
        {
            var container = new ExposedObjectContainer("c", new List<IExposedObject> { new TestObject("main-1") });
            container.Initialize();

            var json1 = LiveSceneSerializer.BuildLiveSceneJson(container, "BaseScene");
            container.AddSource(new List<IExposedObject>(), new object());
            var json2 = LiveSceneSerializer.BuildLiveSceneJson(container, "BaseScene");

            // An empty source must not perturb the serialized bytes (backward compatibility).
            Assert.AreEqual(json1, json2);

            container.Shutdown();
        }

        [Test]
        public void BuildLiveSceneJson_IncludesSourceObjects()
        {
            var container = new ExposedObjectContainer("c", new List<IExposedObject>());
            container.Initialize();

            var owner = new object();
            var worldObj = new TestObject("world-1");
            container.AddSource(new List<IExposedObject> { worldObj }, owner);
            container.InitializeSource(owner); // captures default value = 0

            worldObj.value = 7; // dirty relative to default

            var json = LiveSceneSerializer.BuildLiveSceneJson(container, null);
            var objects = JObject.Parse(json)["objects"] as JArray;
            Assert.IsNotNull(objects);

            var entry = objects.FirstOrDefault(e => e["@source"]?.Value<string>() == "world-1");
            Assert.IsNotNull(entry, "Source object must be serialized into the single live scene file. JSON: " + json);
            Assert.AreEqual(7, entry["value"]?.Value<int>());

            container.Shutdown();
        }

        [Test]
        public void BuildLiveSceneJson_OrdersMainBeforeSources()
        {
            var container = new ExposedObjectContainer("c", new List<IExposedObject> { new TestObject("main-1") });
            container.Initialize();

            var owner = new object();
            container.AddSource(new List<IExposedObject> { new TestObject("world-1") }, owner);
            container.InitializeSource(owner);

            // Make both dirty so both appear.
            ((TestObject)container.objects[0]).value = 1;
            // world object value via registry lookup
            var world = container.FindById("world-1");
            Assert.IsNotNull(world);
            ((TestObject)world.Value.target).value = 2;

            var json = LiveSceneSerializer.BuildLiveSceneJson(container, null);
            var objects = JObject.Parse(json)["objects"] as JArray;

            var keys = objects.Select(e => e["@source"]?.Value<string>() ?? e["@id"]?.Value<string>()).ToList();
            int mainIndex = keys.IndexOf("main-1");
            int worldIndex = keys.IndexOf("world-1");
            Assert.GreaterOrEqual(mainIndex, 0);
            Assert.GreaterOrEqual(worldIndex, 0);
            Assert.Less(mainIndex, worldIndex, "Main objects must serialize before source objects (deterministic order).");

            container.Shutdown();
        }

        [Test]
        public void LiveSceneFromJson_SourceObject_AppliesValue()
        {
            var container = new ExposedObjectContainer("c", new List<IExposedObject>());
            container.Initialize();

            var owner = new object();
            var worldObj = new TestObject("world-1");
            container.AddSource(new List<IExposedObject> { worldObj }, owner);
            container.InitializeSource(owner);

            var json = @"{
                ""format"": ""jp.lilium.remotecontrol.live"",
                ""formatVersion"": 1,
                ""objects"": [
                    { ""@source"": ""world-1"", ""@type"": ""MultiContainerTestObject"", ""value"": 42 }
                ]
            }";

            LiveSceneSerializer.LiveSceneFromJson(json, container);

            Assert.AreEqual(42, worldObj.value, "Saved value must be applied to the merged source object.");

            container.Shutdown();
        }
    }
}
