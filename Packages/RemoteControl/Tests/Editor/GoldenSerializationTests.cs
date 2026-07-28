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
    /// <see cref="LivePropertySerializer"/>. A single fixture exercises every branch the
    /// serializer refactor / GC work touches (metadata, full, delta, arrays, Unity scalar types,
    /// enums, scope filtering) and asserts the produced JSON string verbatim.
    ///
    /// The purpose is to guarantee the REST-invariance boundary: the JSON emitted for GET /
    /// scene persistence / project persistence must stay byte-for-byte identical across the
    /// refactor. Exotic members (@ref, [RawJson], shadow fields, [FormerlyNamedAs]) keep their
    /// own dedicated behavioural tests (RawJsonSerializationTests, ShadowFieldTests,
    /// FormerlyLiveAsTests, ...); this file focuses on the byte-stable common surface.
    ///
    /// The live.json / project settings wrapping (FormatHeader + object map) lives in
    /// LiveSceneSerializer / ProjectSettingsSerializer, which the refactor does not touch and which
    /// LivePropertySceneSerializationTests / ProjectScopeSerializationTests already cover; the
    /// scope-filtered per-object output pinned here is the tightest net around the refactored code.
    /// </summary>
    [TestFixture]
    public class GoldenSerializationTests
    {
        public enum GoldenEnum { Alpha, Beta, Gamma }

        [Serializable]
        [LiveClass("GoldenNested")]
        public class GoldenNested
        {
            [LiveField] public int id;
            [LiveField] public string label;
        }

        [Serializable]
        [LiveClass("GoldenFixture")]
        public class GoldenFixture
        {
            [LiveField] public int intValue;
            [LiveField] public float floatValue;
            [LiveField] public bool boolValue;
            [LiveField] public string stringValue;
            [LiveField] public double doubleValue;
            [LiveField] public Vector2 vector2Value;
            [LiveField] public Vector3 vector3Value;
            [LiveField] public Color colorValue;
            [LiveField] public Quaternion quaternionValue;
            [LiveField] public Rect rectValue;
            [LiveField] public GoldenEnum enumValue;
            [LiveField] public int[] intArray;
            [LiveField] public string[] stringArray;
            [LiveField] public List<int> intList;
            [LiveField] public GoldenNested nested;
            [LiveField(persistScope = PersistScope.Project)] public int projectValue;
        }

        private TestLiveObjectResolver _resolver;

        [SetUp]
        public void SetUp()
        {
            LiveClass.Clear();
            foreach (var obj in LiveObjectRegistry.instances.ToList())
            {
                obj.Unregister();
            }
            _resolver = new TestLiveObjectResolver();
            LiveClass.RegisterFromAttributes<GoldenNested>();
            LiveClass.RegisterFromAttributes<GoldenFixture>();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in LiveObjectRegistry.instances.ToList())
            {
                obj.Unregister();
            }
        }

        // Fixed, format-stable values (no repeating binary fractions) so the JSON is deterministic.
        private LiveObjectHandle _CreateFixture(string id)
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
            var liveClass = LiveClass.Find(typeof(GoldenFixture));
            return new LiveObjectHandle(id, liveClass, target);
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
            Assert.AreEqual(kFull, LivePropertySerializer.ToJson(obj, _resolver));
        }

        [Test]
        public void RestDeltaClean_MatchesGolden()
        {
            // No dirty members: only force-included untracked (project-scoped) properties appear.
            var obj = _CreateFixture("golden-1");
            Assert.AreEqual(kDeltaClean, LivePropertySerializer.ToJson(obj, _resolver, isDirtyOnly: true));
        }

        [Test]
        public void ScenePersistence_MatchesGolden()
        {
            var obj = _CreateFixture("golden-1");
            var scene = LivePropertySerializer.ToJson(
                obj, _resolver, isDirtyOnly: false, forPersistence: true, scopeFilter: PersistScope.Scene);
            Assert.AreEqual(kScene, scene);
        }

        [Test]
        public void ProjectPersistence_MatchesGolden()
        {
            var obj = _CreateFixture("golden-1");
            var project = LivePropertySerializer.ToJson(
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
            Assert.AreEqual(kDeltaDirty, LivePropertySerializer.ToJson(obj, _resolver, isDirtyOnly: true));
        }

        [Test]
        public void ScenePersistence_RoundTrips()
        {
            var obj = _CreateFixture("golden-1");
            var scene = LivePropertySerializer.ToJson(
                obj, _resolver, isDirtyOnly: false, forPersistence: true, scopeFilter: PersistScope.Scene);

            // Deserialize the persisted form into a fresh object and re-serialize; must be identical.
            var freshTarget = new GoldenFixture();
            var freshHandle = new LiveObjectHandle(
                "golden-1", LiveClass.Find(typeof(GoldenFixture)), freshTarget);
            LivePropertySerializer.FromJson(scene, freshHandle, _resolver);

            var reSerialized = LivePropertySerializer.ToJson(
                freshHandle, _resolver, isDirtyOnly: false, forPersistence: true, scopeFilter: PersistScope.Scene);
            Assert.AreEqual(kScene, reSerialized);
        }
    }
}
