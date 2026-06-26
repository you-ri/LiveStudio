// Copyright (c) You-Ri, 2026
using System;
using System.Linq;
using NUnit.Framework;
using Lilium.RemoteControl;
using Lilium.RemoteControl.Reflection;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// Verifies that a property path can address an array element by its [ExposedKey] value
    /// ("entries[Beta].weight") instead of by index, and that the binding follows the keyed element
    /// across reordering. The key→index resolution happens in the ExposedProperty graph layer, so the
    /// resolved property is index-based and get/set behave exactly like an index path.
    /// </summary>
    [TestFixture]
    public class ExposedKeyPathTests
    {
        [Serializable]
        [ExposedClass("KeyedEntry")]
        public class KeyedEntry
        {
            private string _key = string.Empty;

            public KeyedEntry() { }

            public KeyedEntry(string key, float weight)
            {
                _key = key ?? string.Empty;
                this.weight = weight;
            }

            [ExposedProperty, ExposedKey]
            public string key => _key;

            [ExposedProperty]
            public float weight { get; set; }
        }

        [Serializable]
        [ExposedClass("KeyedContainer")]
        public class KeyedContainer
        {
            [ExposedField]
            public KeyedEntry[] entries;
        }

        private KeyedContainer _instance;
        private ExposedObjectHandle _handle;

        [SetUp]
        public void SetUp()
        {
            ExposedClass.Clear();
            ExposedClass.RegisterFromAttributes<KeyedEntry>();
            ExposedClass.RegisterFromAttributes<KeyedContainer>();

            foreach (var obj in ExposedObjectRegistry.instances.ToList()) obj.Unregister();

            _instance = new KeyedContainer
            {
                entries = new[]
                {
                    new KeyedEntry("Alpha", 0.1f),
                    new KeyedEntry("Beta", 0.2f),
                    new KeyedEntry("Gamma", 0.3f),
                }
            };
            _handle = ExposedObjectRegistry.Create(_instance, "keyed_container").Value;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in ExposedObjectRegistry.instances.ToList()) obj.Unregister();
            ExposedClass.Clear();
        }

        [Test]
        public void FindProperty_ByKeyBracket_ResolvesElement()
        {
            var p = _handle.FindProperty("entries[Beta].weight");
            Assert.IsNotNull(p, "the bracket key form resolves");
            Assert.AreEqual(0.2f, p.Value.GetValue());
        }

        [Test]
        public void FindProperty_ByKeyDot_ResolvesElement()
        {
            // ToSlash/FromSlash round-trip lands on the dot form; it must resolve identically.
            var p = _handle.FindProperty("entries.Gamma.weight");
            Assert.IsNotNull(p, "the dot key form resolves");
            Assert.AreEqual(0.3f, p.Value.GetValue());
        }

        [Test]
        public void SetValue_ByKey_WritesThroughToInstance()
        {
            var p = _handle.FindProperty("entries[Beta].weight");
            p.Value.SetValue(0.9f);
            Assert.AreEqual(0.9f, _instance.entries[1].weight, "the matched element is written through");
        }

        [Test]
        public void FindProperty_UnknownKey_ReturnsNull()
        {
            Assert.IsNull(_handle.FindProperty("entries[Zzz].weight"));
        }

        [Test]
        public void FindProperty_ByKey_SurvivesReorder()
        {
            var beta = _instance.entries[1];

            var before = _handle.FindProperty("entries[Beta].weight");
            before.Value.SetValue(0.4f);
            Assert.AreEqual(0.4f, beta.weight);

            // Move Beta from index 1 to index 0 so its index genuinely changes.
            (_instance.entries[0], _instance.entries[1]) = (_instance.entries[1], _instance.entries[0]);
            Assert.AreSame(beta, _instance.entries[0], "Beta is now at index 0");

            var after = _handle.FindProperty("entries[Beta].weight");
            after.Value.SetValue(0.8f);
            Assert.AreEqual(0.8f, beta.weight, "the key resolves to the same Beta element regardless of index");
        }

        [Test]
        public void PropertyPath_KeyRoundTrip_SlashAndDotBracket()
        {
            // A key segment survives the REST slash conversion (lands on dot form) and both forms parse.
            Assert.AreEqual("entries/Beta/weight", new PropertyPath("entries[Beta].weight").ToSlash());
            Assert.AreEqual("entries.Beta.weight", PropertyPath.FromSlash("entries/Beta/weight").Value);
        }
    }
}
