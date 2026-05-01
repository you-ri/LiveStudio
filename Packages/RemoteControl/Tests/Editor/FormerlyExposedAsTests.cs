// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Newtonsoft.Json.Linq;
using Lilium.RemoteControl;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// [FormerlyExposedAs] によるクラス/フィールド/プロパティのリネーム互換のテスト。
    /// typeName やメンバー名を変更しても、旧名で書かれた JSON から復元できることを保証する。
    /// </summary>
    [TestFixture]
    public class FormerlyExposedAsTests
    {
        #region Test Classes

        [Serializable]
        [ExposedClass("NewPlug")]
        [FormerlyExposedAs("OldPlug")]
        [FormerlyExposedAs("AncientPlug")]
        public class NewPlug
        {
            [ExposedField, Persistable]
            [FormerlyExposedAs("oldValue")]
            public int newValue;

            [ExposedProperty, Persistable]
            [FormerlyExposedAs("oldLabel")]
            public string newLabel { get; set; }
        }

        [Serializable]
        [ExposedClass("NoAliasClass")]
        public class NoAliasClass
        {
            [ExposedField, Persistable]
            public int value;
        }

        public class MockResolver : IExposedObjectResolver
        {
            public ExposedObject FindById(string id) => ExposedObjectRegistry.FindById(id);
            public ExposedObject FindByTarget(object target) => ExposedObjectRegistry.FindByTarget(target);
        }

        #endregion

        private MockResolver _resolver;

        [SetUp]
        public void SetUp()
        {
            ExposedClass.Clear();

            var toRemove = ExposedObjectRegistry.instances.ToList();
            foreach (var obj in toRemove) obj.Unregister();

            _resolver = new MockResolver();
        }

        [TearDown]
        public void TearDown()
        {
            var toRemove = ExposedObjectRegistry.instances.ToList();
            foreach (var obj in toRemove) obj.Unregister();
        }

        #region Class alias

        [Test]
        public void Find_ByCurrentTypeName_ReturnsClass()
        {
            ExposedClass.RegisterFromAttributes<NewPlug>();

            var ec = ExposedClass.Find("NewPlug");
            Assert.IsNotNull(ec);
            Assert.AreEqual(typeof(NewPlug), ec.type);
        }

        [Test]
        public void Find_ByFormerTypeName_ReturnsSameClass()
        {
            ExposedClass.RegisterFromAttributes<NewPlug>();

            var byCurrent = ExposedClass.Find("NewPlug");
            var byOld = ExposedClass.Find("OldPlug");
            var byAncient = ExposedClass.Find("AncientPlug");

            Assert.IsNotNull(byOld);
            Assert.IsNotNull(byAncient);
            Assert.AreSame(byCurrent, byOld);
            Assert.AreSame(byCurrent, byAncient);
        }

        [Test]
        public void Find_UnknownTypeName_ReturnsNull()
        {
            ExposedClass.RegisterFromAttributes<NewPlug>();
            Assert.IsNull(ExposedClass.Find("NoSuchClass"));
        }

        [Test]
        public void ExposedClass_formerTypeNames_ContainsAllAliases()
        {
            ExposedClass.RegisterFromAttributes<NewPlug>();
            var ec = ExposedClass.Find(typeof(NewPlug));
            CollectionAssert.AreEquivalent(new[] { "OldPlug", "AncientPlug" }, ec.formerTypeNames);
        }

        [Test]
        public void Unregister_RemovesAliases()
        {
            ExposedClass.RegisterFromAttributes<NewPlug>();
            var ec = ExposedClass.Find(typeof(NewPlug));
            ExposedClass.Unregister(ec);

            Assert.IsNull(ExposedClass.Find("NewPlug"));
            Assert.IsNull(ExposedClass.Find("OldPlug"));
            Assert.IsNull(ExposedClass.Find("AncientPlug"));
        }

        #endregion

        #region Field / Property alias

        [Test]
        public void FindProperty_ByCurrentName_ReturnsProperty()
        {
            ExposedClass.RegisterFromAttributes<NewPlug>();
            var ec = ExposedClass.Find(typeof(NewPlug));

            Assert.IsNotNull(ec.FindProperty("newValue"));
            Assert.IsNotNull(ec.FindProperty("newLabel"));
        }

        [Test]
        public void FindProperty_ByFormerFieldName_ReturnsRenamedProperty()
        {
            ExposedClass.RegisterFromAttributes<NewPlug>();
            var ec = ExposedClass.Find(typeof(NewPlug));

            var current = ec.FindProperty("newValue");
            var viaOld = ec.FindProperty("oldValue");
            Assert.IsNotNull(viaOld);
            Assert.AreSame(current, viaOld);
        }

        [Test]
        public void FindProperty_ByFormerPropertyName_ReturnsRenamedProperty()
        {
            ExposedClass.RegisterFromAttributes<NewPlug>();
            var ec = ExposedClass.Find(typeof(NewPlug));

            var current = ec.FindProperty("newLabel");
            var viaOld = ec.FindProperty("oldLabel");
            Assert.IsNotNull(viaOld);
            Assert.AreSame(current, viaOld);
        }

        [Test]
        public void PropertyType_formerNames_PopulatedFromAttribute()
        {
            ExposedClass.RegisterFromAttributes<NewPlug>();
            var ec = ExposedClass.Find(typeof(NewPlug));

            var valueProp = ec.FindProperty("newValue");
            CollectionAssert.AreEquivalent(new[] { "oldValue" }, valueProp.formerNames);
        }

        [Test]
        public void PropertyType_formerNames_EmptyForUnrenamed()
        {
            ExposedClass.RegisterFromAttributes<NoAliasClass>();
            var ec = ExposedClass.Find(typeof(NoAliasClass));
            var prop = ec.FindProperty("value");
            Assert.IsNotNull(prop);
            Assert.AreEqual(0, prop.formerNames.Length);
        }

        #endregion

        #region Scene load round-trip

        [Test]
        public void SceneFromJson_LoadsLegacyTypeName()
        {
            // 旧 typeName "OldPlug" で書かれた JSON が NewPlug クラスに復元される
            ExposedClass.RegisterFromAttributes<NewPlug>();
            var ec = ExposedClass.Find(typeof(NewPlug));
            var target = new NewPlug();
            new ExposedObject("plug-1", ec, target);

            var legacyJson = @"{
              ""format"": ""jp.lilium.remotecontrol.scene"",
              ""formatVersion"": 1,
              ""objects"": [
                { ""@type"": ""OldPlug"", ""@id"": ""plug-1"", ""newValue"": 42, ""newLabel"": ""ok"" }
              ]
            }";

            ExposedSceneSerializer.SceneFromJson(legacyJson, _resolver);

            Assert.AreEqual(42, target.newValue);
            Assert.AreEqual("ok", target.newLabel);
        }

        [Test]
        public void SceneFromJson_LoadsLegacyFieldName()
        {
            // 旧 field 名 "oldValue" で書かれた JSON が newValue に復元される
            ExposedClass.RegisterFromAttributes<NewPlug>();
            var ec = ExposedClass.Find(typeof(NewPlug));
            var target = new NewPlug();
            new ExposedObject("plug-2", ec, target);

            var legacyJson = @"{
              ""format"": ""jp.lilium.remotecontrol.scene"",
              ""formatVersion"": 1,
              ""objects"": [
                { ""@type"": ""NewPlug"", ""@id"": ""plug-2"", ""oldValue"": 77, ""oldLabel"": ""legacy"" }
              ]
            }";

            ExposedSceneSerializer.SceneFromJson(legacyJson, _resolver);

            Assert.AreEqual(77, target.newValue);
            Assert.AreEqual("legacy", target.newLabel);
        }

        [Test]
        public void SceneFromJson_LoadsLegacyTypeAndFieldNames()
        {
            // typeName とフィールド名の両方が旧名のケース
            ExposedClass.RegisterFromAttributes<NewPlug>();
            var ec = ExposedClass.Find(typeof(NewPlug));
            var target = new NewPlug();
            new ExposedObject("plug-3", ec, target);

            var legacyJson = @"{
              ""format"": ""jp.lilium.remotecontrol.scene"",
              ""formatVersion"": 1,
              ""objects"": [
                { ""@type"": ""AncientPlug"", ""@id"": ""plug-3"", ""oldValue"": 9, ""oldLabel"": ""ancient"" }
              ]
            }";

            ExposedSceneSerializer.SceneFromJson(legacyJson, _resolver);

            Assert.AreEqual(9, target.newValue);
            Assert.AreEqual("ancient", target.newLabel);
        }

        [Test]
        public void SceneToJson_EmitsCurrentNamesOnly()
        {
            // 書き出しは常に最新の typeName / field 名で行う（互換属性は読み取り専用の役割）
            ExposedClass.RegisterFromAttributes<NewPlug>();
            var ec = ExposedClass.Find(typeof(NewPlug));
            new ExposedObject("plug-4", ec, new NewPlug { newValue = 1, newLabel = "hi" });

            var json = ExposedSceneSerializer.SceneToJson(
                new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver);
            var entry = (JObject)((JArray)JObject.Parse(json)["objects"])[0];

            Assert.AreEqual("NewPlug", entry["@type"]?.Value<string>(),
                "Must always emit the current typeName, never a former alias");
            Assert.IsNotNull(entry["newValue"]);
            Assert.IsNotNull(entry["newLabel"]);
            Assert.IsNull(entry["oldValue"], "Former field names must not appear in output");
            Assert.IsNull(entry["oldLabel"], "Former property names must not appear in output");
        }

        #endregion
    }
}
