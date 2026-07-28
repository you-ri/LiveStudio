// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Lilium.RemoteControl;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// Verifies that <see cref="ILiveDeserializeCallback.OnAfterLiveDeserialize"/> fires
    /// at the documented points: after a full <c>FromJson(string, LiveObjectHandle, ...)</c>,
    /// after deserialization of a nested <c>[LiveClass]</c>, and after the SetProperty path
    /// <c>FromJson(string, in LiveProperty, ...)</c> writes a single property.
    ///
    /// The SetProperty case is the regression coverage for the Phase 2.5 fix where a
    /// RemoteApp property update wrote shadow fields via reflection but never re-applied
    /// the change to external state (e.g. SkyboxBackground._ApplyTexture).
    /// </summary>
    [TestFixture]
    public class ILiveDeserializeCallbackTests
    {
        #region Test Classes

        /// <summary>Inner LiveClass that tracks its own callback invocations.</summary>
        [Serializable]
        [LiveClass("TestCallbackInner")]
        public class TestInner : ILiveDeserializeCallback
        {
            [LiveField]
            public int value;

            [NonSerialized]
            public int callbackCount;

            void ILiveDeserializeCallback.OnAfterLiveDeserialize()
            {
                callbackCount++;
            }
        }

        /// <summary>Outer LiveClass with a primitive field, a nested LiveClass field,
        /// and a tracker for its own callback invocations.</summary>
        [Serializable]
        [LiveClass("TestCallbackOuter")]
        public class TestOuter : ILiveDeserializeCallback
        {
            [LiveField]
            public int outerValue;

            [LiveField]
            public TestInner inner;

            [NonSerialized]
            public int callbackCount;

            void ILiveDeserializeCallback.OnAfterLiveDeserialize()
            {
                callbackCount++;
            }
        }

        /// <summary>LiveClass without ILiveDeserializeCallback. Used to verify the
        /// callback path tolerates targets that did not opt in.</summary>
        [Serializable]
        [LiveClass("TestCallbackNoOptIn")]
        public class TestNoOptIn
        {
            [LiveField]
            public int value;
        }

        #endregion

        private TestLiveObjectResolver _resolver;

        [SetUp]
        public void SetUp()
        {
            LiveClass.Clear();

            LiveClass.RegisterFromAttributes<TestInner>();
            LiveClass.RegisterFromAttributes<TestOuter>();
            LiveClass.RegisterFromAttributes<TestNoOptIn>();

            var toRemove = LiveObjectRegistry.instances.ToList();
            foreach (var obj in toRemove) obj.Unregister();

            _resolver = new TestLiveObjectResolver();
        }

        [TearDown]
        public void TearDown()
        {
            var toRemove = LiveObjectRegistry.instances.ToList();
            foreach (var obj in toRemove) obj.Unregister();
        }

        #region Full FromJson(string, LiveObjectHandle) — top-level deserialize

        [Test]
        public void FromJson_FullObject_FiresOwnerCallbackOnce()
        {
            var target = new TestOuter { outerValue = 0, inner = new TestInner { value = 0 } };
            var liveObj = new LiveObjectHandle("test-cb-1", LiveClass.Get<TestOuter>(), target);

            var json = "{\"outerValue\": 42, \"inner\": {\"value\": 7}}";
            LivePropertySerializer.FromJson(json, liveObj, _resolver);

            Assert.AreEqual(1, target.callbackCount,
                "Owner OnAfterLiveDeserialize should fire exactly once after a full FromJson.");
            Assert.AreEqual(42, target.outerValue);
        }

        [Test]
        public void FromJson_FullObject_FiresNestedCallback()
        {
            var target = new TestOuter { outerValue = 0, inner = new TestInner { value = 0 } };
            var liveObj = new LiveObjectHandle("test-cb-2", LiveClass.Get<TestOuter>(), target);

            var json = "{\"outerValue\": 1, \"inner\": {\"value\": 99}}";
            LivePropertySerializer.FromJson(json, liveObj, _resolver);

            Assert.GreaterOrEqual(target.inner.callbackCount, 1,
                "Nested LiveObjectHandle's OnAfterLiveDeserialize should fire when its data is deserialized.");
            Assert.AreEqual(99, target.inner.value);
        }

        #endregion

        #region SetProperty path — FromJson(string, in LiveProperty)

        [Test]
        public void FromJsonProperty_Primitive_FiresOwnerCallback()
        {
            // Phase 2.5 regression: SetProperty for a primitive bypasses the property setter
            // (field reflection write) so the owner needs the callback to re-apply state.
            var target = new TestOuter { outerValue = 10, inner = new TestInner { value = 0 } };
            var liveObj = new LiveObjectHandle("test-cb-3", LiveClass.Get<TestOuter>(), target);
            var prop = liveObj.FindProperty("outerValue");
            Assert.IsNotNull(prop, "outerValue property must be findable");

            var p = prop.Value;
            var ok = LivePropertySerializer.FromJson("{\"value\": 55}", in p, _resolver);

            Assert.IsTrue(ok, "FromJson should report a successful update");
            Assert.AreEqual(55, target.outerValue);
            Assert.AreEqual(1, target.callbackCount,
                "Owner OnAfterLiveDeserialize should fire after SetProperty on a primitive.");
        }

        [Test]
        public void FromJsonProperty_NestedLiveObject_FiresOwnerAndNestedCallbacks()
        {
            // The user-reported bug: updating a nested LiveObjectHandle (e.g.
            // SkyboxBackground._backgroundTexture as ExternalTexture) fires the nested
            // callback (Reload) but the owner's callback (_ApplyTexture) was missed.
            var target = new TestOuter { outerValue = 0, inner = new TestInner { value = 0 } };
            var liveObj = new LiveObjectHandle("test-cb-4", LiveClass.Get<TestOuter>(), target);
            var prop = liveObj.FindProperty("inner");
            Assert.IsNotNull(prop, "inner property must be findable");

            var p = prop.Value;
            var ok = LivePropertySerializer.FromJson("{\"value\": {\"value\": 77}}", in p, _resolver);

            Assert.IsTrue(ok);
            Assert.AreEqual(77, target.inner.value);
            Assert.GreaterOrEqual(target.inner.callbackCount, 1,
                "Nested LiveObjectHandle callback should fire after SetProperty on a nested object.");
            Assert.AreEqual(1, target.callbackCount,
                "Owner callback should fire after SetProperty on a nested object so the parent can re-apply.");
        }

        [Test]
        public void FromJsonProperty_NestedChild_FiresOwnerCallback()
        {
            // Path navigates into nested LiveObjectHandle (inner.value). Owner is still the outer.
            var target = new TestOuter { outerValue = 0, inner = new TestInner { value = 0 } };
            var liveObj = new LiveObjectHandle("test-cb-5", LiveClass.Get<TestOuter>(), target);
            var prop = liveObj.FindProperty("inner.value");
            Assert.IsNotNull(prop, "inner.value path must be findable");

            var p = prop.Value;
            var ok = LivePropertySerializer.FromJson("{\"value\": 33}", in p, _resolver);

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
            var liveObj = new LiveObjectHandle("test-cb-6", LiveClass.Get<TestOuter>(), target);
            var prop = liveObj.FindProperty("outerValue");
            Assert.IsNotNull(prop);

            var p = prop.Value;
            var ok = LivePropertySerializer.FromJson("{\"notValue\": 1}", in p, _resolver);

            Assert.IsFalse(ok, "FromJson without a 'value' field should return false");
            Assert.AreEqual(10, target.outerValue, "Field should not change on a failed update");
            Assert.AreEqual(0, target.callbackCount,
                "Owner callback should not fire on a no-op SetProperty update.");
        }

        [Test]
        public void FromJsonProperty_OwnerWithoutCallback_DoesNotThrow()
        {
            // Targets that did not opt in to ILiveDeserializeCallback must still work.
            var target = new TestNoOptIn { value = 1 };
            var liveObj = new LiveObjectHandle("test-cb-7", LiveClass.Get<TestNoOptIn>(), target);
            var prop = liveObj.FindProperty("value");
            Assert.IsNotNull(prop);

            var p = prop.Value;
            Assert.DoesNotThrow(() =>
            {
                LivePropertySerializer.FromJson("{\"value\": 9}", in p, _resolver);
            }, "Targets without ILiveDeserializeCallback must not throw on SetProperty");

            Assert.AreEqual(9, target.value);
        }

        #endregion
    }
}
