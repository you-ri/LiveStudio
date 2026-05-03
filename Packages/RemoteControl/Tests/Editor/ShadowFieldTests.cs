// Copyright (c) You-Ri, 2026
using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Newtonsoft.Json.Linq;
using Lilium.RemoteControl;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// Verifies the Shadow Field pattern: a Property "X" with a sibling
    /// `[ExposedField, Hide][FormerlyExposedAs("X")]` Field "_X" is collapsed into
    /// a single propertyTypes entry. The Property is the public API surface for
    /// RemoteApp; the Field is the internal storage used by the JSON serializer
    /// to bypass the Property setter during deserialize (preserves round-trip
    /// determinism while leaving the setter as the source of truth for runtime
    /// SetProperty updates).
    /// </summary>
    [TestFixture]
    public class ShadowFieldTests
    {
        #region Test Classes

        /// <summary>
        /// Property + Shadow Field shape, mirroring the Phase 1〜2 pattern used in
        /// CaptureCameraController.lockRoll and SkyboxBackground.backgroundMode.
        /// </summary>
        [Serializable]
        [ExposedClass("TestShadowFieldHost")]
        public class TestHost
        {
            [SerializeField, ExposedField, Hide]
            [FormerlyExposedAs("value")]
            private int _value = 0;

            [ExposedProperty]
            public int value
            {
                get => _value;
                set
                {
                    _value = value;
                    setterCallCount++;
                }
            }

            [NonSerialized]
            public int setterCallCount;

            // For tests that need direct backing field inspection.
            public int rawBackingField => _value;
            public void SetBackingFieldDirectly(int v) => _value = v;
        }

        /// <summary>
        /// Plain Property + Field with no Shadow relationship: Field has no [Hide] so it
        /// stays as an independent UI-visible property. Used to verify that detection
        /// only triggers on the documented Shadow shape.
        /// </summary>
        [Serializable]
        [ExposedClass("TestNonShadowHost")]
        public class TestNonShadowHost
        {
            [SerializeField, ExposedField]    // No [Hide] -> not a shadow
            [FormerlyExposedAs("value")]
            public int _value;

            [ExposedProperty]
            public int value { get => _value; set => _value = value; }
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
            ExposedClass.RegisterFromAttributes<TestHost>();
            ExposedClass.RegisterFromAttributes<TestNonShadowHost>();

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

        #region Detection / propertyTypes registration

        [Test]
        public void Shadow_PropertyTypesContainsOnlyPropertyEntry()
        {
            var ec = ExposedClass.Get<TestHost>();
            var entries = ec.propertyTypes;

            Assert.AreEqual(1, entries.Length,
                "propertyTypes should contain exactly one entry for the Property; the Shadow Field must not register a separate entry.");
            Assert.AreEqual("value", entries[0].name);
            Assert.IsNotNull(entries[0].properyInfo,
                "The single entry must be the Property, not the Field.");
            Assert.IsNotNull(entries[0].shadowField,
                "The Property entry must carry a reference to its shadow field.");
            Assert.AreEqual("_value", entries[0].shadowField.Name);
        }

        [Test]
        public void Shadow_PropertyInheritsFieldPersistability()
        {
            var ec = ExposedClass.Get<TestHost>();
            var entry = ec.propertyTypes[0];

            // The Property has no [Persistable] but the Shadow Field defaults to persistable=true.
            // Field's persistable must be propagated to the Property.
            Assert.IsTrue(entry.isPersistable,
                "Property should inherit isPersistable=true from the shadow field even without [Persistable] on the Property.");
        }

        [Test]
        public void Shadow_FindProperty_ByPropertyName_ReturnsProperty()
        {
            var target = new TestHost();
            var exposedObj = new ExposedObject("test-shadow-1", ExposedClass.Get<TestHost>(), target);
            var prop = exposedObj.FindProperty("value");
            Assert.IsNotNull(prop);
            Assert.IsNotNull(prop.Value.type.properyInfo,
                "FindProperty(\"value\") must resolve to the Property, not a field.");
        }

        [Test]
        public void Shadow_FindProperty_ByFieldName_ResolvesToPropertyViaFormerNames()
        {
            // The Field's member name "_value" must be auto-added as a former alias of the
            // Property so existing scene.json files (Phase 1〜2 era) that use "_value" as the
            // JSON key still resolve to a usable property.
            var target = new TestHost();
            var exposedObj = new ExposedObject("test-shadow-2", ExposedClass.Get<TestHost>(), target);
            var prop = exposedObj.FindProperty("_value");

            Assert.IsNotNull(prop, "Field name should also resolve to the Property via formerNames.");
            Assert.IsNotNull(prop.Value.type.properyInfo,
                "FindProperty(\"_value\") must still resolve to the Property entry, not a separate Field entry.");
        }

        [Test]
        public void NonShadow_FieldWithoutHide_StaysAsSeparateEntry()
        {
            // [ExposedField] without [Hide] is NOT a shadow even with [FormerlyExposedAs("value")].
            // Both Property and Field should remain as independent propertyTypes entries.
            var ec = ExposedClass.Get<TestNonShadowHost>();
            var entries = ec.propertyTypes;

            Assert.AreEqual(2, entries.Length,
                "Without [Hide] on the Field, both Property and Field should be independent entries.");
            Assert.IsTrue(entries.Any(e => e.properyInfo?.Name == "value"));
            Assert.IsTrue(entries.Any(e => e.fieldInfo?.Name == "_value"));
        }

        #endregion

        #region SetProperty path (RemoteApp) -> Property setter fires

        [Test]
        public void Shadow_FromJsonProperty_FiresPropertySetter()
        {
            // Phase 3 goal: SetProperty via RemoteApp routes through the Property setter so
            // its side effects (Apply etc.) run automatically without needing a callback.
            var target = new TestHost();
            var exposedObj = new ExposedObject("test-shadow-3", ExposedClass.Get<TestHost>(), target);
            var prop = exposedObj.FindProperty("value");
            Assert.IsNotNull(prop);

            var p = prop.Value;
            ExposedPropertySerializer.FromJson("{\"value\": 42}", in p, _resolver);

            Assert.AreEqual(42, target.rawBackingField);
            Assert.AreEqual(1, target.setterCallCount,
                "Property setter must be called exactly once on SetProperty (so side effects fire).");
        }

        #endregion

        #region Full FromJson (scene load) -> Property setter bypassed

        [Test]
        public void Shadow_FullFromJson_BypassesPropertySetter()
        {
            // Phase 1 round-trip determinism goal: full FromJson writes via the shadow field
            // directly, so the Property setter does NOT fire during deserialize.
            var target = new TestHost();
            var exposedObj = new ExposedObject("test-shadow-4", ExposedClass.Get<TestHost>(), target);

            ExposedPropertySerializer.FromJson("{\"value\": 99}", exposedObj, _resolver);

            Assert.AreEqual(99, target.rawBackingField,
                "Field should be written via shadow field reflection.");
            Assert.AreEqual(0, target.setterCallCount,
                "Property setter must be bypassed during full FromJson deserialize (round-trip determinism).");
        }

        [Test]
        public void Shadow_FullFromJson_LoadsLegacyFieldNameKey()
        {
            // Backward compat: scene.json saved during Phase 1〜2 used the Field's member
            // name "_value" as the JSON key. New code must still load those files.
            var target = new TestHost();
            var exposedObj = new ExposedObject("test-shadow-5", ExposedClass.Get<TestHost>(), target);

            ExposedPropertySerializer.FromJson("{\"_value\": 77}", exposedObj, _resolver);

            Assert.AreEqual(77, target.rawBackingField,
                "Legacy '_value' JSON key must still load via formerNames fallback.");
        }

        #endregion

        #region Serialize uses Property name as JSON key

        [Test]
        public void Shadow_Serialize_WritesPropertyNameAsJsonKey()
        {
            // New JSON output uses the Property name "value", not the legacy field name "_value".
            var target = new TestHost();
            target.SetBackingFieldDirectly(123);
            var exposedObj = new ExposedObject("test-shadow-6", ExposedClass.Get<TestHost>(), target);

            var json = ExposedPropertySerializer.ToJson(exposedObj, _resolver);
            var parsed = JObject.Parse(json);

            Assert.IsNotNull(parsed["value"], "Output JSON must use Property name 'value' as the key.");
            Assert.AreEqual(123, parsed["value"].Value<int>());
            Assert.IsNull(parsed["_value"], "Output JSON must not use the field name '_value'.");
        }

        #endregion
    }
}
