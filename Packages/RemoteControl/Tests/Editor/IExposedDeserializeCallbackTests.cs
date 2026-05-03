// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Lilium.RemoteControl;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// Verifies that <see cref="IExposedDeserializeCallback.OnAfterExposedDeserialize"/> fires
    /// at the documented points: after a full <c>FromJson(string, ExposedObject, ...)</c>,
    /// after deserialization of a nested <c>[ExposedClass]</c>, and after the SetProperty path
    /// <c>FromJson(string, in ExposedProperty, ...)</c> writes a single property.
    ///
    /// The SetProperty case is the regression coverage for the Phase 2.5 fix where a
    /// RemoteApp property update wrote shadow fields via reflection but never re-applied
    /// the change to external state (e.g. SkyboxBackground._ApplyTexture).
    /// </summary>
    [TestFixture]
    public class IExposedDeserializeCallbackTests
    {
        #region Test Classes

        /// <summary>Inner ExposedClass that tracks its own callback invocations.</summary>
        [Serializable]
        [ExposedClass("TestCallbackInner")]
        public class TestInner : IExposedDeserializeCallback
        {
            [ExposedField]
            public int value;

            [NonSerialized]
            public int callbackCount;

            void IExposedDeserializeCallback.OnAfterExposedDeserialize()
            {
                callbackCount++;
            }
        }

        /// <summary>Outer ExposedClass with a primitive field, a nested ExposedClass field,
        /// and a tracker for its own callback invocations.</summary>
        [Serializable]
        [ExposedClass("TestCallbackOuter")]
        public class TestOuter : IExposedDeserializeCallback
        {
            [ExposedField]
            public int outerValue;

            [ExposedField]
            public TestInner inner;

            [NonSerialized]
            public int callbackCount;

            void IExposedDeserializeCallback.OnAfterExposedDeserialize()
            {
                callbackCount++;
            }
        }

        /// <summary>ExposedClass without IExposedDeserializeCallback. Used to verify the
        /// callback path tolerates targets that did not opt in.</summary>
        [Serializable]
        [ExposedClass("TestCallbackNoOptIn")]
        public class TestNoOptIn
        {
            [ExposedField]
            public int value;
        }

        private class TestResolver : IExposedObjectResolver
        {
            public ExposedObject FindById(string id) => ExposedObjectRegistry.FindById(id);
            public ExposedObject FindByTarget(object target) => ExposedObjectRegistry.FindByTarget(target);
        }

        #endregion

        private TestResolver _resolver;

        [SetUp]
        public void SetUp()
        {
            ExposedClass.Clear();

            ExposedClass.RegisterFromAttributes<TestInner>();
            ExposedClass.RegisterFromAttributes<TestOuter>();
            ExposedClass.RegisterFromAttributes<TestNoOptIn>();

            var toRemove = ExposedObjectRegistry.instances.ToList();
            foreach (var obj in toRemove) obj.Unregister();

            _resolver = new TestResolver();
        }

        [TearDown]
        public void TearDown()
        {
            var toRemove = ExposedObjectRegistry.instances.ToList();
            foreach (var obj in toRemove) obj.Unregister();
        }

        #region Full FromJson(string, ExposedObject) — top-level deserialize

        [Test]
        public void FromJson_FullObject_FiresOwnerCallbackOnce()
        {
            var target = new TestOuter { outerValue = 0, inner = new TestInner { value = 0 } };
            var exposedObj = new ExposedObject("test-cb-1", ExposedClass.Get<TestOuter>(), target);

            var json = "{\"outerValue\": 42, \"inner\": {\"value\": 7}}";
            ExposedPropertySerializer.FromJson(json, exposedObj, _resolver);

            Assert.AreEqual(1, target.callbackCount,
                "Owner OnAfterExposedDeserialize should fire exactly once after a full FromJson.");
            Assert.AreEqual(42, target.outerValue);
        }

        [Test]
        public void FromJson_FullObject_FiresNestedCallback()
        {
            var target = new TestOuter { outerValue = 0, inner = new TestInner { value = 0 } };
            var exposedObj = new ExposedObject("test-cb-2", ExposedClass.Get<TestOuter>(), target);

            var json = "{\"outerValue\": 1, \"inner\": {\"value\": 99}}";
            ExposedPropertySerializer.FromJson(json, exposedObj, _resolver);

            Assert.GreaterOrEqual(target.inner.callbackCount, 1,
                "Nested ExposedObject's OnAfterExposedDeserialize should fire when its data is deserialized.");
            Assert.AreEqual(99, target.inner.value);
        }

        #endregion

        #region SetProperty path — FromJson(string, in ExposedProperty)

        [Test]
        public void FromJsonProperty_Primitive_FiresOwnerCallback()
        {
            // Phase 2.5 regression: SetProperty for a primitive bypasses the property setter
            // (field reflection write) so the owner needs the callback to re-apply state.
            var target = new TestOuter { outerValue = 10, inner = new TestInner { value = 0 } };
            var exposedObj = new ExposedObject("test-cb-3", ExposedClass.Get<TestOuter>(), target);
            var prop = exposedObj.FindProperty("outerValue");
            Assert.IsNotNull(prop, "outerValue property must be findable");

            var p = prop.Value;
            var ok = ExposedPropertySerializer.FromJson("{\"value\": 55}", in p, _resolver);

            Assert.IsTrue(ok, "FromJson should report a successful update");
            Assert.AreEqual(55, target.outerValue);
            Assert.AreEqual(1, target.callbackCount,
                "Owner OnAfterExposedDeserialize should fire after SetProperty on a primitive.");
        }

        [Test]
        public void FromJsonProperty_NestedExposedObject_FiresOwnerAndNestedCallbacks()
        {
            // The user-reported bug: updating a nested ExposedObject (e.g.
            // SkyboxBackground._backgroundTexture as ExternalTexture) fires the nested
            // callback (Reload) but the owner's callback (_ApplyTexture) was missed.
            var target = new TestOuter { outerValue = 0, inner = new TestInner { value = 0 } };
            var exposedObj = new ExposedObject("test-cb-4", ExposedClass.Get<TestOuter>(), target);
            var prop = exposedObj.FindProperty("inner");
            Assert.IsNotNull(prop, "inner property must be findable");

            var p = prop.Value;
            var ok = ExposedPropertySerializer.FromJson("{\"value\": {\"value\": 77}}", in p, _resolver);

            Assert.IsTrue(ok);
            Assert.AreEqual(77, target.inner.value);
            Assert.GreaterOrEqual(target.inner.callbackCount, 1,
                "Nested ExposedObject callback should fire after SetProperty on a nested object.");
            Assert.AreEqual(1, target.callbackCount,
                "Owner callback should fire after SetProperty on a nested object so the parent can re-apply.");
        }

        [Test]
        public void FromJsonProperty_NestedChild_FiresOwnerCallback()
        {
            // Path navigates into nested ExposedObject (inner.value). Owner is still the outer.
            var target = new TestOuter { outerValue = 0, inner = new TestInner { value = 0 } };
            var exposedObj = new ExposedObject("test-cb-5", ExposedClass.Get<TestOuter>(), target);
            var prop = exposedObj.FindProperty("inner.value");
            Assert.IsNotNull(prop, "inner.value path must be findable");

            var p = prop.Value;
            var ok = ExposedPropertySerializer.FromJson("{\"value\": 33}", in p, _resolver);

            Assert.IsTrue(ok);
            Assert.AreEqual(33, target.inner.value);
            Assert.AreEqual(1, target.callbackCount,
                "Owner callback should fire when SetProperty writes into a nested member.");
        }

        [Test]
        public void FromJsonProperty_FailedUpdate_DoesNotFireOwnerCallback()
        {
            // FromJson returns false when the JSON has no "value" token (and the field
            // is not a UnityEngine.Object that allows null). The callback should NOT fire
            // in that no-op case, since nothing actually changed on the target.
            var target = new TestOuter { outerValue = 10, inner = new TestInner { value = 0 } };
            var exposedObj = new ExposedObject("test-cb-6", ExposedClass.Get<TestOuter>(), target);
            var prop = exposedObj.FindProperty("outerValue");
            Assert.IsNotNull(prop);

            var p = prop.Value;
            var ok = ExposedPropertySerializer.FromJson("{\"notValue\": 1}", in p, _resolver);

            Assert.IsFalse(ok, "FromJson without a 'value' field should return false");
            Assert.AreEqual(10, target.outerValue, "Field should not change on a failed update");
            Assert.AreEqual(0, target.callbackCount,
                "Owner callback should not fire on a no-op SetProperty update.");
        }

        [Test]
        public void FromJsonProperty_OwnerWithoutCallback_DoesNotThrow()
        {
            // Targets that did not opt in to IExposedDeserializeCallback must still work.
            var target = new TestNoOptIn { value = 1 };
            var exposedObj = new ExposedObject("test-cb-7", ExposedClass.Get<TestNoOptIn>(), target);
            var prop = exposedObj.FindProperty("value");
            Assert.IsNotNull(prop);

            var p = prop.Value;
            Assert.DoesNotThrow(() =>
            {
                ExposedPropertySerializer.FromJson("{\"value\": 9}", in p, _resolver);
            }, "Targets without IExposedDeserializeCallback must not throw on SetProperty");

            Assert.AreEqual(9, target.value);
        }

        #endregion
    }
}
