// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Lilium.RemoteControl;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// Verifies that <see cref="LiveObjectContainer"/> merges additional object lists registered
    /// as sources (e.g. RemoteControlContainer worlds) into enumeration, resolution and lifecycle.
    /// </summary>
    [TestFixture]
    public class LiveObjectContainerSourceTests
    {
        [Serializable]
        [LiveClass("ContainerSourceTestObject", Icon = "test")]
        public class TestObject : ILiveObject
        {
            private string _id;
            private LiveObjectHandle? _handle;

            public TestObject(string id) { _id = id; }

            [LiveField]
            public int value;

            public string name { get => _id; set => _id = value; }
            public string id => _id;
            public LiveObjectHandle? liveObject => _handle;

            public void OnEnable() { _handle = LiveObjectRegistry.Create<TestObject>(this, _id); }
            public void OnDisable() { _handle?.Unregister(); _handle = null; }
            public void OnDispose() { }
            public void Update() { }
            public void Reset() { value = 0; }
        }

        [SetUp]
        public void SetUp()
        {
            LiveClass.Clear();
            LiveClass.RegisterFromAttributes<LiveObjectContainer>();
            LiveClass.RegisterFromAttributes<TestObject>();
            _ClearRegistry();
        }

        [TearDown]
        public void TearDown()
        {
            _ClearRegistry();
        }

        private static void _ClearRegistry()
        {
            foreach (var obj in LiveObjectRegistry.instances.ToList())
                obj.Unregister();
        }

        [Test]
        public void EnumerateAllObjects_IncludesMainAndSource()
        {
            var main = new List<ILiveObject> { new TestObject("main-1") };
            var container = new LiveObjectContainer("c", main);
            var owner = new object();
            container.AddSource(new List<ILiveObject> { new TestObject("src-1") }, owner);

            var ids = container.EnumerateAllObjects().Select(o => o.id).ToList();

            Assert.AreEqual(2, ids.Count);
            CollectionAssert.AreEqual(new[] { "main-1", "src-1" }, ids); // main first, then source
        }

        [Test]
        public void RemoveSource_DropsSourceObjects()
        {
            var container = new LiveObjectContainer("c", new List<ILiveObject> { new TestObject("main-1") });
            var owner = new object();
            container.AddSource(new List<ILiveObject> { new TestObject("src-1") }, owner);

            container.RemoveSource(owner);

            var ids = container.EnumerateAllObjects().Select(o => o.id).ToList();
            CollectionAssert.AreEqual(new[] { "main-1" }, ids);
        }

        [Test]
        public void AddSource_IsIdempotentPerOwner()
        {
            var container = new LiveObjectContainer("c", new List<ILiveObject>());
            var owner = new object();
            var list = new List<ILiveObject> { new TestObject("src-1") };

            container.AddSource(list, owner);
            container.AddSource(list, owner); // duplicate add for same owner is ignored

            Assert.AreEqual(1, container.EnumerateAllObjects().Count());
        }

        [Test]
        public void FindById_ResolvesThroughSourceList()
        {
            var container = new LiveObjectContainer("c", new List<ILiveObject>());
            var obj = new TestObject("src-1");
            obj.OnEnable();
            // Remove from the global registry so resolution can only succeed via the source list.
            obj.liveObject?.Unregister();

            container.AddSource(new List<ILiveObject> { obj }, new object());

            var hit = container.FindById("src-1");
            Assert.IsNotNull(hit);
            Assert.AreSame(obj, hit.Value.target);
        }

        [Test]
        public void InitializeSource_MarksSourceObjectsPersistent()
        {
            var container = new LiveObjectContainer("c", new List<ILiveObject>());
            container.Initialize();

            var owner = new object();
            container.AddSource(new List<ILiveObject> { new TestObject("src-1") }, owner);
            Assert.IsFalse(container.IsPersistent("src-1"), "Not persistent until the source is initialized.");

            container.InitializeSource(owner);
            Assert.IsTrue(container.IsPersistent("src-1"));

            container.Shutdown();
        }

        [Test]
        public void ShutdownSource_RemovesPersistentAndUnregisters()
        {
            var container = new LiveObjectContainer("c", new List<ILiveObject>());
            container.Initialize();

            var owner = new object();
            container.AddSource(new List<ILiveObject> { new TestObject("src-1") }, owner);
            container.InitializeSource(owner);

            container.ShutdownSource(owner);

            Assert.IsFalse(container.IsPersistent("src-1"));
            Assert.IsNull(LiveObjectRegistry.FindById("src-1"), "Source object should be unregistered globally.");

            container.Shutdown();
        }

        [Test]
        public void UpdateObjects_TicksSourceObjects()
        {
            var container = new LiveObjectContainer("c", new List<ILiveObject>());
            var owner = new object();
            var ticking = new TickCounter("tick-1");
            container.AddSource(new List<ILiveObject> { ticking }, owner);

            container.UpdateObjects();

            Assert.AreEqual(1, ticking.updateCount);
        }

        [Serializable]
        [LiveClass("ContainerSourceTickCounter", Icon = "test")]
        public class TickCounter : ILiveObject
        {
            private string _id;
            public int updateCount;
            public TickCounter(string id) { _id = id; }
            public string name { get => _id; set => _id = value; }
            public string id => _id;
            public LiveObjectHandle? liveObject => null;
            public void OnEnable() { }
            public void OnDisable() { }
            public void OnDispose() { }
            public void Update() { updateCount++; }
            public void Reset() { }
        }
    }
}
