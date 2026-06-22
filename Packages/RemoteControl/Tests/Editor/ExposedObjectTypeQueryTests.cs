// Copyright (c) You-Ri, 2026
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Lilium.RemoteControl;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// Regression tests for the <c>?type=X</c> resolution in
    /// <see cref="ExposedObjectHandler.CollectExposedObjects"/>.
    ///
    /// A scene GameObject can be exposed BOTH as a generic wrapper (e.g. an
    /// <see cref="ExposedGameObject"/> transform handle, registered with an id) AND carry a
    /// first-class [ExposedClass] component such as <c>AvatarController</c> ("Avatar"). These are
    /// distinct exposed identities. A <c>?type=Avatar</c> query must still surface the component
    /// even though its GameObject already has a registered wrapper.
    ///
    /// A prior over-eager de-duplication filtered the component out whenever its GameObject had any
    /// registered wrapper, which made the RemoteApp expression/avatar pages show
    /// "No avatars available" once the scene exposed "Main Avatar" as an ExposedGameObjectWithTransform.
    /// </summary>
    [TestFixture]
    public class ExposedObjectTypeQueryTests
    {
        private const string kAvatarTypeName = "TestAvatarTypeQuery";

        [ExposedClass(kAvatarTypeName, Category = "Avatar", Icon = "person")]
        public class TestAvatarComponent : MonoBehaviour
        {
            [ExposedField]
            public int value;
        }

        private readonly List<GameObject> _createdObjects = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            ExposedClass.Clear();
            // The component under test plus ExposedGameObject, whose constructor self-registers the
            // wrapper handle that reproduces the "Main Avatar" scene layout.
            ExposedClass.RegisterFromAttributes<TestAvatarComponent>();
            ExposedClass.RegisterFromAttributes<ExposedGameObject>();

            ClearRegistry();
        }

        [TearDown]
        public void TearDown()
        {
            ClearRegistry();

            foreach (var go in _createdObjects)
            {
                if (go != null) Object.DestroyImmediate(go);
            }
            _createdObjects.Clear();
        }

        private static void ClearRegistry()
        {
            foreach (var obj in ExposedObjectRegistry.instances.ToList())
            {
                obj.Unregister();
            }
        }

        private GameObject CreateGameObjectWithComponent(string name, out TestAvatarComponent component)
        {
            var go = new GameObject(name);
            component = go.AddComponent<TestAvatarComponent>();
            _createdObjects.Add(go);
            return go;
        }

        private static ExposedObjectContainer CreateEmptyContainer()
        {
            return new ExposedObjectContainer("test", new List<IExposedObject>());
        }

        [Test]
        public void TypeQuery_ComponentOnWrappedGameObject_IsStillReturned()
        {
            // Arrange: a GameObject carrying the component, also exposed via a registered
            // ExposedGameObject wrapper (mirrors "Main Avatar" + AvatarController).
            var go = CreateGameObjectWithComponent("Main Avatar", out var component);
            var wrapper = new ExposedGameObject(go); // self-registers a wrapper handle with an id

            // Precondition: the wrapper is a registered handle resolving to the same GameObject.
            Assert.IsTrue(
                ExposedObjectRegistry.instances.Any(h =>
                    h.hasId && ExposedObjectRegistry.ResolveGameObject(h.target) == go),
                "Test setup must register a wrapper handle for the component's GameObject.");
            Assert.IsNotNull(wrapper.exposedObject, "ExposedGameObject must register itself.");

            // Act
            var result = ExposedObjectHandler
                .CollectExposedObjects(CreateEmptyContainer(), kAvatarTypeName, null)
                .ToList();

            // Assert: the component is surfaced despite the GameObject also having a wrapper.
            Assert.AreEqual(1, result.Count(h => ReferenceEquals(h.target, component)),
                "A type query must return the component even when its GameObject has a registered wrapper.");
        }

        [Test]
        public void TypeQuery_ComponentWithoutWrapper_IsReturned()
        {
            // Arrange: plain component, no wrapper.
            CreateGameObjectWithComponent("PlainAvatar", out var component);

            // Act
            var result = ExposedObjectHandler
                .CollectExposedObjects(CreateEmptyContainer(), kAvatarTypeName, null)
                .ToList();

            // Assert
            Assert.AreEqual(1, result.Count(h => ReferenceEquals(h.target, component)),
                "A type query must return a component on an unwrapped GameObject.");
        }
    }
}
