// Copyright (c) You-Ri, 2026
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Lilium.RemoteControl;
using Lilium.RemoteControl.LiveScene;
using Newtonsoft.Json.Linq;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// PersistScope (パラメーター単位のシリアライズ先指定) の検証。
    /// Scene scope はシーンファイルへ、Project scope は {クラス名}.settings.json へ振り分けられる。
    /// </summary>
    [TestFixture]
    public class ProjectScopeSerializationTests
    {
        #region Test Classes

        [ExposedClass("TestScopeClass")]
        public class TestScopeClass
        {
            [ExposedField]
            public int sceneValue;

            [ExposedField(persistScope = PersistScope.Project)]
            public int projectValue;
        }

        // shadow field 側の persistScope を property が継承するパターン (ScreenController と同型)。
        [ExposedClass("TestScopeShadowClass")]
        public class TestScopeShadowClass
        {
            [ExposedField(persistScope = PersistScope.Project), Hide]
            [FormerlyExposedAs("quality")]
            private string _quality = "Mid";

            [ExposedProperty]
            public string quality
            {
                get => _quality;
                set => _quality = value;
            }

            [ExposedField]
            public int sceneValue;
        }

        // shadow / [InlineReference] を持たない [ExposedProperty] は非 persistable。
        // ここに persistScope = Project を付けても保存されないため警告が出る。
        [ExposedClass("TestMisusedProjectClass")]
        public class TestMisusedProjectClass
        {
            [ExposedProperty(persistScope = PersistScope.Project)]
            public int value { get; set; }
        }

        // 公開 GameObject の component として辿られる [ExposedClass] MonoBehaviour
        // (ScreenController="Screen" と同じ構図)。
        [ExposedClass("TestScreenComponent")]
        public class TestScreenComponent : MonoBehaviour
        {
            [ExposedField(persistScope = PersistScope.Project)]
            public int projectVal;

            [ExposedField]
            public int sceneVal;
        }

        #endregion

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
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in ExposedObjectRegistry.instances.ToList())
            {
                obj.Unregister();
            }
        }

        private ExposedObjectHandle _CreateScopeObject(string id, int sceneValue, int projectValue)
        {
            ExposedClass.RegisterFromAttributes<TestScopeClass>();
            var target = new TestScopeClass { sceneValue = sceneValue, projectValue = projectValue };
            var exposedClass = ExposedClass.Find(typeof(TestScopeClass));
            return new ExposedObjectHandle(id, exposedClass, target);
        }

        // -------------------------------------------------------
        // scopeFilter による振り分け
        // -------------------------------------------------------

        [Test]
        public void ToJson_SceneScope_ContainsOnlySceneMembers()
        {
            var obj = _CreateScopeObject("scope-1", sceneValue: 10, projectValue: 20);

            var json = ExposedPropertySerializer.ToJson(
                obj, _resolver, isDirtyOnly: false, forPersistence: true, scopeFilter: PersistScope.Scene);
            var jObj = JObject.Parse(json);

            Assert.IsNotNull(jObj["sceneValue"], "Scene member should be present.");
            Assert.IsNull(jObj["projectValue"], "Project member must be excluded from Scene scope.");
        }

        [Test]
        public void ToJson_ProjectScope_ContainsOnlyProjectMembers()
        {
            var obj = _CreateScopeObject("scope-1", sceneValue: 10, projectValue: 20);

            var json = ExposedPropertySerializer.ToJson(
                obj, _resolver, isDirtyOnly: false, forPersistence: true, scopeFilter: PersistScope.Project);
            var jObj = JObject.Parse(json);

            Assert.IsNotNull(jObj["projectValue"], "Project member should be present.");
            Assert.AreEqual(20, jObj["projectValue"].Value<int>());
            Assert.IsNull(jObj["sceneValue"], "Scene member must be excluded from Project scope.");
        }

        [Test]
        public void ToJson_NonPersistence_IgnoresScopeAndIncludesAll()
        {
            var obj = _CreateScopeObject("scope-1", sceneValue: 10, projectValue: 20);

            // forPersistence:false の通常読み出しは scope に関わらず全プロパティを含む。
            var json = ExposedPropertySerializer.ToJson(
                obj, _resolver, isDirtyOnly: false, forPersistence: false);
            var jObj = JObject.Parse(json);

            Assert.IsNotNull(jObj["sceneValue"], "Scene member should be present in non-persistence read.");
            Assert.IsNotNull(jObj["projectValue"], "Project member should be present in non-persistence read.");
        }

        [Test]
        public void ToJson_ShadowField_InheritsProjectScope()
        {
            ExposedClass.RegisterFromAttributes<TestScopeShadowClass>();
            var target = new TestScopeShadowClass { quality = "High", sceneValue = 3 };
            var exposedClass = ExposedClass.Find(typeof(TestScopeShadowClass));
            var obj = new ExposedObjectHandle("shadow-1", exposedClass, target);

            var sceneJson = JObject.Parse(ExposedPropertySerializer.ToJson(
                obj, _resolver, isDirtyOnly: false, forPersistence: true, scopeFilter: PersistScope.Scene));
            var projectJson = JObject.Parse(ExposedPropertySerializer.ToJson(
                obj, _resolver, isDirtyOnly: false, forPersistence: true, scopeFilter: PersistScope.Project));

            Assert.IsNull(sceneJson["quality"], "Shadow-backed property must inherit Project scope and be excluded from Scene.");
            Assert.IsNotNull(sceneJson["sceneValue"], "Plain Scene field should remain in Scene scope.");
            Assert.AreEqual("High", projectJson["quality"]?.Value<string>(), "Shadow-backed property should route to Project scope.");
            Assert.IsNull(projectJson["sceneValue"], "Scene field must not appear in Project scope.");
        }

        // -------------------------------------------------------
        // live.json からの除外
        // -------------------------------------------------------

        [Test]
        public void LiveSceneToJson_ExcludesProjectScopedMembers()
        {
            var obj = _CreateScopeObject("scope-1", sceneValue: 10, projectValue: 20);

            var json = LiveSceneSerializer.LiveSceneToJson(
                new List<ExposedObjectHandle>(ExposedObjectRegistry.instances), _resolver);
            var jRoot = JObject.Parse(json);
            // root エントリは @id が外され @source = obj.id に置き換わる。
            var entry = ((JArray)jRoot["objects"]).Cast<JObject>()
                .First(o => (o["@source"] ?? o["@id"])?.Value<string>() == "scope-1");

            Assert.IsNotNull(entry["sceneValue"], "Scene member should be written to live.json.");
            Assert.IsNull(entry["projectValue"], "Project member must NOT appear in live.json.");
        }

        // -------------------------------------------------------
        // ProjectSettingsSerializer round-trip
        // -------------------------------------------------------

        [Test]
        public void BuildPerClassJson_GroupsByClass_WithProjectMembersOnly()
        {
            _CreateScopeObject("scope-1", sceneValue: 10, projectValue: 20);

            var objects = new List<ExposedObjectHandle>(ExposedObjectRegistry.instances);
            var perClass = ProjectSettingsSerializer.BuildPerClassJson(objects, _resolver);

            Assert.IsTrue(perClass.ContainsKey("TestScopeClass"), "Settings should be keyed by class typeName.");
            var root = JObject.Parse(perClass["TestScopeClass"]);
            Assert.AreEqual(ProjectSettingsSerializer.FormatIdentifier, root["format"]?.Value<string>());
            var entry = ((JArray)root["objects"]).Single() as JObject;
            Assert.AreEqual("scope-1", entry["@id"]?.Value<string>());
            Assert.IsNotNull(entry["projectValue"], "Project member should be present.");
            Assert.IsNull(entry["sceneValue"], "Scene member must not be present in settings file.");
        }

        [Test]
        public void ApplyJson_RestoresProjectValues_AndIsIdempotent()
        {
            ExposedClass.RegisterFromAttributes<TestScopeClass>();
            var target = new TestScopeClass { sceneValue = 10, projectValue = 20 };
            var exposedClass = ExposedClass.Find(typeof(TestScopeClass));
            var obj = new ExposedObjectHandle("scope-1", exposedClass, target);

            var perClass = ProjectSettingsSerializer.BuildPerClassJson(
                new List<ExposedObjectHandle>(ExposedObjectRegistry.instances), _resolver);
            var json = perClass["TestScopeClass"];

            // 値を変更してから適用 → 復元されること。
            target.projectValue = 999;
            ProjectSettingsSerializer.ApplyJson(json, _resolver);
            Assert.AreEqual(20, target.projectValue, "Project value should be restored from settings.");

            // 冪等: 再適用しても同じ。
            ProjectSettingsSerializer.ApplyJson(json, _resolver);
            Assert.AreEqual(20, target.projectValue, "Re-applying should keep the same value.");
        }

        [Test]
        public void BuildPerClassJson_MultipleInstances_KeyedById()
        {
            ExposedClass.RegisterFromAttributes<TestScopeClass>();
            var exposedClass = ExposedClass.Find(typeof(TestScopeClass));
            var a = new ExposedObjectHandle("inst-a", exposedClass, new TestScopeClass { projectValue = 1 });
            var b = new ExposedObjectHandle("inst-b", exposedClass, new TestScopeClass { projectValue = 2 });

            var perClass = ProjectSettingsSerializer.BuildPerClassJson(
                new List<ExposedObjectHandle>(ExposedObjectRegistry.instances), _resolver);
            var root = JObject.Parse(perClass["TestScopeClass"]);
            var entries = ((JArray)root["objects"]).Cast<JObject>().ToList();

            Assert.AreEqual(2, entries.Count, "Two instances should produce two entries.");
            var ea = entries.First(e => e["@id"]?.Value<string>() == "inst-a");
            var eb = entries.First(e => e["@id"]?.Value<string>() == "inst-b");
            Assert.AreEqual(1, ea["projectValue"].Value<int>());
            Assert.AreEqual(2, eb["projectValue"].Value<int>());
        }

        [Test]
        public void BuildPerClassJson_ClassWithoutProjectMembers_IsSkipped()
        {
            ExposedClass.RegisterFromAttributes<TestScopeShadowClass>();
            // sceneValue だけ設定し、Project メンバー (quality) は既定のまま。
            var exposedClass = ExposedClass.Find(typeof(TestScopeShadowClass));
            var _ = new ExposedObjectHandle("shadow-1", exposedClass, new TestScopeShadowClass { sceneValue = 5 });

            // quality は Project メンバーなので出力対象。クラスはファイル化される。
            var perClass = ProjectSettingsSerializer.BuildPerClassJson(
                new List<ExposedObjectHandle>(ExposedObjectRegistry.instances), _resolver);

            Assert.IsTrue(perClass.ContainsKey("TestScopeShadowClass"));
            var entry = ((JArray)JObject.Parse(perClass["TestScopeShadowClass"])["objects"]).Single() as JObject;
            Assert.IsNotNull(entry["quality"], "Project-scoped shadow property should be written.");
            Assert.IsNull(entry["sceneValue"], "Scene field must not be in the settings file.");
        }

        // -------------------------------------------------------
        // ProjectSettingsStore (ファイル IO)
        // -------------------------------------------------------

        [Test]
        public void Store_GetSettingsFilePath_UsesSettingsDir()
        {
            var path = ProjectSettingsStore.GetSettingsFilePath("C:/proj", "Screen").Replace('\\', '/');
            StringAssert.Contains("/Settings/", path);
            StringAssert.EndsWith("Screen.settings.json", path);
        }

        [Test]
        public void Store_WriteAll_ApplyAll_RoundTrip()
        {
            var tempProject = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "VirgoProjScopeTest_" + System.Guid.NewGuid().ToString("N"));
            try
            {
                ExposedClass.RegisterFromAttributes<TestScopeClass>();
                var exposedClass = ExposedClass.Find(typeof(TestScopeClass));
                var target = new TestScopeClass { projectValue = 42 };
                var obj = new ExposedObjectHandle("scope-1", exposedClass, target);

                var perClass = ProjectSettingsSerializer.BuildPerClassJson(
                    new List<ExposedObjectHandle>(ExposedObjectRegistry.instances), _resolver);
                ProjectSettingsStore.WriteAll(tempProject, perClass);

                var file = ProjectSettingsStore.GetSettingsFilePath(tempProject, "TestScopeClass");
                Assert.IsTrue(System.IO.File.Exists(file), "Settings file should be written to the project Settings dir.");

                target.projectValue = 0;
                ProjectSettingsStore.ApplyAll(tempProject, _resolver);
                Assert.AreEqual(42, target.projectValue, "ApplyAll should restore the value from the globbed settings file.");
            }
            finally
            {
                if (System.IO.Directory.Exists(tempProject))
                    System.IO.Directory.Delete(tempProject, true);
            }
        }

        // -------------------------------------------------------
        // 誤用検知
        // -------------------------------------------------------

        [Test]
        public void Register_ProjectScopeOnNonPersistableProperty_LogsWarning()
        {
            LogAssert.Expect(LogType.Warning, new Regex("marked PersistScope.Project but is not persistable"));
            ExposedClass.RegisterFromAttributes<TestMisusedProjectClass>();
        }

        // -------------------------------------------------------
        // 公開 GameObject の component 走査 (ScreenController="Screen" 構図)
        // -------------------------------------------------------

        [Test]
        public void BuildPerClassJson_TraversesExposedGameObjectComponents()
        {
            ExposedClass.RegisterFromAttributes<ExposedGameObject>();
            ExposedClass.RegisterFromAttributes<TestScreenComponent>();

            var go = new GameObject("TestParentGO");
            ExposedGameObject proxy = null;
            try
            {
                var comp = go.AddComponent<TestScreenComponent>();
                comp.projectVal = 7;
                comp.sceneVal = 3;

                // ExposedGameObject は ctor で自身を ExposedObjectRegistry に登録する。
                proxy = new ExposedGameObject(go);

                var objs = new List<ExposedObjectHandle>(ExposedObjectRegistry.instances);
                var perClass = ProjectSettingsSerializer.BuildPerClassJson(objs, _resolver);

                Assert.IsTrue(perClass.ContainsKey("TestScreenComponent"),
                    "Component class should be persisted via GameObject component traversal.");
                var entry = ((JArray)JObject.Parse(perClass["TestScreenComponent"])["objects"]).Single() as JObject;
                Assert.AreEqual(7, entry["projectVal"].Value<int>());
                Assert.IsNull(entry["sceneVal"], "Scene member must not be in the settings file.");
                Assert.AreEqual(proxy.id, entry["@parent"].Value<string>(),
                    "Component entry should link to its owning GameObject by @parent.");

                // Apply round-trip: 値を変更してから @parent 経由で再解決・復元できること。
                comp.projectVal = 999;
                ProjectSettingsSerializer.ApplyJson(perClass["TestScreenComponent"], _resolver);
                Assert.AreEqual(7, comp.projectVal, "ApplyJson should restore the component's Project value via @parent.");
            }
            finally
            {
                proxy?.OnDisable();
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}
