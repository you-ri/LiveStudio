// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Lilium.RemoteControl;
using Newtonsoft.Json.Linq;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// Byte-exact snapshot ("golden") pins for the object-level serialization surface of
    /// <see cref="ExposedPropertySerializer"/>. A single fixture exercises every branch the
    /// serializer refactor / GC work touches (metadata, full, delta, arrays, Unity scalar types,
    /// enums, scope filtering) and asserts the produced JSON string verbatim.
    ///
    /// The purpose is to guarantee the REST-invariance boundary: the JSON emitted for GET / SSE /
    /// scene persistence / project persistence must stay byte-for-byte identical across the
    /// refactor. Exotic members (@ref, [RawJson], shadow fields, [FormerlyExposedAs]) keep their
    /// own dedicated behavioural tests (RawJsonSerializationTests, ShadowFieldTests,
    /// FormerlyExposedAsTests, ...); this file focuses on the byte-stable common surface.
    ///
    /// The live.json / project settings wrapping (FormatHeader + object map) lives in
    /// LiveSceneSerializer / ProjectSettingsSerializer, which the refactor does not touch and which
    /// ExposedPropertySceneSerializationTests / ProjectScopeSerializationTests already cover; the
    /// scope-filtered per-object output pinned here is the tightest net around the refactored code.
    /// </summary>
    [TestFixture]
    public class GoldenSerializationTests
    {
        public enum GoldenEnum { Alpha, Beta, Gamma }

        [Serializable]
        [ExposedClass("GoldenNested")]
        public class GoldenNested
        {
            [ExposedField] public int id;
            [ExposedField] public string label;
        }

        [Serializable]
        [ExposedClass("GoldenFixture")]
        public class GoldenFixture
        {
            [ExposedField] public int intValue;
            [ExposedField] public float floatValue;
            [ExposedField] public bool boolValue;
            [ExposedField] public string stringValue;
            [ExposedField] public double doubleValue;
            [ExposedField] public Vector2 vector2Value;
            [ExposedField] public Vector3 vector3Value;
            [ExposedField] public Color colorValue;
            [ExposedField] public Quaternion quaternionValue;
            [ExposedField] public Rect rectValue;
            [ExposedField] public GoldenEnum enumValue;
            [ExposedField] public int[] intArray;
            [ExposedField] public string[] stringArray;
            [ExposedField] public List<int> intList;
            [ExposedField] public GoldenNested nested;
            [ExposedField(persistScope = PersistScope.Project)] public int projectValue;
        }

        private TestExposedObjectResolver _resolver;

        [SetUp]
        public void SetUp()
        {
            ExposedClass.Clear();
            foreach (var obj in ExposedObjectRegistry.instances.ToList())
            {
                obj.Unregister();
            }
            _resolver = new TestExposedObjectResolver();
            ExposedClass.RegisterFromAttributes<GoldenNested>();
            ExposedClass.RegisterFromAttributes<GoldenFixture>();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in ExposedObjectRegistry.instances.ToList())
            {
                obj.Unregister();
            }
        }

        // Fixed, format-stable values (no repeating binary fractions) so the JSON is deterministic.
        private ExposedObjectHandle _CreateFixture(string id)
        {
            var target = new GoldenFixture
            {
                intValue = 7,
                floatValue = 1.5f,
                boolValue = true,
                stringValue = "hello",
                doubleValue = 2.25,
                vector2Value = new Vector2(1.5f, 2.5f),
                vector3Value = new Vector3(1.5f, 2.5f, 3.5f),
                colorValue = new Color(0.5f, 0.25f, 0.75f, 1f),
                quaternionValue = new Quaternion(0f, 0f, 0f, 1f),
                rectValue = new Rect(1f, 2f, 3f, 4f),
                enumValue = GoldenEnum.Beta,
                intArray = new[] { 1, 2, 3 },
                stringArray = new[] { "a", "b" },
                intList = new List<int> { 10, 20 },
                nested = new GoldenNested { id = 99, label = "inner" },
                projectValue = 42,
            };
            var exposedClass = ExposedClass.Find(typeof(GoldenFixture));
            return new ExposedObjectHandle(id, exposedClass, target);
        }

        // Golden strings captured from the current implementation. Any byte difference after a
        // refactor is a REST-invariance violation and must be investigated, not re-baked.
        private const string kFull =
            @"{""@type"":""GoldenFixture"",""@id"":""golden-1"",""@name"":""golden-1"",""intValue"":7,""floatValue"":1.5,""boolValue"":true,""stringValue"":""hello"",""doubleValue"":2.25,""vector2Value"":{""x"":1.5,""y"":2.5},""vector3Value"":{""x"":1.5,""y"":2.5,""z"":3.5},""colorValue"":{""r"":0.5,""g"":0.25,""b"":0.75,""a"":1.0},""quaternionValue"":{""x"":0.0,""y"":0.0,""z"":0.0,""w"":1.0},""rectValue"":{""x"":1.0,""y"":2.0,""width"":3.0,""height"":4.0},""enumValue"":""Beta"",""intArray"":[1,2,3],""stringArray"":[""a"",""b""],""intList"":[10,20],""nested"":{""@type"":""GoldenNested"",""id"":99,""label"":""inner""},""projectValue"":42}";

        private const string kDeltaClean =
            @"{""@type"":""GoldenFixture"",""@id"":""golden-1"",""@name"":""golden-1"",""projectValue"":42}";

        private const string kScene =
            @"{""@type"":""GoldenFixture"",""@id"":""golden-1"",""intValue"":7,""floatValue"":1.5,""boolValue"":true,""stringValue"":""hello"",""doubleValue"":2.25,""vector2Value"":{""x"":1.5,""y"":2.5},""vector3Value"":{""x"":1.5,""y"":2.5,""z"":3.5},""colorValue"":{""r"":0.5,""g"":0.25,""b"":0.75,""a"":1.0},""quaternionValue"":{""x"":0.0,""y"":0.0,""z"":0.0,""w"":1.0},""rectValue"":{""x"":1.0,""y"":2.0,""width"":3.0,""height"":4.0},""enumValue"":""Beta"",""intArray"":[1,2,3],""stringArray"":[""a"",""b""],""intList"":[10,20],""nested"":{""@type"":""GoldenNested"",""id"":99,""label"":""inner""}}";

        private const string kProject =
            @"{""@type"":""GoldenFixture"",""@id"":""golden-1"",""projectValue"":42}";

        private const string kDeltaDirty =
            @"{""@type"":""GoldenFixture"",""@id"":""golden-2"",""@name"":""golden-2"",""intValue"":123,""stringValue"":""world"",""nested"":{""@type"":""GoldenNested"",""label"":""changed""},""projectValue"":42}";

        [Test]
        public void RestFull_MatchesGolden()
        {
            var obj = _CreateFixture("golden-1");
            Assert.AreEqual(kFull, ExposedPropertySerializer.ToJson(obj, _resolver));
        }

        [Test]
        public void RestDeltaClean_MatchesGolden()
        {
            // No dirty members: only force-included untracked (project-scoped) properties appear.
            var obj = _CreateFixture("golden-1");
            Assert.AreEqual(kDeltaClean, ExposedPropertySerializer.ToJson(obj, _resolver, isDirtyOnly: true));
        }

        [Test]
        public void ScenePersistence_MatchesGolden()
        {
            var obj = _CreateFixture("golden-1");
            var scene = ExposedPropertySerializer.ToJson(
                obj, _resolver, isDirtyOnly: false, forPersistence: true, scopeFilter: PersistScope.Scene);
            Assert.AreEqual(kScene, scene);
        }

        [Test]
        public void ProjectPersistence_MatchesGolden()
        {
            var obj = _CreateFixture("golden-1");
            var project = ExposedPropertySerializer.ToJson(
                obj, _resolver, isDirtyOnly: false, forPersistence: true, scopeFilter: PersistScope.Project);
            Assert.AreEqual(kProject, project);
        }

        [Test]
        public void RestDeltaDirty_MatchesGolden()
        {
            var obj = _CreateFixture("golden-2");
            obj.FindProperty("intValue").Value.SetValue(123);
            obj.FindProperty("nested.label").Value.SetValue("changed");
            obj.FindProperty("stringValue").Value.SetValue("world");
            Assert.AreEqual(kDeltaDirty, ExposedPropertySerializer.ToJson(obj, _resolver, isDirtyOnly: true));
        }

        [Test]
        public void ScenePersistence_RoundTrips()
        {
            var obj = _CreateFixture("golden-1");
            var scene = ExposedPropertySerializer.ToJson(
                obj, _resolver, isDirtyOnly: false, forPersistence: true, scopeFilter: PersistScope.Scene);

            // Deserialize the persisted form into a fresh object and re-serialize; must be identical.
            var freshTarget = new GoldenFixture();
            var freshHandle = new ExposedObjectHandle(
                "golden-1", ExposedClass.Find(typeof(GoldenFixture)), freshTarget);
            ExposedPropertySerializer.FromJson(scene, freshHandle, _resolver);

            var reSerialized = ExposedPropertySerializer.ToJson(
                freshHandle, _resolver, isDirtyOnly: false, forPersistence: true, scopeFilter: PersistScope.Scene);
            Assert.AreEqual(kScene, reSerialized);
        }
    }
}
