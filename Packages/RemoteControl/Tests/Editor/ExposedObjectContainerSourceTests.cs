// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Lilium.RemoteControl;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// Verifies that <see cref="ExposedObjectContainer"/> merges additional object lists registered
    /// as sources (e.g. RemoteControlContainer worlds) into enumeration, resolution and lifecycle.
    /// </summary>
    [TestFixture]
    public class ExposedObjectContainerSourceTests
    {
        [Serializable]
        [ExposedClass("ContainerSourceTestObject", Icon = "test")]
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
        public void EnumerateAllObjects_IncludesMainAndSource()
        {
            var main = new List<IExposedObject> { new TestObject("main-1") };
            var container = new ExposedObjectContainer("c", main);
            var owner = new object();
            container.AddSource(new List<IExposedObject> { new TestObject("src-1") }, owner);

            var ids = container.EnumerateAllObjects().Select(o => o.id).ToList();

            Assert.AreEqual(2, ids.Count);
            CollectionAssert.AreEqual(new[] { "main-1", "src-1" }, ids); // main first, then source
        }

        [Test]
        public void RemoveSource_DropsSourceObjects()
        {
            var container = new ExposedObjectContainer("c", new List<IExposedObject> { new TestObject("main-1") });
            var owner = new object();
            container.AddSource(new List<IExposedObject> { new TestObject("src-1") }, owner);

            container.RemoveSource(owner);

            var ids = container.EnumerateAllObjects().Select(o => o.id).ToList();
            CollectionAssert.AreEqual(new[] { "main-1" }, ids);
        }

        [Test]
        public void AddSource_IsIdempotentPerOwner()
        {
            var container = new ExposedObjectContainer("c", new List<IExposedObject>());
            var owner = new object();
            var list = new List<IExposedObject> { new TestObject("src-1") };

            container.AddSource(list, owner);
            container.AddSource(list, owner); // duplicate add for same owner is ignored

            Assert.AreEqual(1, container.EnumerateAllObjects().Count());
        }

        [Test]
        public void FindById_ResolvesThroughSourceList()
        {
            var container = new ExposedObjectContainer("c", new List<IExposedObject>());
            var obj = new TestObject("src-1");
            obj.OnEnable();
            // Remove from the global registry so resolution can only succeed via the source list.
            obj.exposedObject?.Unregister();

            container.AddSource(new List<IExposedObject> { obj }, new object());

            var hit = container.FindById("src-1");
            Assert.IsNotNull(hit);
            Assert.AreSame(obj, hit.Value.target);
        }

        [Test]
        public void InitializeSource_MarksSourceObjectsPersistent()
        {
            var container = new ExposedObjectContainer("c", new List<IExposedObject>());
            container.Initialize();

            var owner = new object();
            container.AddSource(new List<IExposedObject> { new TestObject("src-1") }, owner);
            Assert.IsFalse(container.IsPersistent("src-1"), "Not persistent until the source is initialized.");

            container.InitializeSource(owner);
            Assert.IsTrue(container.IsPersistent("src-1"));

            container.Shutdown();
        }

        [Test]
        public void ShutdownSource_RemovesPersistentAndUnregisters()
        {
            var container = new ExposedObjectContainer("c", new List<IExposedObject>());
            container.Initialize();

            var owner = new object();
            container.AddSource(new List<IExposedObject> { new TestObject("src-1") }, owner);
            container.InitializeSource(owner);

            container.ShutdownSource(owner);

            Assert.IsFalse(container.IsPersistent("src-1"));
            Assert.IsNull(ExposedObjectRegistry.FindById("src-1"), "Source object should be unregistered globally.");

            container.Shutdown();
        }

        [Test]
        public void UpdateObjects_TicksSourceObjects()
        {
            var container = new ExposedObjectContainer("c", new List<IExposedObject>());
            var owner = new object();
            var ticking = new TickCounter("tick-1");
            container.AddSource(new List<IExposedObject> { ticking }, owner);

            container.UpdateObjects();

            Assert.AreEqual(1, ticking.updateCount);
        }

        [Serializable]
        [ExposedClass("ContainerSourceTickCounter", Icon = "test")]
        public class TickCounter : IExposedObject
        {
            private string _id;
            public int updateCount;
            public TickCounter(string id) { _id = id; }
            public string name { get => _id; set => _id = value; }
            public string id => _id;
            public ExposedObjectHandle? exposedObject => null;
            public void OnEnable() { }
            public void OnDisable() { }
            public void OnDispose() { }
            public void Update() { updateCount++; }
            public void Reset() { }
        }
    }
}
